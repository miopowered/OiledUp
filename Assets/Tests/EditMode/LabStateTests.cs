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
    /// Guards the day/run lifecycle. Every test here exists because a PR review found the behaviour
    /// declared but never wired up: a field written once and read nowhere compiles perfectly and
    /// does nothing, which is precisely the failure mode a type checker cannot see.
    /// </summary>
    public sealed class LabStateTests
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

        private static ContractPlan PlanOf(int days, int samplesPerDay = 4, float daySeconds = 600f,
                                           float healthyChance = 0.3f)
        {
            var plan = new ContractPlan { Id = "test", DisplayName = "Test", Days = new List<DayPlan>() };
            for (int i = 0; i < days; i++)
            {
                plan.Days.Add(new DayPlan
                {
                    SampleCount = samplesPerDay,
                    ProfileIds = new[] { "quench_oil_cold" },
                    BorderlineCount = 0,
                    HealthyChance = healthyChance,
                    DaySeconds = daySeconds
                });
            }
            return plan;
        }

        // -----------------------------------------------------------------------------------------
        // §1.2 — a run ends on contract completion or financial failure.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void Contract_EndsAfterItsLastDay_AndCannotBeStartedAgain()
        {
            var lab = new LabState(catalog, PlanOf(3), 4242);

            for (int day = 1; day <= 3; day++)
            {
                Assert.IsTrue(lab.BeginDay(), $"Day {day} should have started.");
                Assert.IsFalse(lab.IsRunOver, $"Run must not be over while day {day} is in progress.");
                lab.EndDay();
            }

            Assert.IsTrue(lab.ContractComplete, "After the final day ends, the contract is complete.");
            Assert.IsTrue(lab.IsRunOver);
            Assert.IsFalse(lab.BeginDay(), "A completed contract must refuse to start another day.");
            Assert.AreEqual(3, lab.Day, "Refusing to start must not advance the day counter.");
        }

        [Test]
        public void Bankruptcy_EndsTheRun()
        {
            var lab = new LabState(catalog, PlanOf(10), 99);
            lab.BeginDay();

            lab.Economy.Charge(lab.Tuning.StartingMoney + 1f);

            Assert.IsTrue(lab.Economy.IsBankrupt);
            lab.EndDay();
            Assert.IsTrue(lab.IsRunOver, "Running out of money must end the run (§1.2).");
            Assert.IsFalse(lab.BeginDay(), "A bankrupt outpost cannot open for another day.");
        }

        /// <summary>
        /// Every verdict the player files must be settled before the run ends.
        /// <para>
        /// §5.4 makes the cost land days later on purpose — that is what turns a wrong call into
        /// something you did rather than something the game told you. But a delay only reads as
        /// suspense if it eventually pays out. A verdict that is still pending when the contract
        /// closes is one the player did diagnostic work for and never got an answer to, which makes
        /// the whole loop feel like it does nothing.
        /// </para>
        /// This is deliberately a structural check rather than an arithmetic one on contract length:
        /// it holds whichever way the delay and the contract are balanced, and it keeps holding if
        /// either is retuned later.
        /// </summary>
        [Test]
        public void EveryFiledVerdict_IsSettledBeforeTheRunEnds()
        {
            var lab = new LabState(catalog, ContractPlan.Default(), 20260823);

            int filed = 0;
            int reported = 0;

            while (lab.BeginDay())
            {
                foreach (var sample in lab.OpenSamples())
                {
                    // Verdict choice is irrelevant here: the delay comes from the unit's fault, not
                    // from what the player called it. Monitor is avoided only because it requeues.
                    if (lab.Samples.FileVerdict(sample.Id, Verdict.Normal, null, lab.Day)) filed++;
                }

                reported += lab.EndDay().Count;
            }

            Assert.Greater(filed, 0, "Test is vacuous unless verdicts were actually filed.");

            Assert.IsEmpty(lab.Samples.Pending,
                $"The run ended with {lab.Samples.Pending.Count} of {filed} filed verdicts never " +
                "settled. Every fault's DaysToFailure is longer than the contract, so the player " +
                "never learns whether a single diagnosis was right.");

            Assert.AreEqual(filed, reported,
                $"Filed {filed} verdicts but only {reported} ever produced a report.");
        }

        // -----------------------------------------------------------------------------------------
        // §6.1 — the working day is a constraint, not a decoration.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void ShiftEnds_WhenTheDayTimerExpires()
        {
            var lab = new LabState(catalog, PlanOf(2, daySeconds: 60f), 7);
            lab.BeginDay();

            Assert.IsFalse(lab.ShiftOver, "The shift is not over at the start of the day.");

            lab.Tick(59f);
            Assert.IsFalse(lab.ShiftOver);

            lab.Tick(2f);
            Assert.IsTrue(lab.ShiftOver, "Once the clock runs out the shift must actually be over.");
            Assert.IsTrue(lab.DayInProgress, "The day stays open so verdicts can still be filed.");
        }

        // -----------------------------------------------------------------------------------------
        // §5.2 — contamination is only fair because a blank reveals it.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void BlankRun_LeavesAReadableResultOnTheInstrument()
        {
            var lab = new LabState(catalog, PlanOf(1), 31337);
            var titrator = lab.Install(catalog.Machine("karl_fischer"), "karl_fischer");
            lab.BeginDay();

            Assert.IsNull(titrator.LastBlank, "No blank has been run yet.");

            // Push real samples through so there is residue to find.
            foreach (var sample in lab.OpenSamples().Take(3))
            {
                sample.IsSettled = true;
                if (titrator.TryLoad(sample) != LoadRefusal.Accepted) continue;
                titrator.TryBeginRun();
                lab.Tick(titrator.RunSeconds + 1f);
                titrator.Unload();
            }

            Assert.IsTrue(titrator.TryBeginBlank(), "A blank should be startable on an empty instrument.");
            lab.Tick(titrator.RunSeconds + 1f);

            Assert.IsNotNull(titrator.LastBlank,
                "The blank result must survive on the instrument. Paying for a tell you cannot read " +
                "is the same as having no tell, and §5.2 depends on it.");
            Assert.IsTrue(titrator.LastBlank.IsBlank);
            Assert.IsTrue(titrator.LastBlank.Values.ContainsKey("Water"));
            Assert.AreEqual(lab.Day, titrator.LastBlankDay);
        }

        /// <summary>
        /// Instruments produce readings; they do not file them. A result reaches the sample's record
        /// only when the player carries the printout to the terminal. If this ever regresses, the
        /// numbers teleport across the room again and the lab goes back to being a menu.
        /// </summary>
        [Test]
        public void RunningAnInstrument_DoesNotFileTheResultItself()
        {
            var lab = new LabState(catalog, PlanOf(1), 606);
            var elemental = lab.Install(catalog.Machine("elemental"), "elemental");
            lab.BeginDay();

            var sample = lab.OpenSamples().First();
            sample.IsSettled = true;

            Assert.AreEqual(LoadRefusal.Accepted, elemental.TryLoad(sample));
            Assert.IsTrue(elemental.TryBeginRun());

            float volumeBefore = sample.VolumeMl;
            lab.Tick(elemental.RunSeconds + 1f);

            Assert.IsNotNull(elemental.LastResult, "The instrument must have produced a reading.");
            Assert.Less(sample.VolumeMl, volumeBefore, "The run must still consume sample volume.");

            Assert.IsEmpty(sample.Results,
                "The instrument filed its own result. Results belong to the sample only once the " +
                "player has walked the printout to the terminal.");
            Assert.AreEqual(ReadingSeverity.Normal, sample.WorstReading(),
                "With nothing filed, the terminal has nothing to score.");

            // Filing is what the terminal does on receiving the slip.
            sample.Results.Add(elemental.LastResult);
            Assert.AreEqual(1, sample.Results.Count);
            Assert.IsTrue(sample.TryGetLatest("Fe", out _, out _),
                "Once filed, the reading is part of the record and can be cross-referenced.");
        }

        [Test]
        public void RunningASample_DoesNotOverwriteTheLastBlank()
        {
            var lab = new LabState(catalog, PlanOf(1), 5150);
            var titrator = lab.Install(catalog.Machine("karl_fischer"), "karl_fischer");
            lab.BeginDay();

            titrator.TryBeginBlank();
            lab.Tick(titrator.RunSeconds + 1f);
            var blank = titrator.LastBlank;
            Assert.IsNotNull(blank);

            var sample = lab.OpenSamples().First();
            sample.IsSettled = true;
            Assert.AreEqual(LoadRefusal.Accepted, titrator.TryLoad(sample));
            titrator.TryBeginRun();
            lab.Tick(titrator.RunSeconds + 1f);

            Assert.AreSame(blank, titrator.LastBlank,
                "A later sample run must not clobber the blank the player is relying on.");
        }

        // -----------------------------------------------------------------------------------------
        // §5.4 — MONITOR on a developing fault re-sends the sample with worse numbers.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void MonitorOnDevelopingFault_ResendsTheSameUnitWorse()
        {
            // Enough arrivals, and few enough healthy ones, that a Developing fault is a certainty
            // rather than a seed lottery. Five of the seven diesel-valid faults are Developing.
            var lab = new LabState(catalog, PlanOf(24, samplesPerDay: 12, healthyChance: 0.05f), 20260823);
            lab.BeginDay();

            SampleState target = null;
            float originalSeverity = 0f;
            string faultId = null;

            foreach (var sample in lab.OpenSamples())
            {
                var truth = lab.Samples.PeekTruthForDebugging(sample.Id);
                if (truth == null || truth.IsHealthy) continue;
                if (truth.WorstSeverity != FaultSeverity.Developing) continue;

                target = sample;
                originalSeverity = truth.FaultSeverities[0];
                faultId = truth.PrimaryFault.Id;
                break;
            }

            Assert.IsNotNull(target, "Test needs at least one developing fault in the first day's arrivals.");

            string tag = target.EquipmentTag;
            lab.Samples.FileVerdict(target.Id, Verdict.Monitor, null, lab.Day);

            // Consequences land days later, so run the contract until this one resolves. Match on
            // ResampleOf rather than the tag: tags repeat legitimately across a contract, and an
            // earlier version of this test matched an unrelated sample that happened to share one.
            SampleState resample = null;
            for (int i = 0; i < 23 && resample == null; i++)
            {
                lab.EndDay();
                if (!lab.BeginDay()) break;

                foreach (var s in lab.Samples.All)
                {
                    if (s.ResampleOf != target.Id) continue;
                    resample = s;
                    break;
                }
            }

            Assert.IsNotNull(resample,
                $"'{tag}' was filed MONITOR and should have come back for another draw (§5.4).");
            Assert.AreEqual(tag, resample.EquipmentTag, "The re-draw must be from the same unit.");

            var resampleTruth = lab.Samples.PeekTruthForDebugging(resample.Id);
            Assert.IsNotNull(resampleTruth);
            Assert.AreEqual(faultId, resampleTruth.PrimaryFault.Id,
                "The re-draw must carry the SAME fault. Re-rolling it would make MONITOR a coin flip " +
                "rather than a decision to keep watching something specific.");
            Assert.Greater(resampleTruth.FaultSeverities[0], originalSeverity,
                "The fault must have progressed, or MONITOR costs the player nothing to repeat.");
        }
    }
}
