using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The host's answer to every request. One method, one switch, one place where a player action
    /// becomes a change to <see cref="LabState"/>.
    /// <para>
    /// <b>It re-implements no rules.</b> <see cref="SampleLifecycle"/>,
    /// <see cref="MachineInstance.CanAccept"/>, <see cref="LabState.TryStartReferenceRun"/>,
    /// <see cref="Economy.TryBuySolvent"/> and the rest already decide what is legal and already
    /// phrase the refusal for the player. This type's job is to be the thing that calls them on the
    /// server, having first established the two facts a gateway cannot check for itself: that the
    /// player is holding what they claim to be holding, and that they are standing near the thing
    /// they are operating. Everything after that is a delegation.
    /// </para>
    /// <para>
    /// <b>It is the same code on every path.</b> Single player runs it directly, the host runs it for
    /// its own player directly, and a client's request runs it after a hop — see
    /// <see cref="LabCommands"/>. There is deliberately no faster route for the local player, because
    /// a rule that only holds on one path is a rule that will eventually only hold on one path.
    /// </para>
    /// <para>
    /// Hard rule 2 holds here structurally rather than by care: nothing below can reach a
    /// <c>SampleGroundTruth</c>, because the only things that can are methods on
    /// <see cref="SampleRegistry"/> that compute inside the vault and return player-facing results.
    /// </para>
    /// </summary>
    public sealed class LabCommandExecutor
    {
        /// <summary>
        /// How far from a fixture a player may still operate it. Generous against §2.6's 2.5 m
        /// interaction ray on purpose: this exists to refuse an instrument across the room, not to
        /// arbitrate a step backwards between the click and the packet.
        /// </summary>
        public const float DefaultReachMetres = 4f;

        private readonly LabState lab;
        private readonly ILabStations stations;

        public float ReachMetres { get; set; } = DefaultReachMetres;

        public LabCommandExecutor(LabState lab, ILabStations stations = null)
        {
            this.lab = lab;
            this.stations = stations;
        }

        public LabState Lab => lab;

        public LabCommandResult Execute(ILabActor actor, LabCommand command)
        {
            if (actor == null) return LabCommandResult.No(LabStrings.NoSuchPlayer);
            if (lab == null) return LabCommandResult.No(LabStrings.LabNotRunning);

            return command.Kind switch
            {
                LabCommandKind.TakeVial => TakeVial(actor, command),
                LabCommandKind.TakeSlip => TakeSlip(actor, command),
                LabCommandKind.TakeBook => TakeBook(actor, command),
                LabCommandKind.TakeBottle => TakeBottle(actor, command),
                LabCommandKind.TakeCarton => TakeCarton(actor, command),
                LabCommandKind.OpenCarton => OpenCarton(actor, command),
                LabCommandKind.TakeDeliveryNote => TakeDeliveryNote(actor, command),
                LabCommandKind.DiscardCarton => DiscardCarton(actor, command),
                LabCommandKind.PutDown => PutDown(actor, command),
                LabCommandKind.SelectInventory => SelectInventory(actor, command),
                LabCommandKind.Agitate => Agitate(actor),

                LabCommandKind.LoadMachine => LoadMachine(actor, command),
                LabCommandKind.StartRun => StartRun(actor, command),
                LabCommandKind.TakeFromMachine => TakeFromMachine(actor, command),
                LabCommandKind.FlushMachine => FlushMachine(actor, command),
                LabCommandKind.RunBlank => RunBlank(actor, command),
                LabCommandKind.RunReference => RunReference(actor, command),
                LabCommandKind.Calibrate => Calibrate(actor, command),

                LabCommandKind.FillBottle => FillBottle(actor, command),

                LabCommandKind.FileSlip => FileSlip(actor, command),
                LabCommandKind.FileVerdict => FileVerdict(actor, command),
                LabCommandKind.OrderSolvent => OrderSolvent(actor, command),
                LabCommandKind.OrderStandards => OrderStandards(actor, command),
                LabCommandKind.ReopenSuspect => ReopenSuspect(actor, command),
                LabCommandKind.RegisterSample => RegisterSample(actor, command),
                LabCommandKind.CallCustomer => CallCustomer(actor, command),
                LabCommandKind.EndDay => EndDay(actor),
                LabCommandKind.StartNextDay => StartNextDay(actor),

                _ => LabCommandResult.No(LabStrings.CommandNotUnderstood)
            };
        }

        // -- Hands -----------------------------------------------------------------------------------

        /// <summary>
        /// Pick a vial up. Deliberately checks the sample's <i>server-side</i> location rather than
        /// reaching for geometry: a vial's prop is local (§3.2), so where it appears to be on the
        /// asking client proves nothing, whereas <see cref="SampleState.Location"/> is the host's own
        /// record of where it put it.
        /// </summary>
        private LabCommandResult TakeVial(ILabActor actor, LabCommand command)
        {
            if (!CanStore(actor)) return LabCommandResult.No(LabStrings.InventoryFull);
            if (!lab.Samples.TryGet(command.Sample, out var sample))
                return LabCommandResult.No(LabStrings.NoSuchSample);

            // A vial in a box is only reachable once somebody has carried the box in, put it down and
            // cut it open (#30, #31). Checked here rather than left to the prop, because the prop for
            // a sealed carton's contents does not exist on any machine and a request naming one is
            // therefore a request the host has to answer for itself.
            if (!CanReachInCarton(actor, sample, out var sealedOff)) return sealedOff;

            switch (sample.Location.Kind)
            {
                case SampleLocationKind.InMachine:
                    return LabCommandResult.No(LabStrings.VialIsInAnInstrument.Format(
                        ("tag", sample.RecordTag),
                        ("instrument", DisplayNameOf(sample.Location.ContainerId))));

                case SampleLocationKind.Held when sample.Location.HolderClientId != actor.ClientId:
                    return LabCommandResult.No(
                        LabStrings.VialHeldBySomeoneElse.Format(("tag", sample.RecordTag)));

                case SampleLocationKind.Consumed:
                    return LabCommandResult.No(
                        LabStrings.VialIsSpent.Format(("tag", sample.RecordTag)));
            }

            if (!SampleLifecycle.TryMove(sample, SampleLocation.Held(actor.ClientId), out string refusal))
                return LabCommandResult.No(refusal);

            Store(actor, LabGrip.OnVial(sample.Id));
            return LabCommandResult.Yes(sample.Id);
        }

        private LabCommandResult TakeSlip(ILabActor actor, LabCommand command)
        {
            if (!CanStore(actor)) return LabCommandResult.No(LabStrings.InventoryFull);

            if (!lab.Slips.TryGet(command.Amount, out var slip))
                return LabCommandResult.No(LabStrings.SlipAlreadyFiled);

            // Reach is checked against where the paper IS, not against the instrument that printed
            // it. While a slip could only ever sit in the tray it came out of, those were the same
            // place; now that paperwork can be set down anywhere, they are not. Checking the printer
            // leaves a slip lying at your feet on the far side of the room refusing to be picked up,
            // with a refusal naming an instrument you are nowhere near.
            bool inATray = slip.Location.Kind != SampleLocationKind.OnSurface ||
                           string.IsNullOrEmpty(slip.Location.ContainerId);

            string standingAt = inATray ? slip.MachineInstanceId : slip.Location.ContainerId;
            string tooFar = inATray
                ? LabStrings.NotStandingAtInstrument.Format(
                    ("instrument", DisplayNameOf(slip.MachineInstanceId)))
                : LabStrings.NotStandingAtSlip.Text;

            if (OutOfReach(actor, standingAt, tooFar, out var far)) return far;

            if (!lab.Slips.TryClaim(slip.Ticket, actor.ClientId, out string refusal))
                return LabCommandResult.No(refusal);

            Store(actor, LabGrip.OnSlip(slip.Sample, slip.Ticket));
            return LabCommandResult.Ok;
        }

        /// <summary>
        /// Pick a manual up. Nothing in the lab changes; the host records it only so that a player
        /// holding a book is known to have their hands full, and cannot also be holding a vial.
        /// </summary>
        private LabCommandResult TakeBook(ILabActor actor, LabCommand command)
        {
            if (!CanStore(actor)) return LabCommandResult.No(LabStrings.InventoryFull);
            Store(actor, string.IsNullOrEmpty(command.FixtureId)
                ? LabGrip.OnBook
                : LabGrip.OnBookItem(command.FixtureId));
            return LabCommandResult.Ok;
        }

        /// <summary>
        /// Pick a solvent bottle up. Checks the bottle's <i>server-side</i> location for the same
        /// reason <see cref="TakeVial"/> does: the prop is local (§3.2), so where it appears to be on
        /// the asking client proves nothing, and two players reaching for the same bottle is a race
        /// somebody has to settle.
        /// <para>
        /// Reach is checked against the bottle's own container rather than against the bottle, because
        /// a bottle is not a fixture and has no registered position — the cradle it is sitting in
        /// does.
        /// </para>
        /// </summary>
        private LabCommandResult TakeBottle(ILabActor actor, LabCommand command)
        {
            if (!CanStore(actor)) return LabCommandResult.No(LabStrings.InventoryFull);

            var bottle = lab.Solvent.Find(command.FixtureId);
            if (bottle == null) return LabCommandResult.No(LabStrings.NoSuchBottle);

            if (bottle.Location.Kind == SampleLocationKind.OnSurface &&
                OutOfReach(actor, bottle.Location.ContainerId, LabStrings.NotStandingAtShelf, out var far))
            {
                return far;
            }

            if (!lab.Solvent.TryTake(bottle.Id, actor.ClientId, out string refusal))
                return LabCommandResult.No(refusal);

            Store(actor, LabGrip.OnBottle(bottle.Id));
            return LabCommandResult.Ok;
        }

        // -- Deliveries (#30, #31) ---------------------------------------------------------------------
        //
        // Every one of these establishes the two facts a gateway cannot check for itself — whose hands
        // and how far away — and then delegates to DeliveryBay, which owns the rules and phrases its
        // own refusals. Nothing below re-states one.

        /// <summary>
        /// Whether a vial in a carton is reachable at all, and why not if it is not.
        /// <para>
        /// Returns true — "nothing to say" — for a sample that is not in a carton, so
        /// <see cref="TakeVial"/> can call it unconditionally. A sample sitting in an
        /// <c>InCrate</c> location with no carton behind it is left alone too: that is the shape a
        /// freshly generated sample has before <c>LabState</c> packs it, and refusing it here would
        /// make a generator detail into a player-facing refusal.
        /// </para>
        /// </summary>
        private bool CanReachInCarton(ILabActor actor, SampleState sample, out LabCommandResult refused)
        {
            refused = default;

            var carton = lab.Deliveries.CartonHolding(sample);
            if (carton == null) return true;

            if (carton.Stage == CartonStage.OnTheRoad)
            {
                refused = LabCommandResult.No(
                    LabStrings.VialStillOnTheTruck.Format(("tag", sample.RecordTag)));
                return false;
            }

            if (carton.IsSealed)
            {
                refused = LabCommandResult.No(
                    LabStrings.CartonStillSealed.Format(("job", carton.JobNumber)));
                return false;
            }

            // A box in somebody's arms is a box nobody can reach into, including the person carrying
            // it. Stated rather than assumed, because the vial's prop is riding in their hands and
            // would otherwise look perfectly grabbable to a second player.
            if (carton.Location.Kind == SampleLocationKind.Held)
            {
                refused = LabCommandResult.No(
                    carton.Location.HolderClientId == actor.ClientId
                        ? LabStrings.CartonIsInYourArms.Text
                        : LabStrings.CartonCarriedBySomeoneElse.Format(("job", carton.JobNumber)));
                return false;
            }

            // Reach is checked against the box rather than the shelf it is standing on: a carton is a
            // registered fixture in its own right, and the bay is 4 m of floor rather than a point.
            return !OutOfReach(actor, carton.Id, NotAtCarton(carton), out refused);
        }

        private LabCommandResult TakeCarton(ILabActor actor, LabCommand command)
        {
            if (!CanStore(actor)) return LabCommandResult.No(LabStrings.InventoryFull);

            var carton = lab.Deliveries.Find(command.FixtureId);
            if (carton == null) return LabCommandResult.No(LabStrings.NoSuchCarton);

            if (carton.Location.Kind == SampleLocationKind.OnSurface &&
                OutOfReach(actor, carton.Id, NotAtCarton(carton), out var far))
            {
                return far;
            }

            if (!lab.Deliveries.TryTake(carton.Id, actor.ClientId, out string refusal))
                return LabCommandResult.No(refusal);

            Store(actor, LabGrip.OnCarton(carton.Id));
            return LabCommandResult.Ok;
        }

        private LabCommandResult OpenCarton(ILabActor actor, LabCommand command)
        {
            var carton = lab.Deliveries.Find(command.FixtureId);
            if (carton == null) return LabCommandResult.No(LabStrings.NoSuchCarton);

            if (OutOfReach(actor, carton.Id, NotAtCarton(carton), out var far)) return far;

            return lab.Deliveries.TryOpen(carton.Id, actor.ClientId, out string refusal)
                ? LabCommandResult.Ok
                : LabCommandResult.No(refusal);
        }

        private LabCommandResult TakeDeliveryNote(ILabActor actor, LabCommand command)
        {
            if (!CanStore(actor)) return LabCommandResult.No(LabStrings.InventoryFull);

            var carton = lab.Deliveries.Find(command.FixtureId);
            if (carton == null) return LabCommandResult.No(LabStrings.NoSuchCarton);

            // Against whatever the paper is sitting in — the box, or the bench somebody left it on —
            // for the reason TakeSlip checks the slip's own container rather than the printer.
            string standingAt = carton.NoteIsInside ? carton.Id : carton.NoteLocation.ContainerId;
            if (OutOfReach(actor, standingAt, LabStrings.NotStandingAtDeliveryNote, out var far))
                return far;

            if (!lab.Deliveries.TryTakeNote(carton.Id, actor.ClientId, out string refusal))
                return LabCommandResult.No(refusal);

            Store(actor, LabGrip.OnNote(carton.Id));
            return LabCommandResult.Ok;
        }

        private LabCommandResult DiscardCarton(ILabActor actor, LabCommand command)
        {
            var carton = lab.Deliveries.Find(command.FixtureId);
            if (carton == null) return LabCommandResult.No(LabStrings.NoSuchCarton);

            if (carton.Location.Kind == SampleLocationKind.OnSurface &&
                OutOfReach(actor, carton.Id, NotAtCarton(carton), out var far))
            {
                return far;
            }

            bool wasInHand = actor.Grip.Kind == GripKind.Carton &&
                             string.Equals(actor.Grip.ItemId, carton.Id, System.StringComparison.Ordinal);

            if (!lab.Deliveries.TryDiscard(carton.Id, actor.ClientId, out string refusal))
                return LabCommandResult.No(refusal);

            if (wasInHand) actor.SetGrip(LabGrip.Empty);
            return LabCommandResult.Ok;
        }

        private static bool CanStore(ILabActor actor) =>
            actor is ILabInventoryActor inventory
                ? inventory.InventoryCount < inventory.InventoryCapacity
                : actor.Grip.IsEmpty;

        private static void Store(ILabActor actor, LabGrip grip)
        {
            if (actor is ILabInventoryActor inventory) inventory.StoreGrip(grip);
            else actor.SetGrip(grip);
        }

        private static LabCommandResult SelectInventory(ILabActor actor, LabCommand command)
        {
            if (!(actor is ILabInventoryActor inventory))
                return LabCommandResult.No(LabStrings.PlayerHasNoInventory);
            if (!int.TryParse(command.FixtureId, out int rawKind) ||
                !System.Enum.IsDefined(typeof(GripKind), rawKind))
                return LabCommandResult.No(LabStrings.NoSuchInventoryItem);

            var kind = (GripKind)rawKind;
            var grip = kind switch
            {
                GripKind.Vial => LabGrip.OnVial(command.Sample),
                GripKind.Slip => LabGrip.OnSlip(command.Sample, command.Amount),
                GripKind.Bottle => LabGrip.OnBottle(command.Text),
                GripKind.Carton => LabGrip.OnCarton(command.Text),
                GripKind.Note => LabGrip.OnNote(command.Text),
                GripKind.Book => LabGrip.OnBookItem(command.Text),
                _ => LabGrip.Empty
            };
            return inventory.SelectGrip(grip)
                ? LabCommandResult.Ok
                : LabCommandResult.No(LabStrings.ItemNotInInventory);
        }

        private LabCommandResult PutDown(ILabActor actor, LabCommand command)
        {
            var grip = actor.Grip;
            if (grip.IsEmpty) return LabCommandResult.No(LabStrings.CarryingNothing);

            string surface = command.FixtureId;
            if (string.IsNullOrEmpty(surface))
                return LabCommandResult.No(LabStrings.NowhereToPutThatDown);
            if (OutOfReach(actor, surface, LabStrings.NotStandingAtShelf, out var far)) return far;

            switch (grip.Kind)
            {
                case GripKind.Vial:
                {
                    if (!lab.Samples.TryGet(grip.Sample, out var sample))
                        return LabCommandResult.No(LabStrings.NoSuchSample);

                    // Every move after the delivery crate is a shelf change rather than progress, so
                    // this cannot refuse on stage — see SampleLifecycle.TryMove.
                    if (!SampleLifecycle.TryMove(sample, SampleLocation.OnSurface(surface, command.Amount),
                                                 out string refusal))
                        return LabCommandResult.No(refusal);
                    break;
                }

                case GripKind.Slip:
                    // The shelf is named, not just the hand emptied. Every other process draws the
                    // paper from this record (see ResultSlips), so a release that only said "nobody is
                    // holding it" would put the slip back in the instrument's tray on every screen
                    // except the one belonging to the player who racked it.
                    lab.Slips.Release(grip.Ticket, SampleLocation.OnSurface(surface, command.Amount));
                    break;

                case GripKind.Bottle:
                {
                    // Any slotted shelf will take one, including a sample rack — a bottle parked in a
                    // rack is a hole a vial cannot use, which is §5.5's shelf pressure doing its job.
                    if (!lab.Solvent.TryPutDown(grip.ItemId, actor.ClientId, surface, command.Amount,
                                                out string refusal))
                    {
                        return LabCommandResult.No(refusal);
                    }
                    break;
                }

                case GripKind.Carton:
                {
                    // Named here for the reason a racked slip is: every other process asks the bay
                    // where a box is, so emptying the hand without recording the shelf would leave the
                    // carton standing in the bay on every screen except the one that moved it.
                    if (!lab.Deliveries.TryPutDown(grip.ItemId, actor.ClientId, surface, command.Amount,
                                                   out string refusal))
                    {
                        return LabCommandResult.No(refusal);
                    }
                    break;
                }

                case GripKind.Note:
                {
                    if (!lab.Deliveries.TryPutDownNote(grip.ItemId, actor.ClientId, surface,
                                                       command.Amount, out string refusal))
                    {
                        return LabCommandResult.No(refusal);
                    }
                    break;
                }
            }

            actor.SetGrip(LabGrip.Empty);
            return LabCommandResult.Yes(grip.Sample);
        }

        private LabCommandResult Agitate(ILabActor actor)
        {
            var grip = actor.Grip;
            if (grip.Kind != GripKind.Vial) return LabCommandResult.No(LabStrings.NotHoldingASample);
            if (!lab.Samples.TryGet(grip.Sample, out var sample))
                return LabCommandResult.No(LabStrings.NoSuchSample);

            return SampleLifecycle.TryPrep(sample, out string refusal)
                ? LabCommandResult.Yes(sample.Id)
                : LabCommandResult.No(refusal);
        }

        // -- Instruments -----------------------------------------------------------------------------

        private LabCommandResult LoadMachine(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            var grip = actor.Grip;
            if (grip.Kind != GripKind.Vial) return LabCommandResult.No(LabStrings.NotHoldingASample);
            if (!lab.Samples.TryGet(grip.Sample, out var sample))
                return LabCommandResult.No(LabStrings.NoSuchSample);

            if (lab.ShiftOver) return LabCommandResult.No(LabStrings.ShiftOverNoNewRuns);

            var verdict = machine.TryLoad(sample);
            if (verdict != LoadRefusal.Accepted)
                return LabCommandResult.No(Describe(verdict, machine, sample));

            actor.SetGrip(LabGrip.Empty);
            return LabCommandResult.Yes(sample.Id);
        }

        private LabCommandResult StartRun(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            if (machine.IsRunning) return LabCommandResult.No(IsBusy(machine));
            if (machine.IsEmpty) return LabCommandResult.No(IsEmpty(machine));
            if (lab.ShiftOver) return LabCommandResult.No(LabStrings.ShiftOverNoNewRuns);

            if (!machine.TryBeginRun())
                return LabCommandResult.No(LabStrings.InstrumentWillNotStart.Format(
                    ("instrument", Name(machine))));

            return LabCommandResult.Yes(machine.LoadedSample);
        }

        private LabCommandResult TakeFromMachine(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            if (!CanStore(actor)) return LabCommandResult.No(LabStrings.InventoryFull);
            if (machine.IsRunning) return LabCommandResult.No(IsBusy(machine));

            var id = machine.Unload();
            if (!id.IsValid) return LabCommandResult.No(IsEmpty(machine));

            if (lab.Samples.TryGet(id, out var sample))
                SampleLifecycle.TryMove(sample, SampleLocation.Held(actor.ClientId), out _);

            Store(actor, LabGrip.OnVial(id));
            return LabCommandResult.Yes(id);
        }

        /// <summary>
        /// Flush. Housekeeping rather than analysis, so the shift clock does not gate it — the same
        /// rule <see cref="LabState.TryStartCalibration"/> gives for recalibration.
        /// <para>
        /// It still happens <b>here</b>, at the instrument, because the residue is in this
        /// instrument's sample path and nowhere else (§5.2). What moved to the wash station is the
        /// solvent: the charge comes out of the bottle the player walked over with, so a flush now
        /// costs a trip as well as a unit (#14, §5.5).
        /// </para>
        /// The bottle is taken from <see cref="ILabActor.Grip"/> — the host's own record of this
        /// player's hands — and re-checked against the store's record of who is holding it. A client
        /// asserting a bottle it left on the far side of the room gets a refusal, not a clean
        /// instrument.
        /// </summary>
        private LabCommandResult FlushMachine(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            if (machine.IsRunning)
                return LabCommandResult.No(LabStrings.CannotFlushWhileRunning.Format(
                    ("instrument", Name(machine))));

            if (actor.Grip.Kind != GripKind.Bottle)
                return LabCommandResult.No(LabStrings.FlushNeedsABottle);

            if (!lab.Solvent.TryConsumeCharge(actor.Grip.ItemId, actor.ClientId, out string refusal))
                return LabCommandResult.No(refusal);

            machine.Clean();
            return LabCommandResult.Ok;
        }

        private LabCommandResult RunBlank(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            if (machine.IsRunning) return LabCommandResult.No(IsBusy(machine));
            if (!machine.IsEmpty)
                return LabCommandResult.No(LabStrings.BlankNeedsAnEmptyInstrument);
            if (lab.ShiftOver) return LabCommandResult.No(LabStrings.ShiftOverNoNewRuns);

            return machine.TryBeginBlank()
                ? LabCommandResult.Ok
                : LabCommandResult.No(LabStrings.InstrumentWillNotTakeABlank.Format(
                    ("instrument", Name(machine))));
        }

        private LabCommandResult RunReference(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            return lab.TryStartReferenceRun(machine, out string refusal)
                ? LabCommandResult.Ok
                : LabCommandResult.No(refusal);
        }

        private LabCommandResult Calibrate(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            return lab.TryStartCalibration(machine, out string refusal)
                ? LabCommandResult.Ok
                : LabCommandResult.No(refusal);
        }

        // -- Wash station ----------------------------------------------------------------------------

        /// <summary>
        /// Top the carried bottle up from the drum.
        /// <para>
        /// The station is named by the request and the bottle is not, so a client cannot fill a bottle
        /// it left in a rack, and reach is checked against the station it claims to be standing at.
        /// Everything else — is it already full, is there anything in the drum, how much comes out —
        /// belongs to <see cref="SolventStore.TryFill"/>, which phrases its own refusals.
        /// </para>
        /// Housekeeping, so the shift clock does not gate it. Being locked out of refilling at 17:01
        /// would mean an instrument you could not clean before tomorrow's first sample went through
        /// it, which is §5.2 punishing the clock rather than the choice.
        /// </summary>
        private LabCommandResult FillBottle(ILabActor actor, LabCommand command)
        {
            string station = string.IsNullOrEmpty(command.FixtureId)
                ? SolventStore.StationId
                : command.FixtureId;

            if (OutOfReach(actor, station, LabStrings.NotStandingAtWashStation, out var far)) return far;

            if (actor.Grip.Kind != GripKind.Bottle)
                return LabCommandResult.No(LabStrings.NotCarryingABottle);

            return lab.Solvent.TryFill(actor.Grip.ItemId, actor.ClientId, out _, out string refusal)
                ? LabCommandResult.Ok
                : LabCommandResult.No(refusal);
        }

        // -- Terminal --------------------------------------------------------------------------------

        private LabCommandResult FileSlip(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, LabStrings.NotStandingAtTerminal, out var far))
                return far;

            var grip = actor.Grip;
            if (grip.Kind != GripKind.Slip || grip.Ticket != command.Amount)
                return LabCommandResult.No(LabStrings.NotCarryingThatSlip);

            if (!lab.Slips.TryGet(grip.Ticket, out var slip))
                return LabCommandResult.No(LabStrings.SlipAlreadyFiled);

            // A solvent blank or a certified standard belongs to the instrument, not to a sample. Both
            // are already readable in the terminal's INSTRUMENTS panel, so filing one just discards
            // the paper — it must not be refused, or the player is left holding something with nowhere
            // to go.
            SampleState sample = null;
            bool belongsToNoSample = slip.Result == null || slip.Result.IsBlank ||
                                     !lab.Samples.TryGet(slip.Sample, out sample);

            if (belongsToNoSample)
            {
                lab.Slips.Discard(grip.Ticket);
                actor.SetGrip(LabGrip.Empty);
                return LabCommandResult.Ok;
            }

            if (!SampleLifecycle.TryFileResult(sample, slip.Result, out string refusal))
                return LabCommandResult.No(refusal);

            lab.Slips.Discard(grip.Ticket);
            actor.SetGrip(LabGrip.Empty);
            return LabCommandResult.Yes(sample.Id);
        }

        private LabCommandResult FileVerdict(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, LabStrings.NotStandingAtTerminal, out var far))
                return far;

            // Cast from the wire, so it has to be checked rather than trusted. An out-of-range value
            // would otherwise be scored by ConsequenceResolver as whichever verdict happened to share
            // its number.
            if (!System.Enum.IsDefined(typeof(Verdict), command.Amount))
                return LabCommandResult.No(LabStrings.NotAVerdict);

            var cause = string.IsNullOrEmpty(command.Text) ? null : lab.Content?.Cause(command.Text);

            return lab.Samples.FileVerdict(command.Sample, (Verdict)command.Amount, cause, lab.Day,
                                           out string refusal)
                ? LabCommandResult.Yes(command.Sample)
                : LabCommandResult.No(refusal);
        }

        private LabCommandResult OrderSolvent(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, LabStrings.NotStandingAtTerminal, out var far))
                return far;
            if (command.Amount <= 0) return LabCommandResult.No(LabStrings.OrderAtLeastOneUnit);

            return lab.Economy.TryBuySolvent(command.Amount)
                ? LabCommandResult.Ok
                : LabCommandResult.No(LabStrings.CannotAffordSolvent.Format(
                    ("units", command.Amount),
                    ("cost", lab.Economy.SolventCost(command.Amount).ToString("N0"))));
        }

        private LabCommandResult OrderStandards(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, LabStrings.NotStandingAtTerminal, out var far))
                return far;
            if (command.Amount <= 0) return LabCommandResult.No(LabStrings.OrderAtLeastOneAmpoule);

            return lab.Economy.TryBuyReferenceStandards(command.Amount)
                ? LabCommandResult.Ok
                : LabCommandResult.No(LabStrings.CannotAffordStandards.Format(
                    ("count", command.Amount),
                    ("cost", lab.Economy.ReferenceStandardCost(command.Amount).ToString("N0"))));
        }

        private LabCommandResult ReopenSuspect(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, LabStrings.NotStandingAtTerminal, out var far))
                return far;

            return lab.TryReopenSuspect(command.Sample, out string refusal)
                ? LabCommandResult.Yes(command.Sample)
                : LabCommandResult.No(refusal);
        }

        /// <summary>
        /// Record which line of a delivery note an ambiguous vial answers (#32).
        /// <para>
        /// At the terminal, like every other piece of paperwork — the note may be anywhere, but the
        /// decision is written into the lab's records and the records are at the desk. Reach is the
        /// only thing established here; whether there is anything to decide at all belongs to
        /// <see cref="DeliveryBay.TryRegisterSample"/>, which refuses a legible bottle in its own
        /// words.
        /// </para>
        /// </summary>
        private LabCommandResult RegisterSample(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, LabStrings.NotStandingAtTerminal, out var far))
                return far;

            return lab.TryRegisterSample(command.Sample, command.Amount, out string refusal)
                ? LabCommandResult.Yes(command.Sample)
                : LabCommandResult.No(refusal);
        }

        /// <summary>
        /// Ring a customer about a label that cannot be read (#32). Costs shift time, which
        /// <see cref="LabState.TryCallCustomer"/> charges — the day clock is the lab's, not this
        /// type's.
        /// </summary>
        private LabCommandResult CallCustomer(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, LabStrings.NotStandingAtTerminal, out var far))
                return far;

            return lab.TryCallCustomer(command.Text, out _, out string refusal)
                ? LabCommandResult.Ok
                : LabCommandResult.No(refusal);
        }

        /// <summary>
        /// Close the shift. Any player may do it, which is deliberate — §5.5 is a shared-room game and
        /// the day is shared state — but it cannot be done twice, so a second click while the report
        /// is on screen is refused rather than settling the queue again.
        /// </summary>
        private LabCommandResult EndDay(ILabActor actor)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, LabStrings.NotStandingAtTerminal, out var far))
                return far;
            if (!lab.DayInProgress) return LabCommandResult.No(LabStrings.DayAlreadyOver);

            lab.EndDay();
            return LabCommandResult.Ok;
        }

        private LabCommandResult StartNextDay(ILabActor actor)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, LabStrings.NotStandingAtTerminal, out var far))
                return far;
            if (lab.DayInProgress) return LabCommandResult.No(LabStrings.ShiftStillRunning);

            return lab.BeginDay()
                ? LabCommandResult.Ok
                : LabCommandResult.No(LabStrings.RunIsOver);
        }

        // -- Shared checks ---------------------------------------------------------------------------

        private bool TryReachMachine(ILabActor actor, string instanceId, out MachineInstance machine,
                                     out LabCommandResult refused)
        {
            machine = string.IsNullOrEmpty(instanceId) ? null : lab.FindMachine(instanceId);

            if (machine == null)
            {
                refused = LabCommandResult.No(LabStrings.NoSuchInstrument);
                return false;
            }

            if (OutOfReach(actor, instanceId,
                           LabStrings.NotStandingAtInstrument.Format(("instrument", Name(machine))),
                           out refused))
            {
                return false;
            }

            refused = default;
            return true;
        }

        /// <summary>
        /// Whether the player is too far off to operate <paramref name="fixtureId"/>, with the whole
        /// refusal supplied by the caller.
        /// <para>
        /// The sentence arrives finished rather than being assembled from a template plus a noun
        /// here (#55). "You are not standing at " + <c>what</c> reads correctly in English and is
        /// untranslatable: a language that puts the place first is unreachable to a translator who
        /// was handed the fragment. Every caller therefore owns a complete
        /// <c>refusal.not_at_…</c> line, which is also why the carton's job number is a named
        /// argument rather than something glued on the front.
        /// </para>
        /// </summary>
        private bool OutOfReach(ILabActor actor, string fixtureId, string tooFar,
                                out LabCommandResult refused)
        {
            refused = default;

            if (stations == null || !actor.HasPosition || string.IsNullOrEmpty(fixtureId)) return false;
            if (!stations.TryLocate(fixtureId, out var position)) return false;

            if ((position - actor.Position).sqrMagnitude <= ReachMetres * ReachMetres) return false;

            refused = LabCommandResult.No(tooFar);
            return true;
        }

        private static string NotAtCarton(Carton carton) =>
            LabStrings.NotStandingAtCarton.Format(("job", carton.JobNumber));

        private static string IsBusy(MachineInstance machine) =>
            LabStrings.InstrumentIsBusy.Format(("instrument", Name(machine)));

        private static string IsEmpty(MachineInstance machine) =>
            LabStrings.InstrumentIsEmpty.Format(("instrument", Name(machine)));

        private static string Name(MachineInstance machine) =>
            machine?.Def != null ? machine.Def.DisplayName : LabStrings.TheInstrument.Text;

        private string DisplayNameOf(string machineInstanceId) =>
            string.IsNullOrEmpty(machineInstanceId)
                ? LabStrings.AnInstrument.Text
                : Name(lab.FindMachine(machineInstanceId));

        /// <summary>
        /// Turn a <see cref="LoadRefusal"/> into the sentence the station used to say in its prompt.
        /// Kept here so the prompt and the refusal cannot drift apart: the player reads one before
        /// pressing and the other after, and they had better agree.
        /// </summary>
        private static string Describe(LoadRefusal refusal, MachineInstance machine, SampleState sample) =>
            refusal switch
            {
                LoadRefusal.MachineBusy => IsBusy(machine),
                LoadRefusal.MachineOccupied =>
                    LabStrings.InstrumentAlreadyLoaded.Format(("instrument", Name(machine))),
                LoadRefusal.NotEnoughVolume => LabStrings.NotEnoughVolume.Format(
                    ("instrument", Name(machine)),
                    ("needed", machine.Def.SampleVolumeMl.ToString("F0")),
                    ("tag", sample.RecordTag),
                    ("left", sample.VolumeMl.ToString("F1"))),
                LoadRefusal.NeedsPreheat => LabStrings.NeedsPreheat.Format(
                    ("tag", sample.RecordTag),
                    ("actual", sample.TemperatureC.ToString("F0")),
                    ("instrument", Name(machine)),
                    ("target", machine.Def.PreheatTargetC.ToString("F0"))),
                LoadRefusal.NotSettled =>
                    LabStrings.HasSettledOut.Format(("tag", sample.RecordTag)),
                _ => LabStrings.InstrumentWillNotTakeThat.Format(("instrument", Name(machine)))
            };
    }
}
