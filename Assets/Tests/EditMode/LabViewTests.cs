using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Editor.Content;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.World;
using Residue.Net.Views;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards the read seam that lets a joined client operate the lab.
    /// <para>
    /// A client has no <see cref="LabState"/> and never will (hard rule 2), so every station in the
    /// room reads an <see cref="IMachineView"/> instead of the host's live instrument. That interface
    /// has two implementations. <b>The failure mode this file exists for is not a crash — it is
    /// drift.</b> Both sides compile, both render, and one of them quietly quotes a different run time
    /// or offers to run a sample somebody has already run, and the two players in the room disagree
    /// about what the machine in front of them is doing.
    /// </para>
    /// So most of what follows is not "does the adapter work" but "do the two adapters give the same
    /// answer for the same instrument".
    /// </summary>
    public sealed class LabViewTests
    {
        private ContentSet content;
        private ContentCatalog catalog;

        private ILabView originalHost;
        private ILabView originalReplicated;

        [SetUp]
        public void SetUp()
        {
            content = ContentBuilder.BuildInMemory();
            catalog = ContentBuilder.BuildCatalogInMemory(content);

            // Static, process-wide, and read by half the world layer. Anything left installed here
            // fails a completely unrelated test somewhere else, which is a miserable thing to debug.
            originalHost = LabView.Host;
            originalReplicated = LabView.Replicated;
        }

        [TearDown]
        public void TearDown()
        {
            LabView.Host = originalHost;
            LabView.Replicated = originalReplicated;

            if (catalog != null) Object.DestroyImmediate(catalog);
            if (content == null) return;

            foreach (var o in content.Elements.Values.Cast<Object>()
                         .Concat(content.Causes.Values)
                         .Concat(content.Profiles.Values)
                         .Concat(content.Faults.Values)
                         .Concat(content.Machines.Values))
            {
                Object.DestroyImmediate(o);
            }
            content = null;
        }

        // -----------------------------------------------------------------------------------------
        // 1. The two implementations must be indistinguishable to anything that draws an instrument.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: the instrument in front of you looks the same whether or not you are the one
        /// hosting.
        /// <para>
        /// Every readable member of <see cref="IMachineView"/> is compared between the host's adapter
        /// over a live <see cref="MachineInstance"/> and a client's adapter over
        /// <c>MachineView.From</c> of that same instance, across the states a station actually
        /// distinguishes: idle, loaded, running, and holding a finished result.
        /// </para>
        /// Driven by reflection rather than a field list, so a member added to the interface later is
        /// covered the day it lands rather than the day someone remembers this file. The two members
        /// that are <i>deliberately</i> asymmetric — <see cref="IMachineView.CanAccept"/> and
        /// <see cref="IMachineView.HasFreshCheck"/> — are methods, and have tests of their own below.
        /// </summary>
        [Test]
        public void HostAndClientAdapters_DrawTheSameInstrument()
        {
            foreach (var (state, machine) in InstrumentStates())
            {
                var host = new HostMachineView(machine);
                var client = ReplicatedMachineView.Of(MachineView.From(machine), catalog);

                foreach (var property in typeof(IMachineView).GetProperties())
                {
                    Assert.AreEqual(property.GetValue(host), property.GetValue(client),
                        $"IMachineView.{property.Name} disagrees between the host and a client for an " +
                        $"instrument that is {state}. Two players standing at the same machine would " +
                        "be shown different things.");
                }
            }
        }

        /// <summary>
        /// Promise: an instrument that has finished a run offers the vial back to whoever walks up,
        /// not only to the player who pressed the button.
        /// <para>
        /// This used to be a bool on the station component, set in the callback of the player who
        /// started the run. That is right for exactly one person: everybody else's station still said
        /// "RUN", and pressing it spent millilitres on a test that had already been done (§4.5). It is
        /// now read off the instrument, so it replicates and it is the same answer for the room.
        /// </para>
        /// </summary>
        [Test]
        public void AFinishedRun_OffersTheVialBackToEveryPlayer()
        {
            var machine = Instrument();
            Assert.IsFalse(machine.HasResultWaiting, "A fresh instrument is holding nothing.");

            machine.LoadedSample = new SampleId(1);
            Assert.IsTrue(machine.TryBeginRun());
            Assert.IsFalse(machine.HasResultWaiting, "A run in progress is not a result waiting.");

            machine.Tick(10_000f);
            Assert.IsTrue(machine.HasResultWaiting, "The run finished and the vial is still in there.");
            Assert.IsTrue(MachineView.From(machine).HasResultWaiting,
                "A client whose station cannot see this offers to run the sample a second time.");

            machine.Unload();
            Assert.IsFalse(machine.HasResultWaiting, "The vial has been taken; there is nothing to collect.");
        }

        /// <summary>
        /// Promise: a blank and a certified standard leave nothing to pick up.
        /// <para>
        /// Neither takes a vial (§5.2, §5.3), so an instrument that flagged a result waiting after one
        /// would offer the player an empty hand — and, worse, would stop offering the run they meant
        /// to do next.
        /// </para>
        /// </summary>
        [Test]
        public void ABlankOrAStandard_LeavesNothingToCollect()
        {
            var machine = Instrument();

            Assert.IsTrue(machine.TryBeginBlank());
            machine.Tick(10_000f);
            Assert.IsFalse(machine.HasResultWaiting, "A solvent blank consumes no vial and leaves none.");

            Assert.IsTrue(machine.TryBeginReference());
            machine.Tick(10_000f);
            Assert.IsFalse(machine.HasResultWaiting, "The ampoule is the consumable, not a vial in the tray.");
        }

        /// <summary>
        /// Promise: loading a vial clears whatever the last one left behind.
        /// <para>
        /// Otherwise the station offers "take your vial back" the instant a fresh sample goes in, and
        /// the sample never gets run.
        /// </para>
        /// </summary>
        [Test]
        public void LoadingAVial_ClearsTheResultTheLastOneLeft()
        {
            var machine = Instrument();
            machine.HasResultWaiting = true;

            Assert.AreEqual(LoadRefusal.Accepted, machine.TryLoad(ReadySample()));
            Assert.IsFalse(machine.HasResultWaiting);
        }

        /// <summary>
        /// Promise: a client greys out the recalibrate button on exactly the same rule the host
        /// enforces — a certificate from <i>today</i> (§5.3).
        /// <para>
        /// The day is the whole point: <c>MachineRuntimeState.BeginDay</c> re-rolls drift every
        /// morning, so yesterday's standard authorises nothing. A client that only knew a certificate
        /// existed would offer an action the host is about to refuse, which hard rule 3 forbids.
        /// </para>
        /// </summary>
        [Test]
        public void AStaleCertificate_AuthorisesNoCalibrationOnEitherSide()
        {
            var machine = Instrument();
            machine.LastCheck = CertificateFor(machine, day: 3);
            Assert.IsNotNull(machine.LastCheck, "Setup failed: no certified lines matched this instrument.");

            var host = new HostMachineView(machine);
            var client = ReplicatedMachineView.Of(MachineView.From(machine), catalog);

            Assert.IsTrue(host.HasFreshCheck(3));
            Assert.IsTrue(client.HasFreshCheck(3), "Today's certificate is on file and the client cannot see it.");

            Assert.IsFalse(host.HasFreshCheck(4));
            Assert.IsFalse(client.HasFreshCheck(4),
                "A certificate from yesterday has had a whole day of drift walk over it.");
        }

        /// <summary>
        /// Promise: a client that cannot check a rule offers the action anyway and lets the host
        /// refuse, rather than inventing an answer.
        /// <para>
        /// Volume, temperature and settling all live on a <see cref="SampleState"/> the client does not
        /// hold. The house pattern (see <c>LabCommands</c>) is that an optimistic prompt costs the
        /// player a refusal sentence they can read, whereas a pessimistic one greys out a button that
        /// would have worked — and, more importantly, that re-implementing §4.5 here would create a
        /// second copy of the rules to drift from the enforced one. What the client <i>can</i> see is
        /// occupancy, and it must still get that right.
        /// </para>
        /// </summary>
        [Test]
        public void AClientRefusesOnlyWhatItCanActuallySee()
        {
            var machine = Instrument();
            var idle = ReplicatedMachineView.Of(MachineView.From(machine), catalog);
            Assert.AreEqual(LoadRefusal.Accepted, idle.CanAccept(null),
                "Nothing the client can see refuses this load, so it asks and lets the host decide.");

            machine.LoadedSample = new SampleId(1);
            var loaded = ReplicatedMachineView.Of(MachineView.From(machine), catalog);
            Assert.AreEqual(LoadRefusal.MachineOccupied, loaded.CanAccept(null),
                "Occupancy is replicated, so a client that offers a load into a full instrument is " +
                "just wrong rather than optimistic.");

            Assert.IsTrue(machine.TryBeginRun());
            var running = ReplicatedMachineView.Of(MachineView.From(machine), catalog);
            Assert.AreEqual(LoadRefusal.MachineBusy, running.CanAccept(null));
        }

        // -----------------------------------------------------------------------------------------
        // 2. The lab-wide view, and the one thing it is honest about not having.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: the shift clock, the books and the calibration price read the same off
        /// <see cref="HostLabView"/> as they do off the lab it wraps.
        /// <para>
        /// These feed the HUD, the terminal prompt and the £ figure on the recalibrate button. The
        /// price in particular is replicated rather than re-derived from a default
        /// <see cref="EconomyTuning"/>, so that tuning it cannot leave a client quoting last week's
        /// number.
        /// </para>
        /// </summary>
        [Test]
        public void HostLabView_ReportsTheLabItWraps()
        {
            var lab = new LabState(catalog, OneDayPlan(), 1234);
            var view = new HostLabView(lab);

            Assert.AreEqual(0, view.Day);
            Assert.IsFalse(view.DayInProgress);

            lab.BeginDay();
            Assert.AreEqual(1, view.Day);
            Assert.IsTrue(view.DayInProgress);
            Assert.IsFalse(view.ShiftOver);
            Assert.AreEqual(lab.Economy.Money, view.Money, 1e-3f);
            Assert.AreEqual(lab.Economy.SolventUnits, view.SolventUnits, 1e-3f);
            Assert.AreEqual(lab.Economy.ReferenceStandards, view.ReferenceStandards);
            Assert.AreEqual(lab.Tuning.CalibrationCost, view.CalibrationCost, 1e-3f);
            Assert.AreEqual(lab.OpenSamples().Count, view.OpenSampleCount,
                "The terminal prompt and the HUD both count this; they must count what the lab holds.");

            lab.Tick(10_000f);
            Assert.IsTrue(view.ShiftOver, "The clock ran out and every station must stop offering runs.");

            Assert.AreEqual(lab.Tuning.CalibrationCost,
                EconomyView.From(lab.Economy, lab.Tuning).CalibrationCost, 1e-3f,
                "A client reads the price off the wire; if it does not travel, the button lies.");
        }

        /// <summary>
        /// Promise: a bottle is in one place, and a client that is told where puts it there.
        /// <para>
        /// This replaces a test that asserted the opposite — that a client had no vial props and said
        /// so out loud. §3.2 still keeps a vial a local prop rather than a <c>NetworkObject</c>, but
        /// the location record replicates now, so "there are no bottles here" stopped being true and
        /// the sentence it guarded was deleted. What has to be guarded instead is the reconciliation
        /// itself, and specifically its three moves: a bottle that appears is built, a bottle that
        /// moves is re-parented, and a bottle that stops appearing is destroyed rather than left
        /// standing in a crate the host has already emptied.
        /// </para>
        /// Driven through <see cref="LabRuntime.SlotsFor"/> exactly as the running game is, so a
        /// container that forgot to register its slots fails here rather than in a session.
        /// </summary>
        [Test]
        public void AClientsProps_FollowTheBottlesItIsToldAbout()
        {
            var runtime = NewRuntime();
            var crate = NewContainer("intake", 4);
            var rack = NewContainer(SampleRack.DefaultRackId, 4);

            var reconciler = new VialReconciler(runtime);
            var sample = new SampleId(7);

            // Appeared.
            reconciler.Reconcile(new[] { InCrate(sample, slot: 2) });
            var prop = runtime.PropFor(sample);
            Assert.IsNotNull(prop, "A bottle the host listed has no prop in the room.");
            Assert.AreSame(crate.Slot(2), prop.transform.parent,
                "The crate's third hole is where the host says it is; two players have to be able to " +
                "talk about the same bottle.");
            Assert.AreEqual("WERK-1 QUENCH 1", prop.Label,
                "The label on the glass is the only tell for a mis-log (§5.1) and has to reach a client.");

            // Moved.
            reconciler.Reconcile(new[] { OnRack(sample, slot: 1) });
            Assert.AreSame(rack.Slot(1), runtime.PropFor(sample).transform.parent,
                "Somebody racked it and this client is still showing it in the crate.");
            Assert.AreEqual(3, rack.FreeSlots, "The rack counts what is parented into it, not what it placed.");

            // Stopped appearing. Consumed vials are dropped from the list rather than tombstoned.
            reconciler.Reconcile(System.Array.Empty<VialPlacement>());
            Assert.IsNull(runtime.PropFor(sample),
                "A spent bottle is still standing in the rack, and it can never be picked up again.");

            Retire(crate);
            Retire(rack);
            Object.DestroyImmediate(runtime.gameObject);
        }

        /// <summary>
        /// Promise: a bottle you picked up is in your hand, and stays there.
        /// <para>
        /// The reconciler used to return early for a locally-held prop, reasoning that the local
        /// player's hands belong to the <c>LabCommands</c> callbacks. That is true about
        /// <c>Carried</c> and false about the transform, and the gap between the two was not a race —
        /// it was permanent.
        /// </para>
        /// <para>
        /// Between pressing the key and the host's next publish, the replicated location still names
        /// the shelf, so the reconciler parents the prop back to it. When the publish lands and says
        /// you are holding it, the early return declined to undo that. The result was a bottle
        /// standing in the rack while the HUD, the interactor and the host all agreed it was in your
        /// hand — reported from a real session, and invisible to every test that only checked one
        /// step at a time.
        /// </para>
        /// So this asserts the <i>sequence</i>: shelf, then held. A test that only published the held
        /// location would pass against the broken version.
        /// </summary>
        [Test]
        public void ABottleYouPickedUp_EndsUpInYourHandAndStaysThere()
        {
            var runtime = NewRuntime();
            var rack = NewContainer(SampleRack.DefaultRackId, 4);

            var hands = new GameObject("CarrySocket").transform;
            var previous = VialFeed.Hands;
            VialFeed.Hands = new StubHands { LocalClientId = 3UL, Socket = hands };

            try
            {
                var reconciler = new VialReconciler(runtime);
                var sample = new SampleId(11);

                // It is on the shelf, which is the state the pickup interrupts.
                reconciler.Reconcile(new[] { OnRack(sample, slot: 0) });
                Assert.AreSame(rack.Slot(0), runtime.PropFor(sample).transform.parent);

                // The host says you took it.
                reconciler.Reconcile(new[] { Held(sample, holder: 3UL) });

                Assert.AreSame(hands, runtime.PropFor(sample).transform.parent,
                    "The bottle is still on the rack. The interactor thinks it is in your hand, the " +
                    "host agrees, and the only thing that disagrees is the object you can see.");

                // Idempotent: it runs every frame the bottle stays there.
                reconciler.Reconcile(new[] { Held(sample, holder: 3UL) });
                Assert.AreSame(hands, runtime.PropFor(sample).transform.parent);
                // Occupancy is read off the slot transforms, so taking the bottle has to free the slot
                // as a consequence of it being re-parented rather than as a second bookkeeping step.
                Assert.AreEqual(4, rack.FreeSlots,
                    "The rack is still counting a bottle that is now in somebody's hand, so nobody " +
                    "else can put one down where it used to be.");
            }
            finally
            {
                VialFeed.Hands = previous;
                Object.DestroyImmediate(hands.gameObject);
                Retire(rack);
                Object.DestroyImmediate(runtime.gameObject);
            }
        }

        private sealed class StubHands : IPlayerHands
        {
            public ulong LocalClientId { get; set; }
            public Transform Socket { get; set; }

            public Transform CarrySocket(ulong clientId) => clientId == LocalClientId ? Socket : null;
        }

        private static VialPlacement Held(SampleId id, ulong holder) =>
            new(id, "WERK-1 QUENCH 1", 62f, SampleLocation.Held(holder));

        private static VialPlacement InCrate(SampleId id, int slot) =>
            new(id, "WERK-1 QUENCH 1", 100f, SampleLocation.InCrate("intake", slot));

        private static VialPlacement OnRack(SampleId id, int slot) =>
            new(id, "WERK-1 QUENCH 1", 62f, SampleLocation.OnSurface(SampleRack.DefaultRackId, slot));

        /// <summary>
        /// Promise: the solvent bottle is in one place, and a client that is told where puts it there.
        /// <para>
        /// This is what keeps a joined client able to flush. Flushing needed no vial, which is why it
        /// was one of the first things a client could do; #14 made it need a bottle, and that would
        /// have quietly taken it away if the bottle had stayed on the host. So the record travels and
        /// every process builds the same prop out of it — the same three moves the vial reconciler
        /// makes, minus the destruction, because a bottle is refilled rather than spent.
        /// </para>
        /// The charge count is asserted alongside the parenting because it is the number the button on
        /// the instrument prints: a bottle in the right cradle showing the wrong count would offer a
        /// flush the host is about to refuse.
        /// </summary>
        [Test]
        public void AClientsSolventBottle_FollowsTheOneItIsToldAbout()
        {
            var runtime = NewRuntime();
            var station = NewWashStation();
            var rack = NewContainer(SampleRack.DefaultRackId, 4);

            var reconciler = new BottleReconciler(runtime);
            const string id = "bottle-1";

            // Appeared, in its cradle, empty.
            reconciler.Reconcile(new[] { BottleAtStation(id, charges: 0, slot: 1) });
            var prop = runtime.BottlePropFor(id);
            Assert.IsNotNull(prop, "A bottle the host listed has no prop in the room.");
            Assert.AreSame(station.Slot(1), prop.transform.parent);
            Assert.AreEqual(0, prop.Charges);

            // Filled. Nothing moved, but the one number a prompt reads did.
            reconciler.Reconcile(
                new[] { BottleAtStation(id, charges: SolventStore.BottleCapacity, slot: 1) });
            Assert.AreEqual(SolventStore.BottleCapacity, runtime.BottlePropFor(id).Charges,
                "A client whose bottle still reads empty cannot flush anything, and the host would " +
                "have let it.");

            // Somebody carried it off and parked it on a rack, where it takes a hole a vial wanted.
            reconciler.Reconcile(new[] { BottleOnRack(id, charges: 3, slot: 0) });
            Assert.AreSame(rack.Slot(0), runtime.BottlePropFor(id).transform.parent);
            Assert.AreEqual(3, runtime.BottlePropFor(id).Charges);
            Assert.AreEqual(3, rack.FreeSlots,
                "A bottle on a rack has to occupy the slot, or vials will be stacked on top of it.");

            // A publish that mentions nothing must not delete the bottles: unlike a vial, a bottle
            // never stops existing, so an empty list means "not told yet".
            reconciler.Reconcile(System.Array.Empty<BottlePlacement>());
            Assert.IsNotNull(runtime.BottlePropFor(id),
                "The solvent bottle vanished on a frame the host had not published yet.");

            LabRuntime.ForgetFixture(WashStation.FixtureId, station.transform);
            Object.DestroyImmediate(station.gameObject);
            Retire(rack);
            Object.DestroyImmediate(runtime.gameObject);
        }

        // -- Results slips ------------------------------------------------------------------------
        //
        // Paper is the third kind of local prop (§3.2), and the one with a life cycle the other two do
        // not have: a slip is CONSUMED by filing. A vial is spent and a bottle is refilled; only this
        // one stops existing because somebody used it correctly, which makes the destroy case the
        // mechanic rather than tidy-up.

        private const string TrayMachineId = "karl_fischer-0";

        /// <summary>
        /// Promise: a slip is in one place, and a client that is told where puts it there.
        /// <para>
        /// This is what makes filing a result possible from a joined seat at all. A printout is
        /// spawned host-side when a run finishes and nothing carried it, so a client's instrument
        /// trays were empty: they could read the numbers off a machine's screen and never walk them to
        /// the desk. Two players run instruments in parallel and only one could do the paperwork.
        /// </para>
        /// So this asserts the same three moves the vial reconciler makes — appeared, moved, stopped
        /// appearing — with the third one carrying more weight here, because "stopped appearing" is
        /// how a filed slip is retired and a stale one is a second chance to file the same numbers.
        /// </summary>
        [Test]
        public void AClientsSlips_FollowThePaperItIsToldAbout()
        {
            var runtime = NewRuntime();
            var tray = NewTray(TrayMachineId);
            var rack = NewContainer(SampleRack.DefaultRackId, 4);

            var reconciler = new SlipReconciler(runtime);
            const int ticket = 3;

            // Printed.
            reconciler.Reconcile(new[] { InTray(ticket, resultKey: 11) });
            var prop = runtime.SlipPropFor(ticket);
            Assert.IsNotNull(prop, "A slip the host printed has no paper in the room.");
            Assert.AreSame(tray.Tray, prop.transform.parent,
                "The slip is not in the instrument's output tray, so there is nothing to walk to the desk.");
            Assert.AreEqual(11, prop.ResultKey,
                "The slip has to name its run, or a client can never read what is on it.");
            Assert.AreEqual("WERK-1 QUENCH 1", prop.RecordTag,
                "The tag printed on the paper is what tells the player which record this belongs to.");
            Assert.IsTrue(prop.GetComponent<Collider>().enabled,
                "A slip in a tray is exactly the thing you walk up and take; §5.4's mediated access " +
                "is about the vial inside the instrument, not the paper it hands out.");

            // Racked. Paper competes with bottles for the same shelf space (§5.5), so it has to take
            // the hole rather than float over it.
            reconciler.Reconcile(new[] { OnRack(ticket, resultKey: 11, slot: 2) });
            Assert.AreSame(rack.Slot(2), runtime.SlipPropFor(ticket).transform.parent,
                "Somebody set the slip down and this client is still showing it in the tray.");
            Assert.AreEqual(3, rack.FreeSlots,
                "A slip in a rack has to occupy the hole, or a vial will be stacked on top of it.");

            // Filed. The host discards the ticket, so the row stops appearing rather than tombstoning.
            reconciler.Reconcile(System.Array.Empty<SlipPlacement>());
            Assert.IsNull(runtime.SlipPropFor(ticket),
                "A filed slip is still lying in the rack. Picking it up would offer to file numbers " +
                "that are already on the record.");

            Retire(tray);
            Retire(rack);
            Object.DestroyImmediate(runtime.gameObject);
        }

        /// <summary>
        /// Promise: a slip you picked up is in your hand, and stays there.
        /// <para>
        /// The same bug <see cref="ABottleYouPickedUp_EndsUpInYourHandAndStaysThere"/> guards, written
        /// out again for paper because it is the kind of mistake a new reconciler reintroduces by
        /// copying the shape and not the reasoning. Between pressing the key and the host's next
        /// publish the replicated location still names the tray, so the reconciler parents the slip
        /// back into the instrument; an early return for a locally-held prop then declines to undo it,
        /// and the paper sits in the tray for ever while the interactor is certain it is in your hand.
        /// </para>
        /// So this asserts the <i>sequence</i>: tray, then held. A test that only published the held
        /// location would pass against the broken version.
        /// </summary>
        [Test]
        public void ASlipYouPickedUp_EndsUpInYourHandAndStaysThere()
        {
            var runtime = NewRuntime();
            var tray = NewTray(TrayMachineId);

            var hands = new GameObject("CarrySocket").transform;
            var previous = VialFeed.Hands;
            VialFeed.Hands = new StubHands { LocalClientId = 3UL, Socket = hands };

            try
            {
                var reconciler = new SlipReconciler(runtime);
                const int ticket = 5;

                reconciler.Reconcile(new[] { InTray(ticket, resultKey: 2) });
                Assert.AreSame(tray.Tray, runtime.SlipPropFor(ticket).transform.parent);

                reconciler.Reconcile(new[] { Held(ticket, resultKey: 2, holder: 3UL) });
                Assert.AreSame(hands, runtime.SlipPropFor(ticket).transform.parent,
                    "The slip is still in the tray. The interactor thinks it is in your hand, the host " +
                    "agrees, and the only thing that disagrees is the object you can see.");

                // Idempotent: it runs every frame the paper stays there.
                reconciler.Reconcile(new[] { Held(ticket, resultKey: 2, holder: 3UL) });
                Assert.AreSame(hands, runtime.SlipPropFor(ticket).transform.parent);
            }
            finally
            {
                VialFeed.Hands = previous;
                Object.DestroyImmediate(hands.gameObject);
                Retire(tray);
                Object.DestroyImmediate(runtime.gameObject);
            }
        }

        /// <summary>
        /// Promise: when two players reach for the same tray, the paper ends up in exactly one pair of
        /// hands.
        /// <para>
        /// A slip is consumed by filing, so a duplicate is not a cosmetic glitch — it is two players
        /// each believing they can put the same numbers on a record. The host arbitrates
        /// (<c>ResultSlips.TryClaim</c> refuses a slip somebody else already holds) and the loser's
        /// callback never runs, so their <c>Carried</c> stays empty. What this checks is the other
        /// half: that the loser's <i>room</i> agrees, showing the paper in the winner's hands rather
        /// than leaving a second copy in the tray.
        /// </para>
        /// The colliders are the specific assertion. There is one prop per ticket, so a slip that
        /// stayed targetable in somebody else's hands would let the loser press it again and be
        /// refused a second time — a request the game invited them to make, which hard rule 3 forbids.
        /// </summary>
        [Test]
        public void ASlipSomebodyElseTook_MovesToTheirHandsAndCannotBeTargeted()
        {
            var runtime = NewRuntime();
            var tray = NewTray(TrayMachineId);

            var mine = new GameObject("MyHands").transform;
            var theirs = new GameObject("TheirHands").transform;

            var previous = VialFeed.Hands;
            VialFeed.Hands = new TwoPlayerHands { LocalClientId = 1UL, Mine = mine, Theirs = theirs };

            try
            {
                var reconciler = new SlipReconciler(runtime);
                const int ticket = 8;

                reconciler.Reconcile(new[] { InTray(ticket, resultKey: 4) });
                var prop = runtime.SlipPropFor(ticket);
                Assert.AreSame(tray.Tray, prop.transform.parent);

                // The host answered the other player first.
                reconciler.Reconcile(new[] { Held(ticket, resultKey: 4, holder: 2UL) });

                Assert.AreSame(theirs, runtime.SlipPropFor(ticket).transform.parent,
                    "The tray still shows a slip the host has already given to somebody else. Two " +
                    "players are looking at one piece of paper.");
                Assert.AreEqual(1, runtime.SlipProps.Count,
                    "There must be exactly one prop per ticket; a second is a second chance to file " +
                    "the same numbers.");

                foreach (var collider in prop.GetComponentsInChildren<Collider>(true))
                {
                    Assert.IsFalse(collider.enabled,
                        "You can still aim at paper in another player's hands, so the game will keep " +
                        "offering a pick-up the host is bound to refuse.");
                }
            }
            finally
            {
                VialFeed.Hands = previous;
                Object.DestroyImmediate(mine.gameObject);
                Object.DestroyImmediate(theirs.gameObject);
                Retire(tray);
                Object.DestroyImmediate(runtime.gameObject);
            }
        }

        /// <summary>
        /// Promise: an instrument hands its paper out and keeps its vial.
        /// <para>
        /// Both are recorded at <c>InMachine(instanceId)</c>, and the two must resolve differently or
        /// one of them is wrong: a vial is inside the sample path where the station mediates access
        /// (§5.4), and a slip is in an output tray, which is exactly the thing you walk up and take.
        /// A single socket lookup serving both would either park printouts inside the titrator or make
        /// the tray unreachable — and an unreachable tray is a result nobody can file.
        /// </para>
        /// </summary>
        [Test]
        public void ASlipInATray_IsTakeableWhereAVialInTheSameInstrumentIsNot()
        {
            var tray = NewTray(TrayMachineId);
            var sampleSocket = NewContainer(TrayMachineId + "-path", 1);

            try
            {
                var inMachine = SampleLocation.InMachine(TrayMachineId, 0);

                var paper = PropSockets.ForSlip(inMachine, null, out bool paperReachable);
                Assert.AreSame(tray.Tray, paper, "A printout belongs in the output tray, not the sample path.");
                Assert.IsTrue(paperReachable, "A tray you cannot reach into is a result nobody can file.");

                var glass = SampleLocation.InMachine(TrayMachineId + "-path", 0);
                PropSockets.For(glass, null, out bool glassReachable);
                Assert.IsFalse(glassReachable,
                    "§5.4: a vial comes back out by pressing the instrument, not by grabbing through " +
                    "its door.");
            }
            finally
            {
                Retire(tray);
                Retire(sampleSocket);
            }
        }

        /// <summary>
        /// Promise: a client's slip shows the host's numbers, and holds none of its own.
        /// <para>
        /// §3.1 forbids a client computing a test result, and a client permitted to <i>post</i> one
        /// would be the same hole with an extra step — so the paper names its run by
        /// <c>ResultView.Key</c> and looks the values up in the one list every published reading
        /// already travels in. That is also what stops the slip in a player's hand and the panel at
        /// the desk quoting different figures for the same run: there is one wire path, not two.
        /// </para>
        /// The lookup is asked once and then remembered, because a finished run's numbers never
        /// change and the prompt on the paper is drawn every frame you are looking at it.
        /// </summary>
        [Test]
        public void AReplicatedSlip_NamesItsReadingRatherThanCarryingOne()
        {
            var go = new GameObject("Printout_UnderTest");
            var slip = go.AddComponent<PrintoutProp>();

            var previous = SlipFeed.Numbers;
            int asked = 0;

            try
            {
                slip.Bind(ticket: 2, resultKey: 42, sampleId: new SampleId(7), isBlank: false,
                          machineName: "Karl Fischer", recordTag: "WERK-1 QUENCH 1");

                SlipFeed.Numbers = null;
                Assert.IsNull(slip.Result,
                    "A replicated slip must not carry values of its own; with nothing to look them up " +
                    "in, it knows nothing.");

                SlipFeed.Numbers = (int key, out TestResult result) =>
                {
                    asked++;
                    result = key == 42 ? new TestResult { Values = { ["water_ppm"] = 310f } } : null;
                    return result != null;
                };

                Assert.IsNotNull(slip.Result);
                Assert.AreEqual(310f, slip.Result.Values["water_ppm"], 1e-3f,
                    "The numbers on the paper have to be the host's own, fetched by key.");
                Assert.AreEqual(1, asked,
                    "A finished run's numbers never change, and the prompt is drawn every frame the " +
                    "player is looking at the slip.");
            }
            finally
            {
                SlipFeed.Numbers = previous;
                Object.DestroyImmediate(go);
            }
        }

        private sealed class TwoPlayerHands : IPlayerHands
        {
            public ulong LocalClientId { get; set; }
            public Transform Mine { get; set; }
            public Transform Theirs { get; set; }

            public Transform CarrySocket(ulong clientId) => clientId == LocalClientId ? Mine : Theirs;
        }

        private static SlipPlacement InTray(int ticket, int resultKey) =>
            new(ticket, resultKey, new SampleId(7), false, "Karl Fischer", "WERK-1 QUENCH 1",
                SampleLocation.InMachine(TrayMachineId, 0));

        private static SlipPlacement OnRack(int ticket, int resultKey, int slot) =>
            new(ticket, resultKey, new SampleId(7), false, "Karl Fischer", "WERK-1 QUENCH 1",
                SampleLocation.OnSurface(SampleRack.DefaultRackId, slot));

        private static SlipPlacement Held(int ticket, int resultKey, ulong holder) =>
            new(ticket, resultKey, new SampleId(7), false, "Karl Fischer", "WERK-1 QUENCH 1",
                SampleLocation.Held(holder));

        /// <summary>
        /// An instrument, as far as paper is concerned: a fixture and the output tray under it,
        /// registered the way <c>MachineStation.OnEnable</c> would be. The tray is a child socket
        /// rather than the station itself — that is what the scene builder wires, and it is what makes
        /// the two <c>InMachine(instanceId)</c> lookups resolve to different places.
        /// </summary>
        private readonly struct TrayFixture
        {
            public readonly string InstanceId;
            public readonly Transform Station;
            public readonly Transform Tray;

            public TrayFixture(string instanceId, Transform station, Transform tray)
            {
                InstanceId = instanceId;
                Station = station;
                Tray = tray;
            }
        }

        private static TrayFixture NewTray(string instanceId)
        {
            var go = new GameObject($"Machine_{instanceId}");
            var tray = new GameObject("PrintoutSocket").transform;
            tray.SetParent(go.transform, false);

            LabRuntime.RegisterFixture(instanceId, go.transform);
            LabRuntime.RegisterTray(instanceId, tray);
            return new TrayFixture(instanceId, go.transform, tray);
        }

        /// <summary>
        /// Withdraw and destroy an instrument. The fixture and tray tables are static and outlive a
        /// test, so a registration left behind would be a destroyed transform the next test resolved
        /// an id to.
        /// </summary>
        private static void Retire(TrayFixture machine)
        {
            LabRuntime.ForgetFixture(machine.InstanceId, machine.Station);
            Object.DestroyImmediate(machine.Station.gameObject);
        }

        private static BottlePlacement BottleAtStation(string id, int charges, int slot) =>
            new(id, charges, SolventStore.BottleCapacity,
                SampleLocation.OnSurface(WashStation.FixtureId, slot));

        private static BottlePlacement BottleOnRack(string id, int charges, int slot) =>
            new(id, charges, SolventStore.BottleCapacity,
                SampleLocation.OnSurface(SampleRack.DefaultRackId, slot));

        /// <summary>
        /// A <see cref="LabRuntime"/> with the three prop prefabs and no lab — the shape a client is
        /// in. <c>Awake</c> does not run in edit mode, so nothing here has to be undone.
        /// </summary>
        private static LabRuntime NewRuntime()
        {
            var go = new GameObject("LabRuntime_UnderTest");
            var runtime = go.AddComponent<LabRuntime>();

            // Parented to the runtime so tearing that down takes the prefabs with it, and inactive so
            // nothing in a prefab itself ever ticks.
            var vialGo = new GameObject("VialPrefab");
            vialGo.transform.SetParent(go.transform, false);
            vialGo.SetActive(false);

            var bottleGo = new GameObject("BottlePrefab");
            bottleGo.transform.SetParent(go.transform, false);
            bottleGo.SetActive(false);

            var slipGo = new GameObject("PrintoutPrefab");
            slipGo.transform.SetParent(go.transform, false);
            slipGo.SetActive(false);

            // Paper is the one prop whose colliders a test asserts on: whether you may aim at a slip
            // is how "somebody else has it" is expressed in the room. Carryable.AttachTo switches them,
            // so there has to be one to switch.
            slipGo.AddComponent<BoxCollider>();

            var so = new UnityEditor.SerializedObject(runtime);
            so.FindProperty("vialPrefab").objectReferenceValue = vialGo.AddComponent<VialProp>();
            so.FindProperty("bottlePrefab").objectReferenceValue = bottleGo.AddComponent<SolventBottle>();
            so.FindProperty("printoutPrefab").objectReferenceValue = slipGo.AddComponent<PrintoutProp>();
            so.ApplyModifiedPropertiesWithoutUndo();

            return runtime;
        }

        /// <summary>The wash station's cradles, registered the way its <c>OnEnable</c> would be.</summary>
        private static WashStation NewWashStation()
        {
            var go = new GameObject("WashStation_UnderTest");
            var station = go.AddComponent<WashStation>();

            LabRuntime.RegisterFixture(WashStation.FixtureId, go.transform, station);
            return station;
        }

        /// <summary>
        /// A slotted container under a given id, registered the way its <c>OnEnable</c> would be.
        /// A rack stands in for the crate as well: what is under test is <see cref="IVialSlots"/>,
        /// and the crate answers it the same way with a different mesh behind it.
        /// </summary>
        private static SampleRack NewContainer(string id, int slots)
        {
            var go = new GameObject($"Container_{id}");
            var rack = go.AddComponent<SampleRack>();

            var so = new UnityEditor.SerializedObject(rack);
            so.FindProperty("rackId").stringValue = id;
            so.FindProperty("slotCount").intValue = slots;
            so.ApplyModifiedPropertiesWithoutUndo();

            LabRuntime.RegisterFixture(id, go.transform, rack);
            return rack;
        }

        /// <summary>
        /// Withdraw and destroy a container. The fixture table is static and outlives a test, so a
        /// registration left behind would be a destroyed transform the next test resolved an id to.
        /// </summary>
        private static void Retire(SampleRack rack)
        {
            LabRuntime.ForgetFixture(rack.RackId, rack.transform);
            Object.DestroyImmediate(rack.gameObject);
        }

        /// <summary>
        /// Promise: a host reads its own lab, not a snapshot of it.
        /// <para>
        /// A host holds both — it publishes the views everyone else reads — and reading its own
        /// snapshot back would put its screens one publish behind its own simulation for nothing.
        /// </para>
        /// </summary>
        [Test]
        public void WhereBothViewsExist_TheHostReadsItsOwnLab()
        {
            var host = new HostLabView(new LabState(catalog, OneDayPlan(), 1234));
            var replicated = new ReplicatedLabView(null);

            LabView.Host = host;
            LabView.Replicated = replicated;
            Assert.AreSame(host, LabView.Current);

            LabView.Host = null;
            Assert.AreSame(replicated, LabView.Current);

            LabView.Replicated = null;
            Assert.IsNull(LabView.Current, "No session and no lab is a real state; every caller handles it.");
        }

        // -- helpers ------------------------------------------------------------------------------

        /// <summary>
        /// The states a station actually distinguishes, each as a fresh instrument. Named so a
        /// parity failure says which one broke.
        /// </summary>
        private IEnumerable<(string State, MachineInstance Machine)> InstrumentStates()
        {
            var idle = Instrument();
            yield return ("idle and empty", idle);

            var loaded = Instrument();
            loaded.LoadedSample = new SampleId(1);
            yield return ("loaded, not yet run", loaded);

            var running = Instrument();
            running.LoadedSample = new SampleId(2);
            running.TryBeginRun();
            running.Tick(running.RunSeconds * 0.25f);   // a quarter in, whatever the tables say it costs
            yield return ("running", running);

            var finished = Instrument();
            finished.LoadedSample = new SampleId(3);
            finished.TryBeginRun();
            finished.Tick(10_000f);
            yield return ("holding a finished result", finished);

            var certified = Instrument();
            certified.LastCheck = CertificateFor(certified, day: 2);
            certified.LastBlank = new TestResult { IsBlank = true, Values = { ["water_ppm"] = 120f } };
            certified.LastBlankDay = 2;
            yield return ("carrying the §5.2 and §5.3 tells", certified);
        }

        /// <summary>
        /// One Karl Fischer titrator, scaled the way a test lab is, so the parity check runs against
        /// the awkward end of the range rather than the comfortable one.
        /// </summary>
        private MachineInstance Instrument() =>
            new("karl_fischer-0", content.Machines["karl_fischer"]) { TimeScale = 0.05f };

        /// <summary>A certificate that reads 20% high — comfortably outside the §5.3 tolerance.</summary>
        private CalibrationCheck CertificateFor(MachineInstance machine, int day)
        {
            var standard = ReferenceStandard.FromProfiles(content.Profiles.Values.ToList());
            var readout = new TestResult { MachineId = machine.Def.Id, IsReference = true };

            foreach (var element in machine.Def.Measures)
            {
                if (element == null || machine.Def.IsBlindTo(element.Id)) continue;
                if (!standard.TryGet(element.Id, out float certified)) continue;
                readout.Values[element.Id] = certified * 1.2f;
            }

            return CalibrationCheck.From(standard, readout, machine.Def, day);
        }

        /// <summary>A sample walked far enough along §5.1 that an instrument will take it.</summary>
        private SampleState ReadySample()
        {
            var rng = new Rng(31337);
            var profile = content.Profiles["quench_oil_cold"];
            var generated = new SampleGenerator(content.AllFaults)
                .Generate(GenerationRequest.Default(profile, "WERK-1 QUENCH 1", 1), ref rng);

            Assert.IsNotNull(generated, "Generator produced nothing.");
            Assert.IsTrue(SampleLifecycle.TryMove(generated.State, SampleLocation.OnSurface("bench", 0), out var move), move);
            Assert.IsTrue(SampleLifecycle.TryLog(generated.State, "WERK-1 QUENCH 1", out var log), log);
            Assert.IsTrue(SampleLifecycle.TryPrep(generated.State, out var prep), prep);
            return generated.State;
        }

        private static ContractPlan OneDayPlan() => new()
        {
            Id = "test",
            DisplayName = "Test",
            Days = new List<DayPlan>
            {
                new()
                {
                    SampleCount = 2,
                    ProfileIds = new[] { "quench_oil_cold" },
                    BorderlineCount = 0,
                    HealthyChance = 0.3f,
                    DaySeconds = 600f
                }
            }
        };
    }
}
