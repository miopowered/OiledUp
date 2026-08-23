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

            var wrongCause = content.Cause("normal_wear");
            float withWrongCause = RunStrategy(truth => truth.IsHealthy
                ? (Verdict.Normal, (RootCauseDef)null)
                : (Verdict.Critical, wrongCause));

            Assert.Greater(withCause, withWrongCause,
                "Naming the right root cause must pay more than guessing. This is the payout that " +
                "rewards understanding over table lookup (§5.4).");
        }

        /// <summary>
        /// The keystone trap from §4.3: dirt ingress's root cause is the air filter, not the worn
        /// component. A player who files the component is diagnosing the symptom.
        /// </summary>
        [Test]
        public void DirtIngress_RootCauseIsTheAirFilter_NotTheWornComponent()
        {
            var dirt = content.Fault("dirt_ingress");
            Assert.IsNotNull(dirt.RootCause, "Dirt ingress must carry a root cause.");
            Assert.AreEqual("air_filter_failure", dirt.RootCause.Id,
                "Replacing the component that shows the wear does not fix dirt ingress; the filter does.");
        }

        [Test]
        public void MissingAnImminentFault_HurtsMoreThanAFalsePositive()
        {
            var profile = content.Profile("diesel_engine_heavy");
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
                content.Profile("diesel_engine_heavy"),
                content.Profile("gearbox_industrial")
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
