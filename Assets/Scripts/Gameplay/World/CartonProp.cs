using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
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
    /// <b>It reads a <see cref="CartonPlacement"/>, not a <see cref="Carton"/>.</b> A client has no
    /// <c>LabState</c> and never will (<see cref="LabRuntime.SimulatesLocally"/>), so a prop that held
    /// the host's live box found nothing on a joined client and switched itself off — no prompt, no
    /// lid to cut, no way to reach the day's samples at all (#80). Going through
    /// <see cref="CartonFeed.TryFind"/> means the same code draws a host's own box and a replicated
    /// snapshot of one, which is the argument <see cref="MachineStation"/> makes for instruments. The
    /// single remaining host branch is <see cref="RevealHostContents"/>, and it is there because a
    /// client's bottles arrive by a different road.
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

        /// <summary>
        /// Who sent it, or empty when nobody said. Empty rather than a stand-in phrase, for the
        /// reason <see cref="DeliveryNoteProp"/> gives (#55): each case is a whole sentence in the
        /// string table instead of a bracket a translator cannot move.
        /// </summary>
        private string sender = string.Empty;

        private Vector3 closedLidPosition;
        private Quaternion closedLidRotation;
        private bool lidPoseCaptured;
        private bool shownOpen;

        /// <summary>What <c>SampleLocation.InCrate</c> names this box. Null until <see cref="Bind"/>.</summary>
        public string CartonId { get; private set; }

        public override string DisplayName => PromptStrings.CartonName.Format(("job", jobNumber));

        public override string InspectionText => string.IsNullOrEmpty(sender)
            ? PromptStrings.CartonInspectionUnnamed.Format(("job", jobNumber))
            : PromptStrings.CartonInspection.Format(("job", jobNumber), ("sender", sender));

        /// <summary>
        /// This box as whatever this process can see of it. Resolved per access rather than cached,
        /// for the reason <see cref="MachineStation.Machine"/> is: on a client the bay is in the room
        /// before <c>LabNetwork</c> spawns, so a prop that latched an answer at startup would stay
        /// dead for the whole session.
        /// </summary>
        public bool TryState(out CartonPlacement box) => CartonFeed.TryFind(CartonId, out box);

        /// <summary>
        /// Give the box its id and announce it. Separate from <c>OnEnable</c> for the reason
        /// <see cref="DropSpot.Bind"/> is: <c>Instantiate</c> runs the lifecycle before the spawner
        /// can hand the id over, so the first registration cannot come from there.
        /// </summary>
        public void Bind(string cartonId, string job, string senderName)
        {
            CartonId = cartonId;
            jobNumber = string.IsNullOrEmpty(job) ? "—" : job;
            sender = senderName ?? string.Empty;
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
        /// exactly that path, and on a client, where <see cref="VialReconciler"/> does. Registering the
        /// position without the slots means a sealed box is a place the host can measure your distance
        /// to and nothing else.
        /// </para>
        /// </summary>
        private void Register()
        {
            if (string.IsNullOrEmpty(CartonId)) return;

            if (TryState(out var box) && !box.IsSealed) LabRuntime.RegisterFixture(CartonId, transform, this);
            else LabRuntime.RegisterFixture(CartonId, transform);
        }

        private void Update()
        {
            if (!TryState(out var box)) return;

            if (!box.IsSealed && !shownOpen) Reveal(box);

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
        private void Reveal(in CartonPlacement box)
        {
            shownOpen = true;

            PoseLid(open: true);
            DisableLid();

            // The holes exist now, so the box becomes a container a location can resolve into. On a
            // client that is the whole of "the bottles appear": each one's record already names this
            // carton, and VialReconciler can finally turn that into a socket.
            LabRuntime.RegisterFixture(CartonId, transform, this);

            var runtime = LabRuntime.Instance;
            if (runtime == null) return;

            RevealHostContents(runtime);

            runtime.SpawnNote(CartonId, box.JobNumber, box.SenderName,
                              DeliveryNoteProp.Printed(box.Note), NoteSocket);
        }

        /// <summary>
        /// Build the bottles this box still holds, on the process that knows what they are.
        /// <para>
        /// The one host branch left in this component, and it is here because the two sides get their
        /// bottles by different roads rather than because they disagree: a host walks its own
        /// <see cref="Carton.Contents"/>, and a client is told where each bottle is by
        /// <see cref="VialReconciler"/>, which resolves the location through the slots registered
        /// just above. Publishing the manifest so that both could take this path would put the answer
        /// to #32 on the wire — see <c>CartonView</c>.
        /// </para>
        /// </summary>
        private void RevealHostContents(LabRuntime runtime)
        {
            var lab = runtime.Lab;
            var carton = lab != null ? lab.Deliveries.Find(CartonId) : null;
            if (carton == null) return;

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
        }

        /// <summary>
        /// Where the paper sits while it is in the box. Built on demand rather than required in the
        /// prefab, for the reason <see cref="SampleRack"/> builds its holes lazily: a socket is a
        /// transform at an offset, and a component that only works when somebody remembered to wire an
        /// empty <c>GameObject</c> is a component that will one day be dropped into a scene and
        /// silently do nothing.
        /// <para>
        /// Public because <see cref="CartonReconciler"/> has to put the note back in the box when
        /// another player sets it down again, and this socket is deliberately <i>not</i> one of the
        /// vial holes — see <see cref="Carton.InsideSlot"/>.
        /// </para>
        /// </summary>
        public Transform NoteSocket
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

        /// <summary>True when the only thing left to do with this box is flatten it (#31).</summary>
        private static bool Spent(in CartonPlacement box) =>
            !box.IsSealed && !box.NoteIsInside && box.VialsRemaining == 0;

        public override string Prompt(PlayerInteractor player)
        {
            if (!TryState(out var box)) return DisplayName;

            if (Spent(box)) return PromptStrings.CartonFlatten.Format(("job", jobNumber));

            if (!box.IsSealed && box.NoteIsInside && box.VialsRemaining == 0)
                return PromptStrings.CartonEmptyNoteInside.Format(("job", jobNumber));

            if (!player.InventoryHasSpace) return PromptStrings.InventoryFull.Text;

            if (box.IsSealed)
            {
                return string.IsNullOrEmpty(sender)
                    ? PromptStrings.CartonTakeSealedUnnamed.Format(("job", jobNumber))
                    : PromptStrings.CartonTakeSealed.Format(("job", jobNumber), ("sender", sender));
            }

            // One whole sentence per count rather than a stem and an "s": English is the only
            // language in the table so far and it must not be the only one the table can hold.
            int left = box.VialsRemaining;
            return left == 1
                ? PromptStrings.CartonTakeOneVial.Format(("job", jobNumber))
                : PromptStrings.CartonTakeVials.Format(("job", jobNumber), ("count", left));
        }

        public override bool CanInteract(PlayerInteractor player)
        {
            if (!TryState(out var box)) return false;
            if (Spent(box)) return true;
            if (!box.IsSealed && box.NoteIsInside && box.VialsRemaining == 0) return false;
            return player.InventoryHasSpace;
        }

        /// <summary>
        /// Pick the box up, or flatten it once there is nothing in it. Both are requests: with four
        /// players in the room, two of them reaching for the same carton is a race the host settles,
        /// exactly as it does for a vial.
        /// </summary>
        public override void Interact(PlayerInteractor player)
        {
            if (!TryState(out var box)) return;

            if (Spent(box))
            {
                LabCommands.Attempt(player, LabCommand.DiscardCarton(CartonId), _ =>
                {
                    player.Say(PromptStrings.CartonFlattened.Format(("job", jobNumber)), 2f);
                    LabRuntime.Instance?.RetireCarton(CartonId);
                });
                return;
            }

            LabCommands.Attempt(player, LabCommand.TakeCarton(CartonId), _ => player.TryCarry(this));
        }
    }
}
