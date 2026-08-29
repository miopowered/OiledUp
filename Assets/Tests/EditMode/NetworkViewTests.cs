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
                         .Concat(content.Machines.Values)
                         .Concat(content.Customers.Values))
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
                    var healthy = Ready(generator.Generate(healthyRequest, ref rng));

                    var doomedRequest = GenerationRequest.Default(profile, tag, 1);
                    doomedRequest.ForcedFault = fault;
                    doomedRequest.ForcedSeverity01 = 1f;
                    doomedRequest.CascadeChance = 0f;
                    doomedRequest.HealthyChance = 0f;
                    var doomed = Ready(generator.Generate(doomedRequest, ref rng));

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
        /// Promise: the terminal names a vial after the tag printed on its label.
        /// <para>
        /// This replaces <c>SampleView_FromAMisLoggedVial_ShowsTheTypedTagAndHidesTheMismatch</c>.
        /// That test guarded hard rule 3 as it applied to §5.1's mis-log: the mismatch had to be
        /// findable by walking back to the bottle and must not be handed over by a screen. #73
        /// removed booking-in, so there is no typed tag, no mismatch, and nothing a screen could
        /// give away. What survives is the half still worth pinning — the name a client sees is the
        /// name the bottle carries, so two players describing the same vial use the same words.
        /// </para>
        /// </summary>
        [Test]
        public void SampleView_NamesTheVialAfterItsLabel()
        {
            var rng = new Rng(4242);
            var profile = content.Profiles["quench_oil_cold"];
            var generator = new SampleGenerator(content.AllFaults);

            const string label = "WERK-1 QUENCH 1";

            var generated = Ready(generator.Generate(GenerationRequest.Default(profile, label, 1), ref rng));

            var view = SampleView.From(generated.State);

            Assert.AreEqual(label, view.RecordTag.ToString(),
                "A client's terminal must call the sample what the bottle calls it.");
            Assert.AreEqual(label, VialView.From(generated.State).Label.ToString(),
                "And so must the bottle. The two cannot disagree since #73.");
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
        /// Promise: a client can walk up to a bottle and read what is on it.
        /// <para>
        /// This was <c>TheLabelReachesTheBottleAndNoScreen</c>, and it pinned two halves in tension:
        /// the label had to reach a client (hard rule 3 — §5.1's mis-log needed an available check)
        /// but had to stay off every screen, or a screen would diff it against the typed tag and
        /// correct the player for free. #73 deleted the typed tag. The second half is now
        /// unsatisfiable and pointless in the same breath: <see cref="SampleView.RecordTag"/> <i>is</i>
        /// the label, and there is no comparison for a screen to make.
        /// </para>
        /// <para>
        /// The first half stands on its own and is what is left here. A client that cannot read the
        /// bottle cannot tell two vials apart in a rack, which is a co-op problem regardless of what
        /// the terminal knows.
        /// </para>
        /// </summary>
        [Test]
        public void TheLabelReachesTheBottle()
        {
            var rng = new Rng(20260823);
            var profile = content.Profiles["quench_oil_cold"];
            var generator = new SampleGenerator(content.AllFaults);

            const string label = "WERK-1 QUENCH 1";

            var sample = Ready(generator.Generate(GenerationRequest.Default(profile, label, 1), ref rng)).State;

            Assert.AreEqual(label, VialView.From(sample).Label.ToString(),
                "A client has to be able to read the bottle it is standing in front of.");
        }

        /// <summary>
        /// Promise: a printout names the record it belongs to, so it can be filed against the right
        /// one.
        /// <para>
        /// This was <c>APrintoutSaysTheTypedTag_NotTheOneOnTheBottle</c>. Its sharp half — that a slip
        /// carried to the desk must not resolve a mis-log for free — went with booking-in (#73). The
        /// blunt half is still load-bearing: paper with no name on it is a test you paid for and
        /// cannot use, which is §5.1's own argument for why results do not file themselves.
        /// </para>
        /// </summary>
        [Test]
        public void APrintoutNamesTheRecordItBelongsTo()
        {
            var rng = new Rng(20260824);
            var profile = content.Profiles["quench_oil_cold"];
            var generator = new SampleGenerator(content.AllFaults);

            const string label = "WERK-1 QUENCH 1";

            var sample = Ready(generator.Generate(GenerationRequest.Default(profile, label, 1), ref rng)).State;

            var paperwork = new ResultSlips();
            int ticket = paperwork.Issue(sample.Id, "karl_fischer-1",
                new TestResult { MachineId = "karl_fischer" });

            var printed = new List<ResultSlips.Slip>();
            paperwork.CollectInto(printed);

            // Projected exactly as LabNetwork.GatherSlips does it.
            var view = SlipView.From(printed.Single(s => s.Ticket == ticket), resultKey: 0,
                "Karl Fischer", sample.RecordTag);

            Assert.AreEqual(label, view.RecordTag.ToString(),
                "A slip has to name a record, or a client cannot tell which one to file it against.");
            Assert.AreEqual(sample.Id.Value, view.Sample,
                "And it has to name it by id too — tags repeat legitimately across a contract.");
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

                // Catch-alls. Substring matching means "PrimaryFaultId" and "FaultName" fail too.
                "Fault",
                "Truth",
                "Actual",

                // Not ground truth either, and protected for the reason drift is: which vial arrived
                // under which claim is #32's whole question, and the player answers it with the note in
                // one hand and the rack in front of them. A carton that carried its manifest — or a
                // note line that named the sample answering it — would hand that over on every client.
                // CartonView carries a count and NoteLineView carries a claim; see both type docs.
                "Contents",
                "Manifest"
            };

            // The list used to hold two more entries, both about §5.1's mis-log: SampleState's
            // IsMislogged, and a ban on the words "EquipmentTag" and "Label" everywhere except
            // VialView. They guarded hard rule 3 — the mismatch had to be found by walking back to
            // the bottle, so no screen could be allowed to hold both halves of the comparison. #73
            // removed booking-in; the record is now named after the label, so there is no second
            // name, no mismatch, and no half to keep apart. The label ban in particular would now be
            // asserting a rule the design does not have, which is worse than not asserting one.
            var offenders = new List<string>();

            foreach (var view in ViewTypes())
            {
                foreach (string member in MemberNames(view))
                {
                    foreach (string word in forbidden)
                    {
                        if (member.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                            offenders.Add($"{view.Name}.{member} (matches '{word}')");
                    }
                }
            }

            // ReportView is scanned like everything else and passes on its own terms: it is the one
            // view allowed to carry a diagnosis, and it carries it inside a finished sentence rather
            // than in a field named after the answer. That is not a loophole — a member called
            // FaultName would be a second copy of something already said, sitting there for the next
            // screen to draw out of context. What actually keeps that type honest is timing, which a
            // name sweep cannot see: see NoReplicatedReport_NamesAFaultOnASampleStillInPlay.
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
        // 2a. The delivery (#30, #31, #32, #80). A box replicates so a client can start the day at
        //     all — and what it must not replicate is what is in it.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a client can see how much is left in a box and never which bottles they are.
        /// <para>
        /// A carton's contents are the answer to #32. The player works out which vial answers which
        /// line of the delivery note by reading the paper and looking at the rack, and
        /// <see cref="Carton.Contents"/> is the lab's own record of the right answer — so a view
        /// carrying it would settle the reconciliation for every client without anybody looking at
        /// anything. What has to travel is the number, because "3 vials still in it" is on the prompt
        /// and a box you cannot count is a box you cannot decide to go back to.
        /// </para>
        /// So this plays a real morning, projects a real box, and pins both halves: the count tracks
        /// what is physically inside, and nothing on the row can name a sample.
        /// </summary>
        [Test]
        public void CartonView_SaysHowMuchIsLeftInTheBoxAndNeverWhatIsInIt()
        {
            var lab = ArrivedLab();
            var carton = FullestCarton(lab);

            int before = lab.Deliveries.RemainingIn(carton);
            Assert.GreaterOrEqual(before, 1, "No box arrived with a vial in it; this proves nothing.");

            var view = CartonView.From(carton, before);

            Assert.AreEqual(carton.Id, view.Id.ToString(),
                "The row has to name the box, or a client cannot match the bottles inside it to it — " +
                "a vial's ContainerId is this same string.");
            Assert.AreEqual(carton.JobNumber, view.JobNumber.ToString(),
                "The job number is on the outside of the box and on the prompt.");
            Assert.AreEqual(before, view.VialsRemaining);
            Assert.IsTrue(view.IsSealed, "A box off the truck is taped shut until somebody cuts it.");
            Assert.IsTrue(view.NoteIsInside, "The paperwork ships in the box (#31).");

            // Lift one bottle out. The count is the only thing about the contents that may move.
            var lifted = InsideOf(lab, carton)[0];
            Assert.IsTrue(
                SampleLifecycle.TryMove(lifted, SampleLocation.OnSurface("bench", 0), out string refusal),
                refusal);

            var after = CartonView.From(carton, lab.Deliveries.RemainingIn(carton));
            Assert.AreEqual(before - 1, after.VialsRemaining,
                "The count is read off where the bottles say they are, so taking one out has to show.");

            AssertNamesNoSample(typeof(CartonView),
                "A carton's contents are the answer to #32. A client holding them has the " +
                "reconciliation done for it.");
        }

        /// <summary>
        /// Promise: a replicated delivery note carries what the customer claimed and not whether the
        /// claim was met.
        /// <para>
        /// <see cref="DeliveryNote.Line"/> holds the sample that really answers each line. It is host
        /// bookkeeping, it is never printed on the paper, and it is exactly the thing #32 asks the
        /// player to work out — a line that arrived on a client knowing it had been answered would let
        /// a screen tick the page off, and a note that reconciles itself leaves nothing to find (hard
        /// rule 3's tell, deleted).
        /// </para>
        /// The rest of the page has to cross intact, though, and in order: a registration is filed by
        /// row number, so a page numbered differently at the two ends would record the wrong claim.
        /// </summary>
        [Test]
        public void AReplicatedNoteLine_CarriesTheClaimAndNotTheVialThatAnswersIt()
        {
            var lab = ArrivedLab();

            var rows = new List<NoteLineView>();
            int lines = 0;
            int answered = 0;

            foreach (var carton in lab.Deliveries.Cartons)
            {
                var note = carton.Note;
                if (note == null) continue;

                int before = rows.Count;
                NoteLineView.Gather(carton, rows);

                Assert.AreEqual(note.Count, rows.Count - before,
                    "Every line the customer typed has to reach the desk, including the ones no " +
                    "bottle answers — a note that quietly dropped those would reconcile perfectly.");

                for (int i = 0; i < note.Count; i++)
                {
                    var row = rows[before + i];

                    Assert.AreEqual(carton.Id, row.CartonId.ToString());
                    Assert.AreEqual(i, row.Index,
                        "Rows are numbered as they are printed. A registration is filed by row number, " +
                        "so a page numbered differently at the two ends records the wrong claim.");
                    Assert.AreEqual(note.Lines[i].TankTag ?? string.Empty, row.TankTag.ToString(),
                        "The tank the sender says they drew is the whole of what the page says.");

                    lines++;
                    if (note.Lines[i].Arrived) answered++;
                }
            }

            Assert.Greater(lines, 0, "The morning issued no paperwork; this test compared nothing.");
            Assert.Greater(answered, 0,
                "No line was answered by a vial, so the field under test never had a value to leak.");

            AssertNamesNoSample(typeof(NoteLineView),
                "A line that named the vial answering it would let a screen tick the page off, which " +
                "is #32 done for the player.");
        }

        /// <summary>
        /// No member of a view may be, or be named after, a sample. Reflection rather than a field
        /// list, so a field added later is covered the day it lands rather than the day someone
        /// remembers this file.
        /// </summary>
        private static void AssertNamesNoSample(Type view, string because)
        {
            var offenders = new List<string>();

            foreach (var field in view.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(SampleId)) offenders.Add($"{view.Name}.{field.Name} (SampleId)");
            }

            foreach (string member in MemberNames(view))
            {
                if (member.IndexOf("Sample", StringComparison.OrdinalIgnoreCase) >= 0)
                    offenders.Add($"{view.Name}.{member}");
            }

            Assert.IsEmpty(offenders, $"{because}\nOffenders:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>A lab one morning in, with the truck unloaded and every box still sealed.</summary>
        private LabState ArrivedLab()
        {
            var lab = new LabState(catalog, PlanOf(1, samplesPerDay: 12, healthyChance: 0.3f), 20260830);
            lab.Deliveries.Capacity = 64;
            lab.BeginDay();
            lab.Tick(lab.Deliveries.SecondsUntilArrival + 0.01f);
            return lab;
        }

        /// <summary>The box the morning put the most bottles in, so the counts have somewhere to move.</summary>
        private static Carton FullestCarton(LabState lab)
        {
            Carton fullest = null;
            int most = 0;

            foreach (var carton in lab.Deliveries.Cartons)
            {
                if (carton.Stage != CartonStage.Delivered) continue;

                int inside = lab.Deliveries.RemainingIn(carton);
                if (inside <= most) continue;

                fullest = carton;
                most = inside;
            }

            Assert.IsNotNull(fullest, "The truck unloaded nothing; this test compared nothing.");
            return fullest;
        }

        private static List<SampleState> InsideOf(LabState lab, Carton carton)
        {
            var inside = new List<SampleState>();
            foreach (var sample in lab.Samples.All)
            {
                if (sample.Location.Kind != SampleLocationKind.InCrate) continue;
                if (sample.Location.ContainerId != carton.Id) continue;
                inside.Add(sample);
            }
            return inside;
        }

        // -----------------------------------------------------------------------------------------
        // 2b. The end-of-day report (§4.3, §5.4). The only view allowed to name a fault, and the
        //     only one whose safety is a question of when rather than what.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// <b>The promise: no report reaches a client naming the fault on a sample still in play.</b>
        /// <para>
        /// A <c>ConsequenceReport</c> is past truth — §4.3 names the fault only after the verdict has
        /// been scored and the money has moved — which is why it may cross at all. But MONITOR on a
        /// developing fault re-sends the unit next cycle carrying the <i>same</i> fault further along
        /// (§5.4), so for that one outcome the report is the answer to a question the game has not
        /// finished asking. It does not look like a leak: the report is genuinely about the past right
        /// up until the oil comes back through the door.
        /// </para>
        /// <para>
        /// So this plays a whole contract filing MONITOR on every developing fault — the verdict that
        /// requeues — and after each morning's arrivals asks, of every row published the night
        /// before: did this unit come back? If it did, the sentence a client was shown must name
        /// neither its fault nor its root cause. Scoped to the report's own unit rather than to every
        /// fault in the lab, because two unrelated tanks sharing a fault is a coincidence, not a
        /// disclosure.
        /// </para>
        /// It also pins the second half of the rule in passing: nothing is on the wire while a shift
        /// is open, so a client cannot be holding last night's summary on the morning the re-draw
        /// arrives.
        /// </summary>
        [Test]
        public void NoReplicatedReport_NamesAFaultOnASampleStillInPlay()
        {
            var lab = new LabState(catalog, PlanOf(20, samplesPerDay: 10, healthyChance: 0.05f), 20260824);

            var published = new List<ReportView>();
            var lastNight = new List<ReportView>();
            int settled = 0;
            int returned = 0;

            while (lab.BeginDay())
            {
                foreach (var row in lastNight)
                {
                    if (!CameBackFor(lab, row.Sample)) continue;

                    var fault = lab.Samples.PeekTruthForDebugging(new SampleId(row.Sample))?.PrimaryFault;
                    if (fault == null) continue;

                    returned++;
                    string said = row.Headline.ToString();

                    StringAssert.DoesNotContain(fault.DisplayName, said,
                        $"The report on S{row.Sample:D5} named '{fault.DisplayName}', and this morning " +
                        "that unit was re-drawn carrying the same fault further along (§5.4). Every " +
                        "client is holding the diagnosis for a sample the game is about to ask them " +
                        "about.");

                    if (fault.RootCause != null)
                    {
                        StringAssert.DoesNotContain(fault.RootCause.DisplayName, said,
                            $"The report on S{row.Sample:D5} named the root cause of a unit that has " +
                            "just come back. §5.4 pays a bonus for that answer; this hands it over.");
                    }
                }

                ReportView.Gather(lab, published);
                Assert.IsEmpty(published,
                    "A shift is open and last night's reckoning is still on the wire. It has to come " +
                    "off every desk before the re-drawn samples arrive, or the window closes on the " +
                    "player's memory rather than on the list.");

                foreach (var sample in lab.OpenSamples())
                    lab.Samples.FileVerdict(sample.Id, VerdictFor(lab, sample), null, lab.Day);

                lab.EndDay();

                ReportView.Gather(lab, published);
                settled += published.Count;

                lastNight.Clear();
                lastNight.AddRange(published);
            }

            Assert.Greater(settled, 0, "No verdict ever settled, so this test compared nothing.");
            Assert.Greater(returned, 0,
                "Nothing was ever re-drawn, so the case this test exists for never happened. MONITOR " +
                "on a developing fault must requeue the unit (§5.4).");
        }

        /// <summary>
        /// Promise: the withholding is structural, not a fact about the copy.
        /// <para>
        /// §5.4's own headline for a requeued unit names nothing, which is the right call and is why
        /// the rule above holds today. But that is one sentence in one branch of a switch, and a leak
        /// that arrives by rewording is exactly the shape this whole file exists to catch. So the
        /// projection is handed a report whose headline <i>does</i> name the fault and is required to
        /// refuse it — while still saying which tank, because a card that names nothing at all is not
        /// a report.
        /// </para>
        /// </summary>
        [Test]
        public void ReportView_OnAUnitComingBackNextCycle_WithholdsTheDiagnosis()
        {
            var report = new ConsequenceReport
            {
                Sample = new SampleId(11),
                RecordTag = "WERK-1 QUENCH 1",
                Outcome = ConsequenceOutcome.MonitorDeveloping,
                MoneyDelta = 120f,
                FaultName = "Oxidation and varnish",
                ActualRootCause = "Bath run above setpoint",
                RequeueSample = true,
                Headline = "WERK-1 QUENCH 1: kept in service. Oxidation and varnish, from bath run " +
                           "above setpoint. Another draw is scheduled."
            };

            string withheld = ReportView.From(report, 4).Headline.ToString();

            StringAssert.DoesNotContain(report.FaultName, withheld,
                "A unit kept in service comes back tomorrow with the same fault. Naming it on the " +
                "card is naming tomorrow's answer.");
            StringAssert.DoesNotContain(report.ActualRootCause, withheld,
                "Same argument for the root cause, which §5.4 pays a bonus for diagnosing.");
            StringAssert.Contains(report.RecordTag, withheld,
                "The card still has to say which tank it is about, or the player cannot tell which " +
                "of their calls this was.");

            // And a unit that is finished with crosses whole: the reveal is the point of §4.3, and
            // withholding it from a record nothing can ask about again would just be a worse screen.
            report.RequeueSample = false;
            StringAssert.Contains(report.FaultName, ReportView.From(report, 4).Headline.ToString(),
                "A settled record's diagnosis is spent. Every player who worked the shift may read it.");
        }

        /// <summary>
        /// Promise: the other way a sample gets back into play cannot reach a report.
        /// <para>
        /// §5.3 lets a record filed on a drifting instrument be re-opened, which puts it back to
        /// <see cref="SampleStage.Measured"/> to be re-tested and re-filed. If a reported record could
        /// take that route, a client would be holding the diagnosis for a call it is about to be asked
        /// to make again — the same leak as the re-draw, arriving by a different door.
        /// </para>
        /// <para>
        /// It cannot, and this is the reason: a report exists only for a sample
        /// <c>SampleRegistry.ResolveDue</c> got <c>SampleLifecycle.TryResolve</c> to accept, and
        /// <see cref="SampleStage.Resolved"/> has no outgoing edge. <c>ReportView</c> depends on that
        /// and does not own it, so it is asserted here rather than remembered: adding an edge out of
        /// Resolved needs the report projection to grow a rule for it in the same change.
        /// </para>
        /// </summary>
        [Test]
        public void AReportedRecord_CanNeverComeBackIntoPlay()
        {
            Assert.IsEmpty(SampleLifecycle.LegalNext(SampleStage.Resolved),
                "Resolved has stopped being terminal. Every published report names a sample that " +
                "reached it, so a way out of Resolved is a way for a sample to come back with its " +
                "answer already on every client's screen (see ReportView's timing rule).");

            foreach (SampleStage stage in Enum.GetValues(typeof(SampleStage)))
            {
                Assert.IsFalse(SampleLifecycle.IsLegal(SampleStage.Resolved, stage),
                    $"Resolved -> {stage} is now legal. See above.");
            }
        }

        /// <summary>
        /// Promise: the day's reckoning is on the wire between shifts and at no other time.
        /// <para>
        /// <c>LabState.LastReports</c> outlives the day it describes — the host's screen retires it
        /// behind START NEXT DAY, and a client has no list of its own to drop. Left on the wire it
        /// would still be readable on the morning the re-drawn samples walk in, which is the window
        /// the whole rule is about. <c>LabState.BeginDay</c> raises <c>DayInProgress</c> before it
        /// generates those re-draws, so withdrawing on that flag closes the window strictly early.
        /// </para>
        /// </summary>
        [Test]
        public void Reports_AreOnTheWireOnlyBetweenShifts()
        {
            var lab = new LabState(catalog, PlanOf(3, healthyChance: 1f), 77);
            var rows = new List<ReportView>();

            ReportView.Gather(lab, rows);
            Assert.IsEmpty(rows, "Nothing has happened yet, so there is nothing to publish.");

            lab.BeginDay();
            foreach (var sample in lab.OpenSamples())
                lab.Samples.FileVerdict(sample.Id, Verdict.Normal, null, lab.Day);

            ReportView.Gather(lab, rows);
            Assert.IsEmpty(rows, "A shift is open. Verdicts filed today settle later (§5.4).");

            lab.EndDay();
            lab.BeginDay();

            ReportView.Gather(lab, rows);
            Assert.IsEmpty(rows, "Day two is open; nothing may be published mid-shift.");

            // A healthy unit has no failure clock, so its verdict settles on the next day's paperwork.
            lab.EndDay();
            ReportView.Gather(lab, rows);
            Assert.IsNotEmpty(rows,
                "Day one's verdicts came due and no client can see them. Everybody worked the shift; " +
                "everybody sees the reckoning.");

            Assert.IsTrue(lab.BeginDay(), "Test needs a third day to watch the summary come down.");
            ReportView.Gather(lab, rows);
            Assert.IsEmpty(rows,
                "The next day has opened and last night's summary is still on every joined desk. " +
                "BeginDay generates the re-draws immediately after this point.");
        }

        /// <summary>
        /// MONITOR on the units that requeue, and the right call on everything else.
        /// <para>
        /// The test is about MONITOR-on-developing, which is the one outcome that re-sends a unit
        /// (§5.4). Calling everything MONITOR would be simpler and useless: MONITOR on an imminent
        /// fault costs the fault's full repair bill, ten of those a day bankrupts the outpost inside
        /// a week, and the run would end before the first re-draw — with §1.2 closing the contract
        /// early and the test passing having compared nothing.
        /// </para>
        /// </summary>
        private static Verdict VerdictFor(LabState lab, SampleState sample)
        {
            var truth = lab.Samples.PeekTruthForDebugging(sample.Id);
            if (truth == null || truth.IsHealthy) return Verdict.Normal;

            return truth.WorstSeverity == FaultSeverity.Developing ? Verdict.Monitor : Verdict.Critical;
        }

        /// <summary>True when a sample re-drawn from <paramref name="original"/> is in the lab (§5.4).</summary>
        private static bool CameBackFor(LabState lab, int original)
        {
            foreach (var sample in lab.Samples.All)
            {
                if (sample.ResampleOf.Value == original) return true;
            }
            return false;
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
                IsRunOver = false,
                ContractName = "Shakedown",
                ContractLength = 20
            };
            Assert.AreEqual(day, RoundTrip(day));

            var carton = new CartonView
            {
                Id = "carton:3:KH-04127",
                JobNumber = "KH-04127",
                CustomerId = "werk_nord",
                Day = 3,
                Stage = CartonStage.Delivered,
                IsSealed = false,
                VialsRemaining = 4,
                Kind = SampleLocationKind.OnSurface,
                ContainerId = "bay",
                SlotIndex = 2,
                NoteKind = SampleLocationKind.Held,
                NoteHolderClientId = 7UL,
                NoteSlotIndex = -1
            };
            Assert.AreEqual(carton, RoundTrip(carton));

            var line = new NoteLineView
            {
                CartonId = "carton:3:KH-04127",
                Index = 2,
                TankTag = "HALLE-3 SEALED QUENCH 1",
                ProfileId = "corrosion_protection_oil"
            };
            Assert.AreEqual(line, RoundTrip(line));

            var report = new ReportView
            {
                Sample = 41,
                Day = 9,
                Outcome = ConsequenceOutcome.MissedFault,
                MoneyDelta = -8_400f,
                ReputationDelta = -12.5f,
                RootCauseCorrect = false,
                Headline = "WERK-4 BATH C: PASSED AS FIT TO QUENCH. Oxidation and varnish. " +
                           "Named in the incident file."
            };
            Assert.AreEqual(report, RoundTrip(report));
        }

        /// <summary>
        /// Promise: a tag longer than the wire budget clips instead of dropping the host mid-shift.
        /// <para>
        /// The player used to type this field, which is why it was never safe to assume the content
        /// tables bounded it. #73 made it come off the label instead, so the tables <i>do</i> bound
        /// it — but a <c>FixedString64Bytes</c> that throws takes the host down for everybody, and a
        /// content table is a much easier thing to change than that failure is to diagnose. The
        /// promise is cheaper to keep than to re-derive, so it stays.
        /// </para>
        /// </summary>
        [Test]
        public void SampleView_TruncatesAnAbsurdlyLongTagRatherThanThrowing()
        {
            var rng = new Rng(7);
            var profile = content.Profiles["quench_oil_cold"];
            string huge = new('X', 400);

            var generated = Ready(
                new SampleGenerator(content.AllFaults).Generate(
                    GenerationRequest.Default(profile, huge, 1), ref rng));

            var view = SampleView.From(generated.State);

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

        /// <summary>Walk a fresh arrival to the point an instrument would take it: unpacked, agitated.</summary>
        private static GeneratedSample Ready(GeneratedSample generated)
        {
            Assert.IsNotNull(generated, "Generator produced nothing.");
            Assert.IsTrue(SampleLifecycle.TryMove(generated.State, SampleLocation.OnSurface("bench", 0), out var move), move);
            Assert.IsTrue(SampleLifecycle.TryPrep(generated.State, out var prep), prep);
            return generated;
        }

        /// <summary>A flat contract of <paramref name="days"/> identical days.</summary>
        private static ContractPlan PlanOf(int days, int samplesPerDay = 4, float healthyChance = 0.3f)
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
                    DaySeconds = 600f
                });
            }

            return plan;
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
