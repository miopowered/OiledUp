using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// The words the lab says when it refuses, and the words the reference books contribute of their
    /// own (#55).
    /// <para>
    /// <c>LocalisationTests</c> guards the primitive; these guard the two places whose text carries
    /// the most weight. Refusals are hard rule 3's mechanism — "never punish something the player
    /// could not have checked" is honoured by the sentence that says what to check — so a refusal
    /// that arrives half-built is not a cosmetic fault, it is the rule failing quietly. The books are
    /// the other half of the same promise, and the one place where prose sits next to figures that
    /// must never be run through a translation table.
    /// </para>
    /// </summary>
    public sealed class LabTextTests
    {
        /// <summary>A <c>{name}</c> nobody filled in. See <c>Loc.Fill</c>: the formatter leaves it
        /// visible on purpose, which makes it findable from here.</summary>
        private static readonly Regex UnfilledPlaceholder = new(@"\{[a-z_]+\}", RegexOptions.Compiled);

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
            Loc.UseEnglish();

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

        // -- Doubles -----------------------------------------------------------------------------

        private sealed class TestActor : ILabActor
        {
            public TestActor(ulong clientId = 0) => ClientId = clientId;

            public ulong ClientId { get; }
            public string DisplayName => $"player-{ClientId}";
            public bool HasPosition { get; set; } = true;
            public Vector3 Position { get; set; } = Vector3.zero;
            public LabGrip Grip { get; private set; } = LabGrip.Empty;
            public void SetGrip(LabGrip grip) => Grip = grip;
        }

        private sealed class TestStations : ILabStations
        {
            private readonly Dictionary<string, Vector3> placed = new();

            public void Place(string fixtureId, Vector3 at) => placed[fixtureId] = at;

            public bool TryLocate(string fixtureId, out Vector3 position) =>
                placed.TryGetValue(fixtureId, out position);
        }

        // -- Fixtures ----------------------------------------------------------------------------

        private static ContractPlan OneDay() => new()
        {
            Id = "test",
            DisplayName = "Test",
            Days = new List<DayPlan>
            {
                new()
                {
                    SampleCount = 4,
                    ProfileIds = new[] { "quench_oil_cold" },
                    BorderlineCount = 0,
                    HealthyChance = 0.3f,
                    DaySeconds = 600f
                }
            }
        };

        private LabState NewLab()
        {
            var lab = new LabState(catalog, OneDay(), 4242);
            lab.Install(catalog.Machine("elemental"), "elemental");
            lab.Install(catalog.Machine("viscometer"), "viscometer");

            lab.Deliveries.ArrivalShiftFraction = 0f;
            lab.BeginDay();
            lab.Deliveries.Capacity = 64;
            lab.Tick(0.01f);

            foreach (var carton in lab.Deliveries.Cartons)
                Assert.IsTrue(lab.Deliveries.TryOpen(carton.Id, 0, out string refusal), refusal);

            return lab;
        }

        private static void AssertFilledIn(LabCommandResult result, string what)
        {
            Assert.IsFalse(result.Accepted, $"{what} was supposed to be refused.");
            Assert.IsNotEmpty(result.Refusal, $"{what} was refused without saying why.");
            Assert.IsFalse(UnfilledPlaceholder.IsMatch(result.Refusal),
                $"{what} refused with a placeholder still in it: \"{result.Refusal}\". The name in " +
                "the call's Format(...) does not match the one in the English.");
        }

        // -----------------------------------------------------------------------------------------
        // Refusals
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a refusal that names something arrives with the something in it.
        /// <para>
        /// Every refusal below used to be an interpolated string, where a wrong name was a compile
        /// error. They are now templates filled by name, where a wrong name is a sentence with
        /// <c>{instrument}</c> printed in the middle of it — visible rather than silent (that is
        /// <c>Loc.Fill</c> doing its job), but only if somebody is looking. This is somebody looking.
        /// </para>
        /// </summary>
        [Test]
        public void EveryRefusalThatNamesSomething_ArrivesWithTheSomethingInIt()
        {
            var lab = NewLab();
            var executor = new LabCommandExecutor(lab);
            var actor = new TestActor();

            AssertFilledIn(executor.Execute(actor, LabCommand.StartRun("elemental")),
                "Starting an empty instrument");

            // Straight out of the carton: never agitated, so the load is refused on §4.5.
            var sample = lab.OpenSamples().First();
            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeVial(sample.Id)).Accepted);
            AssertFilledIn(executor.Execute(actor, LabCommand.LoadMachine("elemental")),
                "Loading a sample that has settled out");

            Assert.IsTrue(SampleLifecycle.TryPrep(sample, out string prep), prep);

            // The viscometer wants 10 ml and a warm vial; this one has neither.
            float volume = sample.VolumeMl;
            sample.VolumeMl = 1f;
            AssertFilledIn(executor.Execute(actor, LabCommand.LoadMachine("viscometer")),
                "Loading a vial with too little left in it");

            sample.VolumeMl = volume;
            sample.TemperatureC = 20f;
            AssertFilledIn(executor.Execute(actor, LabCommand.LoadMachine("viscometer")),
                "Loading a vial that is still cold");

            // The elemental analyser wants neither, so this one goes in — and once it is in, it
            // cannot be picked up off the bench.
            Assert.IsTrue(executor.Execute(actor, LabCommand.LoadMachine("elemental")).Accepted);
            AssertFilledIn(executor.Execute(actor, LabCommand.TakeVial(sample.Id)),
                "Taking a vial that is inside an instrument");

            // Occupied, then busy, both against the second vial.
            var second = lab.OpenSamples().First(s => s.Id != sample.Id);
            Assert.IsTrue(executor.Execute(actor, LabCommand.TakeVial(second.Id)).Accepted);
            Assert.IsTrue(SampleLifecycle.TryPrep(second, out string prepSecond), prepSecond);

            AssertFilledIn(executor.Execute(actor, LabCommand.LoadMachine("elemental")),
                "Loading an instrument that already has a vial in it");

            Assert.IsTrue(executor.Execute(actor, LabCommand.StartRun("elemental")).Accepted);
            AssertFilledIn(executor.Execute(actor, LabCommand.LoadMachine("elemental")),
                "Loading a running instrument");
            AssertFilledIn(executor.Execute(actor, LabCommand.FlushMachine("elemental")),
                "Flushing a running instrument");

            // Money. The account will not stretch to either of these.
            AssertFilledIn(executor.Execute(actor, LabCommand.OrderSolvent(100000)),
                "Ordering solvent the lab cannot afford");
            AssertFilledIn(executor.Execute(actor, LabCommand.OrderStandards(100000)),
                "Ordering standards the lab cannot afford");
        }

        /// <summary>
        /// Promise: a refusal about where you are standing names the place.
        /// <para>
        /// Hard rule 3. "You are not standing there" is not actionable across a room with four
        /// instruments and a delivery bay in it; the sentence has to identify the fixture, which is
        /// why each one is a complete line rather than a shared template with a noun appended.
        /// </para>
        /// </summary>
        [Test]
        public void AReachRefusal_NamesWhatYouAreNotStandingAt()
        {
            var lab = NewLab();
            var stations = new TestStations();
            var executor = new LabCommandExecutor(lab, stations);
            var actor = new TestActor();

            stations.Place("elemental", new Vector3(50f, 0f, 0f));
            var away = executor.Execute(actor, LabCommand.StartRun("elemental"));
            AssertFilledIn(away, "Operating an instrument from across the room");
            StringAssert.Contains(catalog.Machine("elemental").DisplayName, away.Refusal,
                "A reach refusal has to say which instrument you are not standing at.");

            var carton = lab.Deliveries.Cartons.First(c => !string.IsNullOrEmpty(c.JobNumber));
            stations.Place(carton.Id, new Vector3(50f, 0f, 0f));
            var box = executor.Execute(actor, LabCommand.OpenCarton(carton.Id));
            AssertFilledIn(box, "Opening a carton from across the room");
            StringAssert.Contains(carton.JobNumber, box.Refusal,
                "A reach refusal about a box has to say which box.");
        }

        /// <summary>
        /// Promise: a refusal is translatable, not merely looked up.
        /// <para>
        /// This is the half of #55 that cannot be retrofitted. A translation that moves the subject
        /// to the front has to work, because word order is not universal — and it only works if the
        /// refusal was a whole sentence with named holes rather than a reason with a noun glued on.
        /// A refusal assembled by concatenation would pass every other test here and fail this one.
        /// </para>
        /// </summary>
        [Test]
        public void ATranslatedRefusal_CanPutThePlaceFirst()
        {
            var lab = NewLab();
            var stations = new TestStations();
            var executor = new LabCommandExecutor(lab, stations);
            var actor = new TestActor();

            stations.Place("elemental", new Vector3(50f, 0f, 0f));

            Loc.Use("test", new Dictionary<string, string>
            {
                ["refusal.not_at_instrument"] = "{instrument}: you are nowhere near it."
            });

            var away = executor.Execute(actor, LabCommand.StartRun("elemental"));

            Assert.IsFalse(away.Accepted);
            Assert.AreEqual($"{catalog.Machine("elemental").DisplayName}: you are nowhere near it.",
                away.Refusal,
                "The refusal did not follow the translation, so the instrument is still being " +
                "pasted onto a fixed English fragment somewhere.");
        }

        // -----------------------------------------------------------------------------------------
        // The reference books
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: every page the books print is finished text.
        /// <para>
        /// The manuals are the one place prose sits directly against figures out of the content
        /// tables — run times, carryover percentages, threshold limits. A page that printed
        /// <c>{percent}</c> would be a manual that disagrees with the chemistry, which the type
        /// comment on <c>BookContent</c> calls worse than no book at all.
        /// </para>
        /// </summary>
        [Test]
        public void EveryBookPage_IsFinishedText()
        {
            var pages = new List<(string Where, BookPage Page)>();

            foreach (var machine in catalog.Machines)
            {
                foreach (var page in BookContent.Build(BookKind.MachineManual, machine, catalog))
                    pages.Add(($"{machine.Id} manual", page));
            }

            foreach (var kind in new[]
                     {
                         BookKind.ElementIndex, BookKind.DiagnosticGuide, BookKind.ThresholdTables
                     })
            {
                foreach (var page in BookContent.Build(kind, null, catalog))
                    pages.Add((kind.ToString(), page));
            }

            foreach (var page in BookContent.ShiftBrief()) pages.Add(("shift brief", page));

            Assert.IsNotEmpty(pages);

            foreach (var (where, page) in pages)
            {
                Assert.IsFalse(UnfilledPlaceholder.IsMatch(page.Title ?? string.Empty),
                    $"{where}: page title \"{page.Title}\" has an unfilled placeholder.");
                Assert.IsFalse(UnfilledPlaceholder.IsMatch(page.Body ?? string.Empty),
                    $"{where}: page \"{page.Title}\" has an unfilled placeholder in its body.");
            }
        }

        /// <summary>
        /// Promise: the covers a player is told to look for are the covers on the books.
        /// <para>
        /// The shift brief names the three references on the rack, and <c>ReferenceBook.DisplayName</c>
        /// is the same call — so a title that stopped resolving through the same key would leave the
        /// brief pointing at a book with a different name on it. Asserted through a translation,
        /// because in English a broken lookup and a working one are indistinguishable.
        /// </para>
        /// </summary>
        [Test]
        public void RenamingABook_RenamesItInTheBriefToo()
        {
            Loc.Use("test", new Dictionary<string, string>
            {
                ["book.title_element_index"] = "The Big Book of Elements"
            });

            string title = BookContent.TitleFor(BookKind.ElementIndex, null);
            Assert.AreEqual("The Big Book of Elements", title);

            string brief = string.Join("\n", BookContent.ShiftBrief().Select(p => p.Body));
            StringAssert.Contains(title, brief,
                "The brief stopped naming the book through its own title, so the two can now drift.");
        }
    }
}
