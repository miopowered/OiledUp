using Residue.Gameplay.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Raycast interaction from camera centre, 2.5 m, per §2.6. Also carries the one vial the
    /// player can hold.
    /// <para>
    /// Hold-to-act is used wherever the spec calls for a real time cost — agitating, cleaning a
    /// machine. §9 lists "too much reading, not enough doing" as a live risk and requires prep to
    /// be hand-operated tasks rather than menu clicks, so those seconds are the design, not filler.
    /// </para>
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Transform carrySocket;

        [SerializeField] private float range = 2.5f;
        [SerializeField] private LayerMask mask = ~0;

        [Tooltip("Seconds of shaking before a settled sample is homogeneous enough to test.")]
        [SerializeField] private float agitateSeconds = 2.5f;

        private InputAction interactAction;
        private InputAction agitateAction;

        private float holdElapsed;
        private float agitateElapsed;

        public VialProp Carried { get; private set; }
        public Interactable Target { get; private set; }

        /// <summary>0..1 while a hold interaction is in progress, for the HUD ring.</summary>
        public float HoldProgress { get; private set; }

        public string Prompt { get; private set; }
        public bool PromptBlocked { get; private set; }
        public string Toast { get; private set; }

        private float toastUntil;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();

            var map = player != null && player.InputAsset != null
                ? player.InputAsset.FindActionMap("Player", throwIfNotFound: false)
                : null;

            if (map == null)
            {
                Debug.LogError("[PlayerInteractor] No 'Player' action map; interaction is dead.", this);
                return;
            }

            interactAction = map.FindAction("Interact", throwIfNotFound: false);
            agitateAction = map.FindAction("Attack", throwIfNotFound: false);
            map.Enable();

            // Unity's template ships "Interact" with a Hold interaction attached. That is fatal here:
            // with Hold, the action only reaches Performed after its own timeout, so WasPressedThisFrame
            // never fires for a tap and nothing in the lab can be picked up. Hold timing is a property
            // of the interactable (0 s for a tap, 20 s for a flush), not of the binding, so the asset
            // must leave the action raw. Warn loudly rather than fail silently if it comes back.
            if (interactAction != null && !string.IsNullOrEmpty(interactAction.interactions))
            {
                Debug.LogError(
                    $"[PlayerInteractor] The 'Interact' action has interactions [{interactAction.interactions}] " +
                    "configured on it. Clear them in InputSystem_Actions or tap interactions will not fire.",
                    this);
            }
        }

        private void Update()
        {
            AcquireTarget();
            TickInteract();
            TickAgitate();

            if (Toast != null && Time.time > toastUntil) Toast = null;
        }

        public void Say(string message, float seconds = 3.5f)
        {
            Toast = message;
            toastUntil = Time.time + seconds;
        }

        // -- Targeting -----------------------------------------------------------------------------

        private void AcquireTarget()
        {
            var camera = player != null ? player.EyeCamera : null;
            if (camera == null) return;

            Interactable found = null;

            var ray = new Ray(camera.transform.position, camera.transform.forward);
            if (Physics.Raycast(ray, out var hit, range, mask, QueryTriggerInteraction.Collide))
                found = hit.collider.GetComponentInParent<Interactable>();

            // Never target the thing already in your hands.
            if (found != null && Carried != null && found == (Interactable)Carried) found = null;

            if (found != Target)
            {
                if (Target != null) Target.SetTargeted(false);
                Target = found;
                if (Target != null) Target.SetTargeted(true);
                holdElapsed = 0f;
                HoldProgress = 0f;
            }

            if (Target == null)
            {
                Prompt = null;
                PromptBlocked = false;
                return;
            }

            Prompt = Target.Prompt(this);
            PromptBlocked = !Target.CanInteract(this);
        }

        // -- Interact ------------------------------------------------------------------------------

        private void TickInteract()
        {
            if (interactAction == null || Target == null || PromptBlocked)
            {
                holdElapsed = 0f;
                HoldProgress = 0f;
                return;
            }

            float required = Target.HoldSeconds;

            if (required <= 0f)
            {
                if (interactAction.WasPressedThisFrame()) Target.Interact(this);
                return;
            }

            if (interactAction.IsPressed())
            {
                holdElapsed += Time.deltaTime;
                HoldProgress = Mathf.Clamp01(holdElapsed / required);

                if (holdElapsed >= required)
                {
                    holdElapsed = 0f;
                    HoldProgress = 0f;
                    Target.Interact(this);
                }
            }
            else
            {
                holdElapsed = 0f;
                HoldProgress = 0f;
            }
        }

        // -- Agitation -----------------------------------------------------------------------------

        /// <summary>
        /// Shake the carried vial until it is homogeneous. A sample that has stood in a crate has
        /// its heavy particulates on the bottom; testing it unshaken reads low on exactly the wear
        /// metals you care about, so the machines refuse it outright rather than lying.
        /// </summary>
        private void TickAgitate()
        {
            if (Carried == null || agitateAction == null) { agitateElapsed = 0f; return; }

            var lab = LabRuntime.Instance;
            if (lab == null || !lab.Lab.Samples.TryGet(Carried.SampleId, out var sample)) return;
            if (sample.IsSettled) { agitateElapsed = 0f; return; }

            if (!agitateAction.IsPressed()) { agitateElapsed = 0f; return; }

            agitateElapsed += Time.deltaTime;
            HoldProgress = Mathf.Clamp01(agitateElapsed / agitateSeconds);

            if (agitateElapsed < agitateSeconds) return;

            agitateElapsed = 0f;
            HoldProgress = 0f;
            sample.IsSettled = true;
            Say($"{sample.EquipmentTag}: agitated, ready to run.");
        }

        // -- Carrying ------------------------------------------------------------------------------

        public bool TryCarry(VialProp vial)
        {
            if (vial == null || Carried != null) return false;

            Carried = vial;
            vial.AttachTo(carrySocket != null ? carrySocket : transform, interactable: false);

            var lab = LabRuntime.Instance;
            if (lab != null && lab.Lab.Samples.TryGet(vial.SampleId, out var sample))
            {
                sample.Location = Chemistry.SampleLocation.Held(0);
                vial.SetFillFraction(sample.VolumeMl / 100f);
            }
            return true;
        }

        /// <summary>Hand the carried vial over. Caller is responsible for re-parenting it.</summary>
        public VialProp ReleaseCarried()
        {
            var vial = Carried;
            Carried = null;
            return vial;
        }
    }
}
