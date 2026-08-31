using UnityEngine;
using UnityEngine.InputSystem;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Centers the selected physical item for inspection and restores it exactly on close.
    /// <para>
    /// An inspected item deliberately leaves the held-item overlay and renders with the room again.
    /// Parenting it to the eye camera takes it out of the hand socket, which is the whole of the rule
    /// <see cref="HeldItemCamera"/> enforces, so this needs no code — but it is a decision, not an
    /// accident. Inspection is the one time the object is being compared <i>against</i> the lab rather
    /// than carried through it: it wants to be occluded by a bench it is held behind, and the framing
    /// below is computed from the eye camera's field of view and viewport, which would be quietly
    /// wrong against a camera with a different projection. The clipping the overlay exists to prevent
    /// cannot happen here either — the player controller is switched off, so they cannot walk the view
    /// into a wall while looking at it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemInspectionView : MonoBehaviour
    {
        [SerializeField] private float rotationSensitivity = 0.22f;
        [SerializeField] private float viewportHeight = 0.42f;
        [SerializeField] private float minimumDistance = 0.18f;
        /// <summary>
        /// Metres of zoom per unit of wheel, exponentially. A Windows notch reports 120, so this is
        /// about a third closer per notch — the old 0.0015 was a sixth, which took half a dozen
        /// notches to cross a range that only went 55% of the way in anyway.
        /// </summary>
        [SerializeField] private float zoomSensitivity = 0.0035f;

        /// <summary>
        /// Closest the item may come, as a fraction of its framing distance.
        /// <para>
        /// This, not <see cref="zoomSensitivity"/>, was what made the reference books hard to read:
        /// the wheel bottomed out at 45% of the framing distance, so however far you rolled it the
        /// page could not get much bigger. A book is the one inspectable whose whole point is reading
        /// small print, so the floor is now a quarter — the near-plane guard below is what actually
        /// stops the item being pushed through the camera, and it is unchanged.
        /// </para>
        /// </summary>
        private const float ClosestFraction = 0.25f;

        private PlayerController player;
        private PlayerInteractor interactor;
        private Camera eye;
        private Carryable item;
        private Transform previousParent;
        private Vector3 previousLocalPosition;
        private Quaternion previousLocalRotation;
        private int openedFrame = -1;
        private float inspectionDistance;
        private float minimumZoomDistance;
        private float maximumZoomDistance;
        private bool pointerCapturedByItem;

        public bool IsOpen => item != null;
        public Carryable Item => item;

        public void Initialize(PlayerController owner, PlayerInteractor ownerInteractor)
        {
            player = owner;
            interactor = ownerInteractor;
            eye = owner != null ? owner.EyeCamera : null;
        }

        public bool Open(Carryable selected)
        {
            if (selected == null || IsOpen) return false;
            if (eye == null && player != null) eye = player.EyeCamera;
            if (eye == null) return false;

            item = selected;
            openedFrame = Time.frameCount;
            previousParent = item.transform.parent;
            previousLocalPosition = item.transform.localPosition;
            previousLocalRotation = item.transform.localRotation;

            item.BeginInspection();

            var bounds = item.VisualBounds;
            float radius = Mathf.Max(0.08f, bounds.extents.magnitude);
            float halfFov = eye.fieldOfView * 0.5f * Mathf.Deg2Rad;
            // Half the former framing distance makes every inspected item appear exactly twice as
            // large before the player applies any wheel zoom.
            float distance = Mathf.Max(minimumDistance,
                radius / Mathf.Tan(halfFov * viewportHeight) * 0.5f);
            inspectionDistance = distance;
            minimumZoomDistance = Mathf.Max(eye.nearClipPlane + radius * 0.4f,
                                            distance * ClosestFraction);
            maximumZoomDistance = distance * 2.5f;

            item.transform.SetParent(eye.transform, worldPositionStays: false);
            item.transform.localPosition = new Vector3(0f, 0f, distance);
            item.transform.localRotation = item.InspectionRotation;
            item.SetHeldVisible(true);

            // Center the visible geometry, not the prefab pivot (paper and bottles commonly place
            // their pivot at a bottom edge for shelf placement).
            CenterAtInspectionDistance();

            if (player != null) player.enabled = false;
            PlayerController.SetCursorLocked(false);
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;

            item.transform.SetParent(previousParent, worldPositionStays: false);
            item.transform.localPosition = previousLocalPosition;
            item.transform.localRotation = previousLocalRotation;
            var closed = item;
            item = null;
            pointerCapturedByItem = false;
            closed.EndInspection();

            if (player != null) player.enabled = true;
            PlayerController.SetCursorLocked(true);
            if (interactor != null) interactor.RefreshInventoryPresentation();
            closed.SetHeldVisible(true);
        }

        private void OnDisable()
        {
            if (IsOpen) Close();
        }

        private void Update()
        {
            if (!IsOpen || Keyboard.current == null) return;

            item.TickInspection();

            // PlayerInteractor opened the view from this same Space press. Depending on component
            // update order, this component may run later in that frame and must not interpret the
            // opening press as the request to close again.
            if (Time.frameCount == openedFrame) return;

            if (Keyboard.current.escapeKey.wasPressedThisFrame ||
                Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                pointerCapturedByItem = item.HandleInspectionPointer(
                    eye, Mouse.current.position.ReadValue());
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
                pointerCapturedByItem = false;

            if (Mouse.current != null && Mouse.current.leftButton.isPressed && !pointerCapturedByItem)
            {
                Vector2 delta = Mouse.current.delta.ReadValue() * rotationSensitivity;
                item.transform.Rotate(Vector3.up, -delta.x, Space.World);
                item.transform.Rotate(eye.transform.right, delta.y, Space.World);
                CenterAtInspectionDistance();
            }

            if (Mouse.current != null)
            {
                float wheel = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    inspectionDistance = Mathf.Clamp(
                        inspectionDistance * Mathf.Exp(-wheel * zoomSensitivity),
                        minimumZoomDistance, maximumZoomDistance);
                    CenterAtInspectionDistance();
                }
            }
        }

        private void CenterAtInspectionDistance()
        {
            if (item == null || eye == null) return;
            Vector3 targetCenter = eye.ViewportToWorldPoint(
                new Vector3(0.5f, 0.5f, inspectionDistance));
            item.transform.position += targetCenter - item.VisualBounds.center;
        }
    }
}
