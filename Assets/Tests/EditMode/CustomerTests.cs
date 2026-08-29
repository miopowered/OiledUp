using System;
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
    /// Guards #29: customers who send work and the delivery notes their cartons arrive with. The
    /// promise underneath every test here is the one <c>CustomerDef</c>'s own doc comment makes —
    /// "customer identity is not flavour". A sender narrows what a vial can be before an instrument
    /// has run, and that only holds if six distinct firms actually exist, each with its own oils and
    /// sites, and if the paperwork they generate is internally consistent day to day.
    /// <para>
    /// #32 is what later lets a note disagree with its carton and lets one drum answer for several
    /// tanks. Two tests here (<see cref="DeliveryNote_CanExpressALineNoVialAnswered"/> and
    /// <see cref="CountFor_CountsTwoLinesThatNameTheSameTank"/>) exist to prove that shape is already
    /// expressible on <c>DeliveryNote</c> today, without anything yet generating it.
    /// </para>
    /// </summary>
    public sealed class CustomerTests
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

        private static ContractPlan PlanOf(int days, int samplesPerDay, string[] profileIds,
                                           float daySeconds = 600f, float healthyChance = 0.3f)
        {
            var plan = new ContractPlan { Id = "test", DisplayName = "Test", Days = new List<DayPlan>() };
            for (int i = 0; i < days; i++)
            {
                plan.Days.Add(new DayPlan
                {
                    SampleCount = samplesPerDay,
                    ProfileIds = profileIds,
                    BorderlineCount = 0,
                    HealthyChance = healthyChance,
                    DaySeconds = daySeconds
                });
            }
            return plan;
        }

        // -- The roster ---------------------------------------------------------------------------------

        /// <summary>
        /// #29's acceptance criteria: at least six customers exist, and every one of them actually
        /// runs something. A customer with an empty oils list could never be picked by
        /// <c>LabState.PickSender</c> and would sit in the catalog as dead flavour text nobody ever
        /// sees a vial from.
        /// </summary>
        [Test]
        public void SixOrMoreCustomers_EachRunAtLeastOneOil()
        {
            Assert.GreaterOrEqual(catalog.Customers.Count, 6, "#29 asks for at least six customers.");

            foreach (var customer in catalog.Customers)
            {
                Assert.IsNotNull(customer, "A null slipped into the catalog's customer list.");
                Assert.IsNotEmpty(customer.Oils,
                    $"{customer.DisplayName} runs nothing, so it could never be picked as a sender.");
            }
        }

        /// <summary>
        /// The point of naming a sender is that the name narrows the diagnosis before an instrument
        /// runs. Two customers with an identical set of oils would give the player nothing the
        /// profile alone did not already give them — the sender would be decoration rather than
        /// evidence.
        /// </summary>
        [Test]
        public void NoTwoCustomers_RunTheSameSetOfOils()
        {
            var customers = catalog.Customers;
            Assert.GreaterOrEqual(customers.Count, 2, "Fixture needs at least two customers to compare.");

            for (int i = 0; i < customers.Count; i++)
            {
                var a = new HashSet<string>(customers[i].Oils.Select(o => o.Id));
                for (int j = i + 1; j < customers.Count; j++)
                {
                    var b = new HashSet<string>(customers[j].Oils.Select(o => o.Id));
                    Assert.IsFalse(a.SetEquals(b),
                        $"{customers[i].DisplayName} and {customers[j].DisplayName} run exactly the " +
                        "same oils — the sender would decorate a diagnosis rather than narrow it.");
                }
            }
        }

        /// <summary>
        /// A tank tag is built from a customer's own sites (<c>EquipmentTags.For(CustomerDef, ...)</c>).
        /// A customer with no sites falls back to the anonymous plant list and stops being
        /// identifiable from its own vials; a blank site would print a blank plant code onto a label.
        /// </summary>
        [Test]
        public void EveryCustomer_HasAtLeastOneNonEmptySite()
        {
            foreach (var customer in catalog.Customers)
            {
                Assert.IsNotEmpty(customer.Sites, $"{customer.DisplayName} has no sites to draw a tag from.");
                foreach (var site in customer.Sites)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(site),
                        $"{customer.DisplayName} has a blank site.");
                }
            }
        }

        // -- Arrivals and paperwork -----------------------------------------------------------------------

        /// <summary>
        /// Every sample that arrives while the catalog has customers must carry one, plus the job
        /// number off its delivery note (#29). A sample with a null customer or job number is a vial
        /// with no sender to reason about and no note to check it against.
        /// </summary>
        [Test]
        public void DaysArrivals_AllCarryACustomerAndJobNumber()
        {
            Assert.IsNotEmpty(catalog.Customers, "Fixture needs customers or this proves nothing.");

            var lab = new LabState(catalog,
                PlanOf(1, 12, new[] { "hardening_oil_general", "quench_oil_cold" }), 101);
            lab.BeginDay();

            var arrivals = lab.OpenSamples();
            Assert.IsNotEmpty(arrivals, "Fixture generated no samples.");

            foreach (var sample in arrivals)
            {
                Assert.IsNotNull(sample.Customer, $"{sample.EquipmentTag} arrived with no sender.");
                Assert.IsFalse(string.IsNullOrEmpty(sample.JobNumber),
                    $"{sample.EquipmentTag} arrived with no job number.");
            }
        }

        /// <summary>
        /// Today, a note is only ever built from the carton that was actually packed — #32 is what
        /// later lets the two disagree. The sum of every note's line count must equal the number of
        /// samples the day actually generated, or the paperwork and the crate are already lying to
        /// each other before #32 gives them a reason to.
        /// </summary>
        [Test]
        public void NoteLines_MatchTheCartonExactlyToday()
        {
            var lab = new LabState(catalog,
                PlanOf(1, 15, new[] { "hardening_oil_general", "quench_oil_cold", "quench_oil_martempering" }),
                202);
            lab.BeginDay();

            int arrived = lab.Samples.All.Count;
            int noted = lab.Notes.Sum(n => n.Count);

            Assert.Greater(arrived, 0, "Fixture generated no samples.");
            Assert.AreEqual(arrived, noted,
                "The day's notes must account for every sample that arrived, no more and no fewer.");
        }

        /// <summary>
        /// A tank tag on a note is built from its sender's own sites, and the profile on the line is
        /// one that sender actually runs (<c>EquipmentTags.For</c>, <c>CustomerDef.Runs</c>). If a
        /// note ever named a site the customer does not own, or a fluid it does not run, a sender's
        /// history would stop being evidence — the player could not trust that "Kessler's sites all
        /// read W1-W3" the way §6.1 needs them to.
        /// </summary>
        [Test]
        public void EveryNoteLine_NamesASiteAndOilItsCustomerActuallyHas()
        {
            var lab = new LabState(catalog,
                PlanOf(1, 20, new[] { "quench_oil_vacuum", "quench_oil_accelerated" }), 303);
            lab.BeginDay();

            Assert.IsNotEmpty(lab.Notes, "Fixture generated no notes.");

            foreach (var note in lab.Notes)
            {
                Assert.IsNotNull(note.Customer,
                    "Every profile in this fixture is run by a customer, so a note should have one.");

                foreach (var line in note.Lines)
                {
                    Assert.IsTrue(
                        note.Customer.Sites.Any(site => line.TankTag.StartsWith(site, StringComparison.Ordinal)),
                        $"'{line.TankTag}' does not start with any site {note.Customer.DisplayName} owns.");
                    Assert.IsTrue(note.Customer.Runs(line.Profile),
                        $"{note.Customer.DisplayName} does not run '{line.Profile?.Id}', but a line on " +
                        "their own note claims they sent it.");
                }
            }
        }

        /// <summary>
        /// One note per sender per day (<c>LabState.NoteFor</c>). A sample from the same customer on
        /// the same morning must share that note's job number; a sample from a different customer
        /// must not — otherwise a job number stops identifying a single delivery.
        /// </summary>
        [Test]
        public void OneNotePerSender_SameCustomerSharesAJobNumberAndDifferentCustomersDoNot()
        {
            var lab = new LabState(catalog,
                PlanOf(1, 20, new[] { "quench_oil_vacuum", "quench_oil_accelerated" }), 4242);
            lab.BeginDay();

            Assert.GreaterOrEqual(lab.Notes.Count, 2,
                "Fixture needs at least two different senders to appear in one day, or this proves nothing.");

            foreach (var note in lab.Notes)
            {
                foreach (var line in note.Lines)
                {
                    Assert.IsTrue(lab.Samples.TryGet(line.Sample, out var sample),
                        "A note line pointed at a sample that does not exist.");
                    Assert.AreEqual(note.JobNumber, sample.JobNumber,
                        "A sample's job number must match the one note issued for its sender today.");
                }
            }

            var jobNumbers = lab.Notes.Select(n => n.JobNumber).ToList();
            Assert.AreEqual(jobNumbers.Count, jobNumbers.Distinct().Count(),
                "Two different senders' deliveries collapsed onto the same job number.");
        }

        /// <summary>
        /// A run seed has to reproduce a whole contract's senders, not just its chemistry
        /// (<c>LabState.PickSender</c>'s own doc comment says as much). Two labs built from the same
        /// seed must pick the same customers and mint the same job numbers, in the same arrival
        /// order, or two players sharing a seed would see different names on the same day.
        /// </summary>
        [Test]
        public void Generation_PicksTheSameCustomersAndJobNumbersForTheSameSeed()
        {
            var plan = PlanOf(1, 16, new[]
            {
                "hardening_oil_general", "quench_oil_cold", "quench_oil_martempering", "quench_oil_accelerated"
            });

            var labA = new LabState(catalog, plan, 555);
            var labB = new LabState(catalog, plan, 555);

            labA.BeginDay();
            labB.BeginDay();

            var a = labA.Samples.All.OrderBy(s => s.Id.Value)
                .Select(s => (s.Customer != null ? s.Customer.Id : null, s.JobNumber)).ToList();
            var b = labB.Samples.All.OrderBy(s => s.Id.Value)
                .Select(s => (s.Customer != null ? s.Customer.Id : null, s.JobNumber)).ToList();

            Assert.IsNotEmpty(a, "Fixture generated no samples.");
            CollectionAssert.AreEqual(a, b,
                "Two labs seeded identically produced different senders or job numbers — a shared " +
                "seed no longer reproduces a shared contract.");
        }

        // -- The note's shape, by hand --------------------------------------------------------------------

        /// <summary>
        /// #32 needs a note that disagrees with the carton: a line the paperwork claims that no vial
        /// answered. <see cref="DeliveryNote.Line.Arrived"/> must read false for a line built with
        /// <see cref="SampleId.None"/>, and the line still has to count towards
        /// <see cref="DeliveryNote.Count"/> — it is a real claim on paper, not something the note
        /// never mentioned.
        /// </summary>
        [Test]
        public void DeliveryNote_CanExpressALineNoVialAnswered()
        {
            var customer = catalog.Customer("vogel_getriebe");
            var profile = catalog.Profile("quench_oil_cold");
            Assert.IsNotNull(customer, "Fixture assumes vogel_getriebe exists.");
            Assert.IsNotNull(profile, "Fixture assumes quench_oil_cold exists.");

            var note = new DeliveryNote(customer, "VG-00001", 1);
            note.Add("NW1 QUENCH 1", profile, SampleId.None);

            Assert.AreEqual(1, note.Count, "A missing-sample line must still count as a line on the note.");
            Assert.IsFalse(note.Lines[0].Arrived,
                "A line built with SampleId.None must read as not arrived.");
        }

        /// <summary>
        /// §6.1's same-drum trap, as it appears on paper: two lines naming the same tank id.
        /// <see cref="DeliveryNote.CountFor"/> is what #32 will use to notice it, so it has to count
        /// both lines rather than treating the second as overwriting the first.
        /// </summary>
        [Test]
        public void CountFor_CountsTwoLinesThatNameTheSameTank()
        {
            var customer = catalog.Customer("kessler_haerterei");
            var profile = catalog.Profile("quench_oil_cold");
            Assert.IsNotNull(customer, "Fixture assumes kessler_haerterei exists.");
            Assert.IsNotNull(profile, "Fixture assumes quench_oil_cold exists.");

            var note = new DeliveryNote(customer, "KH-00001", 1);
            note.Add("W1 QUENCH 1", profile, new SampleId(1));
            note.Add("W1 QUENCH 1", profile, new SampleId(2));

            Assert.AreEqual(2, note.CountFor("W1 QUENCH 1"),
                "Two lines naming the same tank must both be counted.");
        }

        // -- Persistence (#49) ----------------------------------------------------------------------------

        /// <summary>
        /// A field <c>RunSnapshotCapture</c> does not carry silently resets on CONTINUE. Follows the
        /// round trip pattern in <c>RunSnapshotTests</c> — capture, encode, decode, rebuild — and
        /// checks that a sample's sender and job number are still attached afterwards.
        /// </summary>
        [Test]
        public void CustomerAndJobNumber_SurviveASaveAndReload()
        {
            var lab = new LabState(catalog,
                PlanOf(1, 8, new[] { "hardening_oil_general", "quench_oil_cold" }), 909);
            lab.BeginDay();

            var original = lab.OpenSamples().FirstOrDefault(s => s.Customer != null);
            Assert.IsNotNull(original, "Fixture generated no sample with a sender.");

            lab.EndDay();

            var saved = RunSnapshotCapture.Of(lab);
            string payload = RunSnapshotCodec.Encode(saved);
            Assert.IsTrue(RunSnapshotCodec.TryDecode(payload, out var decoded, out var decodeRefusal),
                          decodeRefusal);
            Assert.IsTrue(RunSnapshotRestore.TryRebuild(decoded, catalog, out var restored, out var refusal),
                          refusal);

            Assert.IsTrue(restored.Samples.TryGet(original.Id, out var reloaded),
                "The sample itself did not survive the round trip.");

            Assert.IsNotNull(reloaded.Customer, "The sender reset to null across a save.");
            Assert.AreEqual(original.Customer.Id, reloaded.Customer.Id,
                "A different customer came back than the one that actually sent this sample.");
            Assert.AreEqual(original.JobNumber, reloaded.JobNumber,
                "The job number reset across a save — CONTINUE would silently lose it.");
        }
    }
}
