using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The physical delivery carton (#30, #31).
    ///
    /// <para>
    /// <b>It is a <see cref="Carryable"/>, and that is the whole of #30's "a carton is heavy".</b>
    /// There is no weight system and there does not need to be one: §2.6 makes hands the scarce
    /// resource, so a box that takes one of the three slots is a box you cannot carry while also
    /// carrying a vial and a printout. Walking a delivery in is therefore several trips, and each of
    /// them is a trip not spent at an instrument.
    /// </para>
    ///
    /// <para>
    /// <b>It is also an <see cref="IVialSlots"/>, and that is #31's "one at a time".</b> Opening the
    /// box builds a vial in each of its holes; taking one out is the ordinary
    /// <see cref="LabCommandKind.TakeVial"/> aimed at that vial, exactly as it is for a rack. There is
    /// deliberately no "empty the carton" action — a command that moved five samples at once would be
    /// the teleporting content #30 exists to remove, one layer down.
    /// </para>
    ///
    /// <para>
    /// Cutting the tape lives on <see cref="CartonLid"/>, not here, for the reason
    /// <see cref="MachineActionButton"/> is separate from <see cref="MachineStation"/>: one is a tap
    /// and one is a hold, and <see cref="Interactable.HoldSeconds"/> is a property of the thing you
    /// are looking at rather than of what you happen to be holding.
    /// </para>
    /// </summary>
    public sealed class CartonProp : Carryable, IVialSlots
    {
        [Tooltip("Parent for the generated vial holes. Built at contentsOrigin if left empty.")]
        [SerializeField] private Transform slotRoot;

        [Tooltip("Where the delivery note sits while it is still in the box. Built at noteOrigin " +
                 "if left empty.")]
        [SerializeField] private Transform noteSocket;

        [Tooltip("Local position of the vial holes when slotRoot is not wired. Sits on the box's " +
                 "top face, because the greybox carton is solid geometry rather than a shell.")]
        [SerializeField] private Vector3 contentsOrigin = new(0f, 0.26f, -0.05f);

        [Tooltip("Local position of the note when noteSocket is not wired.")]
        [SerializeField] private Vector3 noteOrigin = new(0f, 0.262f, 0.10f);

        [Tooltip("The lid. Swung out of the way when the carton is opened.")]
        [SerializeField] private Transform lid;

        [SerializeField] private int columns = 3;
        [SerializeField] private float slotSpacing = 0.1f;

        [Tooltip("Local position the lid takes once the carton is open.")]
        [SerializeField] private Vector3 openLidPosition = new(0f, 0.02f, -0.24f);

        [Tooltip("Local rotation the lid takes once the carton is open.")]
        [SerializeField] private Vector3 openLidEuler = new(-115f, 0f, 0f);

        private readonly List<Transform> slots = new();

        private string jobNumber = "—";
        private string sender = "an unnamed sender";

        private Vector3 closedLidPosition;
        private Quaternion closedLidRotation;
        private bool lidPoseCaptured;
        private bool shownOpen;

        /// <summary>What <c>SampleLocation.InCrate</c> names this box. Null until <see cref="Bind"/>.</summary>
        public string CartonId { get; private set; }

        public override string DisplayName => $"Carton {jobNumber}";

        public override string InspectionText =>
            $"CARTON {jobNumber}\nFrom {sender}";

        /// <summary>
        /// The host's record of this box, or null on a process that has no lab. Resolved per access
        /// rather than cached, for the reason <see cref="MachineStation.Machine"/> is.
        /// </summary>
        public Carton Carton => Bay?.Find(CartonId);

        private static DeliveryBay Bay
        {
            get
            {
                var lab = LabRuntime.Instance != null ? LabRuntime.Instance.Lab : null;
                return lab != null ? lab.Deliveries : null;
            }
        }

        /// <summary>
        /// Give the box its id and announce it. Separate from <c>OnEnable</c> for the reason
        /// <see cref="DropSpot.Bind"/> is: <c>Instantiate</c> runs the lifecycle before the spawner
        /// can hand the id over, so the first registration cannot come from there.
        /// </summary>
        public void Bind(string cartonId, string job, string senderName)
        {
            CartonId = cartonId;
            jobNumber = string.IsNullOrEmpty(job) ? "—" : job;
            sender = string.IsNullOrEmpty(senderName) ? "an unnamed sender" : senderName;
            name = $"Carton_{jobNumber}";
            Register();
        }

        private void OnEnable() => Register();

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(CartonId)) LabRuntime.ForgetFixture(CartonId, transform);
        }

        /// <summary>
        /// Announce the box, and its holes <b>only once it is open</b>.
        /// <para>
        /// That distinction is load-bearing. <c>PropSockets</c> turns a sample's
        /// <c>InCrate(cartonId, n)</c> into a socket by asking <see cref="LabRuntime.SlotsFor"/>, so a
        /// sealed carton that advertised its slots would have every vial in it parented into the box
        /// and visible through the lid — including on a continued run, where <c>RestoreProps</c> walks
        /// exactly that path. Registering the position without the slots means a sealed box is a place
        /// the host can measure your distance to and nothing else.
        /// </para>
        /// </summary>
        private void Register()
        {
            if (string.IsNullOrEmpty(CartonId)) return;

            var carton = Carton;
            if (carton != null && !carton.IsSealed) LabRuntime.RegisterFixture(CartonId, transform, this);
            else LabRuntime.RegisterFixture(CartonId, transform);
        }

        private void Update()
        {
            var carton = Carton;
            if (carton == null) return;

            if (!carton.IsSealed && !shownOpen) Reveal(carton);

            // AttachTo re-enables every collider under this object when the box is set down, which
            // would put the swung-back lid back in front of the vials. Cheap to re-assert, and the
            // alternative is a lid you can target but not use standing between the ray and the oil.
            if (shownOpen) DisableLid();
        }

        // -- Opening ------------------------------------------------------------------------------------

        /// <summary>
        /// Show what is in the box. Called the frame after the host agrees to the open, and again on
        /// any process that finds an already-open carton — a continued run, or a second player's.
        /// <para>
        /// <b>Nothing here touches the oil.</b> A vial comes out of a carton exactly as cold and
        /// exactly as settled as it went in; <c>MachineStation</c>'s load hold is where §4.5 is paid
        /// (#31). The only fields read below are the ones the label already shows.
        /// </para>
        /// </summary>
        private void Reveal(Carton carton)
        {
            shownOpen = true;

            PoseLid(open: true);
            DisableLid();

            // The holes exist now, so the box becomes a container a location can resolve into.
            LabRuntime.RegisterFixture(CartonId, transform, this);

            var runtime = LabRuntime.Instance;
            if (runtime == null) return;

            for (int i = 0; i < carton.Contents.Count; i++)
            {
                var sample = runtime.SampleFor(carton.Contents[i]);
                if (sample == null) continue;

                // Only what is still in this box. A vial somebody already carried out has a location
                // that says so, and re-spawning it here would be a second bottle for one sample.
                if (sample.Location.Kind != SampleLocationKind.InCrate) continue;
                if (sample.Location.ContainerId != CartonId) continue;

                int index = sample.Location.SlotIndex;
                runtime.SpawnVial(sample, Slot(index < 0 ? FreeSlot() : index));
            }

            runtime.SpawnNote(carton, NoteSocket);
        }

        /// <summary>
        /// Where the paper sits. Built on demand rather than required in the prefab, for the reason
        /// <see cref="SampleRack"/> builds its holes lazily: a socket is a transform at an offset, and
        /// a component that only works when somebody remembered to wire an empty <c>GameObject</c> is
        /// a component that will one day be dropped into a scene and silently do nothing.
        /// </summary>
        private Transform NoteSocket
        {
            get
            {
                if (noteSocket != null) return noteSocket;

                var go = new GameObject("NoteSocket");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = noteOrigin;
                noteSocket = go.transform;
                return noteSocket;
            }
        }

        /// <inheritdoc cref="NoteSocket"/>
        private Transform SlotRoot
        {
            get
            {
                if (slotRoot != null) return slotRoot;

                var go = new GameObject("Slots");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = contentsOrigin;
                slotRoot = go.transform;
                return slotRoot;
            }
        }

        private void PoseLid(bool open)
        {
            if (lid == null) return;

            if (!lidPoseCaptured)
            {
                closedLidPosition = lid.localPosition;
                closedLidRotation = lid.localRotation;
                lidPoseCaptured = true;
            }

            lid.localPosition = open ? openLidPosition : closedLidPosition;
            lid.localRotation = open ? Quaternion.Euler(openLidEuler) : closedLidRotation;
        }

        private void DisableLid()
        {
            if (lid == null) return;

            var colliders = lid.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].enabled) colliders[i].enabled = false;
            }
        }

        // -- IVialSlots ---------------------------------------------------------------------------------
        //
        // Grows on demand, like the old delivery crate rather than like a rack: how many vials a firm
        // sent this morning is the contract's business and §10 scales it per day.

        public Transform Slot(int index)
        {
            if (index < 0) index = 0;

            while (slots.Count <= index)
            {
                int i = slots.Count;
                var go = new GameObject($"Slot_{i:D2}");
                go.transform.SetParent(SlotRoot, false);
                go.transform.localPosition = new Vector3(
                    (i % columns - (columns - 1) * 0.5f) * slotSpacing,
                    0f,
                    (i / columns) * slotSpacing);
                slots.Add(go.transform);
            }
            return slots[index];
        }

        public int FreeSlot()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (VialSlot.Occupant(slots[i]) == null) return i;
            }
            return slots.Count;   // never full: Slot() will build the next one
        }

        public int SlotOf(Transform prop) => VialSlot.IndexOf(slots, prop);

        // -- Interaction --------------------------------------------------------------------------------

        /// <summary>Vials the host still says are in this box, or 0 on a process with no lab.</summary>
        private int Remaining
        {
            get
            {
                var bay = Bay;
                var carton = bay?.Find(CartonId);
                return carton == null ? 0 : bay.RemainingIn(carton);
            }
        }

        /// <summary>True when the only thing left to do with this box is flatten it (#31).</summary>
        private bool Spent
        {
            get
            {
                var carton = Carton;
                return carton != null && !carton.IsSealed && !carton.NoteIsInside && Remaining == 0;
            }
        }

        public override string Prompt(PlayerInteractor player)
        {
            var carton = Carton;
            if (carton == null) return DisplayName;

            if (Spent) return $"Flatten carton {jobNumber}";

            if (!carton.IsSealed && carton.NoteIsInside && Remaining == 0)
                return $"Carton {jobNumber} — empty. The delivery note is still in it.";

            if (!player.InventoryHasSpace) return "Inventory full";

            if (carton.IsSealed) return $"Take carton {jobNumber} — {sender}";

            int left = Remaining;
            return $"Take carton {jobNumber} ({left} vial{(left == 1 ? "" : "s")} still in it)";
        }

        public override bool CanInteract(PlayerInteractor player)
        {
            var carton = Carton;
            if (carton == null) return false;
            if (Spent) return true;
            if (!carton.IsSealed && carton.NoteIsInside && Remaining == 0) return false;
            return player.InventoryHasSpace;
        }

        /// <summary>
        /// Pick the box up, or flatten it once there is nothing in it. Both are requests: with four
        /// players in the room, two of them reaching for the same carton is a race the host settles,
        /// exactly as it does for a vial.
        /// </summary>
        public override void Interact(PlayerInteractor player)
        {
            var carton = Carton;
            if (carton == null) return;

            if (Spent)
            {
                LabCommands.Attempt(player, LabCommand.DiscardCarton(CartonId), _ =>
                {
                    player.Say($"Carton {jobNumber} flattened.", 2f);
                    LabRuntime.Instance?.RetireCarton(CartonId);
                });
                return;
            }

            LabCommands.Attempt(player, LabCommand.TakeCarton(CartonId), _ => player.TryCarry(this));
        }
    }
}
