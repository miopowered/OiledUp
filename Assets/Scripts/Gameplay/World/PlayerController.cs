using Residue.Gameplay.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// First-person movement. Grounded and weighty on purpose: you are a technician crossing a room
    /// full of glassware, not a soldier.
    /// <para>
    /// The deliberate acceleration is not flavour. §5.5 makes lab layout the skill ceiling, and that
    /// only works if crossing the room costs something. A frictionless player would make where you
    /// put the centrifuge irrelevant.
    /// </para>
    /// Owns nothing but its own transform and look direction, which is exactly what §3.1 says a
    /// client will be authoritative over — so M4 needs no rework here.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private InputActionAsset inputAsset;

        [Tooltip("Pivot at eye height carrying the pitch. The cameras hang below it via CameraRig, " +
                 "so head bob and eye height never write to the same transform and fight.")]
        [SerializeField] private Transform head;

        [SerializeField] private Camera eyeCamera;

        [Header("Dimensions")]
        [Tooltip("§2.1 fixes this at 1.7 m. Bench height is 0.9 m and reads correctly against it.")]
        [SerializeField] private float standEyeHeight = 1.7f;

        [SerializeField] private float standHeight = 1.8f;
        [SerializeField] private float crouchHeight = 1.15f;
        [SerializeField] private float crouchEyeHeight = 1.05f;
        [SerializeField] private float radius = 0.3f;

        [Header("Speeds")]
        [SerializeField] private float walkSpeed = 3.0f;
        [SerializeField] private float sprintSpeed = 4.6f;
        [SerializeField] private float crouchSpeed = 1.4f;

        [Tooltip("Higher starts you moving sooner. Weight comes from this being modest, not from lag.")]
        [SerializeField] private float groundAcceleration = 16f;

        [SerializeField] private float groundDeceleration = 16f;

        [Tooltip("Air control is deliberately poor: committing to a jump should mean committing.")]
        [SerializeField] private float airAcceleration = 4f;

        [Header("Jump")]
        [Tooltip("Peak height in metres. Low and functional — enough to clear a step, not to vault a bench.")]
        [SerializeField] private float jumpHeight = 0.85f;

        [Tooltip("Gravity while rising. Weaker than the fall value so the arc has a readable hang.")]
        [SerializeField] private float riseGravity = -18f;

        [Tooltip("Gravity while falling. Stronger, so landings are decisive rather than floaty.")]
        [SerializeField] private float fallGravity = -30f;

        [Tooltip("Grace period after walking off an edge during which a jump still works.")]
        [SerializeField] private float coyoteTime = 0.12f;

        [Tooltip("Pressing jump this long before landing still jumps on touchdown.")]
        [SerializeField] private float jumpBuffer = 0.15f;

        [Header("Look")]
        [Tooltip("Authored default only. The live value is GameSettings.LookSensitivity, which this " +
                 "seeds on a profile that has never set one — an Inspector field is unreachable to " +
                 "the person who actually needs to change it.")]
        [SerializeField] private float lookSensitivity = 0.075f;

        [SerializeField] private float maxPitch = 85f;

        private CharacterController controller;
        private InputAction moveAction, lookAction, sprintAction, jumpAction, crouchAction;

        private Vector3 planarVelocity;
        private float verticalVelocity;
        private float pitch;

        private float lastGroundedTime = -99f;
        private float lastJumpPressedTime = -99f;
        private bool crouching;
        private bool wasGrounded = true;
        private int lastKeyboardHorizontalDirection;
        private int lastKeyboardVerticalDirection;

        // -- State other systems read ------------------------------------------------------------

        /// <summary>Horizontal speed in m/s. Drives head bob and the walk cycle.</summary>
        public float PlanarSpeed => planarVelocity.magnitude;

        /// <summary>Fraction of top speed, 0..1.</summary>
        public float SpeedFraction => Mathf.Clamp01(PlanarSpeed / Mathf.Max(0.01f, sprintSpeed));

        public bool IsGrounded { get; private set; }
        public bool IsCrouching => crouching;
        public bool IsSprinting { get; private set; }

        /// <summary>Downward speed at the moment of touchdown, for the landing dip. Consumed by the reader.</summary>
        public float ConsumeLandingImpact()
        {
            float v = pendingLandingImpact;
            pendingLandingImpact = 0f;
            return v;
        }

        private float pendingLandingImpact;

        public Camera EyeCamera => eyeCamera;
        public InputActionAsset InputAsset => inputAsset;
        public float StandEyeHeight => standEyeHeight;
        public float CurrentEyeHeight => crouching ? crouchEyeHeight : standEyeHeight;

        // -- Lifecycle -----------------------------------------------------------------------------

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            controller.height = standHeight;
            controller.radius = radius;
            controller.center = new Vector3(0f, standHeight * 0.5f, 0f);

            // A lab floor is flat, but stepOffset also decides whether you catch on the lip of a
            // bench base or a doorway threshold. Too small and the room feels sticky.
            controller.stepOffset = 0.35f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.02f;

            if (head != null) head.localPosition = new Vector3(0f, standEyeHeight, 0f);

            // Already done at BeforeSceneLoad; called again because this component must not depend
            // on that having happened, and Load is idempotent.
            GameSettings.Load();
            GameSettings.SeedDefaultLookSensitivity(lookSensitivity);

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

            // Before any action is read, so a saved rebind is in force on the first frame rather
            // than after whatever else happens to touch the asset. Overrides are replaced, not
            // accumulated, so a second player spawning in this process is harmless.
            KeyBindings.Load(inputAsset);

            moveAction = map.FindAction("Move", throwIfNotFound: false);
            lookAction = map.FindAction("Look", throwIfNotFound: false);
            sprintAction = map.FindAction("Sprint", throwIfNotFound: false);
            jumpAction = map.FindAction("Jump", throwIfNotFound: false);
            crouchAction = map.FindAction("Crouch", throwIfNotFound: false);
            map.Enable();
        }

        /// <summary>
        /// Whether this controller may grab and release the shared cursor. True for the player at
        /// this keyboard; false for every replica of somebody else.
        /// <para>
        /// The cursor is process-global, and enabling or disabling this component is how the terminal
        /// hands it over — open the screen, controller off, cursor free. That coupling is deliberate
        /// in single player and actively wrong in co-op: a replica is switched off the instant it
        /// spawns, so on a four-player client three of them would fire <c>OnDisable</c> and free the
        /// cursor the owner had just locked. Worse, it recurs — every later join unlocks the mouse of
        /// everyone already in the lab, which reads as "multiplayer breaks the mouse".
        /// </para>
        /// Set by <c>PlayerAvatar</c> before it disables anything, so a replica never gets a vote.
        /// </summary>
        public bool ManagesCursor { get; set; } = true;

        private void OnEnable()
        {
            if (ManagesCursor) SetCursorLocked(true);
        }

        private void OnDisable()
        {
            if (ManagesCursor) SetCursorLocked(false);
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Update()
        {
            if (Cursor.lockState == CursorLockMode.Locked) ApplyLook();

            // Everything below drives the CharacterController, and Unity logs an error per frame if
            // it is asked to move while disabled. It is disabled on purpose between spawning and
            // being placed in the lab (§M4) — looking around while you wait is fine, walking is not,
            // and there is nothing to walk on yet anyway.
            if (!controller.enabled) return;

            UpdateGrounded();
            UpdateCrouch();
            ApplyJump();
            ApplyMovement();
        }

        // -- Look ------------------------------------------------------------------------------------

        private void ApplyLook()
        {
            if (lookAction == null || eyeCamera == null) return;

            // No smoothing: a smoothed FPS look feels like input lag, not weight. Weight belongs in
            // the body, never in the crosshair.
            //
            // Read from GameSettings every frame rather than cached, so dragging the sensitivity
            // slider turns the room under the player's own hand. Judging a look speed from a number
            // is not possible; judging it from the room moving is trivial.
            Vector2 delta = lookAction.ReadValue<Vector2>() * GameSettings.LookSensitivity;

            transform.Rotate(Vector3.up, delta.x, Space.World);

            // Unity's pitch is positive downwards, so the uninverted case subtracts.
            float pitchDelta = GameSettings.InvertLook ? delta.y : -delta.y;
            pitch = Mathf.Clamp(pitch + pitchDelta, -maxPitch, maxPitch);
            if (head != null) head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // -- Ground ----------------------------------------------------------------------------------

        /// <summary>
        /// CharacterController.isGrounded is only true if the last Move pushed into the floor, so it
        /// flickers when standing still. A short spherecast is stable, which matters because coyote
        /// time and the landing dip both key off it.
        /// </summary>
        private void UpdateGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * (radius + 0.05f);
            bool hit = Physics.SphereCast(origin, radius - 0.01f, Vector3.down, out _, 0.12f,
                ~(1 << PlayerInteractor.IgnoreRaycastLayer), QueryTriggerInteraction.Ignore);

            IsGrounded = hit || controller.isGrounded;

            if (IsGrounded)
            {
                lastGroundedTime = Time.time;

                if (!wasGrounded)
                {
                    // Report the impact before it is zeroed, so the camera can dip proportionally.
                    pendingLandingImpact = Mathf.Abs(verticalVelocity);
                }
            }

            wasGrounded = IsGrounded;
        }

        // -- Crouch ----------------------------------------------------------------------------------

        private void UpdateCrouch()
        {
            bool wants = crouchAction != null && crouchAction.IsPressed();

            if (wants == crouching)
            {
                ResizeTowards(crouching ? crouchHeight : standHeight);
                return;
            }

            // Standing up into a bench would push the capsule through it, so refuse if blocked.
            if (!wants && !HasHeadroom(standHeight)) return;

            crouching = wants;
            ResizeTowards(crouching ? crouchHeight : standHeight);
        }

        private void ResizeTowards(float target)
        {
            float h = Mathf.MoveTowards(controller.height, target, 6f * Time.deltaTime);
            controller.height = h;
            controller.center = new Vector3(0f, h * 0.5f, 0f);

            if (head == null) return;

            var p = head.localPosition;
            p.y = Mathf.MoveTowards(p.y, CurrentEyeHeight, 6f * Time.deltaTime);
            head.localPosition = p;
        }

        private bool HasHeadroom(float height)
        {
            Vector3 start = transform.position + Vector3.up * (controller.height - radius);
            return !Physics.SphereCast(start, radius - 0.02f, Vector3.up, out _,
                Mathf.Max(0.01f, height - controller.height),
                ~(1 << PlayerInteractor.IgnoreRaycastLayer), QueryTriggerInteraction.Ignore);
        }

        // -- Jump ------------------------------------------------------------------------------------

        private void ApplyJump()
        {
            if (jumpAction != null && jumpAction.WasPressedThisFrame()) lastJumpPressedTime = Time.time;

            bool buffered = Time.time - lastJumpPressedTime <= jumpBuffer;
            bool coyote = Time.time - lastGroundedTime <= coyoteTime;

            if (!buffered || !coyote || crouching) return;

            // v = sqrt(2gh), using the rise gravity so the authored height is the height you get.
            verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(riseGravity) * jumpHeight);

            lastJumpPressedTime = -99f;
            lastGroundedTime = -99f;
            IsGrounded = false;
            wasGrounded = false;
        }

        // -- Movement --------------------------------------------------------------------------------

        private void ApplyMovement()
        {
            Vector2 input = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
            input = ResolveKeyboardDirectionChanges(input);

            IsSprinting = !crouching && IsGrounded && input.sqrMagnitude > 0.1f &&
                          sprintAction != null && sprintAction.IsPressed();

            float target = crouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;

            Vector3 wish = transform.right * input.x + transform.forward * input.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            Vector3 desired = wish * target;
            bool stopping = wish.sqrMagnitude < 0.01f;

            float rate = IsGrounded
                ? (stopping ? groundDeceleration : groundAcceleration)
                : airAcceleration;

            planarVelocity = Vector3.MoveTowards(planarVelocity, desired, rate * Time.deltaTime);

            // Asymmetric gravity: a softer rise and a harder fall reads as weight without making the
            // jump feel like it never happened.
            float gravity = verticalVelocity > 0f ? riseGravity : fallGravity;

            if (IsGrounded && verticalVelocity <= 0f)
            {
                // Small downward bias keeps the capsule welded to the floor over steps and ramps
                // instead of skipping off them and re-triggering the landing dip.
                verticalVelocity = -2f;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            controller.Move((planarVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
        }

        /// <summary>
        /// A 2DVector composite resolves opposite keys to zero. During an A-to-D transition both
        /// keys are commonly down for a frame (especially while Shift is held), so that default
        /// produces a visible stop. Let the newly pressed key win the overlap; analog controls keep
        /// the value produced by the Input System.
        /// </summary>
        private Vector2 ResolveKeyboardDirectionChanges(Vector2 input)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return input;

            bool left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            bool right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
            bool leftPressed = keyboard.aKey.wasPressedThisFrame ||
                               keyboard.leftArrowKey.wasPressedThisFrame;
            bool rightPressed = keyboard.dKey.wasPressedThisFrame ||
                                keyboard.rightArrowKey.wasPressedThisFrame;
            input.x = ResolveOpposingKeys(input.x, left, right, leftPressed, rightPressed,
                ref lastKeyboardHorizontalDirection);

            bool backward = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed;
            bool forward = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed;
            bool backwardPressed = keyboard.sKey.wasPressedThisFrame ||
                                   keyboard.downArrowKey.wasPressedThisFrame;
            bool forwardPressed = keyboard.wKey.wasPressedThisFrame ||
                                  keyboard.upArrowKey.wasPressedThisFrame;
            input.y = ResolveOpposingKeys(input.y, backward, forward, backwardPressed,
                forwardPressed, ref lastKeyboardVerticalDirection);

            return input;
        }

        private static float ResolveOpposingKeys(float input, bool negative, bool positive,
            bool negativePressed, bool positivePressed, ref int lastDirection)
        {
            if (!negative && !positive)
            {
                lastDirection = 0;
                return input;
            }

            if (negative != positive)
            {
                lastDirection = positive ? 1 : -1;
                return input;
            }

            // Both went down on the exact same input update: preserve the composite's neutral
            // result. Otherwise remember the newly pressed key until the older key is released.
            if (negativePressed && positivePressed)
            {
                lastDirection = 0;
                return input;
            }

            if (negativePressed != positivePressed)
                lastDirection = positivePressed ? 1 : -1;

            return lastDirection != 0 ? lastDirection : input;
        }
    }
}
