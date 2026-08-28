using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Editor.Content;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.World;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards run persistence (#49). A 20-day contract with consequences landing up to 14 days after
    /// a verdict is a run people will quit in the middle of, and every test here exists because a
    /// specific way of losing that run is easy to ship.
    /// <para>
    /// The load-bearing one is <see cref="ContinuedRun_GeneratesTheSameNextDayAsAnUnbrokenOne"/>.
    /// Hard rule 1 says the chemistry never lies, and a save that resumed a <i>slightly</i> different
    /// contract would break it invisibly: nobody would ever see the divergence, and every balance
    /// argument about seeds reproducing a run would quietly stop being true.
    /// </para>
    /// </summary>
    public sealed class RunSnapshotTests
    {
        private ContentSet content;
        private ContentCatalog catalog;

        private string directory;
        private RunSaveStore originalStore;
        private bool originalAuthority;
        private GameObject host;

        [SetUp]
        public void SetUp()
        {
            content = ContentBuilder.BuildInMemory();
            catalog = ContentBuilder.BuildCatalogInMemory(content);

            originalAuthority = LabRuntime.SimulatesLocally;
            originalStore = RunSaveSlot.Store;

            // Never the player's real save. RunSaveSlot.Store is settable for exactly this.
            directory = Path.Combine(Path.GetTempPath(), "oiledup-run-save-tests",
                                     Guid.NewGuid().ToString("N"));
            RunSaveSlot.Store = new RunSaveStore(Path.Combine(directory, "run.save"));
        }

        [TearDown]
        public void TearDown()
        {
            // Statics, and every other test in the suite assumes single player with a fresh slot.
            LabRuntime.SimulatesLocally = originalAuthority;
            RunSaveSlot.ForgetContinueRequest();
            RunSaveSlot.Store = originalStore;
            LogAssert.ignoreFailingMessages = false;

            if (host != null) Object.DestroyImmediate(host);

            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch { /* a failed assertion must stay the useful failure */ }

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

        // -- Fixtures ----------------------------------------------------------------------------------

        /// <summary>
        /// A lab that has actually been played: samples out of the crate, runs on three instruments,
        /// a blank, a certified standard, a filed verdict, a slip nobody collected, and a bottle
        /// half full of solvent. A snapshot of a freshly constructed lab would pass every test below
        /// while proving nothing.
        /// <para>
        /// <b>The shipping contract, not a two-day stub.</b> A save stores its plan by id and rebuilds
        /// it from <see cref="ContractPlan.ById"/>, exactly as it stores content by id, so a test plan
        /// authored in the test would come back as the real one and every "continues identically"
        /// assertion below would be comparing two different contracts. That is the format working as
        /// intended — a save must not pin a copy of the balance tables — and the fixture has to live
        /// with it rather than route around it.
        /// </para>
        /// Day 1 of that contract has a <c>BorderlineCount</c> of 2, and a borderline sample is
        /// forced to have a fault, so the first two arrivals are reliably faulty.
        /// </summary>
        private LabState Played(int seed = 20260823)
        {
            // Small enough that three runs and a calibration all finish well inside the 300-second
            // day. Running the clock out would make TryStartReferenceRun refuse ("shift over") and
            // the fixture would silently stop covering the §5.3 tell.
            var lab = new LabState(catalog, ContractPlan.Default(), seed) { MachineTimeScale = 0.001f };

            foreach (var id in new[] { "cooling_curve", "karl_fischer", "viscometer", "centrifuge",
                                       "elemental" })
            {
                var def = catalog.Machine(id);
                if (def != null) lab.Install(def, id);
            }

            lab.BeginDay();

            var open = lab.OpenSamples();
            Assert.IsNotEmpty(open, "The plan must deliver samples or this fixture proves nothing.");

            var sample = open[0];
            Assert.IsTrue(SampleLifecycle.TryMove(sample, SampleLocation.OnSurface("bench", 0), out var move), move);
            Assert.IsTrue(SampleLifecycle.TryPrep(sample, out var prep), prep);
            sample.TemperatureC = 120f;   // past any instrument's preheat gate

            var runner = lab.Machines.FirstOrDefault(m => m.CanAccept(sample) == LoadRefusal.Accepted);
            Assert.IsNotNull(runner, "No instrument would take a prepped, warm, full vial.");

            Assert.AreEqual(LoadRefusal.Accepted, runner.TryLoad(sample));
            Assert.IsTrue(runner.TryBeginRun());
            lab.Tick(5f);

            Assert.IsNotNull(runner.LastResult, "The run should have produced numbers.");
            Assert.IsTrue(SampleLifecycle.TryFileResult(sample, runner.LastResult, out var file), file);
            runner.Unload();

            // A blank on a second instrument: this is the §5.2 tell and it has to survive a quit.
            var blanker = lab.Machines.First(m => m != runner);
            Assert.IsTrue(blanker.TryBeginBlank());
            lab.Tick(5f);

            // And a certified standard on a third, so LastCheck is rebuilt on load rather than lost.
            var checker = lab.Machines.First(m => m != runner && m != blanker);
            Assert.IsTrue(lab.TryStartReferenceRun(checker, out var refusal), refusal);
            lab.Tick(5f);
            Assert.IsNotNull(checker.LastCheck, "A reference run should have left a certificate reading.");

            // A slip printed and left in the tray. Paper is a paid-for test result; losing it to a
            // quit is losing money the player already spent.
            var second = lab.OpenSamples().First(s => s.Id != sample.Id);
            lab.Slips.Issue(second.Id, runner.InstanceId, runner.LastResult);

            // A verdict whose consequence is still days away — the thing #49 is actually about.
            Assert.IsTrue(lab.Samples.FileVerdict(sample.Id, Verdict.Critical, null, lab.Day, out var filed),
                          filed);

            Assert.IsTrue(lab.Solvent.TryTake("bottle-1", 3ul, out var take), take);
            Assert.IsTrue(lab.Solvent.TryFill("bottle-1", 3ul, out _, out var fill), fill);
            Assert.IsTrue(lab.Solvent.TryPutDown("bottle-1", 3ul, SolventStore.StationId, 0, out var put), put);

            lab.EndDay();
            return lab;
        }

        /// <summary>Two snapshots of the same run must encode identically — save time excepted.</summary>
        private static string Comparable(RunSnapshot snapshot) =>
            string.Join("\n", RunSnapshotCodec.Encode(snapshot)
                .Split('\n')
                .Where(line => !line.StartsWith("saved\t", StringComparison.Ordinal)));

        // -- The round trip ----------------------------------------------------------------------------

        /// <summary>
        /// Save a played run, load it, and assert the whole thing came back. The encoding comparison
        /// is the field-by-field assertion — it covers every element of every reading, including the
        /// ground truth a client never sees — and the named assertions above it are there so a
        /// failure says <i>what</i> was lost rather than "these two long strings differ".
        /// </summary>
        [Test]
        public void SavedRun_RestoresEveryFieldItWasSavedWith()
        {
            var original = Played();
            var saved = RunSnapshotCapture.Of(original);

            string payload = RunSnapshotCodec.Encode(saved);
            Assert.IsTrue(RunSnapshotCodec.TryDecode(payload, out var decoded, out var decodeRefusal),
                          decodeRefusal);
            Assert.IsTrue(RunSnapshotRestore.TryRebuild(decoded, catalog, out var restored, out var refusal),
                          refusal);

            Assert.AreEqual(original.Day, restored.Day, "The day is the run.");
            Assert.AreEqual(original.DayInProgress, restored.DayInProgress);
            Assert.AreEqual(original.Economy.Money, restored.Economy.Money, "The books must survive a quit.");
            Assert.AreEqual(original.Economy.SolventUnits, restored.Economy.SolventUnits);
            Assert.AreEqual(original.Economy.ReferenceStandards, restored.Economy.ReferenceStandards);
            Assert.AreEqual(original.Samples.Count, restored.Samples.Count, "A sample was dropped.");
            Assert.AreEqual(original.Samples.Pending.Count, restored.Samples.Pending.Count,
                            "A filed verdict waiting to resolve was lost — §5.4 is the whole point.");
            Assert.AreEqual(original.Machines.Count, restored.Machines.Count);
            Assert.AreEqual(original.Slips.Count, restored.Slips.Count,
                            "An unfiled slip is a test the player paid for.");

            var originalRunner = original.Machines.First(m => m.LastResult != null);
            var restoredRunner = restored.FindMachine(originalRunner.InstanceId);
            Assert.IsNotNull(restoredRunner);
            Assert.AreEqual(originalRunner.Runtime.DriftPercent, restoredRunner.Runtime.DriftPercent,
                            "Drift is hidden state the player pays ampoules to measure (§5.3).");
            Assert.AreEqual(originalRunner.Runtime.Residue.Count, restoredRunner.Runtime.Residue.Count,
                            "Carryover is what a blank reveals (§5.2). Losing it is losing the mechanic.");

            var withCheck = original.Machines.First(m => m.LastCheck != null);
            Assert.IsNotNull(restored.FindMachine(withCheck.InstanceId).LastCheck,
                             "The certificate reading is the §5.3 tell and it is bought with an ampoule.");

            Assert.AreEqual(Comparable(saved), Comparable(RunSnapshotCapture.Of(restored)),
                            "A loaded run must be the run that was saved, field for field.");
        }

        /// <summary>
        /// Hard rule 1, as a test. If the generator resumed anywhere but where it stopped, the
        /// continued contract would deliver different oil from the one the player was halfway
        /// through — and no screen anywhere would say so.
        /// </summary>
        [Test]
        public void ContinuedRun_GeneratesTheSameNextDayAsAnUnbrokenOne()
        {
            var unbroken = Played();
            var saved = RunSnapshotCapture.Of(unbroken);

            Assert.IsTrue(RunSnapshotRestore.TryRebuild(saved, catalog, out var continued, out var refusal),
                          refusal);

            Assert.IsTrue(unbroken.BeginDay());
            Assert.IsTrue(continued.BeginDay());

            Assert.AreEqual(Comparable(RunSnapshotCapture.Of(unbroken)),
                            Comparable(RunSnapshotCapture.Of(continued)),
                            "The morning after a load must be the morning the run would have had. " +
                            "The seed alone cannot do this — the RNG state has to travel too.");
        }

        /// <summary>
        /// Ground truth is in the save because the host holds it, and this is what it is for: a
        /// verdict filed before the quit has to resolve against the fault that was actually there.
        /// </summary>
        [Test]
        public void PendingVerdict_ResolvesAgainstTheSameChemistryAfterALoad()
        {
            var original = Played();
            Assert.IsNotEmpty(original.Samples.Pending,
                              "The fixture files a verdict whose consequence has not come due yet.");

            var pending = original.Samples.Pending[0];

            var truth = original.Samples.PeekTruthForDebugging(pending.Sample);
            Assert.IsNotNull(truth, "The fixture files a verdict, so there is truth behind it.");

            Assert.IsTrue(RunSnapshotRestore.TryRebuild(RunSnapshotCapture.Of(original), catalog,
                                                        out var restored, out var refusal), refusal);

            var restoredTruth = restored.Samples.PeekTruthForDebugging(pending.Sample);
            Assert.IsNotNull(restoredTruth, "A sample came back with no chemistry behind it.");
            Assert.AreEqual(truth.ActualFaults.Count, restoredTruth.ActualFaults.Count);
            Assert.AreEqual(truth.PrimaryFault, restoredTruth.PrimaryFault,
                            "The fault must be the same definition object, resolved by id from the " +
                            "live catalog rather than a copy pinned in the save.");

            foreach (var kv in truth.TrueValues)
            {
                Assert.AreEqual(kv.Value, restoredTruth.GetTrue(kv.Key),
                                $"True value for '{kv.Key}' moved across a save. Every reading taken " +
                                "after the load would be measured against different oil.");
            }
        }

        // -- Refusals ---------------------------------------------------------------------------------

        /// <summary>
        /// The failure the issue names: a save written before — or after — a fault archetype existed
        /// must never load and silently drop the sample it belonged to. Dropping it is worse than it
        /// sounds. The sample's true values still carry the fault's signature, so it would read
        /// Critical on every instrument and then resolve as "no fault found" when the verdict landed.
        /// </summary>
        [Test]
        public void FaultThisBuildNoLongerHas_RefusesTheLoadAndNamesIt()
        {
            var lab = Played();
            string payload = RunSnapshotCodec.Encode(RunSnapshotCapture.Of(lab));

            Assert.IsTrue(payload.Contains("truth.fault\t"),
                          "The fixture must produce at least one faulty sample or this proves nothing.");

            payload = ReplaceField(payload, "truth.fault", 1, "an_archetype_that_was_deleted");

            Assert.IsTrue(RunSnapshotCodec.TryDecode(payload, out var snapshot, out var decodeRefusal),
                          decodeRefusal);
            Assert.IsFalse(RunSnapshotRestore.TryRebuild(snapshot, catalog, out var restored,
                                                         out string refusal));

            Assert.IsNull(restored, "A refused load must leave nothing half-built.");
            StringAssert.Contains("an_archetype_that_was_deleted", refusal,
                                  "The refusal has to name the content that is missing.");
        }

        [Test]
        public void EquipmentProfileThisBuildNoLongerHas_RefusesTheLoad()
        {
            var lab = Played();
            string payload = ReplaceField(RunSnapshotCodec.Encode(RunSnapshotCapture.Of(lab)),
                                          "sample", 3, "a_profile_that_was_deleted");

            Assert.IsTrue(RunSnapshotCodec.TryDecode(payload, out var snapshot, out _));
            Assert.IsFalse(RunSnapshotRestore.TryRebuild(snapshot, catalog, out _, out string refusal));
            StringAssert.Contains("a_profile_that_was_deleted", refusal);
        }

        [Test]
        public void InstrumentThisBuildNoLongerHas_RefusesTheLoad()
        {
            var lab = Played();
            string payload = ReplaceField(RunSnapshotCodec.Encode(RunSnapshotCapture.Of(lab)),
                                          "machine", 2, "a_machine_that_was_deleted");

            Assert.IsTrue(RunSnapshotCodec.TryDecode(payload, out var snapshot, out _));
            Assert.IsFalse(RunSnapshotRestore.TryRebuild(snapshot, catalog, out _, out string refusal));
            StringAssert.Contains("a_machine_that_was_deleted", refusal);
        }

        [Test]
        public void ContractThisBuildNoLongerOffers_RefusesTheLoad()
        {
            var lab = Played();
            string payload = ReplaceField(RunSnapshotCodec.Encode(RunSnapshotCapture.Of(lab)),
                                          "contract", 1, "a_contract_that_was_retired");

            Assert.IsTrue(RunSnapshotCodec.TryDecode(payload, out var snapshot, out _));
            Assert.IsFalse(RunSnapshotRestore.TryRebuild(snapshot, catalog, out _, out string refusal));
            StringAssert.Contains("a_contract_that_was_retired", refusal);
        }

        /// <summary>
        /// A schema this build does not know is refused with both numbers in the message, because
        /// there is no migration path yet and guessing at fields an older writer never wrote is how a
        /// save loads with something quietly missing from it.
        /// </summary>
        [Test]
        public void SchemaThisBuildCannotRead_RefusesWithBothVersions()
        {
            var lab = Played();
            string payload = ReplaceField(RunSnapshotCodec.Encode(RunSnapshotCapture.Of(lab)),
                                          "schema", 1, "99");

            Assert.IsFalse(RunSnapshotCodec.TryDecode(payload, out var snapshot, out string refusal));
            Assert.IsNull(snapshot);
            StringAssert.Contains("99", refusal);
            StringAssert.Contains(RunSnapshot.SchemaVersion.ToString(), refusal);

            // And the menu can still describe it, so CONTINUE explains itself rather than vanishing.
            Assert.IsTrue(RunSnapshotCodec.TryReadHeadline(payload, out var headline));
            Assert.IsFalse(headline.IsLoadable);
            Assert.AreEqual(lab.Day, headline.Day);
        }

        // -- The codec ---------------------------------------------------------------------------------

        /// <summary>
        /// Floats must round-trip bit for bit. A restored true value that differs in the seventh
        /// digit is a sample that measures fractionally differently from the one that was saved —
        /// invisible, unfalsifiable, and hard rule 1 broken. This is why the codec is hand-rolled
        /// rather than <c>JsonUtility</c>, which rounds.
        /// </summary>
        [Test]
        public void EveryFloat_RoundTripsBitForBit()
        {
            float[] awkward =
            {
                1f / 3f, 0.1f, 1e-8f, 1.7e20f, -0.000123456789f, 12345.6789f,
                float.MaxValue, float.MinValue, 0f, -0f
            };

            var snapshot = new RunSnapshot { Day = 4, ContractId = ContractPlan.DefaultId };
            var sample = new RunSnapshot.SampleRecord { Id = 1, ProfileId = "p" };
            var result = new RunSnapshot.ResultRecord { MachineId = "m" };

            for (int i = 0; i < awkward.Length; i++)
                result.Values.Add(new RunSnapshot.Reading { ElementId = $"e{i}", Value = awkward[i] });

            sample.Results.Add(result);
            snapshot.Samples.Add(sample);
            snapshot.Money = 1f / 7f;

            Assert.IsTrue(RunSnapshotCodec.TryDecode(RunSnapshotCodec.Encode(snapshot),
                                                     out var decoded, out string refusal), refusal);

            Assert.AreEqual(BitConverter.SingleToInt32Bits(snapshot.Money),
                            BitConverter.SingleToInt32Bits(decoded.Money));

            var readings = decoded.Samples[0].Results[0].Values;
            for (int i = 0; i < awkward.Length; i++)
            {
                Assert.AreEqual(BitConverter.SingleToInt32Bits(awkward[i]),
                                BitConverter.SingleToInt32Bits(readings[i].Value),
                                $"{awkward[i]} did not survive the round trip exactly.");
            }
        }

        /// <summary>
        /// Field tech notes are free text from a human and equipment tags come off a paper label.
        /// Both can hold a tab or a newline, which are the two characters the format is made of.
        /// </summary>
        [Test]
        public void TextFields_SurviveSeparatorsAndNulls()
        {
            const string awkward = "RIG-7\tCOMPRESSOR\nB \\ \"quoted\"";

            var snapshot = new RunSnapshot { Day = 1, ContractId = ContractPlan.DefaultId };
            snapshot.Samples.Add(new RunSnapshot.SampleRecord
            {
                Id = 1,
                EquipmentTag = awkward,
                FieldTechNote = null,
                ProfileId = "p"
            });

            Assert.IsTrue(RunSnapshotCodec.TryDecode(RunSnapshotCodec.Encode(snapshot),
                                                     out var decoded, out string refusal), refusal);

            Assert.AreEqual(awkward, decoded.Samples[0].EquipmentTag);
            Assert.IsNull(decoded.Samples[0].FieldTechNote,
                          "A null note must not come back as an empty string — the terminal reads " +
                          "them differently.");
        }

        // -- Authority ---------------------------------------------------------------------------------

        /// <summary>
        /// The save layer must be as unreachable from a client as the simulation it describes.
        /// <c>RunSnapshot</c> carries ground truth, so a type in <c>Residue.Net</c> naming one would
        /// be hard rule 2 broken through the save rather than through an RPC. The menu is allowed
        /// <see cref="RunSaveHeadline"/>, which is a day number and a bank balance.
        /// </summary>
        [Test]
        public void NetworkLayer_NeverMentionsARunSnapshot()
        {
            var netAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Residue.Net");

            if (netAssembly == null)
            {
                Assert.Pass("Residue.Net has no code yet.");
                return;
            }

            var forbidden = typeof(RunSnapshot);
            var offenders = new List<string>();

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var type in netAssembly.GetTypes())
            {
                foreach (var f in type.GetFields(all))
                {
                    if (Mentions(f.FieldType, forbidden)) offenders.Add($"field {type.Name}.{f.Name}");
                }

                foreach (var m in type.GetMethods(all))
                {
                    if (Mentions(m.ReturnType, forbidden)) offenders.Add($"return of {type.Name}.{m.Name}");
                    foreach (var p in m.GetParameters())
                    {
                        if (Mentions(p.ParameterType, forbidden))
                            offenders.Add($"parameter '{p.Name}' of {type.Name}.{m.Name}");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "Residue.Net must never touch a RunSnapshot — it carries ground truth. Offenders:\n  " +
                string.Join("\n  ", offenders));
        }

        private static bool Mentions(Type candidate, Type forbidden)
        {
            if (candidate == null) return false;
            if (candidate == forbidden) return true;
            if (candidate.DeclaringType == forbidden) return true;
            if (candidate.IsArray) return Mentions(candidate.GetElementType(), forbidden);
            if (candidate.IsGenericType) return candidate.GetGenericArguments().Any(a => Mentions(a, forbidden));
            return false;
        }

        /// <summary>
        /// Ground truth may not be nameable outside the assembly that computes it, and the save gave
        /// it a second home. <c>RunSnapshot.Truths</c> and its record type are internal for that
        /// reason; this asserts nobody quietly widened them.
        /// </summary>
        [Test]
        public void GroundTruthInASnapshot_IsNotPubliclyReachable()
        {
            var offenders = typeof(RunSnapshot)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.DeclaredOnly)
                .Where(m => m.Name.IndexOf("truth", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(m => m.Name)
                .ToList();

            Assert.IsEmpty(offenders,
                "A save's ground truth became public API. Residue.Net references Residue.Gameplay, " +
                "so this is the difference between 'a client cannot read it' and 'a client does not " +
                "happen to'. Offenders: " + string.Join(", ", offenders));
        }

        // -- The hook ----------------------------------------------------------------------------------

        private LabRuntime Spawn()
        {
            host = new GameObject("LabRuntime_UnderTest");
            host.SetActive(false);

            var runtime = host.AddComponent<LabRuntime>();

            var so = new UnityEditor.SerializedObject(runtime);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("seed").intValue = 20260823;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Unity runs no lifecycle methods in edit mode; this is the step Awake would have run.
            runtime.BuildLabIfAuthoritative();
            return runtime;
        }

        /// <summary>
        /// The save happens at the day boundary, from the day cycle itself — not from a screen, and
        /// not on a timer. Between <c>EndDay</c> and the next <c>BeginDay</c> is the only moment
        /// nothing in the lab is moving.
        /// </summary>
        [Test]
        public void EndOfDay_WritesTheRunToDisk()
        {
            LabRuntime.SimulatesLocally = true;
            var runtime = Spawn();

            Assert.IsFalse(RunSaveSlot.Store.Exists, "Nothing should be written before a day closes.");

            runtime.Lab.BeginDay();
            Assert.IsFalse(RunSaveSlot.Store.Exists,
                           "Starting a day is not a save point — the room is in motion for all of it.");

            runtime.Lab.EndDay();

            Assert.IsTrue(RunSaveSlot.Store.Exists, "The day closed and nothing was written.");
            Assert.IsTrue(RunSaveSlot.TryReadHeadline(out var headline));
            Assert.AreEqual(1, headline.Day);
        }

        /// <summary>
        /// A client holds no simulation, so it has nothing to save — and a save on a client would be
        /// a second set of books for state the host owns. This is structural rather than a guard
        /// clause: the hook is installed past the return a client takes.
        /// </summary>
        [Test]
        public void AClient_NeitherLoadsNorWritesASave()
        {
            LabRuntime.SimulatesLocally = true;
            var seeded = Spawn();
            seeded.Lab.BeginDay();
            seeded.Lab.EndDay();
            Object.DestroyImmediate(host);
            host = null;

            Assert.IsTrue(RunSaveSlot.Store.Exists, "Fixture: there has to be a save to be tempted by.");

            LabRuntime.SimulatesLocally = false;
            RunSaveSlot.RequestContinue();

            var client = Spawn();

            Assert.IsNull(client.Lab,
                "A client built a lab. Everything about the save layer downstream of this is moot.");
            Assert.IsFalse(client.Continued);
            Assert.IsTrue(RunSaveSlot.TakeContinueRequest(),
                "The latch was consumed, which means the client reached the host-only branch that " +
                "reads it — and therefore reached the save layer at all.");
        }

        /// <summary>
        /// A finished contract clears the slot. CONTINUE on a run with no day left to start is a
        /// button that loads a lab and then refuses to open it.
        /// </summary>
        [Test]
        public void RunEndingInBankruptcy_ClearsTheSlot()
        {
            LabRuntime.SimulatesLocally = true;
            var runtime = Spawn();

            runtime.Lab.BeginDay();
            runtime.Lab.EndDay();
            Assert.IsTrue(RunSaveSlot.Store.Exists);

            runtime.Lab.BeginDay();
            runtime.Lab.Economy.Charge(runtime.Lab.Tuning.StartingMoney + 10_000f);
            runtime.Lab.EndDay();

            Assert.IsTrue(runtime.Lab.IsRunOver);
            Assert.IsFalse(RunSaveSlot.Store.Exists,
                           "A run that is over must not be offered back to the player.");
        }

        /// <summary>
        /// A save this build cannot read must survive the shift the player ends up playing instead.
        /// Overwriting it would destroy the only copy of a run a build with the missing content could
        /// still open.
        /// </summary>
        [Test]
        public void RefusedContinue_LeavesTheSaveAlone()
        {
            LabRuntime.SimulatesLocally = true;

            string unreadable = RunSnapshotCodec.Encode(RunSnapshotCapture.Of(Played()));
            unreadable = ReplaceField(unreadable, "schema", 1, "99");
            Assert.IsTrue(RunSaveSlot.Store.TrySave(unreadable, out string saveRefusal), saveRefusal);

            RunSaveSlot.RequestContinue();

            // The refusal is a Debug.LogError on purpose (§9: never fail quietly), and an error is
            // a test failure unless it is expected.
            LogAssert.ignoreFailingMessages = true;
            var runtime = Spawn();

            Assert.IsNotNull(runtime.Lab, "The player is standing in the lab; they need a run.");
            Assert.IsFalse(runtime.Continued);

            runtime.Lab.BeginDay();
            runtime.Lab.EndDay();

            Assert.IsTrue(RunSaveSlot.Store.TryLoad(out string still, out _, out _));
            Assert.AreEqual(unreadable, still,
                "A shift played after a refused CONTINUE overwrote the save it could not read.");
        }

        // -- Helpers -----------------------------------------------------------------------------------

        /// <summary>
        /// Rewrite one tab-separated field of every line with this tag, so a test can describe
        /// "a save from a build that had a fault this one does not" without hand-writing a payload.
        /// </summary>
        private static string ReplaceField(string payload, string tag, int field, string value)
        {
            var lines = payload.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\t');
                if (parts.Length <= field || parts[0] != tag) continue;

                parts[field] = value;
                lines[i] = string.Join("\t", parts);
            }
            return string.Join("\n", lines);
        }
    }
}
