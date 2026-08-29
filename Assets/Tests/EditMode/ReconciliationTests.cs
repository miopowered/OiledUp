using System;
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
    /// Guards #32: the delivery note against what actually came in the box.
    ///
    /// <para>
    /// <b>Every test here is really the same test.</b> Hard rule 3 says a player may never be punished
    /// for something they could not have checked, and this issue is the keystone case: a discrepancy
    /// is only fair because the note arrived beside the vials. So each of the four kinds is asserted
    /// twice over — once that it is <i>produced</i>, and once that it is <i>findable</i> from the note
    /// plus the contents. A discrepancy that generation can make and reading cannot find is the bug
    /// this file exists to catch, and it is a bug that would never show up as an exception.
    /// </para>
    ///
    /// <para>
    /// The other half guards the settlement with #73. Booking-in was removed because it stopped the
    /// loop at a keyboard; registration came back only for the two bottles a shift that cannot speak
    /// for themselves, and only as a decision that is never waited on. Two tests below hold that line:
    /// a legible vial is refused registration outright, and an unregistered ambiguous vial still runs,
    /// files and pays.
    /// </para>
    /// </summary>
    public sealed class ReconciliationTests
    {
        private ContentSet content;
        private ContentCatalog catalog;

        /// <summary>Every fluid in the tables, so all six senders can turn up in one fixture day.</summary>
        private static readonly string[] AllOils =
        {
            "hardening_oil_general", "quench_oil_cold", "quench_oil_martempering",
            "quench_oil_accelerated", "quench_oil_vacuum", "corrosion_protection_oil"
        };

        /// <summary>
        /// How many seeds a search may try before giving up. Generous: the rarest thing looked for
        /// here is one particular paperwork slip from one particular careless firm on one day.
        /// </summary>
        private const int SeedsToTry = 400;

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

        // -- Fixture ------------------------------------------------------------------------------------

        private static ContractPlan PlanOf(int samplesPerDay, int days = 4, float daySeconds = 600f)
        {
            var plan = new ContractPlan { Id = "test", DisplayName = "Test", Days = new List<DayPlan>() };
            for (int i = 0; i < days; i++)
            {
                plan.Days.Add(new DayPlan
                {
                    SampleCount = samplesPerDay,
                    ProfileIds = AllOils,
                    BorderlineCount = 0,
                    HealthyChance = 0.3f,
                    DaySeconds = daySeconds
                });
            }
            return plan;
        }

        private LabState Lab(int seed, int samplesPerDay = 12)
        {
            var lab = new LabState(catalog, PlanOf(samplesPerDay), seed);
            lab.BeginDay();
            return lab;
        }

        /// <summary>
        /// The first seeded day that produced the discrepancy we are here to look at.
        /// <para>
        /// Searching rather than forcing is deliberate. The generator is the thing under test, so a
        /// test that reached in and set the flag itself would prove only that the flag can be set. It
        /// is stable because <c>Rng</c> is: the same seed produces the same morning forever, and the
        /// search itself is a weak assertion that the discrepancy is reachable at all.
        /// </para>
        /// </summary>
        private LabState FindDay(Func<LabState, bool> wanted, string describe, int samplesPerDay = 12)
        {
            for (int seed = 1; seed <= SeedsToTry; seed++)
            {
                var lab = Lab(seed, samplesPerDay);
                if (wanted(lab)) return lab;
            }

            Assert.Fail($"No seed in 1..{SeedsToTry} produced {describe}. Either the generator stopped " +
                        "producing it or it has become far rarer than the customer tables say.");
            return null;
        }

        private static IEnumerable<SampleState> In(LabState lab, Carton carton) =>
            carton.Contents.Select(id => lab.Samples.TryGet(id, out var s) ? s : null).Where(s => s != null);

        private static Carton CartonOf(LabState lab, SampleState sample) =>
            lab.Deliveries.ByJobNumber(sample.JobNumber);

        /// <summary>
        /// Every sample in arrival order. <c>SampleRegistry.All</c> is a dictionary's values and its
        /// order is not a promise; a test that picked "the first ambiguous vial" out of it would be
        /// choosing a different bottle on a different runtime.
        /// </summary>
        private static List<SampleState> Ordered(LabState lab) =>
            lab.Samples.All.OrderBy(s => s.Id.Value).ToList();

        private static SampleState FirstWhere(LabState lab, Func<SampleState, bool> match) =>
            Ordered(lab).First(match);

        // -- 1. Missing sample ---------------------------------------------------------------------------

        /// <summary>
        /// Promise: the note claims a tank the box does not answer, and the player finds it by
        /// counting. The page declares more vials than came out of the carton, and exactly one tank on
        /// it has no bottle carrying its tag.
        /// <para>
        /// The second half is what makes it fair. A missing line whose tank <i>did</i> arrive would be
        /// invisible to the only method the player has, and the discrepancy would be a punishment for
        /// nothing.
        /// </para>
        /// </summary>
        [Test]
        public void MissingSample_IsOnThePaperAndFindableByCountingTheBox()
        {
            var lab = FindDay(l => l.Deliveries.Cartons.Any(HasUnansweredLine), "a missing sample");

            var carton = lab.Deliveries.Cartons.First(HasUnansweredLine);
            var note = carton.Note;

            Assert.Greater(note.Count, carton.Contents.Count,
                "A missing sample has to show up as a note declaring more vials than the box holds.");

            var tagsInTheBox = In(lab, carton).Select(s => s.EquipmentTag).ToList();

            foreach (var line in note.Lines)
            {
                if (line.Arrived) continue;

                Assert.IsFalse(tagsInTheBox.Contains(line.TankTag),
                    $"'{line.TankTag}' is listed as missing but a bottle in the box carries that tag. " +
                    "The player's only method — compare the tags on the page to the tags on the " +
                    "bottles — would never find it.");
            }
        }

        private static bool HasUnansweredLine(Carton carton) =>
            carton?.Note != null && carton.Note.Lines.Any(l => !l.Arrived);

        // -- 2. Unlisted sample --------------------------------------------------------------------------

        /// <summary>
        /// Promise: a vial in the box that the paperwork never mentions, found by counting from the
        /// other side — and it is a working sample, not a broken one. It has chemistry, it can be run,
        /// and it needs no registration, because its label speaks perfectly well.
        /// </summary>
        [Test]
        public void UnlistedSample_IsInTheBoxOnNoLineAndStillFullyUsable()
        {
            var lab = FindDay(l => l.Deliveries.Cartons.Any(c => Unlisted(l, c).Any()),
                              "an unlisted sample");

            var carton = lab.Deliveries.Cartons.First(c => Unlisted(lab, c).Any());
            var stowaway = Unlisted(lab, carton).First();

            Assert.AreEqual(-1, carton.Note.IndexOf(stowaway.Id),
                "An unlisted vial must be on no line of the note.");
            Assert.IsFalse(carton.Note.TryFind(stowaway.EquipmentTag, out _),
                $"'{stowaway.EquipmentTag}' is meant to be unlisted, but the note names that tank — " +
                "so comparing tags would say it was expected.");

            Assert.IsFalse(string.IsNullOrEmpty(stowaway.EquipmentTag),
                "An unlisted vial's own label is legible; it is the paperwork that is short.");
            Assert.AreEqual(SampleAmbiguity.None, stowaway.Ambiguity,
                "An unlisted vial says what it is, so nothing about it needs registering (#73).");
            Assert.IsNotNull(stowaway.Profile, "An unlisted vial is a real sample with real chemistry.");
        }

        private static IEnumerable<SampleState> Unlisted(LabState lab, Carton carton) =>
            In(lab, carton).Where(s => carton.Note.IndexOf(s.Id) < 0);

        // -- 3. Unreadable label -------------------------------------------------------------------------

        /// <summary>
        /// Promise: a bottle whose tag cannot be read, and a note that still lists its tank — so
        /// elimination has an answer. Exactly one line on the page is left unclaimed by a legible
        /// bottle, and that line is the right one.
        /// <para>
        /// This is the whole of "discoverable by comparing note to contents" for the illegible case,
        /// and it is why at most one paperwork slip is introduced per carton
        /// (<c>DeliveryDiscrepancies.Roll</c>). Two unanswered lines would leave elimination with two
        /// answers and the player with a coin flip.
        /// </para>
        /// </summary>
        [Test]
        public void UnreadableLabel_LeavesExactlyOneSpareLineOnTheNote()
        {
            var lab = FindDay(HasSmudgedLabel, "a vial with an unreadable label");

            var smudged = FirstWhere(lab, s => s.Ambiguity == SampleAmbiguity.UnreadableLabel);
            var carton = CartonOf(lab, smudged);
            Assert.IsNotNull(carton, "An unreadable vial still came in a box with a note in it.");

            Assert.IsTrue(string.IsNullOrEmpty(smudged.EquipmentTag),
                "An unreadable label has no tag to read off it.");
            StringAssert.Contains("UNLABELLED", smudged.RecordTag,
                "Before it is registered the lab has to call it something the player can find again.");

            var legible = In(lab, carton)
                .Where(s => !string.IsNullOrEmpty(s.EquipmentTag))
                .Select(s => s.EquipmentTag)
                .ToList();

            var spare = carton.Note.Lines
                .Select((line, index) => (line, index))
                .Where(row => !legible.Remove(row.line.TankTag))
                .ToList();

            Assert.AreEqual(1, spare.Count,
                "Elimination has to have exactly one answer: one line on the note that no legible " +
                "bottle in the box claims.");

            var truth = lab.Samples.PeekTruthForDebugging(smudged.Id);
            Assert.AreEqual(spare[0].line.TankTag, truth.TrueTankTag,
                "The line elimination points at is not the tank the oil actually came from — the " +
                "method the player is given would give them the wrong answer.");
        }

        /// <summary>
        /// Promise: the call to the customer is the paid-for certainty (#32's "needs a call to the
        /// customer, which costs time"). It comes off the shift clock, it settles the label outright,
        /// and it is charged once per delivery however many bottles it identifies.
        /// </summary>
        [Test]
        public void CallingTheCustomer_SpendsShiftTimeAndSettlesTheLabel()
        {
            var lab = FindDay(HasSmudgedLabel, "a vial with an unreadable label");

            var smudged = FirstWhere(lab, s => s.Ambiguity == SampleAmbiguity.UnreadableLabel);
            var carton = CartonOf(lab, smudged);
            var truth = lab.Samples.PeekTruthForDebugging(smudged.Id);

            float before = lab.DaySecondsRemaining;

            Assert.IsTrue(lab.TryCallCustomer(carton.Id, out int settled, out string refusal), refusal);
            Assert.GreaterOrEqual(settled, 1, "The dispatcher identified nothing.");

            Assert.AreEqual(before - DeliveryDiscrepancies.CallSeconds, lab.DaySecondsRemaining, 0.01f,
                "A call has to cost shift time, or reading the note by hand is never the better trade.");

            Assert.AreEqual(truth.TrueTankTag, smudged.RegisteredTag,
                "The customer read their own dispatch record back and it did not match the truth.");
            Assert.IsFalse(smudged.NeedsRegistering, "The call is a registration; it must record one.");

            // Nothing left for them to settle, so it is refused before any more time is spent.
            float after = lab.DaySecondsRemaining;
            Assert.IsFalse(lab.TryCallCustomer(carton.Id, out _, out string second),
                "A second call with nothing to ask about must be refused, not charged for.");
            Assert.IsNotEmpty(second);
            Assert.AreEqual(after, lab.DaySecondsRemaining, 0.01f,
                "A refused call still took time off the clock.");
        }

        /// <summary>
        /// Promise: the phone cannot settle §6.1's trap. Asked whether they really drew that tank
        /// twice, the customer confirms their own note — and their note is the thing in doubt. The
        /// refusal has to land before any shift time is spent, or the player pays for nothing.
        /// </summary>
        [Test]
        public void CallingTheCustomer_CannotSettleADuplicatedClaim()
        {
            // A box whose only ambiguity is the duplicate. A smudged label in the same carton would
            // legitimately give the dispatcher something to answer, and the call would be accepted.
            var lab = FindDay(
                l => l.Deliveries.Cartons.Any(
                    c => In(l, c).Any(s => s.Ambiguity == SampleAmbiguity.DuplicateClaim) &&
                         In(l, c).All(s => s.Ambiguity != SampleAmbiguity.UnreadableLabel)),
                "a carton whose only discrepancy is a duplicated tank claim");

            var carton = lab.Deliveries.Cartons.First(
                c => In(lab, c).Any(s => s.Ambiguity == SampleAmbiguity.DuplicateClaim) &&
                     In(lab, c).All(s => s.Ambiguity != SampleAmbiguity.UnreadableLabel));

            float before = lab.DaySecondsRemaining;

            Assert.IsFalse(lab.TryCallCustomer(carton.Id, out _, out string refusal),
                "A phone call that resolved the same-drum trap would delete it.");
            Assert.IsNotEmpty(refusal);
            Assert.AreEqual(before, lab.DaySecondsRemaining, 0.01f,
                "The refusal has to come before the clock is charged.");
        }

        // -- 4. Duplicate tank ids -----------------------------------------------------------------------

        /// <summary>
        /// Promise: two vials claiming one tank, against a note that books it twice. Both bottles
        /// carry the tag, so neither can say which draw it is — which is the only reason a decision is
        /// asked for at all.
        /// </summary>
        [Test]
        public void DuplicateClaim_PutsTwoLinesAndTwoVialsOnOneTank()
        {
            var lab = FindDay(l => l.Samples.All.Any(s => s.Ambiguity == SampleAmbiguity.DuplicateClaim),
                              "a duplicated tank claim");

            var first = FirstWhere(lab, s => s.Ambiguity == SampleAmbiguity.DuplicateClaim);
            var carton = CartonOf(lab, first);

            var claimed = In(lab, carton)
                .Where(s => s.Ambiguity == SampleAmbiguity.DuplicateClaim)
                .ToList();

            Assert.AreEqual(2, claimed.Count, "A duplicated claim is exactly two bottles.");
            Assert.AreEqual(claimed[0].EquipmentTag, claimed[1].EquipmentTag,
                "Both bottles have to carry the same tag, or there is nothing ambiguous about them.");

            // At least two: tank codes repeat legitimately across a plant's sites, so a third line
            // naming the same tank is possible and is not a failure of this mechanic.
            Assert.GreaterOrEqual(carton.Note.CountFor(claimed[0].EquipmentTag), 2,
                "The note has to book that tank twice — that is the claim on paper the player reads.");
        }

        /// <summary>
        /// Promise, and the reason §6.1's trap is fair: two vials off one drum <b>are</b> the same oil,
        /// so an instrument reads them the same. The player measures the thing rather than intuiting
        /// it, and nothing anywhere reports a different number to make the trap work (hard rule 1).
        /// </summary>
        [Test]
        public void SameDrumPair_IsLiterallyTheSameOilAndMeasuresLikeIt()
        {
            var lab = FindDay(SameDrumSomewhere, "two vials drawn from one drum");
            lab.Install(catalog.Machine("elemental"), "elemental");

            var first = FirstWhere(lab, s => IsSplit(lab, s));
            var truth = lab.Samples.PeekTruthForDebugging(first.Id);
            var twin = lab.Samples.Get(truth.SameDrumAs);

            Assert.IsNotNull(twin, "A split draw has to point at the bottle it shares a drum with.");
            var twinTruth = lab.Samples.PeekTruthForDebugging(twin.Id);

            Assert.AreEqual(truth.SameDrumAs, twin.Id);
            Assert.AreEqual(twinTruth.SameDrumAs, first.Id,
                "The pairing has to be symmetric, or only one of the two is scored against it.");

            CollectionAssert.AreEquivalent(truth.TrueValues, twinTruth.TrueValues,
                "Two bottles filled from one drum must hold identical oil. Anything else would make " +
                "the tell a coincidence rather than a measurement.");
            CollectionAssert.AreEqual(
                truth.ActualFaults.Select(f => f.Id).ToList(),
                twinTruth.ActualFaults.Select(f => f.Id).ToList(),
                "The same drum cannot have two different faults in it.");

            // And it survives the instrument: the player runs both and the numbers agree.
            var machine = lab.FindMachine("elemental");
            var rng = new Rng(99);

            var a = lab.Samples.RunMachine(first.Id, machine, lab.Day, ref rng);
            machine.Clean();
            var b = lab.Samples.RunMachine(twin.Id, machine, lab.Day, ref rng);

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);

            int compared = 0;
            foreach (var reading in a.Values)
            {
                if (!b.TryGet(reading.Key, out float other)) continue;
                compared++;

                float scale = Mathf.Max(Mathf.Abs(reading.Value), Mathf.Abs(other), 1e-4f);
                Assert.Less(Mathf.Abs(reading.Value - other) / scale, 0.25f,
                    $"{reading.Key} read {reading.Value:0.###} and {other:0.###} on two bottles of the " +
                    "same oil. Instrument noise alone cannot separate them, and the player's only " +
                    "evidence for the trap is that they agree.");
            }

            Assert.Greater(compared, 0, "Nothing was measured on both bottles, so this proved nothing.");
        }

        /// <summary>
        /// Promise: two lines for one tank does <b>not</b> mean the customer cheated. A plant that
        /// genuinely drew twice produces the same page, with different oil in the bottles — which is
        /// what forces the player to run the second vial instead of reading the answer off the paper.
        /// <para>
        /// If this ever stops holding, the duplicate becomes a give-away and the measurement that makes
        /// it fair becomes optional.
        /// </para>
        /// </summary>
        [Test]
        public void ADuplicatedClaimIsNotProofOfAnything_HonestDoubleDrawsExistToo()
        {
            LabState honest = null;

            for (int seed = 1; seed <= SeedsToTry && honest == null; seed++)
            {
                var lab = Lab(seed);
                if (lab.Samples.All.Any(s => s.Ambiguity == SampleAmbiguity.DuplicateClaim &&
                                             !IsSplit(lab, s)))
                {
                    honest = lab;
                }
            }

            Assert.IsNotNull(honest,
                "No seed produced a duplicated claim that was two genuine draws. The paper tell would " +
                "then always mean 'same drum' and could be read without measuring anything.");

            var vial = FirstWhere(honest, s => s.Ambiguity == SampleAmbiguity.DuplicateClaim &&
                                               !IsSplit(honest, s));
            var carton = CartonOf(honest, vial);
            var pair = In(honest, carton)
                .Where(s => s.Ambiguity == SampleAmbiguity.DuplicateClaim)
                .ToList();

            Assert.AreEqual(2, pair.Count);

            var a = honest.Samples.PeekTruthForDebugging(pair[0].Id);
            var b = honest.Samples.PeekTruthForDebugging(pair[1].Id);

            Assert.IsFalse(a.DrawnFromOneDrum, "This fixture is the honest case.");
            CollectionAssert.AreNotEquivalent(a.TrueValues, b.TrueValues,
                "Two independent draws from a tank came back as identical oil, which would make them " +
                "indistinguishable from a split drum by the only test the player has.");
        }

        private static bool SameDrumSomewhere(LabState lab) => lab.Samples.All.Any(s => IsSplit(lab, s));

        private static bool IsSplit(LabState lab, SampleState sample)
        {
            var truth = lab.Samples.PeekTruthForDebugging(sample.Id);
            return truth != null && truth.DrawnFromOneDrum;
        }

        private static bool IsHealthy(LabState lab, SampleState sample)
        {
            var truth = lab.Samples.PeekTruthForDebugging(sample.Id);
            return truth != null && truth.IsHealthy;
        }

        private static bool HasSmudgedLabel(LabState lab) =>
            lab.Samples.All.Any(s => s.Ambiguity == SampleAmbiguity.UnreadableLabel);

        /// <summary>
        /// A smudged label on a note that offers more than one tank, so there is a wrong answer to
        /// register and a right one to be told about afterwards. A one-line note has neither.
        /// </summary>
        private static bool HasSmudgedLabelWithAChoice(LabState lab)
        {
            foreach (var sample in lab.Samples.All)
            {
                if (sample.Ambiguity != SampleAmbiguity.UnreadableLabel) continue;

                var note = lab.Deliveries.ByJobNumber(sample.JobNumber)?.Note;
                if (note == null) continue;

                var truth = lab.Samples.PeekTruthForDebugging(sample.Id);
                if (note.Lines.Any(l => !string.Equals(l.TankTag, truth.TrueTankTag,
                                                       StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        }

        // -- Who sends what ------------------------------------------------------------------------------

        /// <summary>
        /// Promise: the careless firm really is the one worth checking and the meticulous one really is
        /// a control (§6.1). Both propensities are read from the customer and nowhere else, so a
        /// sender's name narrows what to expect before a box is opened.
        /// <para>
        /// Asserted against <c>DeliveryDiscrepancies.Roll</c> rather than against generated days,
        /// because the frequency is the claim and a few thousand rolls settle it exactly where a few
        /// thousand contracts would only suggest it.
        /// </para>
        /// </summary>
        [Test]
        public void PaperworkSlips_TrackTheSendersOwnPropensities()
        {
            const int rolls = 4000;

            var meticulous = catalog.Customer("vogel_getriebe");
            var careless = catalog.Customer("kessler_haerterei");
            Assert.IsNotNull(meticulous);
            Assert.IsNotNull(careless);

            var rng = new Rng(7);

            int cleanSlips = 0, carelessSlips = 0, carelessDrums = 0, cleanDrums = 0;

            for (int i = 0; i < rolls; i++)
            {
                var good = DeliveryDiscrepancies.Roll(meticulous, ref rng);
                var bad = DeliveryDiscrepancies.Roll(careless, ref rng);

                if (good.Slip != PaperworkSlip.None) cleanSlips++;
                if (good.SameDrum) cleanDrums++;
                if (bad.Slip != PaperworkSlip.None) carelessSlips++;
                if (bad.SameDrum) carelessDrums++;
            }

            Assert.AreEqual(0, cleanSlips,
                $"{meticulous.DisplayName}'s paperwork slip chance is " +
                $"{meticulous.PaperworkSlipChance}, so their note must never be wrong. A control that " +
                "occasionally lies is not a control.");
            Assert.AreEqual(0, cleanDrums,
                $"{meticulous.DisplayName} does not cut corners on drum discipline.");

            Assert.Greater(carelessSlips, rolls * careless.PaperworkSlipChance * 0.7f,
                $"{careless.DisplayName}'s paperwork is far better than their file says.");
            Assert.Less(carelessSlips, rolls * careless.PaperworkSlipChance * 1.3f,
                $"{careless.DisplayName}'s paperwork is far worse than their file says.");
            Assert.Greater(carelessDrums, rolls * careless.SameDrumChance * 0.7f,
                $"{careless.DisplayName} stopped running §6.1's trap.");
        }

        /// <summary>
        /// A seed reproduces a whole contract, discrepancies included. Two players on one run have to
        /// open the same wrong box on the same morning, or a shared seed is not a shared run.
        /// </summary>
        [Test]
        public void Discrepancies_AreReproducedByTheSeed()
        {
            var a = Lab(4321, 16);
            var b = Lab(4321, 16);

            string Describe(LabState lab) => string.Join("|", lab.Samples.All
                .OrderBy(s => s.Id.Value)
                .Select(s => $"{s.Id}:{s.EquipmentTag}:{s.Ambiguity}:{s.JobNumber}"));

            Assert.AreEqual(Describe(a), Describe(b),
                "Two labs on one seed produced different vials or different ambiguities.");

            string Paper(LabState lab) => string.Join("|", lab.Deliveries.Cartons
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .Select(c => c.Id + "=" + string.Join(",", c.Note.Lines.Select(l => l.TankTag + "/" + l.Sample))));

            Assert.AreEqual(Paper(a), Paper(b), "Two labs on one seed printed different delivery notes.");
        }

        // -- Registration: the #73 line ------------------------------------------------------------------

        /// <summary>
        /// Promise, and the whole settlement with #73: a vial whose label is legible cannot be
        /// registered at all. Booking-in came back for the two bottles a shift that cannot speak for
        /// themselves; if this refusal ever softens, it has come back for all sixteen.
        /// </summary>
        [Test]
        public void ALegibleVial_CannotBeRegisteredAtAll()
        {
            var lab = Lab(1234);

            var ordinary = FirstWhere(lab, s => s.Ambiguity == SampleAmbiguity.None);

            Assert.IsFalse(lab.TryRegisterSample(ordinary.Id, 0, out string refusal),
                "A bottle with a readable tag has nothing to decide, and offering the step anyway is " +
                "the keyboard #73 removed.");
            Assert.IsNotEmpty(refusal);
            Assert.AreEqual(SampleState.Unregistered, ordinary.RegisteredLine);
        }

        /// <summary>
        /// Promise: registration is a decision, not a gate. An ambiguous vial that nobody has looked at
        /// still comes out of the box, agitates, loads, runs and takes a verdict. Nothing waits.
        /// </summary>
        [Test]
        public void AnUnregisteredAmbiguousVial_IsNeverBlockedFromAnything()
        {
            var lab = FindDay(l => l.Samples.All.Any(s => s.NeedsRegistering), "an ambiguous vial");
            lab.Install(catalog.Machine("elemental"), "elemental");

            var vial = FirstWhere(lab, s => s.NeedsRegistering);
            var machine = lab.FindMachine("elemental");

            Assert.IsTrue(SampleLifecycle.TryMove(vial, SampleLocation.Held(0), out string moved), moved);
            Assert.IsTrue(SampleLifecycle.TryPrep(vial, out string prepped), prepped);

            vial.TemperatureC = machine.Def.PreheatTargetC;
            Assert.AreEqual(LoadRefusal.Accepted, machine.TryLoad(vial),
                "An instrument refused an unidentified bottle. Registration must gate nothing.");

            Assert.IsTrue(lab.Samples.FileVerdict(vial.Id, Verdict.Critical, null, lab.Day,
                                                  out string filed), filed);
        }

        /// <summary>
        /// Promise: the terminal is where a registration is made, and it is a §3.1 request like every
        /// other — validated by the host, refused with a sentence, never computed by the screen.
        /// </summary>
        [Test]
        public void TheTerminal_RegistersAgainstANoteLine()
        {
            var lab = FindDay(
                l => l.Samples.All.Any(s => s.NeedsRegistering &&
                                            (l.Deliveries.ByJobNumber(s.JobNumber)?.Note?.Count ?? 0) >= 2),
                "an ambiguous vial on a note with more than one line");

            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();

            var vial = FirstWhere(lab, s => s.NeedsRegistering &&
                                            (lab.Deliveries.ByJobNumber(s.JobNumber)?.Note?.Count ?? 0) >= 2);
            var note = lab.Deliveries.ByJobNumber(vial.JobNumber).Note;

            var accepted = executor.Execute(actor, LabCommand.RegisterSample(vial.Id, 1));
            Assert.IsTrue(accepted.Accepted, accepted.Refusal);
            Assert.AreEqual(1, vial.RegisteredLine);
            Assert.AreEqual(note.Lines[1].TankTag, vial.RegisteredTag);

            var cannot = executor.Execute(
                actor, LabCommand.RegisterSample(vial.Id, SampleState.CannotTell));
            Assert.IsTrue(cannot.Accepted, cannot.Refusal);
            Assert.AreEqual(SampleState.CannotTell, vial.RegisteredLine);
            Assert.IsNull(vial.RegisteredTag, "Recording 'cannot tell' must not leave a tank named.");

            var offTheEnd = executor.Execute(actor, LabCommand.RegisterSample(vial.Id, note.Count + 4));
            Assert.IsFalse(offTheEnd.Accepted, "A line that is not on the page must be refused.");
            Assert.IsNotEmpty(offTheEnd.Refusal);
        }

        /// <summary>One player, hands the test controls, standing nowhere in particular.</summary>
        private sealed class TestActor : ILabActor
        {
            public ulong ClientId => 0;
            public string DisplayName => "test";

            /// <summary>False, so reach is never checked — these tests are about the rule, not the room.</summary>
            public bool HasPosition => false;

            public Vector3 Position => Vector3.zero;
            public LabGrip Grip { get; private set; } = LabGrip.Empty;
            public void SetGrip(LabGrip grip) => Grip = grip;
        }

        // -- Scoring a registration ----------------------------------------------------------------------

        /// <summary>
        /// The four ways a decision can turn out, asserted directly on the rule. Each row is a claim
        /// about what the customer receives, and the money follows from it in
        /// <c>ConsequenceResolver</c>.
        /// </summary>
        [Test]
        public void Scoring_MatchesWhatTheCustomerActuallyReceives()
        {
            var smudged = new SampleState { Ambiguity = SampleAmbiguity.UnreadableLabel };

            Assert.AreEqual(RegistrationOutcome.Unregistered,
                DeliveryReconciliation.Score(smudged, "W1 QUENCH 1", false),
                "Nobody identified the bottle, so the report names no tank.");

            smudged.RegisteredLine = 2;
            smudged.RegisteredTag = "W2 BATH A";
            Assert.AreEqual(RegistrationOutcome.WrongTank,
                DeliveryReconciliation.Score(smudged, "W1 QUENCH 1", false));

            smudged.RegisteredTag = "W1 QUENCH 1";
            Assert.AreEqual(RegistrationOutcome.Correct,
                DeliveryReconciliation.Score(smudged, "W1 QUENCH 1", false));

            var duplicate = new SampleState
            {
                Ambiguity = SampleAmbiguity.DuplicateClaim,
                RegisteredLine = 0,
                RegisteredTag = "W1 QUENCH 1"
            };

            Assert.AreEqual(RegistrationOutcome.Correct,
                DeliveryReconciliation.Score(duplicate, "W1 QUENCH 1", false),
                "Two genuine draws named as two draws is the right answer.");
            Assert.AreEqual(RegistrationOutcome.MissedSplitDraw,
                DeliveryReconciliation.Score(duplicate, "W1 QUENCH 1", true),
                "One drum certified as two independent draws is §6.1's trap, sprung.");

            duplicate.RegisteredLine = SampleState.CannotTell;
            duplicate.RegisteredTag = null;

            Assert.AreEqual(RegistrationOutcome.Correct,
                DeliveryReconciliation.Score(duplicate, "W1 QUENCH 1", true),
                "Refusing to separate one drum is the right answer and has to be paid for, or " +
                "'cannot tell' is a free move nobody ever has to earn.");
            Assert.AreEqual(RegistrationOutcome.ImaginedSplitDraw,
                DeliveryReconciliation.Score(duplicate, "W1 QUENCH 1", false),
                "Calling two honest draws inseparable is wrong, and has to cost something small.");

            Assert.AreEqual(RegistrationOutcome.NotAmbiguous,
                DeliveryReconciliation.Score(new SampleState(), "W1 QUENCH 1", false),
                "A legible bottle is never scored on a decision nobody was asked to make.");
        }

        /// <summary>
        /// Promise from the acceptance list: filing a verdict on a mis-registered sample carries a real
        /// consequence. It joins §5.4's existing path rather than running beside it — the same report,
        /// the same day, with the payout withheld and the sentence saying why.
        /// </summary>
        [Test]
        public void AMisregisteredVerdict_LosesItsPayoutAndSaysSo()
        {
            var lab = FindDay(HasSmudgedLabelWithAChoice,
                              "a vial with an unreadable label on a note listing more than one tank");

            var vial = FirstWhere(lab, s =>
            {
                if (s.Ambiguity != SampleAmbiguity.UnreadableLabel) return false;
                var candidate = lab.Deliveries.ByJobNumber(s.JobNumber)?.Note;
                var actual = lab.Samples.PeekTruthForDebugging(s.Id);
                return candidate != null && candidate.Lines.Any(
                    l => !string.Equals(l.TankTag, actual.TrueTankTag, StringComparison.Ordinal));
            });

            var truth = lab.Samples.PeekTruthForDebugging(vial.Id);
            var note = lab.Deliveries.ByJobNumber(vial.JobNumber).Note;

            int wrongLine = -1;
            for (int i = 0; i < note.Count; i++)
            {
                if (!string.Equals(note.Lines[i].TankTag, truth.TrueTankTag, StringComparison.Ordinal))
                {
                    wrongLine = i;
                    break;
                }
            }

            Assert.GreaterOrEqual(wrongLine, 0, "Fixture note has only one tank on it.");
            Assert.IsTrue(lab.TryRegisterSample(vial.Id, wrongLine, out string refusal), refusal);

            var report = Settle(lab, vial, Verdict.Normal);

            Assert.IsTrue(report.Misattributed,
                "A report filed against a tank the oil never came from has to be marked as one.");
            Assert.AreEqual(RegistrationOutcome.WrongTank, report.Registration);
            Assert.LessOrEqual(report.MoneyDelta, 0f,
                "The lab cannot be paid for a correct reading sent to the wrong address.");
            Assert.IsFalse(report.IsGood, "A misattributed report is not work to be pleased with.");
            StringAssert.Contains(truth.TrueTankTag, report.Headline,
                "The player has to be told which tank it really was, or they learn nothing.");
        }

        /// <summary>
        /// The other side of the same coin: running both halves of a duplicated claim, seeing they
        /// agree, and recording that they cannot be separated is the §6.1 catch — and it pays.
        /// </summary>
        [Test]
        public void CatchingTheSameDrumTrap_Pays()
        {
            // A healthy one, so the payout the bonus rides on is §5.4's routine payout rather than the
            // cost of a missed fault. Catching the trap does not make a missed fault cheaper and is
            // not meant to.
            var lab = FindDay(l => l.Samples.All.Any(s => IsSplit(l, s) && IsHealthy(l, s)),
                              "a healthy pair of vials drawn from one drum");

            var vial = FirstWhere(lab, s => IsSplit(lab, s) && IsHealthy(lab, s));
            Assert.IsTrue(lab.TryRegisterSample(vial.Id, SampleState.CannotTell, out string refusal),
                          refusal);

            var report = Settle(lab, vial, Verdict.Normal);

            Assert.AreEqual(RegistrationOutcome.Correct, report.Registration);
            Assert.IsFalse(report.Misattributed);
            Assert.Greater(report.MoneyDelta, lab.Tuning.BasePayout,
                "Catching one drum sold as two draws has to be worth more than filing an ordinary " +
                "verdict, or nobody will spend the second instrument run to find it.");
            StringAssert.Contains("drum", report.Headline);
        }

        /// <summary>
        /// File a verdict and run the days out until it comes due, then hand back its report. The
        /// §5.4 delay is the point of the mechanic and is not short-circuited here.
        /// </summary>
        private static ConsequenceReport Settle(LabState lab, SampleState vial, Verdict verdict)
        {
            Assert.IsTrue(lab.Samples.FileVerdict(vial.Id, verdict, null, lab.Day, out string refusal),
                          refusal);

            for (int guard = 0; guard < 30; guard++)
            {
                var reports = lab.EndDay();
                foreach (var report in reports)
                {
                    if (report.Sample == vial.Id) return report;
                }

                if (!lab.BeginDay()) break;
            }

            Assert.Fail("The verdict never came due.");
            return null;
        }

        // -- Persistence ---------------------------------------------------------------------------------

        /// <summary>
        /// A decision made on day 3 has to still be the decision scored on day 9, across a quit. Both
        /// halves have to survive: what the player recorded, and where the oil actually came from.
        /// Losing either would make the §5.4 consequence evaporate over a CONTINUE.
        /// </summary>
        [Test]
        public void RegistrationAndProvenance_SurviveASaveAndReload()
        {
            var lab = FindDay(HasSmudgedLabel, "a vial with an unreadable label");

            var vial = FirstWhere(lab, s => s.Ambiguity == SampleAmbiguity.UnreadableLabel);
            var truth = lab.Samples.PeekTruthForDebugging(vial.Id);
            string trueTank = truth.TrueTankTag;

            Assert.IsTrue(lab.TryRegisterSample(vial.Id, 0, out string refusal), refusal);
            string registered = vial.RegisteredTag;

            lab.EndDay();

            var saved = RunSnapshotCapture.Of(lab);
            string payload = RunSnapshotCodec.Encode(saved);
            Assert.IsTrue(RunSnapshotCodec.TryDecode(payload, out var decoded, out string decodeRefusal),
                          decodeRefusal);
            Assert.IsTrue(RunSnapshotRestore.TryRebuild(decoded, catalog, out var restored,
                                                        out string rebuildRefusal), rebuildRefusal);

            Assert.IsTrue(restored.Samples.TryGet(vial.Id, out var reloaded),
                          "The vial itself did not survive the round trip.");

            Assert.AreEqual(SampleAmbiguity.UnreadableLabel, reloaded.Ambiguity,
                "The label healed itself over a save, and with it the whole decision.");
            Assert.AreEqual(0, reloaded.RegisteredLine);
            Assert.AreEqual(registered, reloaded.RegisteredTag);

            var reloadedTruth = restored.Samples.PeekTruthForDebugging(vial.Id);
            Assert.AreEqual(trueTank, reloadedTruth.TrueTankTag,
                "Where the oil came from was lost, so nothing could be scored against it.");
        }

        /// <summary>
        /// The pairing behind §6.1's trap has to survive a save too, or a continued run quietly
        /// forgives a customer who bottled one drum twice.
        /// </summary>
        [Test]
        public void TheSameDrumPairing_SurvivesASaveAndReload()
        {
            var lab = FindDay(SameDrumSomewhere, "two vials drawn from one drum");

            var vial = FirstWhere(lab, s => IsSplit(lab, s));
            var twinId = lab.Samples.PeekTruthForDebugging(vial.Id).SameDrumAs;

            lab.EndDay();

            var saved = RunSnapshotCapture.Of(lab);
            Assert.IsTrue(RunSnapshotCodec.TryDecode(RunSnapshotCodec.Encode(saved), out var decoded,
                                                     out string decodeRefusal), decodeRefusal);
            Assert.IsTrue(RunSnapshotRestore.TryRebuild(decoded, catalog, out var restored,
                                                        out string rebuildRefusal), rebuildRefusal);

            var reloaded = restored.Samples.PeekTruthForDebugging(vial.Id);
            Assert.IsNotNull(reloaded);
            Assert.IsTrue(reloaded.DrawnFromOneDrum, "The split draw was forgiven by a quit.");
            Assert.AreEqual(twinId, reloaded.SameDrumAs);
        }
    }
}
