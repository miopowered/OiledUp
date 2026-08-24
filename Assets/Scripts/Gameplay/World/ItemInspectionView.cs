using UnityEngine;
using UnityEngine.InputSystem;

namespace Residue.Gameplay.World
{
    /// <summary>Centers the selected physical item for inspection and restores it exactly on close.</summary>
    [DisallowMultipleComponent]
    public sealed class ItemInspectionView : MonoBehaviour
    {
        [SerializeField] private float rotationSensitivity = 0.22f;
        [SerializeField] private float viewportHeight = 0.42f;
        [SerializeField] private float minimumDistance = 0.42f;

        private PlayerController player;
        private PlayerInteractor interactor;
        private Camera eye;
        private Carryable item;
        private Transform previousParent;
        private Vector3 previousLocalPosition;
        private Quaternion previousLocalRotation;
        private int openedFrame = -1;

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
            float distance = Mathf.Max(minimumDistance, radius / Mathf.Tan(halfFov * viewportHeight));

            item.transform.SetParent(eye.transform, worldPositionStays: false);
            item.transform.localPosition = new Vector3(0f, 0f, distance);
            item.transform.localRotation = item.InspectionRotation;
            item.SetHeldVisible(true);

            // Center the visible geometry, not the prefab pivot (paper and bottles commonly place
            // their pivot at a bottom edge for shelf placement).
            var centeredBounds = item.VisualBounds;
            Vector3 targetCenter = eye.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, distance));
            item.transform.position += targetCenter - centeredBounds.center;

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

            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                Vector2 delta = Mouse.current.delta.ReadValue() * rotationSensitivity;
                item.transform.Rotate(Vector3.up, -delta.x, Space.World);
                item.transform.Rotate(eye.transform.right, delta.y, Space.World);
            }
        }
    }
}
