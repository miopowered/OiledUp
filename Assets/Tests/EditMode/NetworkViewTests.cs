using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Editor.Content;
using Residue.Gameplay.Simulation;
using Residue.Net.Views;
using Unity.Collections;
using Unity.Netcode;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards the client-safe view layer (§3.1, §3.2) and, through it, hard rule 2.
    /// <para>
    /// The interesting failure here is not a crash. A view that leaks ground truth compiles, round
    /// trips, and renders a perfectly sensible terminal screen — it just quietly hands every client
    /// the answer the whole game is about working out. So most of these tests are not "does the
    /// projection work" but "is there any observable difference between two samples that differ only
    /// in what is actually wrong with them".
    /// </para>
    /// </summary>
    public sealed class NetworkViewTests
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

        // -----------------------------------------------------------------------------------------
        // 1. The boundary. Nothing a client receives may depend on what is actually wrong with the oil.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: two vials that look identical on the bench look identical on every screen in the
        /// lab, however different their chemistry is.
        /// <para>
        /// This is the test that catches a leak dressed as a feature. A view carrying
        /// <c>PrimaryFault</c>, or a <see cref="FaultSeverity"/>, or a "confidence" float quietly
        /// derived from the truth map, would compile and round trip and pass any naive projection
        /// test — the numbers would simply be right, which is the problem. So this compares a
        /// serviceable sample against one carrying a fully developed fault, staged identically and
        /// logged under the same tag, and demands the projections be indistinguishable field for
        /// field. Anything that separates them separates them for a client too.
        /// </para>
        /// Driven by reflection rather than an explicit field list so a field added later is covered
        /// the day it lands, not the day someone remembers to extend this.
        /// </summary>
        [Test]
        public void SampleView_CannotTellServiceableOilFromOilAboutToFail()
        {
            var rng = new Rng(20260823);
            var generator = new SampleGenerator(content.AllFaults);
            const string tag = "WERK-1 QUENCH 1";
            int compared = 0;

            foreach (var profile in content.Profiles.Values)
            {
                foreach (var fault in content.Faults.Values)
                {
                    if (!fault.IsValidOn(profile)) continue;

                    var healthyRequest = GenerationRequest.Default(profile, tag, 1);
                    healthyRequest.ForceHealthy = true;
                    var healthy = Ready(generator.Generate(healthyRequest, ref rng), tag);

                    var doomedRequest = GenerationRequest.Default(profile, tag, 1);
                    doomedRequest.ForcedFault = fault;
                    doomedRequest.ForcedSeverity01 = 1f;
                    doomedRequest.CascadeChance = 0f;
                    doomedRequest.HealthyChance = 0f;
                    var doomed = Ready(generator.Generate(doomedRequest, ref rng), tag);

                    // Guard the guard: if these two were not genuinely different underneath, the
                    // comparison below would pass for the wrong reason.
                    Assert.IsTrue(healthy.Truth.IsHealthy, "Forced-healthy sample came back with a fault.");
                    Assert.IsFalse(doomed.Truth.IsHealthy, $"'{fault.Id}' on '{profile.Id}' produced no fault.");
                    Assert.IsTrue(
                        doomed.Truth.TrueValues.Any(kv => profile.Evaluate(kv.Key, kv.Value) != ReadingSeverity.Normal),
                        $"'{fault.Id}' at full severity on '{profile.Id}' is not abnormal anywhere, so this " +
                        "comparison proves nothing. Strengthen the signature.");

                    AssertViewsAgree(
                        SampleView.From(healthy.State),
                        SampleView.From(doomed.State),
                        $"differs between a serviceable sample and one carrying '{fault.Id}' at full " +
                        "severity. That difference reaches every client and hands them the diagnosis.");

                    compared++;
                }
            }

            Assert.Greater(compared, 0, "No fault matched any profile; this test compared nothing.");
        }

        /// <summary>
        /// Promise: the terminal files a vial under the tank tag the player typed, and never tells
        /// them they typed the wrong one.
        /// <para>
        /// §5.1 makes mis-logging a real failure mode, and its only tell is physical — walk back to
        /// the bottle and read the label. <see cref="SampleState.IsMislogged"/> exists so systems can
        /// act on the mismatch, but it is computed from a label no client should hold. A view that
        /// carried either half of that comparison would turn "check your work" into a red icon.
        /// </para>
        /// </summary>
        [Test]
        public void SampleView_FromAMisLoggedVial_ShowsTheTypedTagAndHidesTheMismatch()
        {
            var rng = new Rng(4242);
            var profile = content.Profiles["quench_oil_cold"];
            var generator = new SampleGenerator(content.AllFaults);

            const string label = "WERK-1 QUENCH 1";
            const string typed = "WERK-2 QUENCH 1";

            var mislogged = Ready(generator.Generate(GenerationRequest.Default(profile, label, 1), ref rng), typed);
            var correct = Ready(generator.Generate(GenerationRequest.Default(profile, typed, 1), ref rng), typed);

            Assert.IsTrue(mislogged.State.IsMislogged, "Setup failed: this vial is not actually mis-logged.");
            Assert.IsFalse(correct.State.IsMislogged, "Setup failed: the control vial is mis-logged.");

            var view = SampleView.From(mislogged.State);

            Assert.AreEqual(typed, view.RecordTag.ToString(),
                "A mis-logged vial must travel under the tank the player named, not the one on the label.");

            foreach (var (name, value) in ReadableValues(view))
            {
                StringAssert.DoesNotContain(label, value?.ToString() ?? string.Empty,
                    $"SampleView.{name} carries the paper label. The label is read off the bottle, not a screen.");
            }

            AssertViewsAgree(view, SampleView.From(correct.State),
                "differs between a mis-logged vial and a correctly logged one. A client that can spot " +
                "the disagreement gets §5.1's failure mode corrected for free.");
        }

        /// <summary>
        /// Promise: no view ever names, or is typed as, something only the host may know.
        /// <para>
        /// The behavioural tests above catch a leak that changes a value. This catches the other
        /// shape: a member that is correct-looking, correctly typed and simply must not exist — a
        /// <see cref="FaultSeverity"/> beside the <see cref="ReadingSeverity"/>, an <c>IsHealthy</c>
        /// flag, an <c>EquipmentTag</c> "for the tooltip". The forbidden name list is read off
        /// <see cref="SampleGroundTruth"/> itself, so adding a field there extends this test with it.
        /// </para>
        /// </summary>
        /// <summary>
        /// Promise: a client can walk back to the bottle and read the label, and cannot be told the
        /// answer by a screen.
        /// <para>
        /// The two halves of this are in tension, which is why they are pinned together. The label
        /// <b>must</b> reach a client, or §5.1's mis-log is a punishment with no available check —
        /// hard rule 3. It must <b>not</b> reach a screen, or the screen diffs it against the typed
        /// tag and corrects the player for free. The whole reason <see cref="VialView"/> is a separate
        /// list from <see cref="SampleView"/> is to hold both of those true at once, and merging them
        /// "to save a list" is exactly the tidy-up this test exists to fail.
        /// </para>
        /// </summary>
        [Test]
        public void TheLabelReachesTheBottleAndNoScreen()
        {
            var rng = new Rng(20260823);
            var profile = content.Profiles["quench_oil_cold"];
            var generator = new SampleGenerator(content.AllFaults);

            const string label = "WERK-1 QUENCH 1";
            const string typed = "WERK-9 BATH Z";

            var sample = Ready(generator.Generate(GenerationRequest.Default(profile, label, 1), ref rng),
                typed).State;

            Assert.IsTrue(sample.IsMislogged, "Test needs a genuine mismatch to be about anything.");

            var vial = VialView.From(sample);
            Assert.AreEqual(sample.EquipmentTag, vial.Label.ToString(),
                "A client cannot read the label off the bottle, so a mis-log has no tell (§5.1).");

            var view = SampleView.From(sample);
            foreach (string member in MemberNames(typeof(SampleView)))
            {
                object value = typeof(SampleView).GetField(member)?.GetValue(view)
                               ?? typeof(SampleView).GetProperty(member)?.GetValue(view);

                Assert.AreNotEqual(sample.EquipmentTag, value?.ToString(),
                    $"SampleView.{member} carries the paper label. Screens read this, and one that " +
                    "can compare it to RecordTag hands the player their own mistake.");
            }
        }

        [Test]
        public void Views_NameNothingThatOnlyTheHostMayKnow()
        {
            // Ground truth's own vocabulary, minus the one word it shares innocently with everything:
            // a sample id is a handle, not an answer, and every view is keyed on one.
            var forbidden = new HashSet<string>(
                MemberNames(typeof(SampleGroundTruth)).Where(n => n != nameof(SampleGroundTruth.Id)),
                StringComparer.OrdinalIgnoreCase)
            {
                // Not ground truth, but hidden state hard rule 3 protects the same way: the player is
                // meant to discover these through a blank and a certified standard (§5.2, §5.3), and a
                // client handed the raw figure never runs either.
                nameof(MachineRuntimeState.DriftPercent),
                nameof(MachineRuntimeState.DriftSign),
                nameof(MachineRuntimeState.DriftStartedAtRunIndex),

                // The comparison a mis-log is caught by. No view may make it for the player (§5.1).
                nameof(SampleState.IsMislogged),

                // Catch-alls. Substring matching means "PrimaryFaultId" and "FaultName" fail too.
                "Fault",
                "Truth",
                "Actual"
            };

            // The paper label is the one piece of hidden state that has to reach a client, because
            // reading it off the bottle is the only tell a mis-log has (§5.1). So it is banned from
            // every view except the one describing the bottle itself — see VialView, which is kept
            // out of SampleView precisely so no screen can hold both halves of the comparison.
            var labelWords = new[] { nameof(SampleState.EquipmentTag), "Label" };

            var offenders = new List<string>();

            foreach (var view in ViewTypes())
            {
                bool describesTheBottle = view == typeof(VialView);

                foreach (string member in MemberNames(view))
                {
                    foreach (string word in forbidden)
                    {
                        if (member.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                            offenders.Add($"{view.Name}.{member} (matches '{word}')");
                    }

                    if (describesTheBottle) continue;

                    foreach (string word in labelWords)
                    {
                        if (member.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                            offenders.Add($"{view.Name}.{member} (carries the paper label; only VialView may)");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "These view members are named after state a client must never hold:\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Promise: the same thing, structurally, for anything a name would not catch.
        /// <para>
        /// Every leak that matters is a reference type — a <c>FaultDef</c>, a <c>SampleState</c>, the
        /// truth or contamination dictionaries. Requiring every replicated field to be unmanaged bars
        /// all of them at once and needs no list to stay current. <see cref="FaultSeverity"/> is the
        /// exception that proves it: it is an enum, so it slips through this and is caught by name
        /// above instead. Both tests are needed; neither is sufficient.
        /// </para>
        /// It also buys the thing §3.2 needs. 200+ vials rules out a NetworkObject each, so the sample
        /// roster has to be a <c>NetworkList</c>, and a <c>NetworkList</c> only takes unmanaged
        /// elements.
        /// </summary>
        [Test]
        public void Views_ReplicateOnlyUnmanagedFields()
        {
            var offenders = new List<string>();

            foreach (var view in ViewTypes())
            {
                Assert.IsTrue(view.IsValueType, $"{view.Name} must be a struct to live in a NetworkList.");

                foreach (var field in view.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!IsUnmanaged(field.FieldType))
                        offenders.Add($"{view.Name}.{field.Name} is {field.FieldType.Name}");
                }
            }

            Assert.IsEmpty(offenders,
                "A replicated field holding a managed reference is a live server object on a client. " +
                "Project it into a value instead. Offenders:\n  " + string.Join("\n  ", offenders));
        }

        // -----------------------------------------------------------------------------------------
        // 2. The projection has to actually carry the tells, or hard rule 3 holds only for the host.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a player who is not the host can still see that an instrument is dirty.
        /// <para>
        /// Contamination is only fair because a blank reveals it (hard rule 3, §5.2). In co-op the
        /// person standing at the machine is usually not the person running the session, so the tell
        /// has to survive the projection or the rule silently applies to one player out of four.
        /// </para>
        /// </summary>
        [Test]
        public void MachineView_CarriesTheBlankTell()
        {
            var def = content.Machines["karl_fischer"];

            var never = new MachineInstance("karl_fischer-0", def);
            var neverView = MachineView.From(never);
            Assert.IsFalse(neverView.HasBlank, "An instrument with no blank on file must say so.");
            Assert.IsFalse(neverView.LastBlankFoundResidue,
                "Nothing has been measured, so nothing has been found. HasBlank is what separates " +
                "'unknown' from 'clean', and the terminal must read the two together.");

            var flushed = new MachineInstance("karl_fischer-1", def)
            {
                LastBlank = new TestResult { IsBlank = true, Values = { ["water_ppm"] = 0f } },
                LastBlankDay = 3
            };
            var flushedView = MachineView.From(flushed);
            Assert.IsTrue(flushedView.HasBlank);
            Assert.AreEqual(3, flushedView.LastBlankDay);
            Assert.IsFalse(flushedView.LastBlankFoundResidue, "A blank that came back empty is a clean instrument.");

            var dirty = new MachineInstance("karl_fischer-2", def)
            {
                LastBlank = new TestResult { IsBlank = true, Values = { ["water_ppm"] = 120f } },
                LastBlankDay = 3
            };
            Assert.IsTrue(MachineView.From(dirty).LastBlankFoundResidue,
                "A blank reading 120 ppm of carryover is the §5.2 tell. A client must see it.");
        }

        /// <summary>
        /// Promise: a player who is not the host can still see that an instrument is out of tolerance.
        /// <para>
        /// Same argument as the blank, for §5.3. The error fraction is safe to send precisely because
        /// it is the certificate against the readout and nothing more — it cannot separate drift from
        /// residue, which is what keeps the flush decision alive.
        /// </para>
        /// </summary>
        [Test]
        public void MachineView_CarriesTheCalibrationTell()
        {
            var def = content.Machines["karl_fischer"];
            var standard = ReferenceStandard.FromProfiles(content.Profiles.Values.ToList());

            var machine = new MachineInstance("karl_fischer-0", def);
            Assert.IsFalse(MachineView.From(machine).HasCalibrationCheck,
                "No standard has been run, so there is nothing to calibrate against.");

            var readout = new TestResult { MachineId = def.Id, IsReference = true };
            foreach (var element in def.Measures)
            {
                if (element == null || def.IsBlindTo(element.Id)) continue;
                if (!standard.TryGet(element.Id, out float certified)) continue;
                readout.Values[element.Id] = certified * 1.2f;   // reading 20% high
            }

            machine.LastCheck = CalibrationCheck.From(standard, readout, def, 2);
            Assert.IsNotNull(machine.LastCheck, "Setup failed: no certified lines matched this instrument.");

            var view = MachineView.From(machine);
            Assert.IsTrue(view.HasCalibrationCheck);
            Assert.AreEqual(machine.LastCheck.ErrorFraction, view.CalibrationErrorFraction, 1e-5f);
            Assert.IsTrue(view.CalibrationOutOfTolerance,
                "An instrument reading 20% high is far outside the 5% tolerance and every client must know.");
        }

        /// <summary>
        /// Promise: the shift ending is the host's call, and everyone finds out at the same moment.
        /// <para>
        /// §3.1 puts the clock on the host. <see cref="LabState.ShiftOver"/> and
        /// <see cref="LabState.IsRunOver"/> are also fed by the contract length and the bank balance,
        /// so a client inferring them from the seconds alone would be right until the day it mattered.
        /// </para>
        /// </summary>
        [Test]
        public void DayView_CarriesTheShiftClockAndItsDerivedFlags()
        {
            var lab = new LabState(catalog, OneDayPlan(), 99);

            var before = DayView.From(lab);
            Assert.AreEqual(0, before.Day);
            Assert.IsFalse(before.DayInProgress);

            lab.BeginDay();
            var open = DayView.From(lab);
            Assert.AreEqual(1, open.Day);
            Assert.IsTrue(open.DayInProgress);
            Assert.IsFalse(open.ShiftOver);
            Assert.IsFalse(open.IsRunOver);

            lab.Tick(10_000f);
            var expired = DayView.From(lab);
            Assert.IsTrue(expired.DayInProgress, "The day is over but the shift has not been closed out yet.");
            Assert.IsTrue(expired.ShiftOver, "The clock ran out; no client may still be offering new runs.");

            lab.EndDay();
            var closed = DayView.From(lab);
            Assert.IsFalse(closed.DayInProgress);
            Assert.IsTrue(closed.IsRunOver, "That was the only contracted day, so the run is over.");
        }

        /// <summary>
        /// Promise: a client can see the lab is out of the consumables the tells depend on.
        /// <para>
        /// Solvent and certified ampoules are what make §5.2 and §5.3 checkable, and both are
        /// purchasable so running dry is a decision rather than a wall (hard rule 3). A client who
        /// cannot see the count cannot make that decision.
        /// </para>
        /// </summary>
        [Test]
        public void EconomyView_CarriesTheConsumablesTheTellsDependOn()
        {
            var economy = new Economy(new EconomyTuning(), startingSolvent: 5f, startingStandards: 2);
            economy.TryConsumeSolvent();
            economy.TryConsumeReferenceStandard();

            var view = EconomyView.From(economy);

            Assert.AreEqual(economy.Money, view.Money, 1e-3f);
            Assert.AreEqual(economy.Reputation, view.Reputation, 1e-3f);
            Assert.AreEqual(4f, view.SolventUnits, 1e-3f);
            Assert.AreEqual(1, view.ReferenceStandards);
        }

        // -----------------------------------------------------------------------------------------
        // 3. And it has to survive the wire, or none of the above is worth anything.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: what the host projected is what the client reads.
        /// <para>
        /// Every field is written and read back in order. A field added to a struct but forgotten in
        /// <c>NetworkSerialize</c> is invisible to the compiler and shows up in play as one stale
        /// value on someone else's screen.
        /// </para>
        /// </summary>
        [Test]
        public void EveryView_SurvivesARoundTripThroughTheWire()
        {
            var sample = new SampleView
            {
                Id = 17,
                RecordTag = "HALLE-3 SEALED QUENCH 1",
                ProfileId = "corrosion_protection_oil",
                VolumeMl = 62.5f,
                Stage = SampleStage.Archived,
                IsLogged = true,
                HasVerdict = true,
                FiledVerdict = Verdict.Critical,
                WorstReading = ReadingSeverity.Caution,
                HasSuspectResult = true
            };
            Assert.AreEqual(sample, RoundTrip(sample));

            var machine = new MachineView
            {
                InstanceId = "cooling_curve-0",
                IsRunning = true,
                SecondsRemaining = 412.5f,
                RunsSinceFlush = 6,
                LastBlankDay = 4,
                LastBlankFoundResidue = true,
                HasCalibrationCheck = true,
                CalibrationErrorFraction = -0.183f,
                CalibrationOutOfTolerance = true
            };
            Assert.AreEqual(machine, RoundTrip(machine));

            var economy = new EconomyView
            {
                Money = 12_345.5f,
                Reputation = 71.25f,
                SolventUnits = 8f,
                ReferenceStandards = 3
            };
            Assert.AreEqual(economy, RoundTrip(economy));

            var day = new DayView
            {
                Day = 12,
                SecondsRemaining = 88.5f,
                DayInProgress = true,
                ShiftOver = false,
                IsRunOver = false
            };
            Assert.AreEqual(day, RoundTrip(day));
        }

        /// <summary>
        /// Promise: a tag longer than the wire budget clips instead of dropping the host mid-shift.
        /// The player types this field, so its length is not something the content tables control.
        /// </summary>
        [Test]
        public void SampleView_TruncatesAnAbsurdlyLongTagRatherThanThrowing()
        {
            var rng = new Rng(7);
            var profile = content.Profiles["quench_oil_cold"];
            string huge = new('X', 400);

            var generated = Ready(
                new SampleGenerator(content.AllFaults).Generate(
                    GenerationRequest.Default(profile, "WERK-1 QUENCH 1", 1), ref rng),
                huge);

            var view = SampleView.From(generated.State);

            Assert.IsTrue(view.IsLogged);
            Assert.Greater(view.RecordTag.Length, 0, "A clipped tag must still be legible, not empty.");
            StringAssert.StartsWith("XXX", view.RecordTag.ToString());
            Assert.AreEqual(view, RoundTrip(view));
        }

        // -- helpers ------------------------------------------------------------------------------

        /// <summary>
        /// Every public type in <c>Residue.Net</c> that goes on the wire. Discovered rather than
        /// listed, so a fifth view is covered by these tests from the moment it exists.
        /// </summary>
        private static IReadOnlyList<Type> ViewTypes()
        {
            var types = typeof(SampleView).Assembly.GetTypes()
                .Where(t => t.IsPublic && !t.IsInterface && !t.IsAbstract &&
                            typeof(INetworkSerializable).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToList();

            Assert.IsNotEmpty(types, "Found no replicated view types; these tests would pass vacuously.");
            return types;
        }

        private static IEnumerable<string> MemberNames(Type type)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                                     BindingFlags.DeclaredOnly;

            foreach (var f in type.GetFields(all)) yield return f.Name;
            foreach (var p in type.GetProperties(all)) yield return p.Name;
            foreach (var m in type.GetMethods(all))
            {
                if (!m.IsSpecialName) yield return m.Name;
            }
        }

        /// <summary>Public instance fields and readable properties of one view instance, by name.</summary>
        private static IEnumerable<(string Name, object Value)> ReadableValues(object view)
        {
            var type = view.GetType();

            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                yield return (f.Name, f.GetValue(view));

            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length == 0 && p.CanRead)
                    yield return (p.Name, p.GetValue(view));
            }
        }

        /// <summary>
        /// Compare two views field by field, ignoring the id. Reflection rather than
        /// <c>Equals</c> so that a field added later is compared without anyone editing this, and so
        /// a failure names the field that leaked.
        /// </summary>
        private static void AssertViewsAgree(SampleView a, SampleView b, string because)
        {
            foreach (var f in typeof(SampleView).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name == nameof(SampleView.Id)) continue;
                Assert.AreEqual(f.GetValue(a), f.GetValue(b), $"SampleView.{f.Name} {because}");
            }
        }

        private static bool IsUnmanaged(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type.IsPointer) return true;
            if (!type.IsValueType) return false;

            return type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .All(f => IsUnmanaged(f.FieldType));
        }

        private static T RoundTrip<T>(T value) where T : struct, INetworkSerializable
        {
            using var writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteNetworkSerializable(value);

            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out T copy);
            return copy;
        }

        /// <summary>
        /// Walk a fresh arrival to the point an instrument would take it, booking it in under
        /// <paramref name="typedTag"/> — which is the player's transcription, right or wrong (§5.1).
        /// </summary>
        private static GeneratedSample Ready(GeneratedSample generated, string typedTag)
        {
            Assert.IsNotNull(generated, "Generator produced nothing.");
            Assert.IsTrue(SampleLifecycle.TryMove(generated.State, SampleLocation.OnSurface("bench", 0), out var move), move);
            Assert.IsTrue(SampleLifecycle.TryLog(generated.State, typedTag, out var log), log);
            Assert.IsTrue(SampleLifecycle.TryPrep(generated.State, out var prep), prep);
            return generated;
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
