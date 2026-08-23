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

        public override void OnNetworkSpawn()
        {
            bool mine = IsOwner;

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
            if (motor != null) motor.enabled = mine;

            if (hands != null) hands.gameObject.SetActive(mine);

            // The body animates from replicated movement on a replica, and from the controller on
            // the owner. It stays on in both cases; only its source changes.
            if (body != null) body.SetRemote(!mine);

            name = mine ? "Player (you)" : $"Player {OwnerClientId}";
        }

        private static void SetActive(Behaviour b, bool active)
        {
            if (b != null) b.enabled = active;
        }
    }
}
