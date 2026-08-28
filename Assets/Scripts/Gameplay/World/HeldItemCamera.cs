using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// §2.6's separate camera for held items: a URP overlay stacked on the eye camera, with its own
    /// near clip and its own depth buffer.
    /// <para>
    /// The eye camera's 0.05 m near clip is a compromise between not clipping the room and not
    /// clipping the hands, and it loses both ways — press your face into a bench while holding a vial
    /// and the bench cuts straight through the glass, so you are looking at the inside of a sample
    /// you are supposedly holding. An overlay camera solves it structurally rather than by tuning:
    /// URP clears depth before an overlay draws, so nothing in the room can ever intersect the hand,
    /// and the overlay's own 0.01 m near clip lets the item sit as close to the eye as the pose wants.
    /// </para>
    /// <para>
    /// The split is done by layer, the same mechanism <see cref="PlayerInteractor.IgnoreRaycastLayer"/>
    /// and <see cref="ThirdPersonView.PlayerBodyLayer"/> already use: the hands are authored onto
    /// <see cref="HeldItemLayer"/> by the scene builder, the overlay renders only that layer, and the
    /// eye camera culls it.
    /// </para>
    /// <para>
    /// Carried props cannot be authored onto the layer, because they are runtime instances that a
    /// dozen call sites re-parent — the inventory, three reconcilers, every station, the drop code.
    /// So the rule is stated once, here, as a property of the socket rather than of each item:
    /// <b>whatever is parented to the hand socket renders on the hand camera.</b> Polled rather than
    /// hooked onto <c>PlayerInventory.Changed</c> because re-parenting also happens without the
    /// inventory changing — <see cref="ItemInspectionView"/> being the case that matters — and a rule
    /// a future call site can forget to fire is not a rule.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class HeldItemCamera : MonoBehaviour
    {
        /// <summary>Layer the hands and everything in them live on. Rendered only by the overlay.</summary>
        public const int HeldItemLayer = 9;

        [Tooltip("The eye camera. This overlay is stacked on it and culls its own layer out of it.")]
        [SerializeField] private Camera baseCamera;

        [SerializeField] private Camera overlayCamera;

        [Tooltip("PlayerInteractor's carry socket. Its children are the held items.")]
        [SerializeField] private Transform handSocket;

        private bool firstPerson = true;

        // Parallel lists rather than a dictionary: there are never more than PlayerInventory.SlotCount
        // entries, and the original layer has to survive the item being destroyed under us.
        private readonly List<Transform> claimed = new();
        private readonly List<int> claimedLayers = new();

        private void Awake()
        {
            if (overlayCamera == null) overlayCamera = GetComponent<Camera>();

            if (overlayCamera == null || baseCamera == null)
            {
                Debug.LogError("[HeldItemCamera] No overlay or no base camera, so held items would " +
                               "render on nothing at all. Rebuild the lab scene.", this);
                enabled = false;
                return;
            }

            overlayCamera.cullingMask = 1 << HeldItemLayer;
            baseCamera.cullingMask &= ~(1 << HeldItemLayer);

            Stack();
        }

        /// <summary>
        /// Put this camera in the eye camera's stack.
        /// <para>
        /// Done here as well as by the scene builder because a stack entry is a serialized reference
        /// between two components of the same prefab, and the player prefab is instantiated per client
        /// by netcode. Re-establishing it costs a list lookup at spawn; getting it wrong costs an
        /// invisible pair of hands with nothing logged.
        /// </para>
        /// </summary>
        private void Stack()
        {
            // Must be Overlay before the stack is touched: the stack property refuses to hand back a
            // list for a camera it thinks is a Base, and refuses to accept one that is not an Overlay.
            overlayCamera.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;

            var stack = baseCamera.GetUniversalAdditionalCameraData().cameraStack;
            if (stack != null && !stack.Contains(overlayCamera)) stack.Add(overlayCamera);
        }

        /// <summary>
        /// Hand held items back to the eye camera for third person, and take them again on the way out.
        /// <para>
        /// F4 pulls the eye camera behind the body, but the hand socket stays at the head — so an
        /// overlay would draw the item at head height <i>in front of</i> the character carrying it,
        /// depth-cleared over their own back. On the eye camera it is depth-tested against the body
        /// like any other prop, which is what third person is for. See <see cref="ThirdPersonView"/>.
        /// </para>
        /// </summary>
        public void SetFirstPerson(bool value)
        {
            firstPerson = value;
            if (baseCamera == null) return;

            if (value) baseCamera.cullingMask &= ~(1 << HeldItemLayer);
            else baseCamera.cullingMask |= 1 << HeldItemLayer;

            if (overlayCamera != null) overlayCamera.enabled = baseCamera.enabled && value;
        }

        private void LateUpdate()
        {
            if (baseCamera == null || overlayCamera == null) return;

            // PlayerHeadMotion animates the eye camera's field of view — the setting, plus the sprint
            // kick. A fixed overlay FOV would slide the hands against the room every time you ran.
            overlayCamera.fieldOfView = baseCamera.fieldOfView;

            // "Is this camera my eyes?" PlayerAvatar switches the eye camera off on every replica, and
            // that is the only signal available here without Residue.Gameplay knowing what a client is.
            //
            // A stray enabled overlay would not in fact render — URP only draws an Overlay through the
            // Base whose stack it is in, and a replica's Base is off — but the claim below must still
            // be gated, because a replica's socket holds a teammate's bottle. Claiming it would move a
            // prop that everyone else has to see onto a layer everyone else culls, which is exactly the
            // bug PlayerAvatar.PlaceBodyOnItsLayer documents for bodies.
            bool mine = baseCamera.enabled;
            overlayCamera.enabled = mine && firstPerson;

            if (mine) ClaimSocketContents();
            else ReleaseClaims();
        }

        private void OnDisable() => ReleaseClaims();

        private void ClaimSocketContents()
        {
            if (handSocket == null) return;

            // Released first, so an item that moved straight from the socket into the inspection view
            // is back on its own layer in the same frame it left.
            for (int i = claimed.Count - 1; i >= 0; i--)
            {
                if (claimed[i] != null && claimed[i].parent == handSocket) continue;

                Restore(i);
                claimed.RemoveAt(i);
                claimedLayers.RemoveAt(i);
            }

            for (int i = 0; i < handSocket.childCount; i++)
            {
                var child = handSocket.GetChild(i);
                if (claimed.Contains(child)) continue;

                claimed.Add(child);
                claimedLayers.Add(child.gameObject.layer);
                SetLayerRecursively(child.gameObject, HeldItemLayer);
            }
        }

        private void ReleaseClaims()
        {
            for (int i = 0; i < claimed.Count; i++) Restore(i);

            claimed.Clear();
            claimedLayers.Clear();
        }

        /// <summary>Put an item back on the layer it arrived wearing, so dropping it returns it to the room.</summary>
        private void Restore(int index)
        {
            if (claimed[index] != null) SetLayerRecursively(claimed[index].gameObject, claimedLayers[index]);
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
