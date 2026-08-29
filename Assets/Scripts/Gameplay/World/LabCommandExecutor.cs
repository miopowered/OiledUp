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
            if (actor == null) return LabCommandResult.No("No such player.");
            if (lab == null) return LabCommandResult.No("The lab is not running.");

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
                LabCommandKind.EndDay => EndDay(actor),
                LabCommandKind.StartNextDay => StartNextDay(actor),

                _ => LabCommandResult.No("The lab did not understand that.")
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
            if (!CanStore(actor)) return LabCommandResult.No("Your inventory is full.");
            if (!lab.Samples.TryGet(command.Sample, out var sample))
                return LabCommandResult.No("No such sample.");

            // A vial in a box is only reachable once somebody has carried the box in, put it down and
            // cut it open (#30, #31). Checked here rather than left to the prop, because the prop for
            // a sealed carton's contents does not exist on any machine and a request naming one is
            // therefore a request the host has to answer for itself.
            if (!CanReachInCarton(actor, sample, out var sealedOff)) return sealedOff;

            switch (sample.Location.Kind)
            {
                case SampleLocationKind.InMachine:
                    return LabCommandResult.No(
                        $"{sample.RecordTag} is inside {DisplayNameOf(sample.Location.ContainerId)}. " +
                        "Take it out at the instrument.");

                case SampleLocationKind.Held when sample.Location.HolderClientId != actor.ClientId:
                    return LabCommandResult.No($"Someone else is holding {sample.RecordTag}.");

                case SampleLocationKind.Consumed:
                    return LabCommandResult.No($"{sample.RecordTag} is spent — there is nothing left to carry.");
            }

            if (!SampleLifecycle.TryMove(sample, SampleLocation.Held(actor.ClientId), out string refusal))
                return LabCommandResult.No(refusal);

            Store(actor, LabGrip.OnVial(sample.Id));
            return LabCommandResult.Yes(sample.Id);
        }

        private LabCommandResult TakeSlip(ILabActor actor, LabCommand command)
        {
            if (!CanStore(actor)) return LabCommandResult.No("Your inventory is full.");

            if (!lab.Slips.TryGet(command.Amount, out var slip))
                return LabCommandResult.No("That slip has already been filed.");

            // Reach is checked against where the paper IS, not against the instrument that printed
            // it. While a slip could only ever sit in the tray it came out of, those were the same
            // place; now that paperwork can be set down anywhere, they are not. Checking the printer
            // leaves a slip lying at your feet on the far side of the room refusing to be picked up,
            // with a refusal naming an instrument you are nowhere near.
            bool inATray = slip.Location.Kind != SampleLocationKind.OnSurface ||
                           string.IsNullOrEmpty(slip.Location.ContainerId);

            string standingAt = inATray ? slip.MachineInstanceId : slip.Location.ContainerId;
            string what = inATray ? DisplayNameOf(slip.MachineInstanceId) : "that slip";

            if (OutOfReach(actor, standingAt, what, out var far)) return far;

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
            if (!CanStore(actor)) return LabCommandResult.No("Your inventory is full.");
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
            if (!CanStore(actor)) return LabCommandResult.No("Your inventory is full.");

            var bottle = lab.Solvent.Find(command.FixtureId);
            if (bottle == null) return LabCommandResult.No("No such solvent bottle.");

            if (bottle.Location.Kind == SampleLocationKind.OnSurface &&
                OutOfReach(actor, bottle.Location.ContainerId, "that shelf", out var far))
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
                    $"{sample.RecordTag} is still on the truck — the delivery has not been unloaded yet.");
                return false;
            }

            if (carton.IsSealed)
            {
                refused = LabCommandResult.No(
                    $"Carton {carton.JobNumber} is still sealed. Open it before taking anything out.");
                return false;
            }

            // A box in somebody's arms is a box nobody can reach into, including the person carrying
            // it. Stated rather than assumed, because the vial's prop is riding in their hands and
            // would otherwise look perfectly grabbable to a second player.
            if (carton.Location.Kind == SampleLocationKind.Held)
            {
                refused = LabCommandResult.No(
                    carton.Location.HolderClientId == actor.ClientId
                        ? "Set the carton down before taking vials out of it."
                        : $"Someone else is carrying carton {carton.JobNumber}.");
                return false;
            }

            // Reach is checked against the box rather than the shelf it is standing on: a carton is a
            // registered fixture in its own right, and the bay is 4 m of floor rather than a point.
            return !OutOfReach(actor, carton.Id, $"carton {carton.JobNumber}", out refused);
        }

        private LabCommandResult TakeCarton(ILabActor actor, LabCommand command)
        {
            if (!CanStore(actor)) return LabCommandResult.No("Your inventory is full.");

            var carton = lab.Deliveries.Find(command.FixtureId);
            if (carton == null) return LabCommandResult.No("No such carton.");

            if (carton.Location.Kind == SampleLocationKind.OnSurface &&
                OutOfReach(actor, carton.Id, $"carton {carton.JobNumber}", out var far))
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
            if (carton == null) return LabCommandResult.No("No such carton.");

            if (OutOfReach(actor, carton.Id, $"carton {carton.JobNumber}", out var far)) return far;

            return lab.Deliveries.TryOpen(carton.Id, actor.ClientId, out string refusal)
                ? LabCommandResult.Ok
                : LabCommandResult.No(refusal);
        }

        private LabCommandResult TakeDeliveryNote(ILabActor actor, LabCommand command)
        {
            if (!CanStore(actor)) return LabCommandResult.No("Your inventory is full.");

            var carton = lab.Deliveries.Find(command.FixtureId);
            if (carton == null) return LabCommandResult.No("No such carton.");

            // Against whatever the paper is sitting in — the box, or the bench somebody left it on —
            // for the reason TakeSlip checks the slip's own container rather than the printer.
            string standingAt = carton.NoteIsInside ? carton.Id : carton.NoteLocation.ContainerId;
            if (OutOfReach(actor, standingAt, "that delivery note", out var far)) return far;

            if (!lab.Deliveries.TryTakeNote(carton.Id, actor.ClientId, out string refusal))
                return LabCommandResult.No(refusal);

            Store(actor, LabGrip.OnNote(carton.Id));
            return LabCommandResult.Ok;
        }

        private LabCommandResult DiscardCarton(ILabActor actor, LabCommand command)
        {
            var carton = lab.Deliveries.Find(command.FixtureId);
            if (carton == null) return LabCommandResult.No("No such carton.");

            if (carton.Location.Kind == SampleLocationKind.OnSurface &&
                OutOfReach(actor, carton.Id, $"carton {carton.JobNumber}", out var far))
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
                return LabCommandResult.No("This player has no inventory.");
            if (!int.TryParse(command.FixtureId, out int rawKind) ||
                !System.Enum.IsDefined(typeof(GripKind), rawKind))
                return LabCommandResult.No("No such inventory item.");

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
                : LabCommandResult.No("That item is not in your inventory.");
        }

        private LabCommandResult PutDown(ILabActor actor, LabCommand command)
        {
            var grip = actor.Grip;
            if (grip.IsEmpty) return LabCommandResult.No("You are not carrying anything.");

            string surface = command.FixtureId;
            if (string.IsNullOrEmpty(surface)) return LabCommandResult.No("Nowhere to put that down.");
            if (OutOfReach(actor, surface, "that shelf", out var far)) return far;

            switch (grip.Kind)
            {
                case GripKind.Vial:
                {
                    if (!lab.Samples.TryGet(grip.Sample, out var sample))
                        return LabCommandResult.No("No such sample.");

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
            if (grip.Kind != GripKind.Vial) return LabCommandResult.No("You are not holding a sample.");
            if (!lab.Samples.TryGet(grip.Sample, out var sample))
                return LabCommandResult.No("No such sample.");

            return SampleLifecycle.TryPrep(sample, out string refusal)
                ? LabCommandResult.Yes(sample.Id)
                : LabCommandResult.No(refusal);
        }

        // -- Instruments -----------------------------------------------------------------------------

        private LabCommandResult LoadMachine(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            var grip = actor.Grip;
            if (grip.Kind != GripKind.Vial) return LabCommandResult.No("You are not holding a sample.");
            if (!lab.Samples.TryGet(grip.Sample, out var sample))
                return LabCommandResult.No("No such sample.");

            if (lab.ShiftOver) return LabCommandResult.No("Shift over — no new runs.");

            var verdict = machine.TryLoad(sample);
            if (verdict != LoadRefusal.Accepted)
                return LabCommandResult.No(Describe(verdict, machine, sample));

            actor.SetGrip(LabGrip.Empty);
            return LabCommandResult.Yes(sample.Id);
        }

        private LabCommandResult StartRun(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            if (machine.IsRunning) return LabCommandResult.No($"{Name(machine)} is busy.");
            if (machine.IsEmpty) return LabCommandResult.No($"{Name(machine)} is empty.");
            if (lab.ShiftOver) return LabCommandResult.No("Shift over — no new runs.");

            if (!machine.TryBeginRun())
                return LabCommandResult.No($"{Name(machine)} will not start a run right now.");

            return LabCommandResult.Yes(machine.LoadedSample);
        }

        private LabCommandResult TakeFromMachine(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            if (!CanStore(actor)) return LabCommandResult.No("Your inventory is full.");
            if (machine.IsRunning) return LabCommandResult.No($"{Name(machine)} is busy.");

            var id = machine.Unload();
            if (!id.IsValid) return LabCommandResult.No($"{Name(machine)} is empty.");

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
                return LabCommandResult.No($"Cannot flush {Name(machine)} while it is running.");

            if (actor.Grip.Kind != GripKind.Bottle)
            {
                return LabCommandResult.No(
                    "You need a solvent bottle in your hands. Fill one at the wash station.");
            }

            if (!lab.Solvent.TryConsumeCharge(actor.Grip.ItemId, actor.ClientId, out string refusal))
                return LabCommandResult.No(refusal);

            machine.Clean();
            return LabCommandResult.Ok;
        }

        private LabCommandResult RunBlank(ILabActor actor, LabCommand command)
        {
            if (!TryReachMachine(actor, command.FixtureId, out var machine, out var refused)) return refused;

            if (machine.IsRunning) return LabCommandResult.No($"{Name(machine)} is busy.");
            if (!machine.IsEmpty) return LabCommandResult.No("Take the vial out before running a blank.");
            if (lab.ShiftOver) return LabCommandResult.No("Shift over — no new runs.");

            return machine.TryBeginBlank()
                ? LabCommandResult.Ok
                : LabCommandResult.No($"{Name(machine)} will not take a blank right now.");
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

            if (OutOfReach(actor, station, "the wash station", out var far)) return far;

            if (actor.Grip.Kind != GripKind.Bottle)
                return LabCommandResult.No("You are not carrying a solvent bottle.");

            return lab.Solvent.TryFill(actor.Grip.ItemId, actor.ClientId, out _, out string refusal)
                ? LabCommandResult.Ok
                : LabCommandResult.No(refusal);
        }

        // -- Terminal --------------------------------------------------------------------------------

        private LabCommandResult FileSlip(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, "the terminal", out var far)) return far;

            var grip = actor.Grip;
            if (grip.Kind != GripKind.Slip || grip.Ticket != command.Amount)
                return LabCommandResult.No("You are not carrying that slip.");

            if (!lab.Slips.TryGet(grip.Ticket, out var slip))
                return LabCommandResult.No("That slip has already been filed.");

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
            if (OutOfReach(actor, TerminalStation.FixtureId, "the terminal", out var far)) return far;

            // Cast from the wire, so it has to be checked rather than trusted. An out-of-range value
            // would otherwise be scored by ConsequenceResolver as whichever verdict happened to share
            // its number.
            if (!System.Enum.IsDefined(typeof(Verdict), command.Amount))
                return LabCommandResult.No("That is not a verdict.");

            var cause = string.IsNullOrEmpty(command.Text) ? null : lab.Content?.Cause(command.Text);

            return lab.Samples.FileVerdict(command.Sample, (Verdict)command.Amount, cause, lab.Day,
                                           out string refusal)
                ? LabCommandResult.Yes(command.Sample)
                : LabCommandResult.No(refusal);
        }

        private LabCommandResult OrderSolvent(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, "the terminal", out var far)) return far;
            if (command.Amount <= 0) return LabCommandResult.No("Order at least one unit.");

            return lab.Economy.TryBuySolvent(command.Amount)
                ? LabCommandResult.Ok
                : LabCommandResult.No(
                    $"A {command.Amount}-unit restock costs " +
                    $"£{lab.Economy.SolventCost(command.Amount):N0}, and the account will not cover it.");
        }

        private LabCommandResult OrderStandards(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, "the terminal", out var far)) return far;
            if (command.Amount <= 0) return LabCommandResult.No("Order at least one ampoule.");

            return lab.Economy.TryBuyReferenceStandards(command.Amount)
                ? LabCommandResult.Ok
                : LabCommandResult.No(
                    $"{command.Amount} certified ampoules cost " +
                    $"£{lab.Economy.ReferenceStandardCost(command.Amount):N0}, and the account will " +
                    "not cover it.");
        }

        private LabCommandResult ReopenSuspect(ILabActor actor, LabCommand command)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, "the terminal", out var far)) return far;

            return lab.TryReopenSuspect(command.Sample, out string refusal)
                ? LabCommandResult.Yes(command.Sample)
                : LabCommandResult.No(refusal);
        }

        /// <summary>
        /// Close the shift. Any player may do it, which is deliberate — §5.5 is a shared-room game and
        /// the day is shared state — but it cannot be done twice, so a second click while the report
        /// is on screen is refused rather than settling the queue again.
        /// </summary>
        private LabCommandResult EndDay(ILabActor actor)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, "the terminal", out var far)) return far;
            if (!lab.DayInProgress) return LabCommandResult.No("The day is already over.");

            lab.EndDay();
            return LabCommandResult.Ok;
        }

        private LabCommandResult StartNextDay(ILabActor actor)
        {
            if (OutOfReach(actor, TerminalStation.FixtureId, "the terminal", out var far)) return far;
            if (lab.DayInProgress) return LabCommandResult.No("The shift is still running.");

            return lab.BeginDay()
                ? LabCommandResult.Ok
                : LabCommandResult.No("The run is over — there is no next day.");
        }

        // -- Shared checks ---------------------------------------------------------------------------

        private bool TryReachMachine(ILabActor actor, string instanceId, out MachineInstance machine,
                                     out LabCommandResult refused)
        {
            machine = string.IsNullOrEmpty(instanceId) ? null : lab.FindMachine(instanceId);

            if (machine == null)
            {
                refused = LabCommandResult.No("No such instrument.");
                return false;
            }

            if (OutOfReach(actor, instanceId, Name(machine), out refused)) return false;

            refused = default;
            return true;
        }

        private bool OutOfReach(ILabActor actor, string fixtureId, string what, out LabCommandResult refused)
        {
            refused = default;

            if (stations == null || !actor.HasPosition || string.IsNullOrEmpty(fixtureId)) return false;
            if (!stations.TryLocate(fixtureId, out var position)) return false;

            if ((position - actor.Position).sqrMagnitude <= ReachMetres * ReachMetres) return false;

            refused = LabCommandResult.No($"You are not standing at {what}.");
            return true;
        }

        private static string Name(MachineInstance machine) =>
            machine?.Def != null ? machine.Def.DisplayName : "the instrument";

        private string DisplayNameOf(string machineInstanceId) =>
            string.IsNullOrEmpty(machineInstanceId) ? "an instrument" : Name(lab.FindMachine(machineInstanceId));

        /// <summary>
        /// Turn a <see cref="LoadRefusal"/> into the sentence the station used to say in its prompt.
        /// Kept here so the prompt and the refusal cannot drift apart: the player reads one before
        /// pressing and the other after, and they had better agree.
        /// </summary>
        private static string Describe(LoadRefusal refusal, MachineInstance machine, SampleState sample) =>
            refusal switch
            {
                LoadRefusal.MachineBusy => $"{Name(machine)} is busy.",
                LoadRefusal.MachineOccupied => $"{Name(machine)} already has a vial in it.",
                LoadRefusal.NotEnoughVolume =>
                    $"{Name(machine)} needs {machine.Def.SampleVolumeMl:F0} ml and " +
                    $"{sample.RecordTag} has {sample.VolumeMl:F1} ml left.",
                LoadRefusal.NeedsPreheat =>
                    $"{sample.RecordTag} is at {sample.TemperatureC:F0} °C — " +
                    $"{Name(machine)} needs it near {machine.Def.PreheatTargetC:F0} °C.",
                LoadRefusal.NotSettled =>
                    $"{sample.RecordTag} has settled out. Agitate it before running it (§4.5).",
                _ => $"{Name(machine)} will not take that."
            };
    }
}
