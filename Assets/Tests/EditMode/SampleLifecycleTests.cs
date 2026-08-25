using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards the §5.1 sample lifecycle. Every test here protects a promise about the <i>record</i>
    /// rather than about the chemistry: that work happens in the order the lab says it does, and that
    /// a closed record stays closed.
    /// <para>
    /// <b>What used to be here.</b> A block of tests guarded booking-in: that a wrong tank tag was
    /// accepted as readily as a right one, that typography was not a failure mode, and that a mis-log
    /// spotted before work started could be amended. They defended one pillar — hard rule 3, "never
    /// punish something the player could not have checked" — as it applied to a transcription step.
    /// #73 deleted the step: a sample now carries the tag printed on its label from the moment it
    /// exists, so there is nothing to transcribe, nothing to get wrong, and no punishment left for
    /// hard rule 3 to make fair. The pillar is untouched; the mechanic it was protecting is gone.
    /// </para>
    /// </summary>
    public sealed class SampleLifecycleTests
    {
        /// <summary>
        /// The chain, written out again rather than read back from <see cref="SampleLifecycle"/>.
        /// A test that asks the implementation what it permits cannot notice the implementation
        /// permitting the wrong thing.
        /// </summary>
        private static readonly (SampleStage From, SampleStage To)[] LegalTransitions =
        {
            (SampleStage.InCrate, SampleStage.Unpacked),
            (SampleStage.InCrate, SampleStage.Archived),

            (SampleStage.Unpacked, SampleStage.Prepped),
            (SampleStage.Unpacked, SampleStage.Archived),

            (SampleStage.Prepped, SampleStage.Prepped),
            (SampleStage.Prepped, SampleStage.Measured),
            (SampleStage.Prepped, SampleStage.Archived),

            (SampleStage.Measured, SampleStage.Measured),
            (SampleStage.Measured, SampleStage.Archived),

            // §5.3 re-opens an archived record when a certified standard proves the instrument was
            // drifting when its numbers were taken. Only SampleLifecycle.TryReopen may use this edge.
            (SampleStage.Archived, SampleStage.Measured),

            (SampleStage.Archived, SampleStage.Resolved)
        };

        private static SampleStage[] AllStages => (SampleStage[])Enum.GetValues(typeof(SampleStage));

        private const string Tag = "WERK-1 QUENCH 1";

        private static SampleState Arriving(string tag = Tag, int id = 1) => new()
        {
            Id = new SampleId(id),
            EquipmentTag = tag,
            Location = SampleLocation.InCrate("intake", 0)
        };

        /// <summary>
        /// A sample walked to <paramref name="stage"/> along the real chain, so the happy path is
        /// re-checked by every test that needs a starting position.
        /// </summary>
        private static SampleState At(SampleStage stage, string tag = Tag, int id = 1)
        {
            var sample = Arriving(tag, id);
            if (stage == SampleStage.InCrate) return sample;

            Assert.IsTrue(SampleLifecycle.TryMove(sample, SampleLocation.Held(0), out _), "unload");
            if (stage == SampleStage.Unpacked) return sample;

            Assert.IsTrue(SampleLifecycle.TryPrep(sample, out _), "prep");
            if (stage == SampleStage.Prepped) return sample;

            Assert.IsTrue(SampleLifecycle.TryFileResult(sample, Slip("elemental"), out _), "file results");
            if (stage == SampleStage.Measured) return sample;

            Assert.IsTrue(SampleLifecycle.TryArchive(sample, Verdict.Normal, null, 1, out _), "file verdict");
            if (stage == SampleStage.Archived) return sample;

            Assert.IsTrue(SampleLifecycle.TryResolve(sample, out _), "resolve");
            return sample;
        }

        private static Residue.Chemistry.TestResult Slip(string machineId) =>
            new() { MachineId = machineId, DayRun = 1 };

        private static SampleRegistry RegistryWith(SampleState sample)
        {
            var registry = new SampleRegistry();
            registry.Add(new GeneratedSample
            {
                State = sample,
                Truth = new SampleGroundTruth { Id = sample.Id }
            });
            return registry;
        }

        // -----------------------------------------------------------------------------------------
        // The table itself. One place decides what is legal, so one test can state the whole rule.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void TransitionTable_IsExactlyTheSection51Chain()
        {
            var expected = new HashSet<(SampleStage, SampleStage)>(LegalTransitions);

            foreach (var from in AllStages)
            {
                foreach (var to in AllStages)
                {
                    bool want = expected.Contains((from, to));
                    Assert.AreEqual(want, SampleLifecycle.IsLegal(from, to),
                        $"{from} -> {to} should be {(want ? "legal" : "rejected")}.");
                }
            }
        }

        [Test]
        public void LegalNext_AgreesWithTheTableItIsBuiltFrom()
        {
            foreach (var from in AllStages)
            {
                var successors = new HashSet<SampleStage>(SampleLifecycle.LegalNext(from));
                foreach (var to in AllStages)
                {
                    Assert.AreEqual(SampleLifecycle.IsLegal(from, to), successors.Contains(to),
                        $"LegalNext({from}) disagrees with IsLegal({from}, {to}).");
                }
            }
        }

        /// <summary>
        /// A refusal the player cannot read is the same as an action that silently does nothing.
        /// §9 is explicit that the game must say what is wrong, and that starts at this seam —
        /// every station's prompt is only as good as the sentence it gets from here.
        /// </summary>
        [Test]
        public void EveryRejection_SaysWhy()
        {
            foreach (var from in AllStages)
            {
                foreach (var to in AllStages)
                {
                    string why = SampleLifecycle.Explain(from, to);

                    if (SampleLifecycle.IsLegal(from, to))
                    {
                        Assert.IsNull(why, $"{from} -> {to} is legal but produced a refusal: '{why}'.");
                        continue;
                    }

                    Assert.IsFalse(string.IsNullOrWhiteSpace(why),
                        $"{from} -> {to} is rejected with no explanation, so it fails silently.");
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // The stage is a reading of the record, not a second copy of it.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Location, settling, results and the verdict are written from half a dozen places, some of
        /// which predate the lifecycle. If the stage were stored, any one of those writes would put
        /// the machine out of step with the data it guards — and a lifecycle that disagrees with the
        /// record is worse than none, because it looks authoritative.
        /// </summary>
        [Test]
        public void Stage_IsReadOffTheRecord_NotStoredBesideIt()
        {
            var sample = At(SampleStage.Measured);
            Assert.AreEqual(SampleStage.Measured, sample.Stage);

            sample.Results.Clear();
            Assert.AreEqual(SampleStage.Prepped, sample.Stage,
                "Rewriting the record behind the lifecycle's back must move the stage with it.");

            sample.FiledVerdict = Verdict.Critical;
            Assert.AreEqual(SampleStage.Archived, sample.Stage);

            sample.ConsequenceResolved = true;
            Assert.AreEqual(SampleStage.Resolved, sample.Stage);
        }

        /// <summary>
        /// A derived stage has one failure mode a stored one does not: it can report a stage the
        /// transition table cannot produce.
        /// <para>
        /// This test used to be about booking in. Settling was read as <see cref="SampleStage.Prepped"/>
        /// on its own, so a stray <c>IsSettled</c> write put an unlogged vial at Prepped — off-chain,
        /// since Prepped was only reachable via Logged — and booking in was then refused forever,
        /// stranding it. #73 removed the logging step, and with it that particular strand.
        /// </para>
        /// <para>
        /// The <i>class</i> of bug it was guarding did not go away, so the test moved down one step
        /// rather than being deleted. Unloading is still a real edge, and a stray write on a vial
        /// still in the crate would still derive a stage <see cref="SampleStage.InCrate"/> has no
        /// edge to. The pillar — a derived stage must never report something the table cannot
        /// produce — is exactly the one <see cref="SampleLifecycle"/> is built on.
        /// </para>
        /// </summary>
        [Test]
        public void SettlingAVialStillInTheCrate_DoesNotPutItOffTheChain()
        {
            var sample = Arriving();

            // Reach around the lifecycle, the way a careless call site would.
            sample.IsSettled = true;

            Assert.AreEqual(SampleStage.InCrate, sample.Stage,
                "A vial in the crate cannot be Prepped — the table has no edge that gets there.");

            Assert.IsTrue(SampleLifecycle.TryMove(sample, SampleLocation.OnSurface("bench", 0), out var refusal),
                $"Unloading must still be possible after a stray write: {refusal}");

            Assert.AreEqual(SampleStage.Prepped, sample.Stage,
                "Once it is out of the crate, the settling that already happened counts as prep.");
        }

        [Test]
        public void TheHappyPath_VisitsEveryStageInOrder()
        {
            var sample = Arriving();
            var seen = new List<SampleStage> { sample.Stage };

            SampleLifecycle.TryMove(sample, SampleLocation.Held(0), out _);
            seen.Add(sample.Stage);
            SampleLifecycle.TryPrep(sample, out _);
            seen.Add(sample.Stage);
            SampleLifecycle.TryFileResult(sample, Slip("elemental"), out _);
            seen.Add(sample.Stage);
            SampleLifecycle.TryArchive(sample, Verdict.Normal, null, 4, out _);
            seen.Add(sample.Stage);
            SampleLifecycle.TryResolve(sample, out _);
            seen.Add(sample.Stage);

            CollectionAssert.AreEqual(AllStages, seen,
                "The §5.1 chain must be walkable end to end using nothing but the lifecycle API.");
        }

        // -----------------------------------------------------------------------------------------
        // Rejection paths, one per step.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Storage is a location, not progress. §5.1 branches to [fridge | bench] before prep; if
        /// putting a vial down counted as a step, a shelf would become part of the record.
        /// </summary>
        [Test]
        public void OnlyTheMoveOutOfTheCrate_AdvancesTheChain()
        {
            var sample = At(SampleStage.InCrate);
            Assert.IsTrue(SampleLifecycle.TryMove(sample, SampleLocation.Held(0), out _));
            Assert.AreEqual(SampleStage.Unpacked, sample.Stage);

            foreach (var destination in new[]
                     {
                         SampleLocation.InFridge(3),
                         SampleLocation.OnSurface("bench", 1),
                         SampleLocation.Held(0)
                     })
            {
                Assert.IsTrue(SampleLifecycle.TryMove(sample, destination, out _));
                Assert.AreEqual(SampleStage.Unpacked, sample.Stage,
                    $"Moving to {destination.Kind} must not change how far along the sample is.");
                Assert.AreEqual(destination.Kind, sample.Location.Kind);
            }
        }

        /// <summary>
        /// The other half of #73. This replaces <c>Prep_IsRefusedUntilTheVialIsBookedIn</c>, which
        /// guarded "§5.1 happens in order" against a step that no longer exists. The ordering pillar
        /// is unchanged and still has one real gate in front of prep — the crate — so the test now
        /// asserts both directions of it: refused while boxed, available the moment it is not.
        /// </summary>
        [Test]
        public void Prep_IsRefusedInTheCrate_AndAvailableAsSoonAsTheVialIsOut()
        {
            var sample = At(SampleStage.InCrate);

            Assert.IsFalse(SampleLifecycle.TryPrep(sample, out string refusal),
                "A vial nobody has unpacked cannot be agitated.");
            Assert.IsFalse(sample.IsSettled, "A refused transition must not half-apply.");
            StringAssert.Contains("crate", refusal);

            Assert.IsTrue(SampleLifecycle.TryMove(sample, SampleLocation.Held(0), out _));

            Assert.IsTrue(SampleLifecycle.TryPrep(sample, out refusal),
                $"Unloading is the only thing standing in front of prep: {refusal}");
            Assert.AreEqual(SampleStage.Prepped, sample.Stage);
        }

        [Test]
        public void Prep_IsRefusedOnceTheRecordIsClosed()
        {
            foreach (var stage in new[] { SampleStage.Archived, SampleStage.Resolved })
            {
                var sample = At(stage);
                Assert.IsFalse(SampleLifecycle.TryPrep(sample, out string refusal), stage.ToString());
                Assert.IsNotNull(refusal);
            }
        }

        [Test]
        public void Results_AreRefusedBeforeTheSampleCouldHaveBeenRun()
        {
            foreach (var stage in new[] { SampleStage.InCrate, SampleStage.Unpacked })
            {
                var sample = At(stage);

                Assert.IsFalse(SampleLifecycle.TryFileResult(sample, Slip("elemental"), out string refusal),
                    $"Nothing can have measured a {stage} sample.");
                Assert.IsEmpty(sample.Results);
                Assert.IsNotNull(refusal);
            }
        }

        /// <summary>
        /// A verdict closes the record. Appending to a closed one would silently change what the
        /// player was looking at when they made the call, which is what the §5.3 "every verdict
        /// filed since the drift started is suspect" list is built on top of.
        /// </summary>
        [Test]
        public void Results_AreRefusedAfterTheVerdictIsFiled()
        {
            foreach (var stage in new[] { SampleStage.Archived, SampleStage.Resolved })
            {
                var sample = At(stage);
                int onFile = sample.Results.Count;

                Assert.IsFalse(SampleLifecycle.TryFileResult(sample, Slip("karl_fischer"), out string refusal),
                    $"A {stage} record must take no further results.");
                Assert.AreEqual(onFile, sample.Results.Count);
                Assert.IsNotNull(refusal);
            }
        }

        [Test]
        public void ASampleTakesManyRuns_ButNotTheSameSlipTwice()
        {
            var sample = At(SampleStage.Prepped);
            var first = Slip("elemental");

            Assert.IsTrue(SampleLifecycle.TryFileResult(sample, first, out _));
            Assert.IsTrue(SampleLifecycle.TryFileResult(sample, Slip("karl_fischer"), out _),
                "A sample goes through more than one instrument (§4.5).");
            Assert.AreEqual(2, sample.Results.Count);

            Assert.IsFalse(SampleLifecycle.TryFileResult(sample, first, out string duplicate));
            StringAssert.Contains("Already on file", duplicate);

            Assert.IsFalse(SampleLifecycle.TryFileResult(sample, null, out string blank));
            Assert.IsNotNull(blank);
            Assert.AreEqual(2, sample.Results.Count);
        }

        /// <summary>
        /// Filing a verdict is always available, including on a vial nobody ever opened. The tension
        /// in the game is that you <i>can</i> call a tank without testing it; §5.6 requires blanket
        /// strategies to be playable so that they can be shown to lose money.
        /// </summary>
        [Test]
        public void AVerdict_CanBeFiledFromAnyOpenStage_ButOnlyOnce()
        {
            foreach (var stage in new[]
                     {
                         SampleStage.InCrate, SampleStage.Unpacked,
                         SampleStage.Prepped, SampleStage.Measured
                     })
            {
                var sample = At(stage);

                Assert.IsTrue(SampleLifecycle.TryArchive(sample, Verdict.Normal, null, 3, out _),
                    $"A {stage} sample must still be callable.");
                Assert.AreEqual(SampleStage.Archived, sample.Stage);
                Assert.AreEqual(SampleLocationKind.Archived, sample.Location.Kind);
                Assert.AreEqual(3, sample.FiledOnDay);

                Assert.IsFalse(SampleLifecycle.TryArchive(sample, Verdict.Critical, null, 4, out string refusal));
                Assert.AreEqual(Verdict.Normal, sample.FiledVerdict,
                    "A refused re-file must not overwrite the verdict already on record.");
                Assert.AreEqual(3, sample.FiledOnDay);
                Assert.IsNotNull(refusal);
            }
        }

        [Test]
        public void AConsequence_CannotLandBeforeAVerdictIsFiled()
        {
            foreach (var stage in new[]
                     {
                         SampleStage.InCrate, SampleStage.Unpacked,
                         SampleStage.Prepped, SampleStage.Measured
                     })
            {
                var sample = At(stage);

                Assert.IsFalse(SampleLifecycle.TryResolve(sample, out string refusal), stage.ToString());
                Assert.IsFalse(sample.ConsequenceResolved);
                Assert.IsNotNull(refusal);
            }
        }

        [Test]
        public void Resolved_IsTerminal()
        {
            var sample = At(SampleStage.Resolved);

            foreach (var stage in AllStages)
            {
                Assert.IsFalse(SampleLifecycle.CanAdvance(sample, stage, out string refusal),
                    $"A resolved sample must not advance to {stage}.");
                Assert.IsNotNull(refusal);
            }
        }

        // -----------------------------------------------------------------------------------------
        // §5.1 naming. The record and the bottle are the same name, by construction.
        // -----------------------------------------------------------------------------------------
        //
        // Five tests stood here before #73 — AWrongTag_IsAcceptedAsReadilyAsTheRightOne,
        // ACorrectlyLoggedSample_IsNotFlaggedAsMislogged, Typography_IsNotAFailureMode,
        // AnEmptyTag_IsNotARecord and AMislogSpottedBeforeWorkStarts_CanBeCorrected. Between them
        // they guarded that mis-logging was reachable (§5.1's stated failure mode), that it was
        // punished for inattention rather than for typing, and that a player who walked back and
        // read the label could act on what they found (hard rule 3).
        //
        // All five described a transcription step that no longer exists. There is no typed tag to
        // be wrong, no keyboard to be unfair about, and nothing to amend. Deleting them removes no
        // pillar: hard rule 3 still holds, vacuously, because the thing it was making fair is gone.
        // What replaces them is the one assertion that still means something — the record cannot
        // disagree with the bottle, so the two can never be diffed to anyone's advantage.

        [Test]
        public void ASampleIsFiledUnderTheTagPrintedOnItsLabel()
        {
            var sample = At(SampleStage.Unpacked, "WERK-1 QUENCH 1");

            Assert.AreEqual("WERK-1 QUENCH 1", sample.RecordTag,
                "The lab files a sample under the tag it arrived carrying (#73).");
            Assert.AreEqual(sample.EquipmentTag, sample.RecordTag,
                "The record and the bottle must be the same name. Nothing transcribes one into the " +
                "other any more, so they cannot drift apart.");
        }

        /// <summary>
        /// A sample with no label still has to be nameable, because refusals are built out of
        /// <see cref="SampleState.RecordTag"/> and §9 requires a sentence rather than a blank.
        /// </summary>
        [Test]
        public void AnUnlabelledSample_StillHasSomethingToCallIt()
        {
            var sample = new SampleState { Id = new SampleId(9), Location = SampleLocation.Held(0) };

            Assert.IsFalse(string.IsNullOrWhiteSpace(sample.RecordTag));
            StringAssert.Contains("9", sample.RecordTag);
        }

        // -----------------------------------------------------------------------------------------
        // The registry is the only door the rest of the game comes through.
        // -----------------------------------------------------------------------------------------
        //
        // Registry_BooksInThroughTheLifecycle_AndKnowsWhatIsStillWaiting was deleted with #73. It
        // guarded that the registry routed booking-in through the lifecycle rather than writing the
        // field itself, and that it did not quietly correct a player's typo. Both doors it tested —
        // LogSample and AwaitingLog — are gone, and the "one door" pillar they belonged to is still
        // covered by the verdict tests below, which are the registry's remaining lifecycle entries.

        [Test]
        public void Registry_RefusesASecondVerdict_AndDoesNotQueueASecondConsequence()
        {
            var sample = At(SampleStage.Measured);
            var registry = RegistryWith(sample);

            Assert.IsTrue(registry.FileVerdict(sample.Id, Verdict.Critical, null, 2, out _));
            Assert.AreEqual(1, registry.Pending.Count);

            Assert.IsFalse(registry.FileVerdict(sample.Id, Verdict.Normal, null, 2, out string refusal));
            Assert.AreEqual(Verdict.Critical, sample.FiledVerdict);
            Assert.AreEqual(1, registry.Pending.Count,
                "A refused verdict must not leave a consequence queued for a call that was rejected.");
            Assert.IsNotNull(refusal);
        }

        [Test]
        public void Registry_ResolvesEachVerdictExactlyOnce()
        {
            var sample = At(SampleStage.Measured);
            var registry = RegistryWith(sample);

            registry.FileVerdict(sample.Id, Verdict.Normal, null, 1);

            var reports = registry.ResolveDue(9, new EconomyTuning());
            Assert.AreEqual(1, reports.Count);
            Assert.AreEqual(SampleStage.Resolved, sample.Stage);

            Assert.IsFalse(SampleLifecycle.TryResolve(sample, out string refusal),
                "A consequence that could land twice would pay the player twice.");
            Assert.IsNotNull(refusal);
        }

        // -----------------------------------------------------------------------------------------
        // Hard rule 2. The lifecycle sits between the player and the record, which is precisely
        // where a convenience overload reaching for the real chemistry would look reasonable.
        // -----------------------------------------------------------------------------------------

        [Test]
        public void Lifecycle_HasNoPathToGroundTruth()
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Static | BindingFlags.Instance |
                                     BindingFlags.DeclaredOnly;

            foreach (var method in typeof(SampleLifecycle).GetMethods(All))
            {
                Assert.AreNotEqual(typeof(SampleGroundTruth), Unwrap(method.ReturnType),
                    $"{method.Name} returns ground truth.");

                foreach (var parameter in method.GetParameters())
                {
                    Assert.AreNotEqual(typeof(SampleGroundTruth), Unwrap(parameter.ParameterType),
                        $"{method.Name} takes ground truth as '{parameter.Name}'.");
                }
            }

            foreach (var field in typeof(SampleLifecycle).GetFields(All))
            {
                Assert.AreNotEqual(typeof(SampleGroundTruth), Unwrap(field.FieldType),
                    $"{field.Name} holds ground truth.");
            }
        }

        private static Type Unwrap(Type type)
        {
            if (type.IsByRef || type.IsArray) type = type.GetElementType();
            return type;
        }
    }
}
