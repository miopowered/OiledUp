using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Editor.Content;
using Residue.Gameplay.Simulation;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards design pillar §1.1.3 — two-sided failure. "There is no safe default answer" is a claim
    /// about the payout table, and a claim about numbers is a thing you can either test or merely
    /// hope for. Every blanket strategy must lose money; only reading the results should win.
    /// <para>
    /// If one of these fails after a balance change, the game has acquired a correct default answer
    /// and the diagnosis has stopped mattering.
    /// </para>
    /// </summary>
    public sealed class EconomyTests
    {
        private const int Population = 300;
        private const int Seed = 8675309;

        private ContentSet content;
        private EconomyTuning tuning;

        [SetUp]
        public void SetUp()
        {
            content = ContentBuilder.BuildInMemory();
            tuning = new EconomyTuning();
        }

        [TearDown]
        public void TearDown()
        {
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

        /// <summary>
        /// Solvent must be replenishable. §5.2 makes skipping the flush tempting; it must never make
        /// it <i>compulsory</i>.
        /// <para>
        /// A run starts with twelve units and every flush spends one. With no way to buy more, a
        /// twenty-day contract across five instruments runs dry in the first few days, after which
        /// residue accumulates with nothing the player can do about it. That is not a difficulty
        /// curve, it is a soft-lock on the mechanic §9 calls non-cuttable.
        /// </para>
        /// </summary>
        [Test]
        public void Solvent_CanBeRestocked_SoFlushingNeverBecomesImpossible()
        {
            var economy = new Economy(tuning, startingSolvent: 1f);

            Assert.IsTrue(economy.TryConsumeSolvent(), "The one unit we started with should flush.");
            Assert.IsFalse(economy.TryConsumeSolvent(), "Now dry — this is the state under test.");

            Assert.IsTrue(economy.TryBuySolvent(10),
                "A dry lab with money in the bank must be able to restock, or the flush mechanic " +
                "is permanently gone for the rest of the run.");

            Assert.IsTrue(economy.TryConsumeSolvent(), "Restocked solvent has to actually be usable.");
        }

        [Test]
        public void BuyingSolvent_IsRefused_WhenItCannotBeAfforded_AndChargesNothing()
        {
            var economy = new Economy(tuning, startingSolvent: 0f);
            economy.Charge(tuning.StartingMoney);   // spend down to nothing

            float before = economy.Money;

            Assert.IsFalse(economy.TryBuySolvent(50), "Cannot buy what is not affordable.");
            Assert.AreEqual(before, economy.Money, "A refused purchase must not charge.");
            Assert.AreEqual(0f, economy.SolventUnits, "A refused purchase must not deliver.");
        }

        /// <summary>
        /// Flushing between every sample must stay affordable against what the work pays. If a
        /// disciplined lab cannot cover its own solvent, "skip the flush" stops being a temptation
        /// and becomes the only viable strategy — which deletes the §5.2 decision entirely.
        /// <para>
        /// This used to read <c>tuning.SolventUnitCost</c> directly, on the unstated assumption that
        /// one unit was one flush. Since #14 the unit is spent filling a bottle at the wash station
        /// rather than at the instrument, so the assumption became a claim about two systems agreeing
        /// — and it is now asked of <see cref="SolventStore.FlushCost"/>, which is the one place the
        /// conversion lives. A future change that made a bottle cost more per charge than the drum
        /// charges per unit would fail here instead of quietly re-pricing the mechanic.
        /// </para>
        /// </summary>
        [Test]
        public void FlushingAfterEverySample_CostsFarLessThanTheWorkPays()
        {
            float perSample = SolventStore.FlushCost(tuning);

            Assert.AreEqual(tuning.SolventUnitCost, perSample, 1e-3f,
                "A flush still costs exactly one solvent unit. #14 moved where the unit is spent, " +
                "not what it costs — the walk is the new cost, and it is paid in hands and seconds.");

            Assert.Less(perSample, tuning.BasePayout * 0.5f,
                $"One flush costs {perSample:F0} against a {tuning.BasePayout:F0} base payout. " +
                "Cleaning has to be a cost the player weighs, not one that outruns the job.");
        }

        /// <summary>
        /// Promise: carrying the solvent instead of counting it did not change the price of a clean
        /// lab.
        /// <para>
        /// A bottle is a container, not a markup. Filling one draws exactly one unit per flush it
        /// buys, so the money a disciplined shift spends on solvent is the same figure the payout
        /// table above was balanced against. If a bottle ever cost more than the flushes in it, every
        /// two-sided-failure test in this file would still pass while the §5.2 temptation quietly got
        /// stronger.
        /// </para>
        /// </summary>
        [Test]
        public void FillingABottle_DrawsOneUnitPerFlushItBuys()
        {
            var economy = new Economy(tuning, startingSolvent: 12f);
            var store = new SolventStore(economy);
            var bottle = store.All[0];

            Assert.AreEqual(0, bottle.Charges,
                "Bottles start empty. Pre-filled ones would be free flushes no economy test counts.");

            Assert.IsTrue(store.TryTake(bottle.Id, 1UL, out var takeRefusal), takeRefusal);
            Assert.IsTrue(store.TryFill(bottle.Id, 1UL, out int added, out var fillRefusal), fillRefusal);

            Assert.AreEqual(SolventStore.BottleCapacity, added);
            Assert.AreEqual(SolventStore.BottleCapacity, bottle.Charges);
            Assert.AreEqual(12f - SolventStore.BottleCapacity * SolventStore.UnitsPerCharge,
                            economy.SolventUnits, 1e-3f,
                            "The drum has to fall by exactly what went into the bottle.");
        }

        /// <summary>
        /// Promise: topping up early is free, so "top up now or risk running dry across the room" is
        /// a decision about the walk rather than about waste.
        /// <para>
        /// A fill that discarded whatever was already in the bottle would answer the question for the
        /// player — always run it to empty first — and the wash station would be a place you visit on
        /// a schedule rather than one you choose to visit.
        /// </para>
        /// </summary>
        [Test]
        public void ToppingUpAPartFullBottle_PaysOnlyForWhatItTakes()
        {
            var economy = new Economy(tuning, startingSolvent: 12f);
            var store = new SolventStore(economy);
            var bottle = store.All[0];

            Assert.IsTrue(store.TryTake(bottle.Id, 1UL, out _));
            Assert.IsTrue(store.TryFill(bottle.Id, 1UL, out _, out _));
            Assert.IsTrue(store.TryConsumeCharge(bottle.Id, 1UL, out var spend), spend);

            float drumBefore = economy.SolventUnits;

            Assert.IsTrue(store.TryFill(bottle.Id, 1UL, out int added, out var refusal), refusal);
            Assert.AreEqual(1, added, "One charge was spent, so one charge is what a top-up costs.");
            Assert.AreEqual(drumBefore - SolventStore.UnitsPerCharge, economy.SolventUnits, 1e-3f);

            Assert.IsFalse(store.TryFill(bottle.Id, 1UL, out _, out var full),
                "A full bottle takes nothing and says so.");
            Assert.IsNotEmpty(full);
        }

        /// <summary>
        /// Promise: the last of the drum still reaches an instrument.
        /// <para>
        /// A fill that refused unless it could top the bottle right up would strand whatever was left
        /// below a bottle's worth — solvent the lab had already paid for and could never use. Hard
        /// rule 3 in its other direction: never take away a fix the player was entitled to.
        /// </para>
        /// </summary>
        [Test]
        public void AnAlmostEmptyDrum_StillFillsWhatItCan()
        {
            var economy = new Economy(tuning, startingSolvent: SolventStore.UnitsPerCharge);
            var store = new SolventStore(economy);
            var bottle = store.All[0];

            Assert.IsTrue(store.TryTake(bottle.Id, 1UL, out _));
            Assert.IsTrue(store.TryFill(bottle.Id, 1UL, out int added, out var refusal), refusal);

            Assert.AreEqual(1, added, "One unit left in the drum is one flush, not nothing.");
            Assert.AreEqual(0f, economy.SolventUnits, 1e-3f);

            Assert.IsFalse(store.TryFill(bottle.Id, 1UL, out _, out var dry));
            Assert.IsNotEmpty(dry, "A dry drum has to say where more comes from.");
        }

        /// <summary>
        /// Promise: the wash station is a room you walk to, not a corridor you live in.
        /// <para>
        /// #14 named both failure modes. One charge per trip makes the lab a shuttle run and turns
        /// §5.5's layout cost into tedium; a bottle that covered every instrument would make the
        /// station a once-a-morning ritual and then scenery. The number itself is tuning — this pins
        /// the shape it has to keep.
        /// </para>
        /// </summary>
        [Test]
        public void ABottleHoldsSeveralFlushes_ButNotAWholeSweepOfTheLab()
        {
            Assert.Greater(SolventStore.BottleCapacity, 1,
                "One flush per trip is a corridor, not a layout decision.");

            Assert.Less(SolventStore.BottleCapacity, InstalledInstruments,
                $"A bottle holding {SolventStore.BottleCapacity} covers every instrument in the lab, " +
                "so the trip to the wash station happens once and then never again during a shift.");
        }

        /// <summary>
        /// Instruments the MVP lab installs — <c>LabRuntime.installedMachineIds</c>. Repeated rather
        /// than read off a MonoBehaviour because this is a claim about the balance, and the day the
        /// lab grows is the day the capacity above should be reconsidered rather than silently
        /// re-passed.
        /// </summary>
        private const int InstalledInstruments = 5;

        [Test]
        public void FilingCriticalOnEverything_LosesMoney()
        {
            float net = RunStrategy(_ => (Verdict.Critical, null));
            Assert.Less(net, 0f,
                $"Flagging everything CRITICAL netted {net:F0}. If defensive filing is profitable, " +
                "the safe play is to never diagnose anything.");
        }

        [Test]
        public void FilingNormalOnEverything_LosesMoney()
        {
            float net = RunStrategy(_ => (Verdict.Normal, null));
            Assert.Less(net, 0f,
                $"Passing everything as NORMAL netted {net:F0}. Missed faults must be ruinous (§5.4).");
        }

        [Test]
        public void FilingMonitorOnEverything_LosesMoney()
        {
            float net = RunStrategy(_ => (Verdict.Monitor, null));
            Assert.Less(net, 0f,
                $"Hedging with MONITOR on everything netted {net:F0}. MONITOR must not be a free " +
                "middle option — an imminent fault filed as MONITOR still fails in service.");
        }

        [Test]
        public void ReadingTheResultsCorrectly_MakesMoney()
        {
            float net = RunStrategy(truth => truth.IsHealthy
                ? (Verdict.Normal, (RootCauseDef)null)
                : (Verdict.Critical, truth.PrimaryFault.RootCause));

            Assert.Greater(net, 0f,
                $"Perfect play netted {net:F0}. Correct diagnosis must be the profitable strategy, " +
                "or none of the chemistry matters.");
        }

        [Test]
        public void PerfectPlay_BeatsEveryBlanketStrategy()
        {
            float perfect = RunStrategy(truth => truth.IsHealthy
                ? (Verdict.Normal, (RootCauseDef)null)
                : (Verdict.Critical, truth.PrimaryFault.RootCause));

            float critical = RunStrategy(_ => (Verdict.Critical, null));
            float normal = RunStrategy(_ => (Verdict.Normal, null));
            float monitor = RunStrategy(_ => (Verdict.Monitor, null));

            Assert.Greater(perfect, critical);
            Assert.Greater(perfect, normal);
            Assert.Greater(perfect, monitor);
        }

        [Test]
        public void RootCauseBonus_PaysOnlyForTheCorrectCause()
        {
            float withCause = RunStrategy(truth => truth.IsHealthy
                ? (Verdict.Normal, (RootCauseDef)null)
                : (Verdict.Critical, truth.PrimaryFault.RootCause));

            var wrongCause = content.Cause("normal_service");
            float withWrongCause = RunStrategy(truth => truth.IsHealthy
                ? (Verdict.Normal, (RootCauseDef)null)
                : (Verdict.Critical, wrongCause));

            Assert.Greater(withCause, withWrongCause,
                "Naming the right root cause must pay more than guessing. This is the payout that " +
                "rewards understanding over table lookup (§5.4).");
        }


        [Test]
        public void MissingAnImminentFault_HurtsMoreThanAFalsePositive()
        {
            var profile = content.Profile("hardening_oil_general");
            var imminent = content.Faults.Values.First(f => f.Severity == FaultSeverity.Imminent && f.IsValidOn(profile));

            var rng = new Rng(11);
            var generator = new SampleGenerator(content.AllFaults);

            // NORMAL filed over a live imminent fault.
            var missed = ScoreOne(profile, imminent, Verdict.Normal, ref rng, generator);

            // CRITICAL filed on a clean unit.
            var falsePositive = ScoreOne(profile, null, Verdict.Critical, ref rng, generator);

            Assert.Less(missed.MoneyDelta, falsePositive.MoneyDelta,
                "Both directions must cost, but missing a failing machine has to be the worse one — " +
                "otherwise the rational play is to under-report.");
            Assert.Less(missed.ReputationDelta, falsePositive.ReputationDelta);
        }

        // -- helpers ------------------------------------------------------------------------------

        /// <summary>
        /// Generate a mixed population, apply one strategy to all of it, resolve every consequence,
        /// and return the net money change. Uses the real registry and resolver rather than
        /// re-implementing the payout maths, so the test cannot drift from shipping behaviour.
        /// </summary>
        private float RunStrategy(System.Func<SampleGroundTruth, (Verdict, RootCauseDef)> decide)
        {
            var registry = new SampleRegistry();
            var economy = new Economy(tuning);
            var generator = new SampleGenerator(content.AllFaults);
            var rng = new Rng(Seed);

            var profiles = new[]
            {
                content.Profile("hardening_oil_general"),
                content.Profile("quench_oil_cold")
            };

            for (int i = 0; i < Population; i++)
            {
                var profile = profiles[i % profiles.Length];
                var request = GenerationRequest.Default(profile, $"TEST-{i:D3}", 1);
                request.HealthyChance = 0.4f;
                request.CascadeChance = 0f;

                var generated = generator.Generate(request, ref rng);
                registry.Add(generated);

                var (verdict, cause) = decide(generated.Truth);
                registry.FileVerdict(generated.State.Id, verdict, cause, 1);
            }

            // A day far enough out that every pending consequence has come due.
            foreach (var report in registry.ResolveDue(9999, tuning)) economy.Apply(report);

            return economy.Money - tuning.StartingMoney;
        }

        private ConsequenceReport ScoreOne(
            EquipmentProfileDef profile,
            FaultDef fault,
            Verdict verdict,
            ref Rng rng,
            SampleGenerator generator)
        {
            var request = GenerationRequest.Default(profile, "SCORE-01", 1);
            request.CascadeChance = 0f;
            if (fault != null)
            {
                request.ForcedFault = fault;
                request.ForcedSeverity01 = 1f;
            }
            else
            {
                request.ForceHealthy = true;
            }

            var generated = generator.Generate(request, ref rng);
            var registry = new SampleRegistry();
            registry.Add(generated);
            registry.FileVerdict(generated.State.Id, verdict, null, 1);

            var reports = registry.ResolveDue(9999, tuning);
            Assert.AreEqual(1, reports.Count);
            return reports[0];
        }
    }
}
