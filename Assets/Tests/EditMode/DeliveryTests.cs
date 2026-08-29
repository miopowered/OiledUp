using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Editor.Content;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards #30 and #31: the truck, the bay, and getting a vial out of a box.
    ///
    /// <para>
    /// The promise underneath all of it is the one the printout work made everywhere else — <b>content
    /// does not teleport into the room</b>. Vials used to exist, in reach, in a crate, the instant the
    /// day began; now they arrive on a lorry part-way through the shift, in sealed boxes that have to
    /// be carried in and opened by hand. Each test below pins one step of that so it cannot quietly
    /// collapse back into "the samples are simply there".
    /// </para>
    ///
    /// <para>
    /// The sharpest of them is <see cref="Unboxing_LeavesTheOilColdAndUnsettled"/>. Opening a carton is
    /// a satisfying moment to attach a convenience to, and marking the contents ready would delete the
    /// §4.5 refusal the whole load-hold exists to pay for — silently, and only visible as instruments
    /// that stopped complaining.
    /// </para>
    /// </summary>
    public sealed class DeliveryTests
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
            LabCommands.Router = null;
            LabCommands.Executor = null;

            if (catalog != null) Object.DestroyImmediate(catalog);
            if (content == null) return;

            foreach (var o in content.Elements.Values.Cast<Object>()
                         .Concat(content.Causes.Values)
                         .Concat(content.Profiles.Values)
                         .Concat(content.Faults.Values)
                         .Concat(content.Machines.Values)
                         .Concat(content.Customers.Values))
            {
                Object.DestroyImmediate(o);
            }
            content = null;
        }

        // -- Doubles and fixtures ------------------------------------------------------------------------

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

        private const float DaySeconds = 600f;

        private static readonly string[] Profiles = { "hardening_oil_general", "quench_oil_cold" };

        private static ContractPlan Plan(int samplesPerDay, int days = 3) => new()
        {
            Id = "test",
            DisplayName = "Test",
            Days = Enumerable.Range(0, days).Select(_ => new DayPlan
            {
                SampleCount = samplesPerDay,
                ProfileIds = Profiles,
                BorderlineCount = 0,
                HealthyChance = 0.3f,
                DaySeconds = DaySeconds
            }).ToList()
        };

        private LabState NewLab(int samplesPerDay = 8, int seed = 9090)
        {
            var lab = new LabState(catalog, Plan(samplesPerDay), seed);
            lab.Install(catalog.Machine("elemental"), "elemental");
            lab.Deliveries.Capacity = 64;
            lab.BeginDay();
            return lab;
        }

        /// <summary>Run the shift forward until the truck has put everything it can down.</summary>
        private static void RunToArrival(LabState lab)
        {
            lab.Tick(lab.Deliveries.SecondsUntilArrival + 0.01f);
        }

        private static List<SampleState> Inside(LabState lab, Carton carton) =>
            lab.Samples.All
                .Where(s => s.Location.Kind == SampleLocationKind.InCrate &&
                            s.Location.ContainerId == carton.Id)
                .OrderBy(s => s.Id.Value)
                .ToList();

        // -----------------------------------------------------------------------------------------
        // 1. The truck. #30: a delivery arrives into the shift, not at the instant it begins.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: nothing the day generated is standing in the room when the day starts.
        /// <para>
        /// This is the whole of #30's first criterion. The chemistry is still minted at 09:00 — the job
        /// exists, the paperwork exists — but every box is on a lorry, and the bay is empty until a
        /// quarter of the shift has run. A regression here would not look like a bug: the lab would
        /// simply be full of work again from the first frame, which is the state the issue exists to
        /// remove.
        /// </para>
        /// </summary>
        [Test]
        public void Delivery_IsStillOnTheTruckWhenTheDayBegins()
        {
            var lab = NewLab();

            Assert.IsNotEmpty(lab.Deliveries.Cartons, "The day generated no cartons.");
            Assert.AreEqual(0, lab.Deliveries.StandingInBay,
                "Nothing may be standing in the bay before the truck has arrived.");

            foreach (var carton in lab.Deliveries.Cartons)
            {
                Assert.AreEqual(CartonStage.OnTheRoad, carton.Stage,
                    $"{carton.Id} was in the room at 09:00 — that is the teleporting content #30 removes.");
            }

            // A tick short of the arrival is still a tick short of it.
            lab.Tick(DaySeconds * DeliveryBay.DefaultArrivalShiftFraction - 1f);
            Assert.AreEqual(0, lab.Deliveries.StandingInBay, "The truck arrived early.");

            lab.Tick(2f);
            Assert.Greater(lab.Deliveries.StandingInBay, 0, "The truck never arrived.");
        }

        /// <summary>
        /// Promise: the arrival is announced before it happens, so "finish this run first" is a choice
        /// the player gets to make rather than one the game makes for them (#30).
        /// <para>
        /// Asserted as an ordering with a gap rather than a fixed number of seconds, because the
        /// warning is balance and the ordering is the design.
        /// </para>
        /// </summary>
        [Test]
        public void ArrivalIsAnnounced_BeforeTheTruckLands()
        {
            var lab = NewLab();

            float elapsed = 0f;
            float warnedAt = -1f;
            float arrivedAt = -1f;

            lab.DeliveryDue += _ => warnedAt = elapsed;
            lab.DeliveryArrived += _ => arrivedAt = elapsed;

            for (int i = 0; i < 4000 && arrivedAt < 0f; i++)
            {
                elapsed += 0.1f;
                lab.Tick(0.1f);
            }

            Assert.GreaterOrEqual(warnedAt, 0f, "The delivery landed with no warning at all.");
            Assert.GreaterOrEqual(arrivedAt, 0f, "The delivery never landed.");
            Assert.Less(warnedAt, arrivedAt, "The warning must come before the truck, not with it.");
            Assert.Greater(arrivedAt - warnedAt, 5f,
                "A warning that arrives five seconds ahead is not enough to finish a run on.");
        }

        /// <summary>
        /// Promise: one carton per note, and no second grouping (#31 leans on this, #32 depends on it).
        /// <para>
        /// #29 already fixed one note per sender per day. A carton that split or merged those would
        /// give the lab two answers to "what arrived together", and the reconciliation #32 is about
        /// would have to pick one.
        /// </para>
        /// </summary>
        [Test]
        public void OneCartonPerNote_AndNoOtherGrouping()
        {
            var lab = NewLab(samplesPerDay: 14);

            Assert.AreEqual(lab.Notes.Count, lab.Deliveries.Cartons.Count,
                "Cartons and delivery notes must be the same set of things.");

            var jobs = lab.Deliveries.Cartons.Select(c => c.JobNumber).ToList();
            CollectionAssert.AllItemsAreUnique(jobs, "Two cartons share a job number.");

            int packed = lab.Deliveries.Cartons.Sum(c => c.Contents.Count);
            Assert.AreEqual(lab.Samples.All.Count, packed,
                "Every sample the day generated has to be in exactly one box.");
        }

        // -----------------------------------------------------------------------------------------
        // 2. The seal. #31: a carton has to be opened before its contents are reachable.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: the host refuses a vial that is still on the truck or still taped up, whatever the
        /// asking client believes.
        /// <para>
        /// Hard rule 2 in its usual shape. A sealed carton has no prop and no slots, so on an honest
        /// client the request cannot even be formed — which is exactly why the refusal has to live on
        /// the server, where a dishonest one is answered too.
        /// </para>
        /// </summary>
        [Test]
        public void SealedCarton_WillNotHandOverItsVials()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();

            var sample = lab.OpenSamples().First();

            var onTheRoad = executor.Execute(actor, LabCommand.TakeVial(sample.Id));
            Assert.IsFalse(onTheRoad.Accepted, "A vial still on the truck was handed over.");
            StringAssert.Contains("truck", onTheRoad.Refusal);

            RunToArrival(lab);

            var stillSealed = executor.Execute(actor, LabCommand.TakeVial(sample.Id));
            Assert.IsFalse(stillSealed.Accepted, "A sealed carton was reached into.");
            StringAssert.Contains("sealed", stillSealed.Refusal);

            var carton = lab.Deliveries.CartonHolding(sample);
            Assert.IsTrue(executor.Execute(actor, LabCommand.OpenCarton(carton.Id)).Accepted,
                "The carton refused to open with nothing wrong.");

            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeVial(sample.Id)).Accepted,
                "An opened carton must give up its vials.");
        }

        /// <summary>
        /// Promise: a box fills your hands, so you cannot open one while carrying it (#30's "a carton
        /// occupies your hands entirely").
        /// <para>
        /// It is also what forces the walk. If a carton could be opened in the arms that lifted it out
        /// of the bay, the vials could be handed straight to an instrument and the trip from the bay to
        /// the lab would never have to happen.
        /// </para>
        /// </summary>
        [Test]
        public void Carton_MustBeSetDownBeforeItIsOpened()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();

            RunToArrival(lab);
            var carton = lab.Deliveries.Cartons.First(c => c.Stage == CartonStage.Delivered);

            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeCarton(carton.Id)).Accepted);
            Assert.AreEqual(GripKind.Carton, actor.Grip.Kind, "A carton must occupy a hand.");

            var carried = executor.Execute(actor, LabCommand.OpenCarton(carton.Id));
            Assert.IsFalse(carried.Accepted, "A carton was opened in mid-air.");
            StringAssert.Contains("Set the carton down", carried.Refusal);

            Assert.IsTrue(executor.Execute(actor, LabCommand.PutDown("bench", 0)).Accepted);
            Assert.IsTrue(executor.Execute(actor, LabCommand.OpenCarton(carton.Id)).Accepted,
                "A carton standing on a bench must open.");
        }

        // -----------------------------------------------------------------------------------------
        // 3. The gotcha. #31: unboxing must not quietly make a sample ready.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a vial comes out of a carton exactly as cold and exactly as settled as it went in.
        /// <para>
        /// §4.5 makes an unagitated sample read low on the wear metals the player is hunting, and §9
        /// requires that preparation to be a hand-operated cost rather than a menu click — which since
        /// #73 is paid in the hold that loads an instrument. Opening a box is the obvious place for a
        /// convenience to creep in, and one that marked the contents ready would delete that hold's
        /// entire reason for existing without a single test failing anywhere else.
        /// </para>
        /// The instrument's own refusal is asserted alongside the fields, because the fields are the
        /// mechanism and the refusal is the promise.
        /// </summary>
        [Test]
        public void Unboxing_LeavesTheOilColdAndUnsettled()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();
            var machine = lab.Machines.First();

            RunToArrival(lab);
            var carton = lab.Deliveries.Cartons.First(c => c.Stage == CartonStage.Delivered);

            var before = Inside(lab, carton)
                .ToDictionary(s => s.Id, s => (s.IsSettled, s.TemperatureC));
            Assert.IsNotEmpty(before, "Fixture put nothing in the box.");

            Assert.IsTrue(executor.Execute(actor, LabCommand.OpenCarton(carton.Id)).Accepted);

            foreach (var sample in Inside(lab, carton))
            {
                var was = before[sample.Id];

                Assert.IsFalse(sample.IsSettled,
                    $"{sample.RecordTag} came out of the box already agitated. Unboxing must not " +
                    "pay §4.5's cost — the load hold does.");
                Assert.AreEqual(was.TemperatureC, sample.TemperatureC, 1e-4f,
                    $"{sample.RecordTag} was warmed by being unpacked.");
                Assert.AreEqual(was.IsSettled, sample.IsSettled,
                    $"{sample.RecordTag} changed state simply by having a lid taken off it.");

                Assert.AreNotEqual(LoadRefusal.Accepted, machine.CanAccept(sample),
                    $"{machine.Def.DisplayName} accepted {sample.RecordTag} straight out of the " +
                    "carton, so the instruments have stopped refusing cold, unshaken oil.");
            }
        }

        // -----------------------------------------------------------------------------------------
        // 4. One at a time. #31: a carton's contents cannot be teleported into a rack.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: emptying a carton costs one request and one pair of hands per vial.
        /// <para>
        /// The failure this guards is not a wrong number, it is a shortcut: any command that moved a
        /// whole box's contents somewhere would be the teleporting content #30 removed, one layer
        /// down. So this asserts the shape rather than the count — a take moves exactly the vial it
        /// named and leaves every other one where it was, and a second take is refused until the hand
        /// is free.
        /// </para>
        /// </summary>
        [Test]
        public void OpenedCarton_HandsVialsOutOneAtATime()
        {
            // Twelve samples across at most five senders, so pigeonhole guarantees a box with more
            // than one vial in it whatever the seed draws.
            var lab = NewLab(samplesPerDay: 12);
            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();

            RunToArrival(lab);

            var carton = lab.Deliveries.Cartons
                .FirstOrDefault(c => c.Stage == CartonStage.Delivered && lab.Deliveries.RemainingIn(c) >= 2);
            Assert.IsNotNull(carton, "Fixture produced no carton with two vials in it.");

            Assert.IsTrue(executor.Execute(actor, LabCommand.OpenCarton(carton.Id)).Accepted);

            int packed = lab.Deliveries.RemainingIn(carton);
            var contents = Inside(lab, carton);

            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeVial(contents[0].Id)).Accepted);

            Assert.AreEqual(packed - 1, lab.Deliveries.RemainingIn(carton),
                "Taking one vial moved something other than one vial.");
            foreach (var other in contents.Skip(1))
            {
                Assert.AreEqual(SampleLocationKind.InCrate, other.Location.Kind,
                    $"{other.RecordTag} left the box on somebody else's request.");
                Assert.AreEqual(carton.Id, other.Location.ContainerId);
            }

            var second = executor.Execute(actor, LabCommand.TakeVial(contents[1].Id));
            Assert.IsFalse(second.Accepted,
                "A second vial came out with the first one still in hand — a carton is emptied by " +
                "walking, not by asking harder.");

            // Empty the hand and the next one comes out. The cost is the trip, not the refusal.
            Assert.IsTrue(executor.Execute(actor, LabCommand.PutDown("rack", 0)).Accepted);
            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeVial(contents[1].Id)).Accepted);
        }

        // -----------------------------------------------------------------------------------------
        // 5. Cardboard. #31: an opened carton must not clutter the lab forever.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a spent box can be flattened, and one that is not spent cannot.
        /// <para>
        /// The refusals are the point. Flattening is a one-key action next to a bench full of vials,
        /// so it has to be impossible to destroy a sample or the delivery note with it — hard rule 3
        /// covers things the player could not have checked, and paper that vanished on a mis-aimed
        /// keypress is squarely one of them.
        /// </para>
        /// </summary>
        [Test]
        public void Carton_IsFlattenedOnlyOnceItIsTrulyEmpty()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();

            RunToArrival(lab);
            var carton = lab.Deliveries.Cartons.First(c => c.Stage == CartonStage.Delivered);

            var sealedRefusal = executor.Execute(actor, LabCommand.DiscardCarton(carton.Id));
            Assert.IsFalse(sealedRefusal.Accepted, "A sealed carton was flattened unopened.");

            Assert.IsTrue(executor.Execute(actor, LabCommand.OpenCarton(carton.Id)).Accepted);

            var full = executor.Execute(actor, LabCommand.DiscardCarton(carton.Id));
            Assert.IsFalse(full.Accepted, "A carton with vials in it was flattened.");
            StringAssert.Contains("vial", full.Refusal);

            foreach (var sample in Inside(lab, carton))
            {
                Assert.IsTrue(executor.Execute(actor, LabCommand.TakeVial(sample.Id)).Accepted);
                Assert.IsTrue(executor.Execute(actor, LabCommand.PutDown("rack", -1)).Accepted);
            }

            var noteInside = executor.Execute(actor, LabCommand.DiscardCarton(carton.Id));
            Assert.IsFalse(noteInside.Accepted, "The delivery note was thrown out with the box.");
            StringAssert.Contains("delivery note", noteInside.Refusal);

            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeDeliveryNote(carton.Id)).Accepted);
            Assert.AreEqual(GripKind.Note, actor.Grip.Kind, "The note has to occupy a hand like anything else.");

            Assert.IsTrue(executor.Execute(actor, LabCommand.DiscardCarton(carton.Id)).Accepted,
                "An empty carton with its paperwork out of it must be disposable.");
            Assert.AreEqual(CartonStage.Discarded, carton.Stage);
        }

        /// <summary>
        /// Promise: a box the player never got round to flattening does not survive the night, and one
        /// that still has work in it does.
        /// <para>
        /// The second half matters more than the first. A sweep that took anything with a vial left in
        /// it would destroy samples the player has records for, at a moment they are not looking — the
        /// exact shape hard rule 3 forbids.
        /// </para>
        /// </summary>
        [Test]
        public void SpentCartons_AreSweptAtTheEndOfTheDay_AndFullOnesAreNot()
        {
            var lab = NewLab(samplesPerDay: 12);
            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();

            RunToArrival(lab);

            var emptied = lab.Deliveries.Cartons.First(c => c.Stage == CartonStage.Delivered);
            var untouched = lab.Deliveries.Cartons
                .FirstOrDefault(c => c.Stage == CartonStage.Delivered && c.Id != emptied.Id &&
                                     lab.Deliveries.RemainingIn(c) > 0);
            Assert.IsNotNull(untouched, "Fixture produced only one sender, so nothing is left untouched.");

            Assert.IsTrue(executor.Execute(actor, LabCommand.OpenCarton(emptied.Id)).Accepted);

            foreach (var sample in Inside(lab, emptied))
            {
                Assert.IsTrue(executor.Execute(actor, LabCommand.TakeVial(sample.Id)).Accepted);
                Assert.IsTrue(executor.Execute(actor, LabCommand.PutDown("rack", -1)).Accepted);
            }

            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeDeliveryNote(emptied.Id)).Accepted);
            Assert.IsTrue(executor.Execute(actor, LabCommand.PutDown("bench", 0)).Accepted);

            lab.EndDay();

            Assert.AreEqual(CartonStage.Discarded, emptied.Stage,
                "An open, empty box was still standing in the lab the next morning.");
            Assert.AreEqual(CartonStage.Delivered, untouched.Stage,
                "A box with vials still in it was swept away overnight, taking the samples with it.");
        }

        // -----------------------------------------------------------------------------------------
        // 6. The bay as a buffer. #30's "unload before the bay blocks".
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a full bay holds the rest of the load on the truck, loses nothing, and drains as
        /// soon as a place frees.
        /// <para>
        /// This is the whole of what "the bay blocks" means, and what it deliberately does not mean.
        /// Nothing is destroyed and no delivery is cancelled — the cost of ignoring the bay is the
        /// shift time spent unloading two deliveries at once later, which is a pressure the player can
        /// see standing outside the window. A version that binned the overflow would be punishing
        /// something with no tell.
        /// </para>
        /// </summary>
        [Test]
        public void FullBay_HoldsTheRestOnTheTruck_AndReleasesAsPlacesFree()
        {
            var lab = NewLab(samplesPerDay: 14);
            lab.Deliveries.Capacity = 1;

            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();

            int held = 0;
            lab.DeliveryHeld += _ => held++;

            int booked = lab.Deliveries.Cartons.Count;
            Assert.Greater(booked, 1, "Fixture needs more than one carton to fill a one-place bay.");

            RunToArrival(lab);

            Assert.AreEqual(1, lab.Deliveries.StandingInBay, "The bay took more than it has room for.");
            Assert.AreEqual(booked - 1, lab.Deliveries.OnTheRoadCount,
                "The overflow was dropped instead of staying on the truck.");

            lab.Tick(0.1f);
            Assert.AreEqual(1, held, "A blocked bay must say so, exactly once, rather than silently.");

            // Carry one in, and the next comes off the lorry. That is the buffer draining.
            var standing = lab.Deliveries.Cartons.First(c => c.Stage == CartonStage.Delivered);
            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeCarton(standing.Id)).Accepted);

            lab.Tick(0.1f);
            Assert.AreEqual(1, lab.Deliveries.StandingInBay,
                "Clearing a place did not let the next carton off the truck.");
            Assert.AreEqual(booked - 2, lab.Deliveries.OnTheRoadCount);

            // And nothing was lost on the way.
            Assert.AreEqual(booked, lab.Deliveries.Cartons.Count(c => c.Stage != CartonStage.Discarded),
                "A carton went missing while the bay was full.");
        }

        // -----------------------------------------------------------------------------------------
        // 7. Continuing a run.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a continued run's boxes come back, so no sample is left in a container the room has
        /// no prop for.
        /// <para>
        /// A carton is derived from where the vials say they are rather than written into the save (see
        /// <c>DeliveryBay.RebuildFrom</c>), so this is really a test that the derivation is total: every
        /// vial the save left in a box must land in a rebuilt box on load, or it is a record on the
        /// terminal with no bottle anywhere in the lab.
        /// </para>
        /// </summary>
        [Test]
        public void ContinuedRun_RebuildsEveryBoxItsVialsStillPointAt()
        {
            // The shipping contract, not this file's fixture plan: a save stores the contract by id
            // and the loader refuses one this build does not offer (see RunSnapshotRestore).
            var lab = new LabState(catalog, ContractPlan.Default(), 5150);
            lab.Install(catalog.Machine("elemental"), "elemental");
            lab.BeginDay();

            RunToArrival(lab);
            lab.EndDay();

            var snapshot = RunSnapshotCapture.Of(lab);
            Assert.IsTrue(RunSnapshotRestore.TryRebuild(snapshot, catalog, out var restored,
                                                        out string refusal), refusal);

            var stillBoxed = restored.Samples.All
                .Where(s => s.Location.Kind == SampleLocationKind.InCrate)
                .ToList();

            Assert.IsNotEmpty(stillBoxed, "Fixture saved nothing in a box, so this proves nothing.");

            foreach (var sample in stillBoxed)
            {
                var carton = restored.Deliveries.CartonHolding(sample);
                Assert.IsNotNull(carton,
                    $"{sample.RecordTag} came back in container '{sample.Location.ContainerId}', " +
                    "which no rebuilt carton answers to — the bottle would have nowhere to appear.");

                Assert.AreEqual(CartonStage.Delivered, carton.Stage,
                    "A restored box must be standing in the bay, not back on a lorry that has gone.");
                Assert.IsTrue(carton.IsSealed, "A restored box comes back sealed; see RebuildFrom.");
                Assert.IsTrue(carton.Contents.Contains(sample.Id),
                    "The rebuilt manifest lost a vial that is physically in the box.");
            }

            // The paperwork rebuilds with it, including lines for vials already lifted out — #32
            // reconciles against the note, and a note that shrank as the box emptied would always agree.
            foreach (var carton in restored.Deliveries.Cartons)
            {
                Assert.IsNotNull(carton.Note, $"{carton.Id} came back with no delivery note.");
                Assert.AreEqual(carton.Contents.Count, carton.Note.Count,
                    $"{carton.Id}'s note and manifest disagree straight out of a save.");
            }
        }
    }
}
