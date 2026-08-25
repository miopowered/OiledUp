using System.Linq;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Editor.Content;
using Residue.Gameplay.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards §5.3 — calibration drift, and the deal it strikes with the player.
    /// <para>
    /// Drift is hidden state that silently scales every reading, which hard rule 3 only tolerates
    /// because a certified reference sample reveals it. Every test here protects one leg of that
    /// bargain: the standard has to measure the error honestly, the correction has to reach back over
    /// the work the error touched, and the records it puts in doubt have to be re-openable while
    /// there is still oil left to check them with. Take away any one and drift stops being a fair
    /// mechanic and becomes a tax on nothing the player could have seen.
    /// </para>
    /// </summary>
    public sealed class CalibrationTests
    {
        private const int Seed = 1974;

        private ContentSet content;

        [SetUp]
        public void SetUp() => content = ContentBuilder.BuildInMemory();

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

        private ReferenceStandard Standard =>
            ReferenceStandard.FromProfiles(new System.Collections.Generic.List<EquipmentProfileDef>(
                content.Profiles.Values));

        private static MachineRuntimeState Instrument(MachineDef def) =>
            new() { InstanceId = $"{def.Id}-test", Def = def };

        // -----------------------------------------------------------------------------------------
        // The standard itself. A tell nobody can check the answer against is not a tell.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Every instrument has to be checkable. An instrument the standard does not cover would
        /// accumulate drift with no way to measure it — hidden state with no tell, which hard rule 3
        /// forbids outright.
        /// </summary>
        [Test]
        public void EveryInstrument_CanBeCheckedAgainstTheStandard()
        {
            var standard = Standard;

            foreach (var def in content.Machines.Values)
            {
                bool covered = def.Measures.Any(
                    e => e != null && !def.IsBlindTo(e.Id) && standard.TryGet(e.Id, out float v) && v > 0f);

                Assert.IsTrue(covered,
                    $"{def.Id} measures nothing the certified standard carries, so its calibration " +
                    "can never be checked and its drift is unfalsifiable.");
            }
        }

        /// <summary>
        /// The certificate must be made of numbers the player can already look up. Deriving it from
        /// the published healthy baselines is what makes that true; an independently authored set
        /// would be a second source of truth that could disagree with the manual.
        /// </summary>
        [Test]
        public void EveryCertifiedValue_SitsInsideThePublishedBaselines()
        {
            var standard = Standard;

            foreach (var kv in standard.Certified)
            {
                float low = float.PositiveInfinity;
                float high = float.NegativeInfinity;

                foreach (var profile in content.Profiles.Values)
                {
                    if (!profile.TryGetThreshold(kv.Key, out var threshold)) continue;
                    if (threshold.Baseline <= 0f) continue;
                    low = Mathf.Min(low, threshold.Baseline);
                    high = Mathf.Max(high, threshold.Baseline);
                }

                Assert.GreaterOrEqual(kv.Value, low - 0.001f, $"{kv.Key} certified below every baseline.");
                Assert.LessOrEqual(kv.Value, high + 0.001f, $"{kv.Key} certified above every baseline.");
            }
        }

        // -----------------------------------------------------------------------------------------
        // The measurement. Hard rule 1 — the chemistry never lies, so the error must be the drift.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void AHonestInstrument_ReadsTheCertificateBack()
        {
            var machine = Instrument(content.Machine("karl_fischer"));
            var rng = new Rng(Seed);

            var result = MeasurementPipeline.RunReference(Standard, machine, 1, ref rng);
            var check = CalibrationCheck.From(Standard, result, machine.Def, 1);

            Assert.IsTrue(result.IsReference, "A standard run must be distinguishable from a customer's oil.");
            Assert.Greater(check.Lines.Count, 0);
            Assert.IsFalse(check.IsOutOfTolerance,
                $"A clean, undrifted instrument read {check.ErrorFraction:P1} off its own certificate. " +
                "If the check cannot clear a good machine, the player learns to ignore it.");
        }

        /// <summary>
        /// The whole mechanic in one assertion: known values in, so whatever comes back out is the
        /// instrument's error and nothing else. If this drifts, the reference sample stops measuring
        /// drift and starts measuring the pipeline.
        /// </summary>
        [Test]
        public void ADriftedInstrument_ReportsAnErrorEqualToItsDrift()
        {
            var machine = Instrument(content.Machine("karl_fischer"));
            machine.DriftPercent = 0.18f;

            var rng = new Rng(Seed);
            var result = MeasurementPipeline.RunReference(Standard, machine, 1, ref rng);
            var check = CalibrationCheck.From(Standard, result, machine.Def, 1);

            Assert.IsTrue(check.IsOutOfTolerance);
            Assert.AreEqual(0.18f, check.ErrorFraction, 0.04f,
                $"An instrument reading 18% high reported {check.ErrorFraction:P1}. The certificate is " +
                "the only thing standing between the player and unfalsifiable hidden state.");
        }

        /// <summary>
        /// A standard is an oil, so residue inflates it exactly as it inflates a real sample. That is
        /// deliberate and it is why the check does not name its own cause: separating "dirty" from
        /// "drifted" is what the §5.2 blank is for. Deleting this confound would delete the flush
        /// decision along with it.
        /// </summary>
        [Test]
        public void AnUnflushedInstrument_FailsItsOwnCheck()
        {
            var def = content.Machine("karl_fischer");
            var standard = Standard;
            standard.TryGet("Water", out float certified);

            var machine = Instrument(def);
            machine.Residue["Water"] = certified * 0.5f;

            var rng = new Rng(Seed);
            var result = MeasurementPipeline.RunReference(standard, machine, 1, ref rng);
            var check = CalibrationCheck.From(standard, result, def, 1);

            Assert.IsTrue(check.IsOutOfTolerance,
                "Carryover has to show up in a standard, or a player could calibrate away contamination.");
        }

        [Test]
        public void AStandardRun_CostsNoSampleVolume()
        {
            var machine = Instrument(content.Machine("viscometer"));
            var rng = new Rng(Seed);

            var result = MeasurementPipeline.RunReference(Standard, machine, 1, ref rng);

            Assert.AreEqual(0f, result.VolumeConsumedMl,
                "The ampoule is the consumable. Charging a customer's sample for a calibration check " +
                "would make checking the instrument cost the very thing checking it is meant to protect.");
        }

        // -----------------------------------------------------------------------------------------
        // The retroactive list. §5.3's dread, and the reason a verdict is never quite finished.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void Calibrating_ZeroesTheDrift_AndOpensAFreshSuspicionWindow()
        {
            var machine = Instrument(content.Machine("karl_fischer"));
            machine.DriftPercent = 0.18f;
            machine.RunIndex = 9;
            machine.DriftStartedAtRunIndex = 3;

            float corrected = machine.Calibrate(day: 4);

            Assert.AreEqual(0.18f, corrected, 0.0001f, "The correction has to report what it took out.");
            Assert.AreEqual(0f, machine.DriftPercent);
            Assert.AreEqual(0, machine.RunsSinceCalibration);
            Assert.AreEqual(9, machine.DriftStartedAtRunIndex,
                "Suspicion must start again from now, or the next calibration re-flags work that " +
                "was taken on a corrected instrument.");
            Assert.AreEqual(4, machine.LastCalibratedDay);
        }

        /// <summary>
        /// Only the runs taken inside the drift episode are in doubt. Flagging everything the machine
        /// ever touched would make the list noise, and a list nobody reads is the same as no list.
        /// </summary>
        [Test]
        public void OnlyTheRunsTakenWhileDrifting_AreMarkedSuspect()
        {
            var machine = Instrument(content.Machine("karl_fischer"));
            machine.DriftPercent = 0.18f;
            machine.RunIndex = 8;
            machine.DriftStartedAtRunIndex = 4;

            var registry = new SampleRegistry();
            var before = Measured(1, "WERK-1 QUENCH 1", machine.Def.Id, runIndex: 2);
            var during = Measured(2, "WERK-1 QUENCH 2", machine.Def.Id, runIndex: 6);
            Register(registry, before);
            Register(registry, during);

            registry.FileVerdict(during.Id, Verdict.Critical, null, 2);

            var outcome = registry.FlagDriftSuspects(machine, machine.DriftPercent, day: 3);

            Assert.AreEqual(1, outcome.FlaggedResults);
            Assert.AreEqual(1, outcome.AffectedSamples);
            Assert.AreEqual(1, outcome.AffectedArchived);
            Assert.IsTrue(outcome.CastsDoubt);

            Assert.IsFalse(before.Results[0].Suspect, "A run from before the drift started is not in doubt.");
            Assert.IsTrue(during.Results[0].Suspect);

            CollectionAssert.Contains(registry.SuspectArchive(), during);
            CollectionAssert.DoesNotContain(registry.SuspectArchive(), before);
        }

        /// <summary>
        /// Drift below the tolerance is instrument noise, not error. Calling it error would put good
        /// records in doubt every single time anyone calibrated anything.
        /// </summary>
        [Test]
        public void ADriftTooSmallToMatter_PutsNothingInDoubt()
        {
            var machine = Instrument(content.Machine("karl_fischer"));
            machine.DriftPercent = CalibrationCheck.Tolerance * 0.5f;
            machine.RunIndex = 5;
            machine.DriftStartedAtRunIndex = 0;

            var registry = new SampleRegistry();
            var sample = Measured(1, "WERK-1 QUENCH 1", machine.Def.Id, runIndex: 2);
            Register(registry, sample);

            var outcome = registry.FlagDriftSuspects(machine, machine.DriftPercent, day: 3);

            Assert.AreEqual(0, outcome.FlaggedResults);
            Assert.IsFalse(outcome.CastsDoubt);
            Assert.IsFalse(sample.Results[0].Suspect);
        }

        // -----------------------------------------------------------------------------------------
        // Re-opening. Hard rule 3 both ways: the player checked, so they must be able to act — and
        // the oil is finite, so sometimes there is nothing left to act with.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void ASuspectRecord_ReopensWhenThereIsOilLeftToRetestWith()
        {
            var (registry, sample) = SuspectFiledRecord(volumeMl: 40f);

            Assert.IsTrue(registry.ReopenForRetest(sample.Id, 5f, out string refusal), refusal);

            Assert.AreEqual(SampleStage.Measured, sample.Stage);
            Assert.IsFalse(sample.FiledVerdict.HasValue, "The verdict is what comes off.");
            Assert.AreEqual(-1, sample.FiledOnDay);
            Assert.AreEqual(1, sample.Results.Count,
                "Results stay on file. Re-opening invites a re-run to compare against, not a blank slate.");
            Assert.IsTrue(sample.Results[0].Suspect,
                "The suspect flag stays too — it is the reason the record was re-opened.");
            Assert.AreEqual("WERK-1 QUENCH 1", sample.RecordTag,
                "The tag was never in doubt, so re-opening must not put it back in play.");
        }

        /// <summary>
        /// The refusal is the mechanic. §5.3 makes the retroactive list pressure rather than
        /// paperwork precisely because some of it cannot be answered: the oil is gone, the reading
        /// can never be repeated, and the verdict stands on numbers the player now knows were wrong.
        /// </summary>
        [Test]
        public void ASuspectRecord_WithNoOilLeft_IsRefused_AndSaysHowShortItIs()
        {
            var (registry, sample) = SuspectFiledRecord(volumeMl: 2f);

            Assert.IsFalse(registry.ReopenForRetest(sample.Id, 5f, out string refusal));

            Assert.AreEqual(SampleStage.Archived, sample.Stage, "A refused re-opening must not half-apply.");
            Assert.AreEqual(Verdict.Critical, sample.FiledVerdict);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("2.0 ml", refusal);
            StringAssert.Contains("5 ml", refusal);
        }

        /// <summary>
        /// The consequence of the withdrawn verdict has to be withdrawn with it. A record that could
        /// be re-filed while its first call was still on the way to landing would pay out twice.
        /// </summary>
        [Test]
        public void Reopening_CancelsTheQueuedConsequence()
        {
            var (registry, sample) = SuspectFiledRecord(volumeMl: 40f);
            Assert.AreEqual(1, registry.Pending.Count);

            Assert.IsTrue(registry.ReopenForRetest(sample.Id, 5f, out _));
            Assert.AreEqual(0, registry.Pending.Count);

            Assert.IsTrue(registry.FileVerdict(sample.Id, Verdict.Normal, null, 4, out string refusal), refusal);
            Assert.AreEqual(1, registry.Pending.Count,
                "Re-filing after a re-opening must queue exactly one consequence, not a second.");

            var reports = registry.ResolveDue(9999, new EconomyTuning());
            Assert.AreEqual(1, reports.Count, "A re-opened record must resolve once, for the call that stuck.");
        }

        [Test]
        public void AResolvedRecord_CannotBeReopened()
        {
            var (registry, sample) = SuspectFiledRecord(volumeMl: 40f);
            registry.ResolveDue(9999, new EconomyTuning());
            Assert.AreEqual(SampleStage.Resolved, sample.Stage);

            Assert.IsFalse(registry.ReopenForRetest(sample.Id, 5f, out string refusal),
                "The consequence has landed. The equipment was pulled or it was not; there is no re-test.");
            Assert.IsNotNull(refusal);
            StringAssert.Contains("resolved", refusal);
        }

        /// <summary>
        /// The Archived -> Measured edge exists for <c>TryReopen</c> and nothing else. Before it was
        /// added, a closed record refused new slips because the table refused the transition; now the
        /// table permits it, and only the gateway stands between a filed verdict and evidence being
        /// appended behind it.
        /// </summary>
        [Test]
        public void AFiledRecord_StillRefusesNewSlips()
        {
            var (_, sample) = SuspectFiledRecord(volumeMl: 40f);

            Assert.IsFalse(SampleLifecycle.TryFileResult(
                sample, new TestResult { MachineId = "viscometer", DayRun = 3 }, out string refusal));

            Assert.AreEqual(1, sample.Results.Count);
            Assert.AreEqual(Verdict.Critical, sample.FiledVerdict);
            StringAssert.Contains("Re-open", refusal,
                "The refusal should point at the remedy, not just say no.");
        }

        // -----------------------------------------------------------------------------------------
        // Consumables. The tell has to stay purchasable or drift becomes uncheckable mid-contract.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void ReferenceStandards_CanBeRestocked_SoDriftNeverBecomesUncheckable()
        {
            var tuning = new EconomyTuning();
            var economy = new Economy(tuning, startingSolvent: 12f, startingStandards: 1);

            Assert.IsTrue(economy.TryConsumeReferenceStandard());
            Assert.IsFalse(economy.TryConsumeReferenceStandard(), "Out of standards — the state under test.");

            Assert.IsTrue(economy.TryBuyReferenceStandards(3),
                "A lab with money in the bank must be able to restock, or every instrument becomes " +
                "unfalsifiable for the rest of the contract.");
            Assert.IsTrue(economy.TryConsumeReferenceStandard());
        }

        [Test]
        public void BuyingStandards_IsRefused_WhenUnaffordable_AndChargesNothing()
        {
            var tuning = new EconomyTuning();
            var economy = new Economy(tuning, startingSolvent: 0f, startingStandards: 0);
            economy.Charge(tuning.StartingMoney);

            float before = economy.Money;

            Assert.IsFalse(economy.TryBuyReferenceStandards(5));
            Assert.AreEqual(before, economy.Money, "A refused purchase must not charge.");
            Assert.AreEqual(0, economy.ReferenceStandards, "A refused purchase must not deliver.");
        }

        /// <summary>
        /// Checking an instrument you have started to distrust must be comfortably affordable against
        /// what the work pays. If a check plus its correction outruns a job, the rational play is to
        /// never look — and drift goes back to being hidden state with no tell.
        /// </summary>
        [Test]
        public void CheckingAndCorrectingAnInstrument_CostsLessThanTheWorkPays()
        {
            var tuning = new EconomyTuning();

            Assert.Less(tuning.ReferenceStandardCost + tuning.CalibrationCost, tuning.BasePayout,
                $"A check plus a recalibration costs " +
                $"{tuning.ReferenceStandardCost + tuning.CalibrationCost:F0} against a " +
                $"{tuning.BasePayout:F0} base payout.");
            Assert.Greater(tuning.ReferenceStandardCost, tuning.SolventUnitCost,
                "A certified ampoule must cost more than a flush, or checking every instrument every " +
                "morning becomes the obvious routine rather than a decision.");
        }

        // -- helpers ------------------------------------------------------------------------------

        /// <summary>An unpacked, prepped sample with one filed run from <paramref name="machineId"/>.</summary>
        private static SampleState Measured(int id, string tag, string machineId, int runIndex,
                                            float volumeMl = 40f)
        {
            var sample = new SampleState
            {
                Id = new SampleId(id),
                EquipmentTag = tag,
                VolumeMl = volumeMl,
                Location = SampleLocation.InCrate("intake", 0)
            };

            Assert.IsTrue(SampleLifecycle.TryMove(sample, SampleLocation.Held(0), out _));
            Assert.IsTrue(SampleLifecycle.TryPrep(sample, out _));
            Assert.IsTrue(SampleLifecycle.TryFileResult(
                sample,
                new TestResult { MachineId = machineId, DayRun = 1, MachineRunIndex = runIndex },
                out _));

            return sample;
        }

        private static void Register(SampleRegistry registry, SampleState sample) =>
            registry.Add(new GeneratedSample
            {
                State = sample,
                Truth = new SampleGroundTruth { Id = sample.Id }
            });

        /// <summary>
        /// One filed record whose only reading came off an instrument that has since been shown to be
        /// drifting — the exact situation §5.3 builds its retroactive list out of.
        /// </summary>
        private (SampleRegistry Registry, SampleState Sample) SuspectFiledRecord(float volumeMl)
        {
            var machine = Instrument(content.Machine("karl_fischer"));
            machine.DriftPercent = 0.18f;
            machine.RunIndex = 6;
            machine.DriftStartedAtRunIndex = 1;

            var registry = new SampleRegistry();
            var sample = Measured(1, "WERK-1 QUENCH 1", machine.Def.Id, runIndex: 3, volumeMl: volumeMl);
            Register(registry, sample);

            registry.FileVerdict(sample.Id, Verdict.Critical, null, 2);
            var outcome = registry.FlagDriftSuspects(machine, machine.DriftPercent, day: 3);
            Assert.AreEqual(1, outcome.FlaggedResults, "Fixture must actually produce a suspect record.");

            return (registry, sample);
        }
    }
}
