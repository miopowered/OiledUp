using System.Collections.Generic;
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
    /// <para>
    /// It is also this player's <see cref="ILabActor"/>: the thing the host is answering when a
    /// request arrives from this process. Aiming, hold timing and every prompt are decided here and
    /// never asked of the host — they are advisory, and the executor re-checks whatever they thought
    /// when the request lands (see <see cref="LabCommands"/>).
    /// </para>
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour, ILabActor
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Transform carrySocket;

        /// <summary>
        /// Where a carried item hangs. Exposed so <c>Residue.Net</c> can answer "show me that
        /// player's hands" for a bottle the host says somebody else is holding — see
        /// <see cref="IPlayerHands"/>. Read on replicas too, where this component is disabled but its
        /// transforms are still in the room.
        /// </summary>
        public Transform CarrySocket => carrySocket != null ? carrySocket : transform;

        [SerializeField] private float range = 2.5f;

        [Tooltip("Must exclude the player's own layer.\n\n" +
                 "The eye camera sits INSIDE the CharacterController capsule, so a ray cast from it " +
                 "exits through the capsule's inner surface about 0.12 m out. Physics.Raycast returns " +
                 "that nearest hit, it resolves to no Interactable, and every real target further " +
                 "along the ray is discarded. The player is on the built-in Ignore Raycast layer and " +
                 "this mask omits it.")]
        [SerializeField] private LayerMask mask = ~(1 << IgnoreRaycastLayer);

        /// <summary>Unity's built-in layer 2. Used for the player so it cannot occlude its own aim.</summary>
        public const int IgnoreRaycastLayer = 2;

        [Tooltip("Seconds of shaking before a settled sample is homogeneous enough to test.")]
        [SerializeField] private float agitateSeconds = 2.5f;

        private InputAction interactAction;
        private InputAction agitateAction;

        private float holdElapsed;
        private float agitateElapsed;

        /// <summary>Whatever is in your hands — a vial, a printout, a manual. One at a time.</summary>
        public Carryable Carried { get; private set; }

        /// <summary>The carried item as a vial, or null if you are holding something else.</summary>
        public VialProp CarriedVial => Carried as VialProp;

        public Interactable Target { get; private set; }

        /// <summary>0..1 while a hold interaction is in progress, for the HUD ring.</summary>
        public float HoldProgress { get; private set; }

        public string Prompt { get; private set; }
        public bool PromptBlocked { get; private set; }
        public string Toast { get; private set; }

        // -- This player's own screens ---------------------------------------------------------------
        //
        // Found inside this player rather than pointed at from the scene. With four players in the
        // room there is no such thing as "the" terminal view any more: a station has to open the one
        // belonging to whoever walked up to it, and a book has to open in the hands that are holding
        // it. Resolving from the interacting player is what makes that true without any station
        // knowing how many players exist.
        //
        // Cached because Interactable.Prompt runs every frame you are looking at a station, and a
        // recursive component search per station per frame is a cost with nothing to show for it.
        // Cached even when nothing was found, so a scene that keeps its screens at the root pays for
        // exactly one failed search and then falls back to whatever it wired.

        private TerminalScreen terminal;
        private BookScreen manual;
        private bool screensResolved;

        /// <summary>This player's terminal view, or null if they carry none.</summary>
        public TerminalScreen Terminal
        {
            get { ResolveScreens(); return terminal; }
        }

        /// <summary>This player's reading view for a <see cref="ReferenceBook"/>.</summary>
        public BookScreen Manual
        {
            get { ResolveScreens(); return manual; }
        }

        private void ResolveScreens()
        {
            if (screensResolved) return;
            screensResolved = true;

            // Inactive included: a replica's screens are switched off, and the only thing that would
            // do with the reference is a station this player can never reach anyway.
            terminal = GetComponentInChildren<TerminalScreen>(includeInactive: true);
            manual = GetComponentInChildren<BookScreen>(includeInactive: true);
        }

        // -- Diagnostics -----------------------------------------------------------------------------
        //
        // Exposed so InteractionDebug can draw the EXACT query this component runs. A debug overlay
        // that rebuilds its own ray would diagnose a different raycast than the one misbehaving,
        // which is worse than no overlay at all.

        public float Range => range;
        public LayerMask Mask => mask;

        /// <summary>The ray cast this frame, from camera centre.</summary>
        public Ray LastRay { get; private set; }

        public bool LastHadHit { get; private set; }
        public RaycastHit LastHit { get; private set; }

        /// <summary>
        /// Every collider along the ray, nearest first. Only populated while
        /// <see cref="InteractionDebug.Enabled"/> — RaycastAll allocates, so it stays off by default.
        /// </summary>
        public IReadOnlyList<RaycastHit> LastAllHits => allHits;

        private readonly List<RaycastHit> allHits = new();

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
            if (string.IsNullOrEmpty(message)) return;
            Toast = message;
            toastUntil = Time.time + seconds;
        }

        // -- ILabActor ------------------------------------------------------------------------------
        //
        // What the host is answering when a request comes from this process.

        /// <summary>
        /// NGO gives the host client id 0, and single player is a host with nobody connected — so the
        /// local player is client 0 on both. A remote player's requests are never answered through
        /// this object; the host builds its own actor from that client's session.
        /// </summary>
        public ulong ClientId => 0;

        public bool HasPosition => true;

        public Vector3 Position => transform.position;

        /// <summary>
        /// Derived from what is actually in the hand rather than stored, for the same reason
        /// <c>SampleLifecycle</c> derives a stage: a second copy is a second set of books, and the
        /// prop is the one the player can see.
        /// </summary>
        public LabGrip Grip
        {
            get
            {
                // Unity's == rather than a null pattern, on purpose: a pattern match sees a destroyed
                // prop as a live reference and would dereference it. A slip whose instrument has since
                // reprinted is exactly that case.
                if (Carried == null) return LabGrip.Empty;

                return Carried switch
                {
                    VialProp vial => LabGrip.OnVial(vial.SampleId),
                    PrintoutProp slip => LabGrip.OnSlip(slip.SampleId, slip.Ticket),
                    SolventBottle bottle => LabGrip.OnBottle(bottle.BottleId),
                    _ => LabGrip.OnBook
                };
            }
        }

        /// <summary>
        /// Ignored on purpose. This actor's hands <i>are</i> <see cref="Carried"/>, and that changes
        /// in the callback the command came back through — so writing it here would either duplicate
        /// that or race it.
        /// </summary>
        public void SetGrip(LabGrip grip) { }

        string ILabActor.DisplayName => name;

        // -- Targeting -----------------------------------------------------------------------------

        private void AcquireTarget()
        {
            var camera = player != null ? player.EyeCamera : null;
            if (camera == null) return;

            Interactable found = null;

            var ray = new Ray(camera.transform.position, camera.transform.forward);
            LastRay = ray;

            // Belt and braces alongside the layer mask: if the player is ever moved back onto a
            // raycast layer, ignoring self keeps aiming working instead of silently breaking all of it.
            bool didHit = Physics.Raycast(ray, out var hit, range, mask, QueryTriggerInteraction.Collide)
                          && !IsSelf(hit.collider);

            LastHadHit = didHit;
            LastHit = hit;

            if (didHit) found = hit.collider.GetComponentInParent<Interactable>();

            if (InteractionDebug.Enabled)
            {
                allHits.Clear();
                allHits.AddRange(Physics.RaycastAll(ray, range, mask, QueryTriggerInteraction.Collide));
                allHits.Sort((a, b) => a.distance.CompareTo(b.distance));
            }
            else if (allHits.Count > 0)
            {
                allHits.Clear();
            }

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

        /// <summary>True if the collider belongs to this player rather than to the world.</summary>
        public bool IsSelf(Collider c) => c != null && c.transform.IsChildOf(transform);

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
            if (agitateAction == null) { agitateElapsed = 0f; return; }

            var vial = CarriedVial;
            if (vial == null)
            {
                // Anything that is not a vial gets a tap rather than a hold: open a manual, glance
                // at a slip. A carried item cannot be looked at, so this is its only input route.
                agitateElapsed = 0f;
                if (Carried != null && agitateAction.WasPressedThisFrame()) Carried.UseInHand(this);
                return;
            }

            // A local read, asked every frame, never a request. On a client there is no lab to read
            // and the gate simply opens — the hold still costs its seconds, and the host refuses on
            // arrival if the vial was not shakeable after all.
            var sample = LabRuntime.Instance != null
                ? LabRuntime.Instance.SampleFor(vial.SampleId)
                : null;

            if (sample != null && sample.IsSettled) { agitateElapsed = 0f; return; }

            if (!agitateAction.IsPressed()) { agitateElapsed = 0f; return; }

            // Refuse before the hold rather than after it. §5.1 puts logging ahead of prep, so an
            // unlogged vial cannot be agitated — and spending 2.5 s shaking one only to be told it
            // was never booked in is precisely the kind of unannounced rule §9 forbids. Asked as a
            // pure query so a player leaning on the key does not fill the console.
            if (sample != null)
            {
                var refusal = Chemistry.SampleLifecycle.Refusal(sample, Chemistry.SampleStage.Prepped);
                if (refusal != null)
                {
                    agitateElapsed = 0f;
                    HoldProgress = 0f;
                    Say(refusal);
                    return;
                }
            }

            agitateElapsed += Time.deltaTime;
            HoldProgress = Mathf.Clamp01(agitateElapsed / agitateSeconds);

            if (agitateElapsed < agitateSeconds) return;

            agitateElapsed = 0f;
            HoldProgress = 0f;

            LabCommands.Attempt(this, LabCommand.Agitate(),
                _ => Say($"{(sample != null ? sample.RecordTag : "Sample")}: agitated, ready to run."));
        }

        // -- Carrying ------------------------------------------------------------------------------

        /// <summary>
        /// Ask for something to end up in your hands.
        /// <para>
        /// Taking a vial out of the delivery crate is §5.1's unload step and changes where the host
        /// thinks that sample is, so it is a request rather than a local grab — and with four players
        /// in the room, two of them reaching for the same bottle is a race the host has to settle. The
        /// prop only moves once the answer comes back; §3.1 is explicit that there is no fast-twitch
        /// action here and that simplicity beats responsiveness, which is what makes waiting for the
        /// answer preferable to predicting it and having to take it back.
        /// </para>
        /// A manual is a request too, for one reason: the host tracks whose hands are full, and a
        /// player holding a book must not also be able to claim a vial.
        /// </summary>
        public void Take(Carryable item)
        {
            if (item == null || Carried != null) return;

            var command = item switch
            {
                VialProp vial => LabCommand.TakeVial(vial.SampleId),
                PrintoutProp slip => LabCommand.TakeSlip(slip.Ticket),
                SolventBottle bottle => LabCommand.TakeBottle(bottle.BottleId),
                _ => LabCommand.TakeBook()
            };

            LabCommands.Attempt(this, command, _ => TryCarry(item));
        }

        /// <summary>
        /// Put an item in the hand socket. Purely local: the host has already agreed, or has just
        /// handed the item over itself (a vial coming out of an instrument). Nothing here writes lab
        /// state — <see cref="Take"/> is the door that does.
        /// </summary>
        public bool TryCarry(Carryable item)
        {
            if (item == null || Carried != null) return false;

            Carried = item;
            item.AttachTo(CarrySocket, interactable: false);

            if (item is VialProp vial)
            {
                var sample = LabRuntime.Instance != null
                    ? LabRuntime.Instance.SampleFor(vial.SampleId)
                    : null;

                if (sample != null) vial.SetFillFraction(sample.VolumeMl / VialProp.FullMl);
            }
            return true;
        }

        /// <summary>Hand the carried item over. Caller is responsible for re-parenting or destroying it.</summary>
        public Carryable ReleaseCarried()
        {
            var item = Carried;
            Carried = null;
            return item;
        }
    }
}
