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
    public sealed class PlayerInteractor : MonoBehaviour, ILabActor, ILabInventoryActor
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Transform carrySocket;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private ItemInspectionView inspection;

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

        // The shake duration used to live here. It belongs to the instrument now — see
        // MachineStation.loadHoldSeconds — because that is where the seconds are actually spent.

        private InputAction interactAction;
        private InputAction agitateAction;
        private InputAction dropAction;

        private float holdElapsed;

        /// <summary>The selected inventory item—the only item currently presented in the hands.</summary>
        public Carryable Carried => Inventory != null ? Inventory.Selected : null;

        /// <summary>
        /// The three slots, resolved on demand rather than only in <c>Awake</c>.
        /// <para>
        /// Lazy for the same reason <see cref="Terminal"/> is: this component is reached before its
        /// <c>Awake</c> has run. A component added to a deactivated <c>GameObject</c> does not get one
        /// until the object is enabled, and until then a field assigned in <c>Awake</c> is null — so
        /// <see cref="TryCarry"/> refused every pickup and returned false with nothing logged, which
        /// is the quietest possible way for a player to be unable to hold anything.
        /// </para>
        /// Awake still does the wiring, because it is also where the hand socket is handed over; this
        /// only makes reading it safe before that point.
        /// </summary>
        public PlayerInventory Inventory
        {
            get
            {
                if (inventory != null) return inventory;

                inventory = GetComponent<PlayerInventory>();
                if (inventory == null) inventory = gameObject.AddComponent<PlayerInventory>();
                inventory.Initialize(CarrySocket);
                return inventory;
            }
        }

        public ItemInspectionView Inspection => inspection;
        public bool InventoryHasSpace => Inventory != null && Inventory.HasSpace;

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
        private bool screensResolved;

        /// <summary>This player's terminal view, or null if they carry none.</summary>
        public TerminalScreen Terminal
        {
            get { ResolveScreens(); return terminal; }
        }

        private void ResolveScreens()
        {
            if (screensResolved) return;
            screensResolved = true;

            // Inactive included: a replica's screens are switched off, and the only thing that would
            // do with the reference is a station this player can never reach anyway.
            terminal = GetComponentInChildren<TerminalScreen>(includeInactive: true);
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
            if (inventory == null) inventory = GetComponent<PlayerInventory>();
            if (inventory == null) inventory = gameObject.AddComponent<PlayerInventory>();
            inventory.Initialize(CarrySocket);

            if (inspection == null) inspection = GetComponent<ItemInspectionView>();
            if (inspection == null) inspection = gameObject.AddComponent<ItemInspectionView>();
            inspection.Initialize(player, this);

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
            dropAction = map.FindAction("Drop", throwIfNotFound: false);
            map.Enable();

            if (dropAction == null)
            {
                Debug.LogError(
                    "[PlayerInteractor] The 'Player' map has no 'Drop' action, so nothing can be set " +
                    "down and an item picked up is held for the rest of the run. Add it to " +
                    "InputSystem_Actions.", this);
            }

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
            if (inspection != null && inspection.IsOpen) return;

            TickInventoryInput();
            AcquireTarget();
            TickDrop();
            TickInteract();
            TickUseInHand();

            if (Toast != null && Time.time > toastUntil) Toast = null;
        }

        private void TickInventoryInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || inventory == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) SelectInventorySlot(0);
            else if (keyboard.digit2Key.wasPressedThisFrame) SelectInventorySlot(1);
            else if (keyboard.digit3Key.wasPressedThisFrame) SelectInventorySlot(2);

            if (keyboard.spaceKey.wasPressedThisFrame && Carried != null && inspection != null)
                inspection.Open(Carried);
        }

        public void SelectInventorySlot(int index)
        {
            if (inventory == null) return;
            var next = inventory.ItemAt(index);
            if (next == null)
            {
                LabCommands.Attempt(this, LabCommand.SelectInventory(LabGrip.Empty),
                    _ => inventory.Select(index));
                return;
            }

            var grip = GripFor(next);
            LabCommands.Attempt(this, LabCommand.SelectInventory(grip), _ => inventory.Select(index));
        }

        public void RefreshInventoryPresentation()
        {
            if (inventory != null) inventory.RefreshPresentation();
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

                return GripFor(Carried);
            }
        }

        /// <summary>
        /// Ignored on purpose. This actor's hands <i>are</i> <see cref="Carried"/>, and that changes
        /// in the callback the command came back through — so writing it here would either duplicate
        /// that or race it.
        /// </summary>
        public void SetGrip(LabGrip grip) { }

        public int InventoryCapacity => PlayerInventory.SlotCount;
        public int InventoryCount => inventory != null ? inventory.Count : 0;

        public bool ContainsGrip(LabGrip grip)
        {
            if (inventory == null) return false;
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
                if (GripFor(inventory.ItemAt(i)) == grip) return true;
            return false;
        }

        // Accepted callbacks own local props. The zero-hop path reaches this before that callback.
        public void StoreGrip(LabGrip grip) { }

        public bool SelectGrip(LabGrip grip)
        {
            if (inventory == null) return false;
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                if (GripFor(inventory.ItemAt(i)) != grip) continue;
                inventory.Select(i);
                return true;
            }
            return false;
        }

        private static LabGrip GripFor(Carryable item) => item switch
        {
            VialProp vial => LabGrip.OnVial(vial.SampleId),
            PrintoutProp slip => LabGrip.OnSlip(slip.SampleId, slip.Ticket),
            SolventBottle bottle => LabGrip.OnBottle(bottle.BottleId),
            CartonProp carton => LabGrip.OnCarton(carton.CartonId),
            DeliveryNoteProp note => LabGrip.OnNote(note.CartonId),
            ReferenceBook book => LabGrip.OnBookItem(book.InventoryId),
            null => LabGrip.Empty,
            _ => LabGrip.OnBook
        };

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

        // -- Using what is in your hands ---------------------------------------------------------------

        /// <summary>
        /// Use the carried item where it stands: open a manual, glance at a slip.
        /// <para>
        /// <b>Shaking a vial used to live here and no longer does.</b> Preparing a sample was a hold
        /// on this button, separate from the press that put the vial into the instrument — so the
        /// player performed two inputs, in an order nothing told them about, and discovered the first
        /// one only when an instrument refused the bottle. The seconds §4.5 and §9 require are still
        /// spent, but they are spent holding Interact at the instrument, where the action is and where
        /// the progress ring is already pointed. See <see cref="MachineStation.HoldSeconds"/>.
        /// </para>
        /// A vial therefore has nothing to do in the hand any more, which is why there is no branch
        /// for one here.
        /// </summary>
        private void TickUseInHand()
        {
            if (agitateAction == null || Carried == null) return;
            if (Carried is VialProp) return;

            if (agitateAction.WasPressedThisFrame()) Carried.UseInHand(this);
        }

        // -- Dropping ------------------------------------------------------------------------------

        /// <summary>
        /// Set the selected item down where the player is looking, or at their feet.
        /// <para>
        /// Deliberately its own action rather than Interact. Interact is aimed at something that has
        /// agreed to take the item — a rack, an instrument, a tray — and the entire point of a drop is
        /// that there is nothing there to agree. Bound as a real action rather than read off the
        /// keyboard so the rebinding screen can see it (#45).
        /// </para>
        /// Runs after <see cref="AcquireTarget"/> so the aim it resolves against is this frame's,
        /// which is the same ray the crosshair is drawn from. Resolving it a frame late would let the
        /// item land somewhere the player was no longer looking.
        /// </summary>
        private void TickDrop()
        {
            if (dropAction == null || Carried == null) return;
            if (!dropAction.WasPressedThisFrame()) return;

            ItemDrop.Attempt(this);
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
            if (item == null || Inventory == null || !Inventory.HasSpace) return;

            var command = item switch
            {
                VialProp vial => LabCommand.TakeVial(vial.SampleId),
                PrintoutProp slip => LabCommand.TakeSlip(slip.Ticket),
                SolventBottle bottle => LabCommand.TakeBottle(bottle.BottleId),
                CartonProp carton => LabCommand.TakeCarton(carton.CartonId),
                DeliveryNoteProp note => LabCommand.TakeDeliveryNote(note.CartonId),
                ReferenceBook book => LabCommand.TakeBook(book.InventoryId),
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
            if (item == null || Inventory == null || !Inventory.Add(item)) return false;

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
            return Inventory != null ? Inventory.RemoveSelected() : null;
        }
    }
}
