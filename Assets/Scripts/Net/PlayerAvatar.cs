using Residue.Gameplay.World;
using Unity.Netcode;
using UnityEngine;

namespace Residue.Net
{
    /// <summary>
    /// Decides which half of a player object runs on this machine.
    /// <para>
    /// One prefab serves both jobs, because the alternative — a "me" prefab and a "them" prefab —
    /// means every change to the character has to be made twice and the two drift. So the prefab
    /// carries everything, and this switches off what does not belong here: input, cameras, hands
    /// and the interaction ray exist only for the owner, and a body exists only for everyone else.
    /// </para>
    /// The body is culled from its owner by layer rather than disabled, so it still casts a shadow
    /// you can see at your own feet — see <see cref="ThirdPersonView.PlayerBodyLayer"/>.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerAvatar : NetworkBehaviour
    {
        [Header("Owner only")]
        [SerializeField] private PlayerController controller;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private PlayerHeadMotion headMotion;
        [SerializeField] private PlayerHands hands;
        [SerializeField] private ThirdPersonView thirdPerson;
        [SerializeField] private InteractionDebug interactionDebug;
        [SerializeField] private Camera eyeCamera;
        [SerializeField] private AudioListener earsOfTheOwner;
        [SerializeField] private CharacterController motor;

        [Header("Everyone")]
        [SerializeField] private CharacterBody body;

        /// <summary>
        /// Where this player's carried bottle hangs.
        /// <para>
        /// The one thing this component exposes about a player other than which half of them runs
        /// here, and it exists because a client id has to become a place in the room somehow. Vials
        /// are local props (§3.2) and only their <c>SampleLocation</c> travels, so a bottle the host
        /// says client 2 is holding has to be parented to client 2's hands by every process
        /// separately — and this is the only component that knows which body belongs to which
        /// connection. See <c>Residue.Gameplay.World.IPlayerHands</c>.
        /// </para>
        /// Valid on a replica, where <see cref="interactor"/> is disabled but its transforms are
        /// still in the scene and still following the body around.
        /// </summary>
        public Transform CarrySocket => interactor != null ? interactor.CarrySocket : transform;

        /// <summary>
        /// Crouching and carrying, replicated because a replica cannot infer them from position.
        /// <para>
        /// Owner-write rather than server-write: the owner is the only one who knows, and routing a
        /// pose flag through the host would add a round trip to a thing with no authority stakes.
        /// Lying about them buys nothing — the arms move, the lab does not care.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<bool> crouching = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<bool> carrying = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner)
            {
                // Written only on change. NGO does not dirty a variable that is assigned its own
                // value, but going through the property every frame is still needless churn.
                bool nowCrouching = controller != null && controller.IsCrouching;
                bool nowCarrying = interactor != null && interactor.Carried != null;

                if (crouching.Value != nowCrouching) crouching.Value = nowCrouching;
                if (carrying.Value != nowCarrying) carrying.Value = nowCarrying;
                return;
            }

            if (body != null) body.SetRemoteState(crouching.Value, carrying.Value);
        }

        /// <summary>
        /// Put this player somewhere, and let them start falling once they are there.
        /// <para>
        /// Sent to the owner rather than applied on the server, because §3.1 gives a client authority
        /// over its own transform — a server write would be overwritten by the next owner update.
        /// </para>
        /// The <see cref="CharacterController"/> has to be off while the transform is written: it
        /// caches its own position and will happily put the player back where it thought it was.
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void PlaceRpc(Vector3 position, float yaw)
        {
            bool hadMotor = motor != null && motor.enabled;
            if (motor != null) motor.enabled = false;

            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            if (motor != null) motor.enabled = true;
            if (!hadMotor && controller != null) controller.enabled = true;

            placed = true;
        }

        /// <summary>
        /// True once the server has said where this player belongs.
        /// <para>
        /// Until then the owner is frozen. A player object is created when the connection is
        /// approved, which for a host is during <c>StartHost</c> — before the lab scene exists. In an
        /// empty Boot scene there is no floor, so an unfrozen player falls for the whole Relay and
        /// Lobby handshake: measured at roughly twenty-two kilometres down by the time the lab
        /// arrived, which presents as "the game loaded but the world is empty".
        /// </para>
        /// </summary>
        private bool placed;

        public override void OnNetworkSpawn()
        {
            bool mine = IsOwner;

            // Before anything is switched off. Disabling a controller frees the shared cursor, and a
            // replica doing that would take the mouse away from the person actually playing — once
            // per remote player at spawn, and again every time somebody new joins.
            if (controller != null) controller.ManagesCursor = mine;

            SetActive(controller, mine);
            SetActive(interactor, mine);
            SetActive(headMotion, mine);
            SetActive(hands, mine);
            SetActive(thirdPerson, mine);
            SetActive(interactionDebug, mine);

            // A second camera or listener in the scene is not a small bug: Unity renders whichever it
            // feels like and warns about the listeners, so a remote player's eyes end up being yours.
            if (eyeCamera != null) eyeCamera.enabled = mine;
            if (earsOfTheOwner != null) earsOfTheOwner.enabled = mine;

            // The motor moves this transform. On a replica the transform is written by
            // OwnerNetworkTransform instead, and two things writing one transform is jitter.
            //
            // Frozen for the owner too until PlaceRpc arrives: gravity in a scene with no floor is
            // how the player ended up twenty-two kilometres beneath the lab.
            if (motor != null) motor.enabled = mine && placed;

            if (hands != null) hands.gameObject.SetActive(mine);

            // The body animates from replicated movement on a replica, and from the controller on
            // the owner. It stays on in both cases; only its source changes.
            if (body != null)
            {
                body.SetRemote(!mine);
                PlaceBodyOnItsLayer(body.gameObject, mine);
            }

            name = mine ? "Player (you)" : $"Player {OwnerClientId}";
        }

        private static void SetActive(Behaviour b, bool active)
        {
            if (b != null) b.enabled = active;
        }

        /// <summary>
        /// Only <i>your own</i> body goes on the hidden layer.
        /// <para>
        /// Hiding is done by culling <see cref="ThirdPersonView.PlayerBodyLayer"/> out of the eye
        /// camera, which was exactly right when the only body in the lab was yours. With four
        /// players it hides all of them: the camera cannot tell one layer member from another, so
        /// everybody ends up invisible to everybody. The bodies were there, animating, being
        /// replicated — and culled by a mask written for single player.
        /// </para>
        /// So the layer means "mine", not "a body". A teammate's goes on Default and is simply seen.
        /// F4 still works: <see cref="ThirdPersonView"/> adds the layer back and your own body
        /// appears, which is the one case the layer exists for.
        /// </para>
        /// </summary>
        private static void PlaceBodyOnItsLayer(GameObject body, bool mine)
        {
            SetLayerRecursively(body, mine ? ThirdPersonView.PlayerBodyLayer : DefaultLayer);
        }

        private const int DefaultLayer = 0;

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
