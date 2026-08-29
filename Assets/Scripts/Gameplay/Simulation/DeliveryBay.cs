using System;
using System.Collections.Generic;
using Residue.Chemistry;
using UnityEngine;

namespace Residue.Gameplay.Simulation
{
    /// <summary>What the bay did on a tick, so <see cref="LabState"/> can announce it.</summary>
    public enum DeliveryEvent
    {
        None,

        /// <summary>The truck is a short way out. Said out loud so a run can be finished first (#30).</summary>
        DueSoon,

        /// <summary>Cartons have been set down in the bay and can be carried in.</summary>
        Arrived,

        /// <summary>The bay is full and the rest of the load is staying on the truck.</summary>
        Held
    }

    /// <summary>
    /// The loading bay and the truck standing at it (#30).
    ///
    /// <para>
    /// <b>Why a delivery is not the start of the day.</b> Vials used to appear in a crate the instant
    /// <see cref="LabState.BeginDay"/> ran, which is the teleporting-content problem the printout work
    /// removed everywhere else: the day's work simply existed, in the room, in reach. Here the
    /// chemistry is still generated at day start — the paperwork has to exist for the job to exist —
    /// but the boxes are on a truck that turns up a fraction of the way into the shift, and every vial
    /// in them has to be carried in by hand. §6.1 wants the queue to outpace your hands; a load landing
    /// while three instruments are running is that pressure as a decision rather than a chore.
    /// </para>
    ///
    /// <para>
    /// <b>The bay is a buffer, and that is what "the bay blocks" means.</b> It has
    /// <see cref="Capacity"/> standing places. The courier fills them, and anything that does not fit
    /// stays on the truck until a place frees — which happens when <i>you</i> carry a box into the lab.
    /// Nothing is destroyed and no delivery is ever cancelled, so a player who ignores the bay all
    /// morning loses no samples; what they lose is the shift time they will have to spend unloading
    /// two deliveries at once later. The tell is a truck still parked outside with boxes on it, plus
    /// the refusal that says so, both shipped here — hard rule 3 does not permit a cost that only
    /// shows up in the ledger.
    /// </para>
    ///
    /// <para>
    /// Plain C# on the host, like everything else under <c>Residue.Gameplay.Simulation</c>. It reads
    /// <see cref="SampleRegistry"/> to answer "is this box empty yet" rather than keeping its own
    /// count, for the reason <c>VialSlot</c> gives: a parallel list is a second set of books, and only
    /// one of the two writers ever updates it.
    /// </para>
    /// </summary>
    public sealed class DeliveryBay
    {
        /// <summary>
        /// Fixture id of the bay's standing area, and therefore the container a carton lives in while
        /// nobody has carried it inside. A constant here rather than on the scene component for the
        /// reason <see cref="SolventStore.StationId"/> is one: the host writes it into a location
        /// record and the world has to resolve it back to a transform.
        /// </summary>
        public const string BayId = "bay";

        /// <summary>
        /// Cartons that fit in the bay at once.
        /// <para>
        /// Four, against a day that peaks at sixteen samples across perhaps five senders. That is
        /// enough that a normal delivery lands in one go and the bay is never in the player's way — it
        /// only fills when a whole load has been left standing, which is exactly the case #30 wants to
        /// have a consequence. A larger number would make the buffer decorative; a smaller one would
        /// make an ordinary morning feel like a fault.
        /// </para>
        /// </summary>
        public const int DefaultCapacity = 4;

        /// <summary>
        /// How far into the shift the truck arrives, as a fraction of the working day.
        /// <para>
        /// A quarter — 75 s into the default 300 s day. It is a fraction rather than a wall-clock time
        /// because <see cref="DayPlan.DaySeconds"/> is balance and may be retuned, and an arrival
        /// pinned to seconds would silently move to a different part of a longer day.
        /// </para>
        /// <para>
        /// A quarter specifically, because the arrival has to land <i>inside</i> work rather than
        /// beside it. Earlier than that and nothing is running yet, so there is no run to choose to
        /// finish and the delivery is just the start of the day with extra steps. Much later and the
        /// remaining shift cannot absorb a full load, which turns "unload now or after this run" into
        /// "unload now or lose the day" — a decision with one answer is not a decision.
        /// </para>
        /// </summary>
        public const float DefaultArrivalShiftFraction = 0.25f;

        /// <summary>
        /// Warning given before the truck pulls in. Long enough to finish a short run and walk over,
        /// which is the whole point of announcing it early rather than on arrival.
        /// </summary>
        public const float DefaultWarningSeconds = 30f;

        private readonly SampleRegistry samples;
        private readonly List<Carton> cartons = new();
        private readonly List<Carton> justArrived = new();

        private float secondsUntilArrival;
        private bool arrivalScheduled;
        private bool warned;
        private bool heldReported;

        public DeliveryBay(SampleRegistry samples)
        {
            this.samples = samples;
        }

        /// <inheritdoc cref="DefaultCapacity"/>
        public int Capacity { get; set; } = DefaultCapacity;

        /// <inheritdoc cref="DefaultArrivalShiftFraction"/>
        public float ArrivalShiftFraction { get; set; } = DefaultArrivalShiftFraction;

        /// <inheritdoc cref="DefaultWarningSeconds"/>
        public float WarningSeconds { get; set; } = DefaultWarningSeconds;

        /// <summary>Every carton the run still knows about, on the road and in the lab alike.</summary>
        public IReadOnlyList<Carton> Cartons => cartons;

        /// <summary>The cartons the last <see cref="DeliveryEvent.Arrived"/> put down.</summary>
        public IReadOnlyList<Carton> JustArrived => justArrived;

        /// <summary>Seconds until the truck pulls in, or 0 once it has.</summary>
        public float SecondsUntilArrival => arrivalScheduled ? Mathf.Max(0f, secondsUntilArrival) : 0f;

        public bool ArrivalScheduled => arrivalScheduled;

        public Carton Find(string cartonId)
        {
            if (string.IsNullOrEmpty(cartonId)) return null;

            for (int i = 0; i < cartons.Count; i++)
            {
                if (string.Equals(cartons[i].Id, cartonId, StringComparison.Ordinal)) return cartons[i];
            }
            return null;
        }

        /// <summary>Cartons the courier has not been able to put down yet.</summary>
        public int OnTheRoadCount => CountAtStage(CartonStage.OnTheRoad);

        /// <summary>Cartons standing in the bay's marked places, waiting to be carried in.</summary>
        public int StandingInBay
        {
            get
            {
                int n = 0;
                for (int i = 0; i < cartons.Count; i++)
                {
                    if (IsStandingInBay(cartons[i])) n++;
                }
                return n;
            }
        }

        /// <summary>
        /// True while there is anything left to unload. The truck is a physical object with a reason
        /// to be there, so it leaves when its reason does.
        /// </summary>
        public bool TruckAtBay => OnTheRoadCount > 0 || StandingInBay > 0;

        /// <summary>Standing places with nothing in them.</summary>
        public int FreeStandings => Mathf.Max(0, Capacity - StandingInBay);

        // -- Arrivals ---------------------------------------------------------------------------------

        /// <summary>
        /// A carton for this delivery, minted at day start and left on the truck. Called by
        /// <see cref="LabState"/> as it generates the morning's paperwork.
        /// </summary>
        internal Carton Book(DeliveryNote note, int day)
        {
            var carton = new Carton(Carton.IdFor(day, note != null ? note.JobNumber : null), note, day);
            cartons.Add(carton);
            return carton;
        }

        /// <summary>Put the clock on the next truck. The day's cartons are already on it.</summary>
        internal void ScheduleArrival(float daySeconds)
        {
            secondsUntilArrival = Mathf.Max(0f, daySeconds) * Mathf.Clamp01(ArrivalShiftFraction);
            arrivalScheduled = true;
            warned = false;
            heldReported = false;
        }

        /// <summary>
        /// Advance the delivery clock and unload whatever will fit.
        /// <para>
        /// Unloading continues after the arrival tick: the courier keeps setting boxes down as places
        /// free, so carrying one carton inside is what lets the next one land. That is the buffer
        /// draining, and it is why a full bay reads as pressure rather than as a lost delivery.
        /// </para>
        /// </summary>
        internal DeliveryEvent Tick(float deltaSeconds)
        {
            var pending = DeliveryEvent.None;

            if (arrivalScheduled)
            {
                secondsUntilArrival -= deltaSeconds;

                if (!warned && secondsUntilArrival <= WarningSeconds)
                {
                    warned = true;

                    // Only worth saying if there is actually something coming. A day with no arrivals
                    // must not announce a truck that will never appear.
                    if (OnTheRoadCount > 0) pending = DeliveryEvent.DueSoon;
                }

                if (secondsUntilArrival > 0f) return pending;
                arrivalScheduled = false;
            }

            // The arrival outranks its own warning on a tick where both land — a heads-up about
            // something that has already happened would be the wrong sentence on screen.
            var unloaded = Unload();
            return unloaded != DeliveryEvent.None ? unloaded : pending;
        }

        /// <summary>
        /// Set down as many cartons as there are places for.
        /// <para>
        /// Ordered by the list, which is generation order, so the same seed drops the same boxes in the
        /// same places — two players on one run have to be able to talk about "the one on the left".
        /// </para>
        /// </summary>
        private DeliveryEvent Unload()
        {
            if (OnTheRoadCount == 0) return DeliveryEvent.None;

            justArrived.Clear();

            for (int i = 0; i < cartons.Count; i++)
            {
                var carton = cartons[i];
                if (carton.Stage != CartonStage.OnTheRoad) continue;

                int standing = FirstFreeStanding();
                if (standing < 0) break;

                carton.Stage = CartonStage.Delivered;
                carton.Location = SampleLocation.OnSurface(BayId, standing);
                justArrived.Add(carton);
            }

            if (justArrived.Count > 0)
            {
                heldReported = false;
                return DeliveryEvent.Arrived;
            }

            if (heldReported) return DeliveryEvent.None;
            heldReported = true;
            return DeliveryEvent.Held;
        }

        private int FirstFreeStanding()
        {
            for (int slot = 0; slot < Capacity; slot++)
            {
                bool taken = false;
                for (int i = 0; i < cartons.Count && !taken; i++)
                {
                    taken = IsStandingInBay(cartons[i]) && cartons[i].Location.SlotIndex == slot;
                }
                if (!taken) return slot;
            }
            return -1;
        }

        private static bool IsStandingInBay(Carton carton) =>
            carton.Stage == CartonStage.Delivered &&
            carton.Location.Kind == SampleLocationKind.OnSurface &&
            string.Equals(carton.Location.ContainerId, BayId, StringComparison.Ordinal);

        private int CountAtStage(CartonStage stage)
        {
            int n = 0;
            for (int i = 0; i < cartons.Count; i++)
            {
                if (cartons[i].Stage == stage) n++;
            }
            return n;
        }

        // -- What is still in a box ---------------------------------------------------------------------

        /// <summary>
        /// Vials still physically inside this carton. Read off <see cref="SampleState.Location"/>
        /// rather than counted down as they come out, so the box and the vials cannot disagree.
        /// </summary>
        public int RemainingIn(Carton carton)
        {
            if (carton == null || samples == null) return 0;

            int n = 0;
            for (int i = 0; i < carton.Contents.Count; i++)
            {
                if (!samples.TryGet(carton.Contents[i], out var sample)) continue;
                if (sample.Location.Kind != SampleLocationKind.InCrate) continue;
                if (!string.Equals(sample.Location.ContainerId, carton.Id, StringComparison.Ordinal)) continue;
                n++;
            }
            return n;
        }

        /// <summary>The carton a sample is sitting in, or null if it is not in one.</summary>
        public Carton CartonHolding(SampleState sample) =>
            sample != null && sample.Location.Kind == SampleLocationKind.InCrate
                ? Find(sample.Location.ContainerId)
                : null;

        // -- Gateways -----------------------------------------------------------------------------------
        //
        // Every one of these is the whole rule for one action, phrased for the player, exactly as
        // SolventStore's are. LabCommandExecutor establishes whose hands are involved and how far away
        // they are standing, and then delegates — it re-implements nothing below.

        /// <summary>Pick a carton up. A box fills your hands the way a vial does (§2.6, #30).</summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryTake(string cartonId, ulong clientId, out string refusal)
        {
            if (!TryReach(cartonId, out var carton, out refusal)) return false;

            if (carton.Location.Kind == SampleLocationKind.Held)
            {
                refusal = carton.Location.HolderClientId == clientId
                    ? "You are already carrying that carton."
                    : "Someone else is carrying that carton.";
                return false;
            }

            carton.Location = SampleLocation.Held(clientId);
            return true;
        }

        /// <summary>Set a carried carton down on a surface.</summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryPutDown(string cartonId, ulong clientId, string surfaceId, int slot,
                               out string refusal)
        {
            if (!TryReach(cartonId, out var carton, out refusal)) return false;
            if (!HeldBy(carton.Location, clientId, "carton", out refusal)) return false;

            carton.Location = SampleLocation.OnSurface(surfaceId, slot);
            return true;
        }

        /// <summary>
        /// Cut the tape. The seconds are spent at the box in a hold, like a flush (#31) — and nothing
        /// about the oil changes here. See the note on <see cref="Carton"/>.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryOpen(string cartonId, ulong clientId, out string refusal)
        {
            if (!TryReach(cartonId, out var carton, out refusal)) return false;

            if (!carton.IsSealed)
            {
                refusal = "That carton is already open.";
                return false;
            }

            // A box you are holding is a box you cannot get both hands into. It also forces the walk
            // #30 is about: the carton has to be put down somewhere before it becomes a source of
            // vials, and where you put it down is a §5.5 layout decision.
            if (carton.Location.Kind == SampleLocationKind.Held)
            {
                refusal = "Set the carton down before opening it.";
                return false;
            }

            carton.IsSealed = false;
            return true;
        }

        /// <summary>Lift the delivery note out. It is paper, and it is #32's evidence.</summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryTakeNote(string cartonId, ulong clientId, out string refusal)
        {
            if (!TryReach(cartonId, out var carton, out refusal)) return false;

            if (carton.IsSealed)
            {
                refusal = "That carton is still sealed.";
                return false;
            }

            if (carton.NoteLocation.Kind == SampleLocationKind.Held)
            {
                refusal = carton.NoteLocation.HolderClientId == clientId
                    ? "You are already carrying that note."
                    : "Someone else is holding that delivery note.";
                return false;
            }

            carton.NoteLocation = SampleLocation.Held(clientId);
            return true;
        }

        /// <summary>Set a carried delivery note down.</summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryPutDownNote(string cartonId, ulong clientId, string surfaceId, int slot,
                                   out string refusal)
        {
            if (!TryReach(cartonId, out var carton, out refusal)) return false;
            if (!HeldBy(carton.NoteLocation, clientId, "note", out refusal)) return false;

            carton.NoteLocation = SampleLocation.OnSurface(surfaceId, slot);
            return true;
        }

        /// <summary>
        /// Flatten an empty box (#31). Refused while anything is still in it — including the note,
        /// because losing the paperwork to a mis-aimed keypress is not a decision anybody made.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryDiscard(string cartonId, ulong clientId, out string refusal)
        {
            if (!TryReach(cartonId, out var carton, out refusal)) return false;

            if (carton.IsSealed)
            {
                refusal = "That carton has not been opened yet.";
                return false;
            }

            int left = RemainingIn(carton);
            if (left > 0)
            {
                refusal = $"There {(left == 1 ? "is still a vial" : $"are still {left} vials")} in that carton.";
                return false;
            }

            if (carton.NoteIsInside)
            {
                refusal = "Take the delivery note out before flattening it.";
                return false;
            }

            if (carton.Location.Kind == SampleLocationKind.Held &&
                carton.Location.HolderClientId != clientId)
            {
                refusal = "Someone else is carrying that carton.";
                return false;
            }

            carton.Stage = CartonStage.Discarded;
            carton.Location = SampleLocation.Consumed();
            return true;
        }

        private bool TryReach(string cartonId, out Carton carton, out string refusal)
        {
            refusal = null;
            carton = Find(cartonId);

            if (carton == null) { refusal = "No such carton."; return false; }

            switch (carton.Stage)
            {
                case CartonStage.OnTheRoad:
                    refusal = "That carton is still on the truck.";
                    return false;
                case CartonStage.Discarded:
                    refusal = "That carton has been flattened.";
                    return false;
                default:
                    return true;
            }
        }

        private static bool HeldBy(SampleLocation location, ulong clientId, string what,
                                   out string refusal)
        {
            refusal = null;
            if (location.Kind == SampleLocationKind.Held && location.HolderClientId == clientId) return true;

            refusal = $"You are not carrying that {what}.";
            return false;
        }

        // -- Housekeeping -------------------------------------------------------------------------------

        /// <summary>
        /// Clear away boxes nobody is using any more, at the one moment the lab is quiescent.
        /// <para>
        /// #31 asks that an opened carton not clutter the lab for ever. A player can flatten one the
        /// moment it is empty, and this catches the ones they did not: an open box with no vials left
        /// and its note out of it is cardboard, and cardboard does not survive the night. A note that
        /// is in somebody's <i>hands</i> spares its box, so nothing is ever pulled out of a grip by
        /// the clock.
        /// </para>
        /// Returns what it flattened, so the scene can retire the props it built for them.
        /// </summary>
        internal List<Carton> SweepSpent()
        {
            var swept = new List<Carton>();

            for (int i = 0; i < cartons.Count; i++)
            {
                var carton = cartons[i];
                if (carton.Stage != CartonStage.Delivered || carton.IsSealed) continue;
                if (RemainingIn(carton) > 0) continue;
                if (carton.NoteLocation.Kind == SampleLocationKind.Held) continue;

                carton.Stage = CartonStage.Discarded;
                carton.Location = SampleLocation.Consumed();
                carton.NoteLocation = SampleLocation.Consumed();
                swept.Add(carton);
            }

            return swept;
        }

        /// <summary>
        /// Rebuild the bay from where a continued run's vials say they are (#49).
        ///
        /// <para>
        /// <b>Derived, not saved.</b> A carton is a grouping and a lid, and both are recoverable from
        /// data the save already carries: a vial's <c>ContainerId</c> names its box, and its
        /// <c>JobNumber</c>, sender, tag and profile rebuild the note that came with it. Adding a
        /// carton table to <see cref="RunSnapshot"/> would be a second copy of facts that are already
        /// on disk, and the two would part company the first time either was written without the
        /// other — the same argument <c>SampleLifecycle</c> makes for deriving the stage.
        /// </para>
        ///
        /// <para>
        /// Everything comes back <b>sealed and standing in the bay</b>, wherever it was left. That is a
        /// deliberate simplification and it is generous rather than costly: the overnight story is that
        /// the bay was tidied, the player is out one hold on a box they had already opened, and no
        /// sample can end up in a container this scene has no prop for. A box with nothing left in it
        /// is not rebuilt at all — it was cardboard by then.
        /// </para>
        /// </summary>
        internal void RebuildFrom(IReadOnlyCollection<SampleState> all)
        {
            cartons.Clear();
            justArrived.Clear();
            arrivalScheduled = false;
            warned = false;
            heldReported = false;

            if (all == null) return;

            // Boxes are named by the vials still in them. A container id nothing points at is a box
            // that no longer exists.
            var byContainer = new Dictionary<string, Carton>(StringComparer.Ordinal);

            foreach (var sample in all)
            {
                if (sample == null) continue;
                if (sample.Location.Kind != SampleLocationKind.InCrate) continue;

                string container = sample.Location.ContainerId;
                if (!Carton.IsCartonId(container) || byContainer.ContainsKey(container)) continue;

                var note = new DeliveryNote(sample.Customer, sample.JobNumber, sample.CollectedDay);
                var carton = new Carton(container, note, sample.CollectedDay)
                {
                    Stage = CartonStage.Delivered,
                    IsSealed = true
                };

                byContainer[container] = carton;
                cartons.Add(carton);
            }

            // Second pass, so a vial already lifted out yesterday still appears on the paper it came
            // under. #32 reconciles the note against the box, and a note that silently lost its lines
            // as the box emptied would make every carton reconcile perfectly.
            foreach (var sample in all)
            {
                if (sample == null || string.IsNullOrEmpty(sample.JobNumber)) continue;

                foreach (var carton in cartons)
                {
                    if (!string.Equals(carton.JobNumber, sample.JobNumber, StringComparison.Ordinal)) continue;

                    carton.Add(sample.Id);
                    carton.Note.Add(sample.EquipmentTag, sample.Profile, sample.Id);
                    break;
                }
            }

            // Stand them in the bay, in id order, so two processes rebuilding the same save lay the
            // bay out the same way.
            cartons.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            for (int i = 0; i < cartons.Count; i++)
            {
                cartons[i].Location = SampleLocation.OnSurface(BayId, i);
                cartons[i].NoteLocation = SampleLocation.InCrate(cartons[i].Id, Carton.InsideSlot);
            }
        }
    }
}
