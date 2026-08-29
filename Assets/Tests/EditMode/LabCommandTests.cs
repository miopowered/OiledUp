using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Editor.Content;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.World;
using Residue.Net;
using Residue.Net.Session;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards the request layer: §3.1's "every interaction is a request the host validates".
    /// <para>
    /// The failure these exist to catch is not a crash either. A command layer that trusts its caller
    /// works perfectly for every player who is not trying anything — the lab behaves, the prompts
    /// read correctly, and a single modified client can quietly load samples it is not holding, file
    /// numbers it made up, and operate instruments from the car park. So most of what follows asks
    /// the executor to do something the <i>prompt</i> would never have offered, and demands a refusal
    /// with a sentence attached.
    /// </para>
    /// The other half guards the seam. Single player is the same scene with nobody connected, and the
    /// whole design rests on it being the same code path with a zero-length hop — so it is tested as
    /// such rather than assumed.
    /// </summary>
    public sealed class LabCommandTests
    {
        private ContentSet content;
        private ContentCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            content = ContentBuilder.BuildInMemory();
            catalog = ContentBuilder.BuildCatalogInMemory(content);
        }

        [TearDown]
        public void TearDown()
        {
            // The seam is process-wide, so a test that installs into it has to put it back or the
            // next test in the run inherits a router that points at a dead lab.
            LabCommands.Router = null;
            LabCommands.Executor = null;

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

        // -- Doubles -----------------------------------------------------------------------------------

        /// <summary>One player, with hands and a position the test controls outright.</summary>
        private sealed class TestActor : ILabActor
        {
            public TestActor(ulong clientId = 0) => ClientId = clientId;

            public ulong ClientId { get; }
            public string DisplayName => $"player-{ClientId}";
            public bool HasPosition { get; set; } = true;
            public Vector3 Position { get; set; } = Vector3.zero;
            public LabGrip Grip { get; private set; } = LabGrip.Empty;
            public void SetGrip(LabGrip grip) => Grip = grip;
        }

        private sealed class TestStations : ILabStations
        {
            private readonly Dictionary<string, Vector3> placed = new();

            public void Place(string fixtureId, Vector3 at) => placed[fixtureId] = at;

            public bool TryLocate(string fixtureId, out Vector3 position) =>
                placed.TryGetValue(fixtureId, out position);
        }

        // -- Fixtures ----------------------------------------------------------------------------------

        private static ContractPlan OneDay(int samples = 4) => new()
        {
            Id = "test",
            DisplayName = "Test",
            Days = new List<DayPlan>
            {
                new()
                {
                    SampleCount = samples,
                    ProfileIds = new[] { "quench_oil_cold" },
                    BorderlineCount = 0,
                    HealthyChance = 0.3f,
                    DaySeconds = 600f
                }
            }
        };

        private LabState NewLab(int seed = 4242)
        {
            var lab = new LabState(catalog, OneDay(), seed);
            lab.Install(catalog.Machine("elemental"), "elemental");

            // The truck now turns up a quarter of the way into the shift and every vial arrives in a
            // sealed box (#30, #31). None of the tests below are about the bay, so the fixture lands
            // the delivery and cuts the tape rather than repeating it in fifteen places. What the bay
            // itself promises is guarded in DeliveryTests.
            lab.Deliveries.ArrivalShiftFraction = 0f;
            lab.BeginDay();
            DeliverAndOpenEverything(lab);

            return lab;
        }

        /// <summary>Land the whole delivery and open every box, so vials are reachable.</summary>
        private static void DeliverAndOpenEverything(LabState lab)
        {
            // Wide enough that the bay cannot hold anything back — capacity is what DeliveryTests
            // exercises, and a fixture that quietly hit it would fail these tests for the wrong reason.
            lab.Deliveries.Capacity = 64;
            lab.Tick(0.01f);

            foreach (var carton in lab.Deliveries.Cartons)
            {
                Assert.IsTrue(lab.Deliveries.TryOpen(carton.Id, 0, out string refusal), refusal);
            }
        }

        /// <summary>Out of the crate and agitated — ready for an instrument.</summary>
        private static void Ready(LabState lab, SampleState sample)
        {
            Assert.IsTrue(SampleLifecycle.TryMove(sample, SampleLocation.OnSurface("bench", 0), out var move), move);
            Assert.IsTrue(SampleLifecycle.TryPrep(sample, out var prep), prep);
        }

        // -----------------------------------------------------------------------------------------
        // 1. The seam. Single player has to be the same code with the hop taken out.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: single player is the same scene with nobody connected, and it keeps working.
        /// <para>
        /// With no router installed, <see cref="LabCommands.Send"/> must run the executor and call
        /// back before it returns. Every call site does its local half in that callback — releasing a
        /// carried vial, destroying a slip — so if the answer ever stopped arriving synchronously on
        /// this path, single player would appear to accept every action and do none of them, one
        /// frame late and only sometimes.
        /// </para>
        /// </summary>
        [Test]
        public void WithNoSession_ASendIsAnsweredBeforeItReturns()
        {
            var lab = NewLab();
            LabCommands.Executor = new LabCommandExecutor(lab);

            var actor = new TestActor();
            var sample = lab.OpenSamples().First();

            bool answered = false;
            LabCommands.Send(actor, LabCommand.TakeVial(sample.Id), result =>
            {
                answered = true;
                Assert.IsTrue(result.Accepted, result.Refusal);
            });

            Assert.IsTrue(answered,
                "The callback must have run already. Single player is the case where the hop is zero " +
                "long, not a case where it is short.");

            Assert.AreEqual(GripKind.Vial, actor.Grip.Kind);
            Assert.AreEqual(SampleLocationKind.Held, sample.Location.Kind);
        }

        /// <summary>
        /// A process with no lab refuses rather than pretending. This is what a client does before it
        /// has a router: without it, every action on a half-connected client would silently succeed
        /// against nothing.
        /// </summary>
        [Test]
        public void WithNoLab_EveryCommandIsRefusedWithASentence()
        {
            LabCommands.Executor = null;
            LabCommands.Router = null;

            LabCommandResult answer = default;
            LabCommands.Send(new TestActor(), LabCommand.EndDay(), r => answer = r);

            Assert.IsFalse(answer.Accepted);
            Assert.IsNotEmpty(answer.Refusal);
        }

        // -----------------------------------------------------------------------------------------
        // 2. The caller is not trusted. Each of these is something no prompt would ever offer.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a client cannot act on something it is not holding.
        /// <para>
        /// The station's prompt checks this too, but a prompt is a drawing. The host tracks hands in
        /// <see cref="LabGrip"/> precisely so this refusal does not depend on the asking process
        /// having been honest about them.
        /// </para>
        /// </summary>
        [Test]
        public void LoadingASampleYouAreNotHolding_IsRefused()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);

            var sample = lab.OpenSamples().First();
            Ready(lab, sample);

            // Empty-handed, and asking for a sample sitting on a bench across the lab.
            var result = executor.Execute(new TestActor(), LabCommand.LoadMachine("elemental"));

            Assert.IsFalse(result.Accepted, "An empty-handed player loaded a vial into an instrument.");
            Assert.IsTrue(lab.FindMachine("elemental").IsEmpty);
        }

        /// <summary>
        /// Promise: you operate the instrument you are standing at.
        /// <para>
        /// Not a security boundary — §3.1 gives a client authority over its own transform, so a
        /// determined one can lie about where it is. It stops a stale prompt or a lagged click
        /// reaching across the room, which is the failure that actually happens in a four-player
        /// shift.
        /// </para>
        /// </summary>
        [Test]
        public void OperatingAnInstrumentFromAcrossTheRoom_IsRefused()
        {
            var lab = NewLab();
            var stations = new TestStations();
            stations.Place("elemental", new Vector3(0f, 0f, 0f));

            var executor = new LabCommandExecutor(lab, stations);
            var actor = new TestActor { Position = new Vector3(0f, 0f, 40f) };

            var far = executor.Execute(actor, LabCommand.RunBlank("elemental"));
            Assert.IsFalse(far.Accepted, "A blank was started from forty metres away.");
            Assert.IsFalse(lab.FindMachine("elemental").IsRunning);

            actor.Position = new Vector3(0f, 0f, 1f);
            var near = executor.Execute(actor, LabCommand.RunBlank("elemental"));
            Assert.IsTrue(near.Accepted, near.Refusal);
            Assert.IsTrue(lab.FindMachine("elemental").IsRunning);
        }

        /// <summary>
        /// Promise: two players reaching for the same bottle, and only one of them gets it.
        /// <para>
        /// With one pair of hands each and instruments that block, a duplicated vial is not a cosmetic
        /// bug — it is two records of the same oil, and §4.5's whole volume economy stops meaning
        /// anything.
        /// </para>
        /// </summary>
        [Test]
        public void TwoPlayersCannotTakeTheSameVial()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);

            var sample = lab.OpenSamples().First();
            var first = new TestActor(1);
            var second = new TestActor(2);

            Assert.IsTrue(executor.Execute(first, LabCommand.TakeVial(sample.Id)).Accepted);

            var stolen = executor.Execute(second, LabCommand.TakeVial(sample.Id));
            Assert.IsFalse(stolen.Accepted, "Two players walked off with the same vial.");
            Assert.AreEqual(GripKind.Empty, second.Grip.Kind);
            Assert.AreEqual(1UL, sample.Location.HolderClientId);
        }

        /// <summary>
        /// Promise: a vial inside a running instrument is not something you can palm off the bench.
        /// </summary>
        [Test]
        public void TakingAVialThatIsInsideAnInstrument_IsRefused()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var machine = lab.FindMachine("elemental");

            var sample = lab.OpenSamples().First();
            Ready(lab, sample);
            Assert.AreEqual(LoadRefusal.Accepted, machine.TryLoad(sample));

            var result = executor.Execute(new TestActor(), LabCommand.TakeVial(sample.Id));

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(SampleLocationKind.InMachine, sample.Location.Kind);
        }

        /// <summary>
        /// Promise: a verdict is one of the three the game defines.
        /// <para>
        /// <see cref="Verdict"/> travels as an integer because that is what fits in a fixed-shape
        /// message, so it arrives as a number that has to be checked rather than as a value that has
        /// already been cast. An unchecked cast would let a client file verdict 7, which
        /// <c>ConsequenceResolver</c> would then score as whichever real verdict shares its number.
        /// </para>
        /// </summary>
        [Test]
        public void AVerdictOutsideTheThreeTheGameDefines_IsRefused()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var sample = lab.OpenSamples().First();

            var forged = new LabCommand(LabCommandKind.FileVerdict, sample: sample.Id, amount: 7);
            var result = executor.Execute(new TestActor(), forged);

            Assert.IsFalse(result.Accepted);
            Assert.IsFalse(sample.FiledVerdict.HasValue);
        }

        /// <summary>
        /// Promise: a message with a nonsense action in it does nothing at all.
        /// <see cref="LabCommandMessage"/> carries the kind as a byte for exactly this reason — an
        /// enum field would have been cast before anything could look at it.
        /// </summary>
        [Test]
        public void AnUnknownCommandKind_ArrivesAsNothingAndIsRefused()
        {
            var message = new LabCommandMessage { Kind = 200 };
            Assert.AreEqual(LabCommandKind.None, message.ToCommand().Kind);

            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            Assert.IsFalse(executor.Execute(new TestActor(), message.ToCommand()).Accepted);
        }

        // -----------------------------------------------------------------------------------------
        // 3. Filing results. A client says which slip; it never says what is on it.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: the numbers in a record are the host's own.
        /// <para>
        /// §3.1 forbids a client computing a test result. A client permitted to <i>post</i> one
        /// instead is the same hole with an extra step, and the archive it writes into is what every
        /// verdict is scored against — so hard rule 1 would be breakable from outside the process.
        /// This test pins the shape that prevents it: the request names a ticket, and what lands in
        /// the record is the exact object the host issued.
        /// </para>
        /// </summary>
        [Test]
        public void FilingASlip_FilesTheHostsOwnResult()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var machine = lab.FindMachine("elemental");

            var sample = lab.OpenSamples().First();
            Ready(lab, sample);
            Assert.AreEqual(LoadRefusal.Accepted, machine.TryLoad(sample));
            Assert.IsTrue(machine.TryBeginRun());
            lab.Tick(machine.RunSeconds + 1f);

            var produced = machine.LastResult;
            Assert.IsNotNull(produced);

            int ticket = lab.Slips.Issue(sample.Id, machine.InstanceId, produced);
            machine.Unload();

            var actor = new TestActor();
            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeSlip(ticket)).Accepted);
            Assert.IsTrue(executor.Execute(actor, LabCommand.FileSlip(ticket)).Accepted);

            Assert.AreEqual(1, sample.Results.Count);
            Assert.AreSame(produced, sample.Results[0],
                "The record must hold the object the instrument produced. Anything else means the " +
                "values travelled from the caller.");
            Assert.AreEqual(GripKind.Empty, actor.Grip.Kind);
        }

        /// <summary>
        /// Promise: a slip files once. A ticket that has been spent names nothing, so a replayed
        /// request cannot append the same reading twice — which would double a sample's evidence
        /// without spending any of its oil.
        /// </summary>
        [Test]
        public void AFiledSlipCannotBeFiledAgain()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var machine = lab.FindMachine("elemental");

            var sample = lab.OpenSamples().First();
            Ready(lab, sample);
            machine.TryLoad(sample);
            machine.TryBeginRun();
            lab.Tick(machine.RunSeconds + 1f);
            machine.Unload();

            int ticket = lab.Slips.Issue(sample.Id, machine.InstanceId, machine.LastResult);

            var actor = new TestActor();
            executor.Execute(actor, LabCommand.TakeSlip(ticket));
            Assert.IsTrue(executor.Execute(actor, LabCommand.FileSlip(ticket)).Accepted);

            var again = executor.Execute(actor, LabCommand.FileSlip(ticket));
            Assert.IsFalse(again.Accepted);
            Assert.AreEqual(1, sample.Results.Count);
        }

        /// <summary>
        /// Promise: you cannot file paper out of somebody else's hand.
        /// </summary>
        [Test]
        public void FilingASlipSomebodyElseIsCarrying_IsRefused()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var machine = lab.FindMachine("elemental");

            machine.TryBeginBlank();
            lab.Tick(machine.RunSeconds + 1f);

            int ticket = lab.Slips.Issue(SampleId.None, machine.InstanceId, machine.LastBlank);

            var carrier = new TestActor(1);
            var thief = new TestActor(2);

            Assert.IsTrue(executor.Execute(carrier, LabCommand.TakeSlip(ticket)).Accepted);

            Assert.IsFalse(executor.Execute(thief, LabCommand.TakeSlip(ticket)).Accepted);
            Assert.IsFalse(executor.Execute(thief, LabCommand.FileSlip(ticket)).Accepted);
        }

        // -----------------------------------------------------------------------------------------
        // 4. The refusals are the gateways'. Nothing here writes its own.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: the sentence the player reads is the one the rule wrote.
        /// <para>
        /// §5.1 closes a record once a verdict is filed, and <see cref="SampleLifecycle"/> phrases
        /// that refusal for the player. If the command layer ever started composing its own, there
        /// would be two wordings of the same rule — the one the prompt shows before you press and the
        /// one you get after — and §9's whole "never punish something you could not have checked"
        /// rests on those being the same sentence.
        /// <para>
        /// The scenario used to be an unlogged vial, which #73 made unreachable: <c>Unpacked</c> leads
        /// straight to <c>Prepped</c> now, so taking a bottle out of the crate is enough to make it
        /// agitatable. An archived record is the remaining case where prep is refused by the gateway
        /// rather than by the executor, and the promise being guarded is unchanged.
        /// </para>
        /// </summary>
        [Test]
        public void ARefusalIsTheGatewaysOwnWords()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);

            var sample = lab.OpenSamples().First();
            Assert.IsTrue(lab.Samples.FileVerdict(sample.Id, Verdict.Normal, null, lab.Day, out var filed), filed);

            var actor = new TestActor();
            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeVial(sample.Id)).Accepted);

            // The record is closed, so §5.1 says it cannot be prepped.
            var expected = SampleLifecycle.Refusal(sample, SampleStage.Prepped);
            Assert.IsNotNull(expected);

            var result = executor.Execute(actor, LabCommand.Agitate());

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(expected, result.Refusal);
        }

        /// <summary>
        /// Promise: an order you cannot afford is refused and charges nothing. The terminal greys the
        /// button out from the balance it is showing, which is a local read and can be stale; the
        /// host's answer is the one that decides.
        /// </summary>
        [Test]
        public void AnUnaffordableOrder_IsRefusedAndChargesNothing()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);

            float before = lab.Economy.Money;
            int huge = Mathf.CeilToInt(before / Mathf.Max(0.01f, lab.Economy.SolventCost(1))) + 100;

            var result = executor.Execute(new TestActor(), LabCommand.OrderSolvent(huge));

            Assert.IsFalse(result.Accepted);
            Assert.IsNotEmpty(result.Refusal);
            Assert.AreEqual(before, lab.Economy.Money);
        }

        // -----------------------------------------------------------------------------------------
        // 5. The rest of the loop still runs through the same door.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void PuttingAVialDown_RecordsTheShelfAndEmptiesTheHands()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);

            var sample = lab.OpenSamples().First();
            var actor = new TestActor();

            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeVial(sample.Id)).Accepted);
            Assert.IsTrue(executor.Execute(actor, LabCommand.PutDown("rack", 3)).Accepted);

            Assert.AreEqual(GripKind.Empty, actor.Grip.Kind);
            Assert.AreEqual(SampleLocationKind.OnSurface, sample.Location.Kind);
            Assert.AreEqual("rack", sample.Location.ContainerId);
            Assert.AreEqual(3, sample.Location.SlotIndex);
        }

        /// <summary>
        /// Promise: whoever takes the vial out of the instrument is the one holding it afterwards.
        /// The result names the sample, because the asking process has no way to know what was in
        /// there — the machine's contents are the host's.
        /// </summary>
        [Test]
        public void TakingAVialBackOut_HandsItToTheAskingPlayer()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var machine = lab.FindMachine("elemental");

            var sample = lab.OpenSamples().First();
            Ready(lab, sample);
            Assert.AreEqual(LoadRefusal.Accepted, machine.TryLoad(sample));

            var actor = new TestActor(3);
            var result = executor.Execute(actor, LabCommand.TakeFromMachine("elemental"));

            Assert.IsTrue(result.Accepted, result.Refusal);
            Assert.AreEqual(sample.Id, result.Sample);
            Assert.AreEqual(GripKind.Vial, actor.Grip.Kind);
            Assert.AreEqual(SampleLocationKind.Held, sample.Location.Kind);
            Assert.AreEqual(3UL, sample.Location.HolderClientId);
            Assert.IsTrue(machine.IsEmpty);
        }

        /// <summary>
        /// Promise: the day ends once. Four players share one END DAY button, and a second press
        /// while the report is on screen must not settle the queue again — every consequence would
        /// pay out twice.
        /// </summary>
        [Test]
        public void TheDayCannotBeEndedTwice()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();

            Assert.IsTrue(executor.Execute(actor, LabCommand.EndDay()).Accepted);

            var again = executor.Execute(actor, LabCommand.EndDay());
            Assert.IsFalse(again.Accepted);
            Assert.IsNotEmpty(again.Refusal);
        }

        // -----------------------------------------------------------------------------------------
        // 6. The host's record of a player's hands is what the drop path reads.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: when somebody's router dies mid-shift, the vial they were carrying comes back.
        /// <para>
        /// <c>PlayerSession</c> argues at length for the rack over a reservation, and
        /// <c>SessionRegistry.ItemReleased</c> exists to announce it — but the whole chain hangs off
        /// <c>PlayerSession.Held</c>, which nothing wrote until commands started tracking hands
        /// server-side. This is the test that says the source is connected: take a vial through the
        /// executor as a remote player would, drop the connection, and the registry must know a
        /// sample went with them.
        /// </para>
        /// </summary>
        [Test]
        public void ACarriedVialIsOnTheSession_SoADroppedPlayerReleasesIt()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);

            var roster = new SessionRegistry();
            Assert.IsTrue(roster.Join("player-one", 1, 0d).Accepted);
            Assert.IsTrue(roster.TryGet(1, out var session));

            HeldItem released = HeldItem.None;
            roster.ItemReleased += (_, item) => released = item;

            var actor = new SessionActor(session);
            var sample = lab.OpenSamples().First();

            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeVial(sample.Id)).Accepted);
            Assert.IsTrue(session.Held.IsSample,
                "The host has to know a vial is in that player's hands, or a disconnect strands it.");
            Assert.AreEqual(sample.Id, session.Held.Sample);

            roster.Disconnect(1, 1d);

            Assert.IsTrue(released.IsSample);
            Assert.AreEqual(sample.Id, released.Sample);
        }

        /// <summary>
        /// Promise: paper a dropped player was carrying becomes takeable again, rather than being
        /// held for the rest of the contract by a connection that no longer exists.
        /// </summary>
        [Test]
        public void ADroppedPlayersSlipCanBePickedUpByAnyoneElse()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var machine = lab.FindMachine("elemental");

            machine.TryBeginBlank();
            lab.Tick(machine.RunSeconds + 1f);
            int ticket = lab.Slips.Issue(SampleId.None, machine.InstanceId, machine.LastBlank);

            var gone = new TestActor(1);
            var here = new TestActor(2);

            Assert.IsTrue(executor.Execute(gone, LabCommand.TakeSlip(ticket)).Accepted);
            Assert.IsFalse(executor.Execute(here, LabCommand.TakeSlip(ticket)).Accepted);

            lab.Slips.ReleaseAllHeldBy(1);

            Assert.IsTrue(executor.Execute(here, LabCommand.TakeSlip(ticket)).Accepted,
                "A slip nobody can reach is a run the lab paid for and can never file.");
        }

        // -----------------------------------------------------------------------------------------
        // 7. Solvent is a thing you carry (#14). The flush is still at the instrument; the solvent
        //    is not, and the host owns both halves of that.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a flush needs solvent in your hands, not solvent in the books.
        /// <para>
        /// This is the whole point of #14 and the one rule the executor has to hold on its own. The
        /// button on the instrument greys itself out when you are empty-handed, but a prompt is a
        /// drawing — if the host did not check the grip, a client could clean every instrument in the
        /// lab from the terminal without ever visiting the wash station, and §5.2's cost would be a
        /// suggestion.
        /// </para>
        /// </summary>
        [Test]
        public void FlushingWithNoBottleInYourHands_IsRefused()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var machine = lab.FindMachine("elemental");

            // Plenty of solvent in the drum, and none of it within reach.
            Assert.Greater(lab.Economy.SolventUnits, 0f);

            machine.Runtime.RunsSinceClean = 4;
            var result = executor.Execute(new TestActor(), LabCommand.FlushMachine("elemental"));

            Assert.IsFalse(result.Accepted, "An empty-handed player flushed an instrument.");
            Assert.IsNotEmpty(result.Refusal);
            Assert.AreEqual(4, machine.Runtime.RunsSinceClean, "The instrument was cleaned anyway.");
        }

        /// <summary>
        /// Promise: a flush spends a charge out of the bottle the player actually walked over with,
        /// and the drum is untouched — the units left it when the bottle was filled.
        /// </summary>
        [Test]
        public void FlushingSpendsAChargeFromTheCarriedBottle_NotFromTheDrum()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var machine = lab.FindMachine("elemental");
            var actor = new TestActor(2);

            var bottle = lab.Solvent.All[0];
            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeBottle(bottle.Id)).Accepted);
            Assert.AreEqual(GripKind.Bottle, actor.Grip.Kind);

            Assert.IsTrue(executor.Execute(actor, LabCommand.FillBottle(SolventStore.StationId)).Accepted);
            Assert.AreEqual(SolventStore.BottleCapacity, bottle.Charges);

            float drum = lab.Economy.SolventUnits;
            machine.Runtime.RunsSinceClean = 3;

            var result = executor.Execute(actor, LabCommand.FlushMachine("elemental"));

            Assert.IsTrue(result.Accepted, result.Refusal);
            Assert.AreEqual(SolventStore.BottleCapacity - 1, bottle.Charges);
            Assert.AreEqual(drum, lab.Economy.SolventUnits, 1e-3f,
                "The drum pays at the wash station. Charging it again at the instrument would " +
                "double the price of a clean lab.");
            Assert.AreEqual(0, machine.Runtime.RunsSinceClean, "The flush did not clear the residue.");
        }

        /// <summary>
        /// Promise: an empty bottle is a trip back to the wash station, not a free clean.
        /// </summary>
        [Test]
        public void FlushingWithAnEmptyBottle_IsRefused()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var machine = lab.FindMachine("elemental");
            var actor = new TestActor();

            var bottle = lab.Solvent.All[0];
            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeBottle(bottle.Id)).Accepted);
            Assert.AreEqual(0, bottle.Charges, "Bottles start empty.");

            machine.Runtime.RunsSinceClean = 2;
            var result = executor.Execute(actor, LabCommand.FlushMachine("elemental"));

            Assert.IsFalse(result.Accepted);
            Assert.IsNotEmpty(result.Refusal);
            Assert.AreEqual(2, machine.Runtime.RunsSinceClean);
        }

        /// <summary>
        /// Promise: you fill the bottle in your hands, standing at the drum.
        /// <para>
        /// The request names the station and never the bottle, so there is no field a client could put
        /// somebody else's bottle in — but a request from a player carrying nothing still has to be
        /// refused rather than silently filling whichever bottle happens to be first in the store.
        /// </para>
        /// </summary>
        [Test]
        public void FillingWithEmptyHands_IsRefused()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);

            float drum = lab.Economy.SolventUnits;
            var result = executor.Execute(new TestActor(), LabCommand.FillBottle(SolventStore.StationId));

            Assert.IsFalse(result.Accepted);
            Assert.IsNotEmpty(result.Refusal);
            Assert.AreEqual(drum, lab.Economy.SolventUnits, 1e-3f, "A refused fill must not charge.");
            Assert.AreEqual(0, lab.Solvent.All[0].Charges);
        }

        /// <summary>
        /// Promise: two bottles, and two players cannot both be holding the same one. Same race as
        /// <see cref="TwoPlayersCannotTakeTheSameVial"/>, and it matters more here — there are only
        /// two bottles in the lab, so a duplicated one doubles the flushing capacity of the run.
        /// </summary>
        [Test]
        public void TwoPlayersCannotCarryTheSameSolventBottle()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);

            var first = new TestActor(1);
            var second = new TestActor(2);
            string id = lab.Solvent.All[0].Id;

            Assert.IsTrue(executor.Execute(first, LabCommand.TakeBottle(id)).Accepted);

            var stolen = executor.Execute(second, LabCommand.TakeBottle(id));
            Assert.IsFalse(stolen.Accepted, "Two players walked off with the same solvent bottle.");
            Assert.AreEqual(GripKind.Empty, second.Grip.Kind);

            // The other one is still free, so the second player is inconvenienced rather than stuck.
            Assert.IsTrue(executor.Execute(second, LabCommand.TakeBottle(lab.Solvent.All[1].Id)).Accepted);
        }

        /// <summary>
        /// Promise: when somebody's router dies holding the solvent, the bottle comes back.
        /// <para>
        /// A bottle marked held by a connection that no longer exists is a bottle nobody can pick up
        /// — with two in the lab, half the run's flushing capacity gone for good. It cannot ride the
        /// <c>SessionRegistry.ItemReleased</c> path a vial does, because <see cref="HeldItem"/> has no
        /// bottle kind to describe it with, so the store releases directly and this is the test that
        /// says so.
        /// </para>
        /// </summary>
        [Test]
        public void ADroppedPlayersSolventBottle_GoesBackToTheWashStation()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);

            var gone = new TestActor(4);
            var here = new TestActor(5);
            string id = lab.Solvent.All[0].Id;

            Assert.IsTrue(executor.Execute(gone, LabCommand.TakeBottle(id)).Accepted);
            Assert.IsFalse(executor.Execute(here, LabCommand.TakeBottle(id)).Accepted);

            lab.Solvent.ReleaseAllHeldBy(4);

            Assert.AreEqual(SampleLocationKind.OnSurface, lab.Solvent.Find(id).Location.Kind);
            Assert.AreEqual(SolventStore.StationId, lab.Solvent.Find(id).Location.ContainerId,
                "It has to come back somewhere every player knows to look.");

            Assert.IsTrue(executor.Execute(here, LabCommand.TakeBottle(id)).Accepted,
                "A bottle nobody can reach is a lab that can never be cleaned again.");
        }

        /// <summary>
        /// Promise: a bottle set down on a rack is on that rack, and the hole it is in is a hole a
        /// vial cannot use. §5.5's shelf pressure, and §2.6's one pair of hands, applied to the thing
        /// that makes an instrument trustworthy.
        /// </summary>
        [Test]
        public void PuttingABottleDown_RecordsTheShelfAndEmptiesTheHands()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();

            string id = lab.Solvent.All[0].Id;
            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeBottle(id)).Accepted);
            Assert.IsTrue(executor.Execute(actor, LabCommand.PutDown("rack", 2)).Accepted);

            Assert.AreEqual(GripKind.Empty, actor.Grip.Kind);

            var location = lab.Solvent.Find(id).Location;
            Assert.AreEqual(SampleLocationKind.OnSurface, location.Kind);
            Assert.AreEqual("rack", location.ContainerId);
            Assert.AreEqual(2, location.SlotIndex);
        }

        /// <summary>
        /// Promise: a vial out of the crate is ready to work on, with no paperwork in between.
        /// <para>
        /// This replaces <c>BookingInAcceptsAWrongTagAsReadilyAsARightOne</c>, which asserted that
        /// the host accepted a mis-typed tank tag as readily as a right one — §5.1's mis-logging
        /// mechanic dies the moment the host starts checking. #73 removed the step, so there is no
        /// tag to type and no check to refrain from making. The pillar that test served was hard
        /// rule 3, and it is untouched: nothing is punished here at all now.
        /// </para>
        /// <para>
        /// What is worth asserting instead is the thing #73 was asked for — that the loop does not
        /// stop dead at a keyboard before any analysis can start.
        /// </para>
        /// </summary>
        [Test]
        public void AnUnpackedVialIsImmediatelyWorkable()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);

            // Any bottle whose label survived the post. #32 lets a label arrive unreadable, and that
            // one vial in a shift is the single case where a name is decided rather than read — the
            // claim under test is about all the others.
            var sample = lab.OpenSamples().First(s => s.Ambiguity == SampleAmbiguity.None);
            Assert.IsTrue(SampleLifecycle.TryMove(sample, SampleLocation.OnSurface("bench", 0), out _));

            Assert.AreEqual(sample.EquipmentTag, sample.RecordTag,
                "It is filed under the tag it arrived carrying — nobody registered it.");

            var actor = new TestActor();
            actor.SetGrip(LabGrip.OnVial(sample.Id));

            var result = executor.Execute(actor, LabCommand.Agitate());

            Assert.IsTrue(result.Accepted, result.Refusal);
            Assert.AreEqual(SampleStage.Prepped, sample.Stage,
                "Unpacked leads straight to Prepped. No terminal visit stands in between (#73).");
        }
    }
}
