using UnityEngine;
using UnityEngine.InputSystem;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// First-person movement and look. No visible body (§2.6) — hands and held items come later.
    /// <para>
    /// Owns nothing but its own transform and look direction, deliberately: §3.1 says that is the
    /// only thing a client will be authoritative over, so keeping game state out of here means M4
    /// needs no rework of the controller.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private InputActionAsset inputAsset;
        [SerializeField] private Camera eyeCamera;

        [Tooltip("Renders held items only, with a near clip tight enough that they cannot poke " +
                 "through walls. Standard FPS setup (§2.6).")]
        [SerializeField] private Camera heldItemCamera;

        [Header("Dimensions")]
        [Tooltip("§2.1 fixes this at 1.7 m. Bench height is 0.9 m and reads correctly against it, " +
                 "so if movement feels wrong the fix is the controller, never the scale.")]
        [SerializeField] private float eyeHeight = 1.7f;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 3.2f;
        [SerializeField] private float sprintSpeed = 5.0f;
        [SerializeField] private float acceleration = 14f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float lookSensitivity = 0.075f;
        [SerializeField] private float maxPitch = 85f;

        private CharacterController controller;
        private InputAction moveAction, lookAction, sprintAction;
        private Vector3 planarVelocity;
        private float verticalVelocity;
        private float pitch;

        public Camera EyeCamera => eyeCamera;
        public Camera HeldItemCamera => heldItemCamera;
        public InputActionAsset InputAsset => inputAsset;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();

            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            if (eyeCamera != null)
                eyeCamera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);

            BindActions();
        }

        private void BindActions()
        {
            if (inputAsset == null)
            {
                Debug.LogError("[PlayerController] No InputActionAsset assigned; movement is dead.", this);
                return;
            }

            var map = inputAsset.FindActionMap("Player", throwIfNotFound: false);
            if (map == null)
            {
                Debug.LogError("[PlayerController] InputActionAsset has no 'Player' action map.", this);
                return;
            }

            moveAction = map.FindAction("Move", throwIfNotFound: false);
            lookAction = map.FindAction("Look", throwIfNotFound: false);
            sprintAction = map.FindAction("Sprint", throwIfNotFound: false);
            map.Enable();
        }

        private void OnEnable() => SetCursorLocked(true);

        private void OnDisable() => SetCursorLocked(false);

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Update()
        {
            if (Cursor.lockState == CursorLockMode.Locked) ApplyLook();
            ApplyMovement();
        }

        private void ApplyLook()
        {
            if (lookAction == null || eyeCamera == null) return;

            Vector2 delta = lookAction.ReadValue<Vector2>() * lookSensitivity;

            transform.Rotate(Vector3.up, delta.x, Space.World);

            pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
            eyeCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            if (heldItemCamera != null)
                heldItemCamera.transform.localRotation = eyeCamera.transform.localRotation;
        }

        private void ApplyMovement()
        {
            Vector2 input = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
            bool sprinting = sprintAction != null && sprintAction.IsPressed();

            Vector3 wish = transform.right * input.x + transform.forward * input.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float speed = sprinting ? sprintSpeed : walkSpeed;
            planarVelocity = Vector3.MoveTowards(
                planarVelocity, wish * speed, acceleration * Time.deltaTime);

            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            var motion = planarVelocity + Vector3.up * verticalVelocity;
            controller.Move(motion * Time.deltaTime);
        }
    }
}
