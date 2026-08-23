using UnityEngine;
using UnityEngine.InputSystem;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// F4 pulls the camera back behind the player and un-hides the body.
    /// <para>
    /// The body is built for other players to look at, and until M4 there are no other players. That
    /// leaves a character nobody can ever see, which is how a walk cycle quietly rots. This makes it
    /// checkable now, with the same one-keypress cost as the F3 interaction overlay.
    /// </para>
    /// Debug only: it writes the camera transform directly and does not collide with the world, so
    /// backing into a wall puts the camera inside it.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ThirdPersonView : MonoBehaviour
    {
        /// <summary>Layer the character body lives on, culled from its owner's eye camera.</summary>
        public const int PlayerBodyLayer = 8;

        [SerializeField] private PlayerController player;
        [SerializeField] private Camera eyeCamera;

        [Tooltip("First-person hands. Hidden in third person, where they would float in mid-air.")]
        [SerializeField] private GameObject hands;

        [SerializeField] private float distance = 2.8f;
        [SerializeField] private float pivotHeight = 1.45f;

        private bool active;
        private Vector3 restPosition;
        private Quaternion restRotation;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (eyeCamera == null && player != null) eyeCamera = player.EyeCamera;

            if (eyeCamera == null) return;

            restPosition = eyeCamera.transform.localPosition;
            restRotation = eyeCamera.transform.localRotation;

            // First person is the default, so the owner never renders their own body.
            eyeCamera.cullingMask &= ~(1 << PlayerBodyLayer);
        }

        private void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.f4Key.wasPressedThisFrame) return;
            SetActive(!active);
        }

        private void SetActive(bool value)
        {
            active = value;
            if (hands != null) hands.SetActive(!active);
            if (eyeCamera == null) return;

            if (active)
            {
                eyeCamera.cullingMask |= 1 << PlayerBodyLayer;
            }
            else
            {
                eyeCamera.cullingMask &= ~(1 << PlayerBodyLayer);

                // Hand the camera back to the rig exactly where it was, or head bob starts from a
                // stale offset and the view sits permanently askew.
                eyeCamera.transform.localPosition = restPosition;
                eyeCamera.transform.localRotation = restRotation;
            }
        }

        private void LateUpdate()
        {
            if (!active || eyeCamera == null || player == null) return;

            // Runs after PlayerHeadMotion (execution order 100) so it overrides bob rather than
            // being overwritten by it.
            Vector3 pivot = player.transform.position + Vector3.up * pivotHeight;
            Vector3 back = pivot - player.transform.forward * distance + player.transform.right * 0.5f;

            eyeCamera.transform.position = back;
            eyeCamera.transform.rotation = Quaternion.LookRotation(pivot - back, Vector3.up);
        }
    }
}
