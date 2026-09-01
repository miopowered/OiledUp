using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// The guided two-day contract and its objective card.
    ///
    /// <para>
    /// Two of these matter more than the rest.
    /// <see cref="NoObjectiveText_NamesAnElementFaultOrRootCause"/> is hard rule 1: the tutorial
    /// teaches where to look and how the room works, and the moment it names a symptom it has handed
    /// out the diagnostic table the whole game is about building for yourself — with the game's own
    /// authority behind it, which is worse than a wiki.
    /// <see cref="AttachingTheObjectives_ChangesNothingAboutTheRun"/> is the other: an objective that
    /// gated the next thing would be #73's mistake wearing a tutorial's clothes, and the only honest
    /// way to assert "it never blocks" is to show the run is identical with the card attached and
    /// without it.
    /// </para>
    /// </summary>
    public sealed class TutorialTests
    {
        private ContentSet content;
        private ContentCatalog catalog;

        private string directory;
        private RunSaveStore originalStore;
        private bool originalAuthority;
        private GameObject host;

        /// <summary>Fixtures put into the open scene by a marker test, torn down with it.</summary>
        private readonly List<GameObject> placed = new();

        [SetUp]
        public void SetUp()
        {
            content = ContentBuilder.BuildInMemory();
            catalog = ContentBuilder.BuildCatalogInMemory(content);

            originalAuthority = LabRuntime.SimulatesLocally;
            originalStore = RunSaveSlot.Store;

            // Never the player's real save. RunSaveSlot.Store is settable for exactly this.
            directory = Path.Combine(Path.GetTempPath(), "oiledup-tutorial-tests",
                                     Guid.NewGuid().ToString("N"));
            RunSaveSlot.Store = new RunSaveStore(Path.Combine(directory, "run.save"));
        }

        [TearDown]
        public void TearDown()
        {
            // Every one of these is a static, and every other test in the suite assumes single
            // player, no router, no tutorial and a fresh slot.
            LabCommands.Router = null;
            TutorialObjectives.End(TutorialObjectives.Current?.Lab);
            TutorialRun.Forget();
            RunSaveSlot.ForgetContinueRequest();
            RunSaveSlot.Store = originalStore;
            LabRuntime.SimulatesLocally = originalAuthority;

            if (host != null) Object.DestroyImmediate(host);
            host = null;

            foreach (var go in placed)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            placed.Clear();

            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch { /* a failed assertion must stay the useful failure */ }

            if (catalog != null) Object.DestroyImmediate(catalog);
            if (content == null) return;

            foreach (var o in AllDefinitions(content)) Object.DestroyImmediate(o);
            content = null;
        }

        private static IEnumerable<Object> AllDefinitions(ContentSet set) =>
            set.Elements.Values.Cast<Object>()
                .Concat(set.Causes.Values)
                .Concat(set.Profiles.Values)
                .Concat(set.Faults.Values)
                .Concat(set.Machines.Values)
                .Concat(set.Customers.Values);

        /// <summary>A tutorial lab on the tutorial's own seed, with the shipping bench installed.</summary>
        private LabState TutorialLab()
        {
            var lab = new LabState(catalog, ContractPlan.Tutorial(), ContractPlan.TutorialSeed)
            {
                MachineTimeScale = 0.001f
            };

            foreach (string id in new[] { "cooling_curve", "karl_fischer", "viscometer", "centrifuge",
                                          "elemental" })
            {
                var def = catalog.Machine(id);
                if (def != null) lab.Install(def, id);
            }

            return lab;
        }

        // -- The contract ------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a save naming the tutorial can be opened.
        /// <para>
        /// <c>RunSnapshotRestore</c> turns a null from <c>ById</c> into "this build no longer offers
        /// that contract" and refuses the whole load rather than substituting a different one under
        /// the player. A build that ships the tutorial and answers null for it is refusing a run on a
        /// lie — so <c>ById</c> has to answer for every contract the build has, whether or not that
        /// contract is one the save layer currently writes.
        /// </para>
        /// </summary>
        [Test]
        public void TheTutorialPlan_ResolvesThroughById()
        {
            var tutorial = ContractPlan.ById(ContractPlan.TutorialId);

            Assert.IsNotNull(tutorial, "The build ships the tutorial and cannot resolve its id.");
            Assert.AreEqual(ContractPlan.TutorialId, tutorial.Id);
            Assert.AreEqual(2, tutorial.Length, "The tutorial is two days.");

            Assert.IsNotNull(ContractPlan.ById(ContractPlan.DefaultId),
                "Adding a contract must not cost the shipping one.");
            Assert.AreEqual(20, ContractPlan.ById(ContractPlan.DefaultId).Length);

            Assert.IsNull(ContractPlan.ById("a_contract_that_never_existed"),
                "Null is the honest answer for an id this build does not have, and the loader " +
                "depends on getting it rather than a substitute.");
        }

        /// <summary>
        /// Promise: the tutorial survives the save format end to end. It stores its plan by id like
        /// every other run, so if <c>ById</c> ever stops answering, this is the test that says so
        /// before a player finds out by losing a run.
        /// </summary>
        [Test]
        public void TheTutorialPlan_RoundTripsThroughASave()
        {
            var lab = TutorialLab();
            lab.BeginDay();

            var snapshot = RunSnapshotCapture.Of(lab);
            Assert.AreEqual(ContractPlan.TutorialId, snapshot.ContractId,
                "A snapshot has to record which contract it is, or it cannot be rebuilt against one.");

            string payload = RunSnapshotCodec.Encode(snapshot);
            Assert.IsTrue(RunSnapshotCodec.TryDecode(payload, out var decoded, out string decodeRefusal),
                          decodeRefusal);
            Assert.IsTrue(RunSnapshotRestore.TryRebuild(decoded, catalog, out var restored,
                                                        out string refusal), refusal);

            Assert.AreEqual(ContractPlan.TutorialId, restored.Plan.Id);
            Assert.AreEqual(2, restored.Plan.Length);
            Assert.AreEqual(lab.Day, restored.Day);
            Assert.AreEqual(lab.Samples.Count, restored.Samples.Count,
                "A restored tutorial dropped a vial, which is the failure the loader refuses on.");
        }

        /// <summary>
        /// Promise: the two days are the shape the design asked for. One fluid — the one
        /// <c>ContentTables</c> already nominates as the tutorial oil — no §6.3 ambiguity budget, and
        /// a healthy rate generous enough that a first-day player is not condemning tanks all morning.
        /// <para>
        /// Not 1, though. A tutorial in which every vial is fine teaches the one lesson it is possible
        /// to teach wrongly here.
        /// </para>
        /// </summary>
        [Test]
        public void TheTutorialDays_AreOneForgivingFluidWithNoAmbiguityBudget()
        {
            var plan = ContractPlan.Tutorial();

            for (int day = 1; day <= plan.Length; day++)
            {
                var today = plan.ForDay(day);

                CollectionAssert.AreEqual(new[] { "hardening_oil_general" }, today.ProfileIds,
                    $"Day {day} is not the tutorial fluid.");

                Assert.AreEqual(0, today.BorderlineCount,
                    $"Day {day} spends §6.3's ambiguity budget on somebody who has not yet carried a " +
                    "vial across the room.");

                Assert.GreaterOrEqual(today.HealthyChance, 0.5f,
                    $"Day {day} is not a generous healthy rate.");
                Assert.Less(today.HealthyChance, 1f,
                    $"Day {day} makes every vial healthy, which teaches that everything is fine.");

                Assert.Greater(today.SampleCount, 0);
                Assert.GreaterOrEqual(today.DaySeconds, ContractPlan.DefaultDaySeconds,
                    $"Day {day} is shorter than a shipping day. §6.1 asks for generous time, and the " +
                    "truck does not arrive until a quarter of the way in.");
            }
        }

        /// <summary>
        /// Promise: the shipping contract is untouched. <c>DayPlan</c> grew a field and
        /// <c>LabState.PickSender</c> grew a filter; neither may change what the real run draws.
        /// </summary>
        [Test]
        public void TheShippingContract_StillDrawsFromEverySender()
        {
            foreach (var day in ContractPlan.Default().Days)
            {
                Assert.IsNull(day.CustomerIds,
                    "A day of the shipping contract has acquired a sender filter. Its arrivals are " +
                    "balance and this change was supposed to leave them alone.");
            }
        }

        /// <summary>
        /// Promise: the tutorial's paperwork is never wrong.
        ///
        /// <para>
        /// Six firms run the tutorial oil and they differ in how reliable their paperwork is, so
        /// without a sender filter #32's smudged labels can land on a morning whose job is teaching
        /// where the instruments are — an advanced mechanic introduced at the worst possible moment,
        /// and one whose only guaranteed answer costs 45 seconds of a shift the player is still
        /// learning to spend.
        /// </para>
        ///
        /// <para>
        /// The chosen firm's propensities are asserted rather than only its id, because that is the
        /// actual guarantee: if Vogel is ever retuned away from being the control the content tables
        /// describe, the tutorial stops being clean and this is what says so.
        /// </para>
        ///
        /// <para>
        /// A legitimate double draw can still happen — <c>DeliveryDiscrepancies.DoubleDrawChance</c>
        /// is a world constant that must never be zero — and that is fine and deliberately not
        /// excluded. Both bottles are legible, it gates nothing, and it is ordinary plant practice;
        /// §6.1's trap is the <i>same drum</i> case, which needs a propensity Vogel does not have.
        /// </para>
        /// </summary>
        [Test]
        public void TheTutorialDelivery_ComesFromTheSenderWhosePaperworkIsNeverWrong()
        {
            var sender = catalog.Customer("vogel_getriebe");
            Assert.IsNotNull(sender, "The tutorial names a firm this build does not have.");
            Assert.AreEqual(0f, sender.PaperworkSlipChance,
                "The tutorial's sender can now get its own delivery note wrong.");
            Assert.AreEqual(0f, sender.SameDrumChance,
                "The tutorial's sender can now bottle one drum as two tanks, which is §6.1's trap.");

            var lab = TutorialLab();

            for (int day = 1; day <= 2; day++)
            {
                lab.BeginDay();
                lab.EndDay();
            }

            Assert.IsNotEmpty(lab.Samples.All, "The tutorial delivered nothing to look at.");

            foreach (var sample in lab.Samples.All)
            {
                Assert.IsNotNull(sample.Customer, $"{sample.EquipmentTag} arrived from nobody.");
                Assert.AreEqual("vogel_getriebe", sample.Customer.Id,
                    $"{sample.EquipmentTag} came from a firm the tutorial did not ask for.");

                Assert.AreEqual("hardening_oil_general", sample.Profile.Id,
                    $"{sample.EquipmentTag} is not the tutorial fluid.");

                Assert.AreNotEqual(SampleAmbiguity.UnreadableLabel, sample.Ambiguity,
                    $"{sample.EquipmentTag} arrived with a label nobody can read. That is a puzzle " +
                    "with a 45-second answer, and the tutorial is not where it gets introduced.");
            }
        }

        /// <summary>
        /// Promise: the fixed seed actually fixes the run. An objective card saying "load an
        /// instrument" against a delivery that varied between attempts would be a generality rather
        /// than a guided day, and the whole point of pinning the seed is that the two days are the
        /// same two days every time.
        /// </summary>
        [Test]
        public void TheSameSeed_ProducesTheSameFirstDayTwice()
        {
            string first = DescribeFirstDay(TutorialLab());
            string second = DescribeFirstDay(TutorialLab());

            Assert.AreEqual(first, second,
                "Two tutorials on the same seed produced different mornings.");
            Assert.IsNotEmpty(first, "The comparison is empty, so it is comparing nothing.");
        }

        /// <summary>Everything about a morning that a seed is supposed to pin.</summary>
        private static string DescribeFirstDay(LabState lab)
        {
            lab.BeginDay();

            var text = new StringBuilder();
            foreach (var sample in lab.OpenSamples())
            {
                text.Append(sample.Id).Append('\t')
                    .Append(sample.EquipmentTag).Append('\t')
                    .Append(sample.Profile != null ? sample.Profile.Id : "-").Append('\t')
                    .Append(sample.Customer != null ? sample.Customer.Id : "-").Append('\t')
                    .Append(sample.JobNumber).Append('\t')
                    .Append(sample.HoursSinceOilChange.ToString("F4")).Append('\t')
                    .Append(sample.TemperatureC.ToString("F4")).Append('\t')
                    .Append(sample.VolumeMl.ToString("F4")).Append('\t')
                    .Append(sample.FieldTechNote).Append('\n');
            }
            return text.ToString();
        }

        // -- The objectives ----------------------------------------------------------------------------

        /// <summary>
        /// Promise: every objective the card draws is one a real signal will report.
        /// <para>
        /// A row asking for something no <c>LabCommandKind</c> maps to is a box the player can never
        /// tick however correctly they play — they do the thing, nothing happens, and the card is
        /// now lying about what the room did. <see cref="TutorialStep.LetARunFinish"/> is the one
        /// exception and it is deliberate: it is the only thing on the card that happens without the
        /// player, and it comes off <c>LabState.RunCompleted</c> instead.
        /// </para>
        /// </summary>
        [Test]
        public void EveryObjective_IsReachableFromARealSignal()
        {
            var fromCommands = new HashSet<TutorialStep>();
            foreach (LabCommandKind kind in Enum.GetValues(typeof(LabCommandKind)))
            {
                var step = TutorialObjectives.StepFor(kind);
                if (step != TutorialStep.None) fromCommands.Add(step);
            }

            using var objectives = new TutorialObjectives(null);

            foreach (var objective in objectives.All)
            {
                if (objective.Step == TutorialStep.LetARunFinish) continue;

                Assert.IsTrue(fromCommands.Contains(objective.Step),
                    $"{objective.Step} is on the card and no accepted command reports it, so the box " +
                    "can never be ticked.");
            }

            Assert.IsFalse(fromCommands.Contains(TutorialStep.LetARunFinish),
                "LetARunFinish is meant to come from the day cycle, not from a command.");
        }

        /// <summary>
        /// Promise: the card tracks what the lab agreed to, not what the player pressed. A refused
        /// request is not a thing that happened, and a box ticked by one would send someone off
        /// looking for the next step having never done this one.
        /// </summary>
        [Test]
        public void Objectives_TickOnAcceptedCommandsAndNotOnRefusedOnes()
        {
            using var objectives = new TutorialObjectives(null);
            var actor = new StubActor();

            LabCommands.Router = (_, _, answered) => answered?.Invoke(LabCommandResult.Ok);
            LabCommands.Send(actor, LabCommand.TakeCarton("carton-1"));

            Assert.IsTrue(objectives.IsDone(TutorialStep.TakeACarton),
                "An accepted action did not reach the card.");

            LabCommands.Router = (_, _, answered) => answered?.Invoke(LabCommandResult.No("Not there."));
            LabCommands.Send(actor, LabCommand.OpenCarton("carton-1"));

            Assert.IsFalse(objectives.IsDone(TutorialStep.OpenTheCarton),
                "A refused action ticked the card.");
        }

        /// <summary>
        /// Promise: nothing waits for anything. The card is a list, not a sequence — a player who
        /// files a verdict before they have opened a box gets both boxes ticked, in whatever order
        /// they got round to them.
        /// </summary>
        [Test]
        public void Objectives_AdvanceInWhateverOrderThePlayerWorks()
        {
            using var objectives = new TutorialObjectives(null);
            var actor = new StubActor();
            LabCommands.Router = (_, _, answered) => answered?.Invoke(LabCommandResult.Ok);

            // The last thing on day one, first, with nothing before it done.
            LabCommands.Send(actor, LabCommand.EndDay());

            Assert.IsTrue(objectives.IsDone(TutorialStep.EndTheDay));
            Assert.IsFalse(objectives.IsDone(TutorialStep.TakeACarton),
                "Ticking a later step must not tick the ones before it.");
            Assert.AreEqual(TutorialStep.TakeACarton, objectives.Next,
                "Next marks a row to read; it does not withhold the rows after it.");

            LabCommands.Send(actor, LabCommand.TakeCarton("carton-1"));
            Assert.IsTrue(objectives.IsDone(TutorialStep.TakeACarton),
                "An objective the player skipped past must still be available afterwards.");
        }

        /// <summary>
        /// Promise: day two's objectives appear when day two does. Not a gate — nothing on the card is
        /// — but a first morning that listed the blank would be pointing at an instrument nothing has
        /// been through, which teaches the opposite of the lesson.
        /// </summary>
        [Test]
        public void DayTwosObjectives_AppearWhenDayTwoBegins()
        {
            var lab = TutorialLab();
            using var objectives = new TutorialObjectives(lab);

            lab.BeginDay();

            foreach (var objective in objectives.All)
            {
                if (objective.Day <= 1) continue;
                Assert.IsFalse(objectives.IsVisible(objective),
                    $"{objective.Step} is on the card on the first morning, pointing at an " +
                    "instrument nothing has been through.");
            }

            int dayOneCount = objectives.VisibleCount;
            Assert.Greater(dayOneCount, 0);

            lab.EndDay();
            lab.BeginDay();

            Assert.Greater(objectives.VisibleCount, dayOneCount,
                "Day two began and the card did not grow.");
            Assert.AreEqual(objectives.All.Count, objectives.VisibleCount,
                "By day two every objective is on the card.");
        }

        /// <summary>
        /// Promise: a finished run ticks the one objective no command reports. Driven through a real
        /// instrument rather than by calling <c>Complete</c>, because what is under test is the
        /// subscription and not the method.
        /// </summary>
        [Test]
        public void AFinishedRun_TicksTheObjectiveNoCommandReports()
        {
            var lab = TutorialLab();
            using var objectives = new TutorialObjectives(lab);

            lab.BeginDay();

            var sample = lab.OpenSamples().First();
            Assert.IsTrue(SampleLifecycle.TryMove(sample, SampleLocation.OnSurface("bench", 0),
                                                  out string move), move);
            Assert.IsTrue(SampleLifecycle.TryPrep(sample, out string prep), prep);
            sample.TemperatureC = 120f;   // past any instrument's preheat gate

            var runner = lab.Machines.First(m => m.CanAccept(sample) == LoadRefusal.Accepted);
            Assert.AreEqual(LoadRefusal.Accepted, runner.TryLoad(sample));
            Assert.IsTrue(runner.TryBeginRun());

            Assert.IsFalse(objectives.IsDone(TutorialStep.LetARunFinish),
                "Fixture: the box must start empty or this proves nothing.");

            lab.Tick(5f);

            Assert.IsNotNull(runner.LastResult, "Fixture: the run did not finish.");
            Assert.IsTrue(objectives.IsDone(TutorialStep.LetARunFinish),
                "A run finished and the card did not notice.");
        }

        /// <summary>
        /// Promise: the card never blocks, asserted rather than claimed.
        /// <para>
        /// <c>CLAUDE.md</c> records booking-in being torn out (#73) because the loop stopped dead at a
        /// keyboard. An objective that had to be finished before the next thing worked would be that
        /// mistake wearing a tutorial's clothes, and the honest way to check is to run the same day
        /// twice — once watched, once not — and show the lab could not tell the difference.
        /// </para>
        /// </summary>
        [Test]
        public void AttachingTheObjectives_ChangesNothingAboutTheRun()
        {
            var watched = TutorialLab();
            var unwatched = TutorialLab();

            using (new TutorialObjectives(watched))
            {
                for (int day = 1; day <= 2; day++)
                {
                    watched.BeginDay();
                    for (int i = 0; i < 20; i++) watched.Tick(10f);
                    watched.EndDay();
                }
            }

            for (int day = 1; day <= 2; day++)
            {
                unwatched.BeginDay();
                for (int i = 0; i < 20; i++) unwatched.Tick(10f);
                unwatched.EndDay();
            }

            Assert.AreEqual(Comparable(unwatched), Comparable(watched),
                "A run with the objective card attached diverged from one without it. Something in " +
                "the tutorial is participating in the simulation instead of watching it.");
        }

        /// <summary>Two runs encode identically when nothing has diverged — save time excepted.</summary>
        private static string Comparable(LabState lab) =>
            string.Join("\n", RunSnapshotCodec.Encode(RunSnapshotCapture.Of(lab))
                .Split('\n')
                .Where(line => !line.StartsWith("saved\t", StringComparison.Ordinal)));

        /// <summary>
        /// Promise: nothing in the simulation asks the tutorial for permission. The structural half of
        /// the test above — the gateways a player action passes through must not be able to name the
        /// tracker at all, so an objective cannot become a precondition by accident later.
        /// </summary>
        [Test]
        public void NoGateway_MentionsTheObjectiveTracker()
        {
            string scripts = Path.Combine(Application.dataPath, "Scripts");

            var gateways = new[]
            {
                Path.Combine(scripts, "Gameplay", "World", "LabCommandExecutor.cs"),
                Path.Combine(scripts, "Gameplay", "Simulation", "LabState.cs"),
                Path.Combine(scripts, "Gameplay", "Simulation", "SampleRegistry.cs"),
                Path.Combine(scripts, "Gameplay", "Simulation", "MachineInstance.cs"),
                Path.Combine(scripts, "Chemistry", "SampleLifecycle.cs")
            };

            foreach (string path in gateways)
            {
                Assert.IsTrue(File.Exists(path),
                    $"{Path.GetFileName(path)} has moved; this check is pointed nowhere.");

                Assert.IsFalse(File.ReadAllText(path).Contains("TutorialObjectives"),
                    $"{Path.GetFileName(path)} names the objective tracker. A gateway that can see it " +
                    "is a gateway that can consult it, and an objective that gates the next action is " +
                    "#73 all over again.");
            }
        }

        // -- What it is allowed to say -----------------------------------------------------------------

        /// <summary>
        /// Promise: hard rule 1. The tutorial teaches where to look and how the room works, and never
        /// what you will find, so not one measurable quantity, fault or root cause may be named
        /// anywhere on the card — in either language. A line that said "high {x} means {y}" would hand
        /// a first-time player the diagnostic table the whole game is about building for themselves,
        /// before they had run anything.
        /// <para>
        /// Checked against the real content tables rather than a hardcoded word list, so a fault added
        /// later is covered the day it is added — the same arrangement as
        /// <c>OnboardingTests.TheShiftBrief_NamesNoElementFaultOrRootCause</c>.
        /// </para>
        /// </summary>
        [Test]
        public void NoObjectiveText_NamesAnElementFaultOrRootCause()
        {
            string text = AllTutorialText();

            foreach (var element in content.Elements.Values)
            {
                AssertAbsent(text, element.DisplayName, "element");

                // Ids are chemical symbols: "P" and "Ca" as whole words never occur in prose, so only
                // the ones long enough to be a real collision are worth asserting on.
                if (element.Id.Length >= 4) AssertAbsent(text, element.Id, "element id");
            }

            foreach (var cause in content.Causes.Values)
                AssertAbsent(text, cause.DisplayName, "root cause");

            foreach (var fault in content.Faults.Values)
                AssertAbsent(text, fault.DisplayName, "fault");
        }

        /// <summary>
        /// Promise: hard rule 3. Contamination and calibration drift are only fair because a blank run
        /// and a certified standard reveal them, and nothing else in the game gives anybody a reason
        /// to push solvent through a working instrument. If the tutorial points at nothing else, it
        /// points at those two.
        /// </summary>
        [Test]
        public void TheObjectives_PointAtTheBlankAndTheStandard()
        {
            using var objectives = new TutorialObjectives(null);
            var steps = objectives.All.Select(o => o.Step).ToList();

            CollectionAssert.Contains(steps, TutorialStep.RunABlank,
                "The blank is the only tell for carried-over residue and the tutorial does not " +
                "mention it exists.");
            CollectionAssert.Contains(steps, TutorialStep.RunAStandard,
                "The certified standard is the only tell for drift and the tutorial does not mention " +
                "it exists.");

            string text = AllTutorialText().ToLowerInvariant();
            Assert.IsTrue(text.Contains("blank"));
            Assert.IsTrue(text.Contains("standard"));
        }

        /// <summary>
        /// Promise: it stays short enough to read with a shift clock running. Fourteen one-line
        /// objectives is already the ceiling; a card that grows into a chapter is one nobody finishes,
        /// and the day does not pause for it.
        /// </summary>
        [Test]
        public void EveryObjective_HasBothHalvesAndTheCardStaysShort()
        {
            using var objectives = new TutorialObjectives(null);

            Assert.IsNotEmpty(objectives.All);
            Assert.LessOrEqual(objectives.All.Count, 16,
                "The card has grown past what anyone will read with a day clock running.");

            foreach (var objective in objectives.All)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(objective.Line.English),
                    $"{objective.Step} has no line.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(objective.Detail.English),
                    $"{objective.Step} has no detail, so the row says what to do and never how.");

                Assert.LessOrEqual(objective.Line.English.Length, 80,
                    $"{objective.Step}'s line is a paragraph. The line is the imperative; the detail " +
                    "is where the explanation goes.");
            }
        }

        /// <summary>
        /// Promise: hard rule 4. Red, amber and green mean verdict state and nothing else, and a tick
        /// on a completed objective would be the most-seen green in the game on a first run —
        /// spending the exact thing that makes red mean CRITICAL at a glance.
        /// <para>
        /// A source scan rather than a colour comparison, for the reason
        /// <c>LocalisationEnforcementTests</c> reads source: by the time it is a <c>Color</c> the
        /// distinction between "the signal set" and "a colour that happens to be green" is gone.
        /// </para>
        /// </summary>
        [Test]
        public void TheObjectiveCard_DrawsNoVerdictColour()
        {
            string path = Path.Combine(Application.dataPath, "Scripts", "Gameplay", "World",
                                       "TutorialCard.cs");
            Assert.IsTrue(File.Exists(path), "TutorialCard.cs has moved; this check is pointed nowhere.");

            var offenders = File.ReadAllLines(path)
                .Select((line, index) => (line, index))
                .Where(entry =>
                {
                    string trimmed = entry.line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///")) return false;
                    return Regex.IsMatch(entry.line,
                        @"SignalPalette\.(Critical|Caution|Normal)\b");
                })
                .Select(entry => $"TutorialCard.cs:{entry.index + 1}  {entry.line.Trim()}")
                .ToList();

            Assert.IsEmpty(offenders,
                "The objective card draws a verdict colour. Palette row 4 is reserved (hard rule 4) " +
                "and a tick is not a verdict:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Every word the tutorial puts in front of a player, in English and in German. Both, because
        /// the rule is about the sentences rather than about the source language — a translated line
        /// that named a fault would break hard rule 1 in German only, where nobody reviewing the
        /// English would ever see it.
        /// </summary>
        private static string AllTutorialText()
        {
            var text = new StringBuilder();

            using (var objectives = new TutorialObjectives(null))
            {
                foreach (var objective in objectives.All)
                {
                    Append(text, objective.Line);
                    Append(text, objective.Detail);
                }
            }

            Append(text, TutorialStrings.CardTitle);
            Append(text, TutorialStrings.DayOneHeading);
            Append(text, TutorialStrings.DayTwoHeading);
            Append(text, TutorialStrings.Progress);
            Append(text, TutorialStrings.Closing);
            Append(text, MenuStrings.Tutorial);
            Append(text, MenuStrings.TutorialNote);

            return text.ToString();
        }

        private static void Append(StringBuilder text, LocKey key)
        {
            text.AppendLine(key.English);
            if (German.Table.TryGetValue(key.Id, out string german)) text.AppendLine(german);
        }

        /// <summary>
        /// Word-boundary match, but only at an end that is actually a word character — a term like
        /// "Saponification No." ends in a full stop, and a <c>\b</c> pasted after it would produce a
        /// pattern that can never match, which is a test that passes by accident.
        /// </summary>
        private static void AssertAbsent(string text, string term, string kind)
        {
            if (string.IsNullOrWhiteSpace(term)) return;

            string lead = char.IsLetterOrDigit(term[0]) ? @"\b" : string.Empty;
            string tail = char.IsLetterOrDigit(term[^1]) ? @"\b" : string.Empty;

            Assert.IsFalse(
                Regex.IsMatch(text, lead + Regex.Escape(term) + tail, RegexOptions.IgnoreCase),
                $"The tutorial names the {kind} \"{term}\". It says where to look and how the room " +
                "works, never what the answer is (hard rule 1).");
        }

        // -- The in-world markers ----------------------------------------------------------------------

        /// <summary>
        /// Promise: the arrows exist on a tutorial and nowhere else.
        ///
        /// <para>
        /// <c>TutorialObjectives.Current</c> is null on every run that is not the tutorial, and its
        /// doc says every reader treats that as "draw nothing". <see cref="TutorialTargets"/> is where
        /// that check lives for the world marker and the screen compass both, so this is the whole of
        /// "the real contract acquires no hand-hold" — asserted with a positive control either side of
        /// it, because a resolver that answered nothing under every condition would pass the negative
        /// half on its own.
        /// </para>
        /// </summary>
        [Test]
        public void NothingIsMarked_OnARunThatIsNotTheTutorial()
        {
            PlaceInstrument("marker-a", new Vector3(100000f, 0f, 0f));

            var targets = new TutorialTargets();
            var here = new Vector3(100000f, 0f, 0f);

            Assert.IsNull(TutorialObjectives.Current,
                "Fixture: a run is already being tracked, so the negative half proves nothing.");

            Assert.IsFalse(targets.ResolveCurrent(here, null).Exists,
                "Something in the room is being marked on a run that is not the tutorial.");

            // The control: with a tracker attached and an objective that points at an instrument,
            // the same room and the same resolver do produce a target.
            var lab = TutorialLab();
            var objectives = TutorialObjectives.Begin(lab);
            objectives.Complete(TutorialStep.TakeACarton);
            objectives.Complete(TutorialStep.OpenTheCarton);
            objectives.Complete(TutorialStep.TakeAVial);

            Assert.AreEqual(TutorialStep.LoadAnInstrument, objectives.Next,
                "Fixture: the control is not pointing at an instrument.");

            targets.Rescan();
            Assert.IsTrue(targets.ResolveCurrent(here, null).Exists,
                "The tutorial is running, an objective points at an instrument, and there is one in " +
                "the room — and nothing was marked.");

            TutorialObjectives.End(lab);

            targets.Rescan();
            Assert.IsFalse(targets.ResolveCurrent(here, null).Exists,
                "The tutorial ended and the arrow stayed up.");
        }

        /// <summary>
        /// Promise: the arrow does not change its mind while you walk towards it.
        ///
        /// <para>
        /// Nearest is only how a target is <i>chosen</i>. Several instruments are equally valid for
        /// "load an instrument", and a marker recomputed from distance every frame would hop between
        /// two of them as the player crossed the room — which reads as a bug and, worse, sends
        /// somebody back the way they came. The incumbent therefore wins for as long as it is still a
        /// candidate, and only stops being the answer when it stops being one at all.
        /// </para>
        /// </summary>
        [Test]
        public void AStep_KeepsItsTargetWhileThatTargetIsStillValid()
        {
            // Far from anything the open scene might contain, so "nearest" is unambiguously one of
            // these two and the test cannot be decided by another suite's leftovers.
            var near = new Vector3(100000f, 0f, 0f);
            var far = new Vector3(100000f, 0f, 40f);

            var first = PlaceInstrument("marker-near", near);
            var second = PlaceInstrument("marker-far", far);

            var targets = new TutorialTargets();
            targets.Rescan();

            var chosen = targets.Resolve(TutorialStep.LoadAnInstrument, near, null);
            Assert.IsTrue(chosen.Exists, "Fixture: two instruments in the room and neither was picked.");
            Assert.AreSame(first.transform, chosen.Anchor, "The nearest instrument was not chosen.");

            // Walk to the other one, looking again the whole way. Every one of these answers has to
            // be the same answer.
            for (int step = 1; step <= 8; step++)
            {
                targets.Rescan();
                var from = Vector3.Lerp(near, far, step / 8f);
                var again = targets.Resolve(TutorialStep.LoadAnInstrument, from, null);

                Assert.AreSame(first.transform, again.Anchor,
                    $"The marker moved to a different instrument {step}/8 of the way across the room.");
            }

            // It gives the target up only when the target is gone.
            Object.DestroyImmediate(first);
            targets.Rescan();

            var replacement = targets.Resolve(TutorialStep.LoadAnInstrument, far, null);
            Assert.IsTrue(replacement.Exists, "The marked instrument was removed and nothing replaced it.");
            Assert.AreSame(second.transform, replacement.Anchor,
                "The only instrument left was not picked up.");
        }

        /// <summary>
        /// Promise: hard rule 1. The marker points at fixtures, never at answers.
        ///
        /// <para>
        /// A quest arrow is the most dangerous possible reader of ground truth: "the vial that needs
        /// the standard" is a single line of code away, it would be invisible in a screenshot, and a
        /// player who followed it would beat the game without ever learning the diagnostic tree the
        /// whole design is about building. "An instrument", "the terminal", "a carton" are procedural
        /// and safe; anything picked for a reason a <c>SampleGroundTruth</c> knows is not.
        /// </para>
        ///
        /// <para>
        /// Checked two ways. A source scan, because by the time a chemistry fact has become a
        /// <c>Transform</c> the distinction is gone — the same argument
        /// <see cref="TheObjectiveCard_DrawsNoVerdictColour"/> makes for colour. And the public
        /// surface by reflection, so no future caller can hand one of these types a sample either.
        /// </para>
        /// </summary>
        [Test]
        public void TheInWorldMarker_NeverAsksAnythingAboutASample()
        {
            // Everything a resolution must not be allowed to consult. Deliberately wider than
            // SampleGroundTruth itself: a measured reading, a severity and a filed verdict are all
            // downstream of it, and a marker that read one of those would be laundering the same
            // information through a type that is allowed on a client.
            var forbidden = new[]
            {
                "SampleGroundTruth", "SampleState", "SampleRegistry", "MeasurementPipeline",
                "TestResult", "ReadingSeverity", "Verdict", "FaultSeverity", "FaultDef",
                "ElementDef", "ProfileDef", "SampleAmbiguity", "Residue.Chemistry"
            };

            foreach (string file in MarkerSources())
            {
                Assert.IsTrue(File.Exists(file),
                    $"{Path.GetFileName(file)} has moved; this check is pointed nowhere.");

                foreach (var (line, index) in Code(file))
                {
                    foreach (string term in forbidden)
                    {
                        Assert.IsFalse(Regex.IsMatch(line, $@"\b{Regex.Escape(term)}\b"),
                            $"{Path.GetFileName(file)}:{index + 1} names {term}. The tutorial's arrow " +
                            "picks a target by kind and by position, never for a reason drawn from a " +
                            $"sample's chemistry (hard rule 1):\n  {line.Trim()}");
                    }
                }
            }

            // And nothing on the public surface can be handed one either.
            foreach (var type in new[] { typeof(TutorialTarget), typeof(TutorialTargets),
                                         typeof(TutorialMarker), typeof(TutorialCompass) })
            {
                foreach (var member in type.GetMembers())
                {
                    foreach (var used in Signature(member))
                    {
                        Assert.IsFalse(used.Namespace != null &&
                                       used.Namespace.StartsWith("Residue.Chemistry",
                                                                 StringComparison.Ordinal),
                            $"{type.Name}.{member.Name} takes or returns {used.FullName}. The marker " +
                            "layer must have no way to be told what a sample is.");
                    }
                }
            }
        }

        /// <summary>
        /// Promise: hard rule 4, for the half of the tutorial that is drawn in the room rather than on
        /// a card. A floating quest marker is the single most likely thing in the game to reach for
        /// green, and it would be the most-seen green on a first run — spending the exact thing that
        /// makes red mean CRITICAL at a glance. Both arrows are
        /// <c>SignalPalette.Accent</c>, like the card's "next" row.
        /// </summary>
        [Test]
        public void TheInWorldMarkers_DrawNoVerdictColour()
        {
            var offenders = new List<string>();

            foreach (string file in MarkerSources())
            {
                foreach (var (line, index) in Code(file))
                {
                    if (Regex.IsMatch(line, @"SignalPalette\.(Critical|Caution|Normal)\b") ||
                        Regex.IsMatch(line, @"PaletteUv\.Signal\b") ||
                        Regex.IsMatch(line, @"PaletteUv\.Family\.Signal\b"))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{index + 1}  {line.Trim()}");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "The tutorial's in-world marker draws a verdict colour. Palette row 4 is reserved " +
                "(hard rule 4) and a signpost is not a verdict:\n  " + string.Join("\n  ", offenders));
        }

        private static string[] MarkerSources()
        {
            string world = Path.Combine(Application.dataPath, "Scripts", "Gameplay", "World");

            return new[]
            {
                Path.Combine(world, "TutorialTarget.cs"),
                Path.Combine(world, "TutorialTargets.cs"),
                Path.Combine(world, "TutorialMarker.cs"),
                Path.Combine(world, "TutorialCompass.cs")
            };
        }

        /// <summary>
        /// The lines of a file that are code. Comments are excluded because these checks are about
        /// what the marker <i>does</i>, and the doc comments on all four files explain at length
        /// exactly which types they are forbidden to touch.
        /// </summary>
        private static IEnumerable<(string Line, int Index)> Code(string path) =>
            File.ReadAllLines(path)
                .Select((line, index) => (Line: line, Index: index))
                .Where(entry =>
                {
                    string trimmed = entry.Line.TrimStart();
                    return !trimmed.StartsWith("//") && !trimmed.StartsWith("*") &&
                           !trimmed.StartsWith("/*");
                });

        /// <summary>Every type that appears in a member's signature.</summary>
        private static IEnumerable<Type> Signature(System.Reflection.MemberInfo member)
        {
            switch (member)
            {
                case System.Reflection.MethodInfo method:
                    yield return method.ReturnType;
                    foreach (var p in method.GetParameters()) yield return p.ParameterType;
                    break;

                case System.Reflection.ConstructorInfo constructor:
                    foreach (var p in constructor.GetParameters()) yield return p.ParameterType;
                    break;

                case System.Reflection.PropertyInfo property:
                    yield return property.PropertyType;
                    break;

                case System.Reflection.FieldInfo field:
                    yield return field.FieldType;
                    break;
            }
        }

        /// <summary>
        /// An instrument in the open scene, far enough out that no other suite's leftovers can be
        /// nearer to the test's own vantage point than it is.
        /// </summary>
        private GameObject PlaceInstrument(string instanceId, Vector3 at)
        {
            var go = new GameObject(instanceId);
            go.transform.position = at;
            go.AddComponent<MachineStation>();

            placed.Add(go);
            return go;
        }

        // -- The save slot -----------------------------------------------------------------------------

        /// <summary>
        /// Promise: the tutorial never touches the run save slot.
        /// <para>
        /// There is one slot (#49) and it belongs to the real contract. A guided two-day run that
        /// wrote to it would destroy a twenty-day contract at day 14 for anyone who pressed TUTORIAL
        /// to look at it — and <c>OnDayEndedSaveRun</c> deletes the file outright once a run is over,
        /// which the tutorial reaches in ten minutes. Nothing is lost by not saving: it is short,
        /// fixed-seed and replayable from the menu.
        /// </para>
        /// </summary>
        [Test]
        public void TheTutorial_NeitherWritesNorDeletesTheRunSave()
        {
            LabRuntime.SimulatesLocally = true;

            // A real run, saved, exactly as a player who has one would have.
            var real = Spawn();
            real.Lab.BeginDay();
            real.Lab.EndDay();
            Assert.IsTrue(RunSaveSlot.Store.Exists, "Fixture: there has to be a save to be endangered.");
            Assert.IsTrue(RunSaveSlot.TryReadHeadline(out var before));

            Object.DestroyImmediate(host);
            host = null;

            // Now the tutorial, right through to the end of its last day.
            TutorialRun.Request();
            var tutorial = Spawn();

            Assert.AreEqual(ContractPlan.TutorialId, tutorial.Lab.Plan.Id,
                "TUTORIAL did not produce a tutorial.");
            Assert.IsTrue(tutorial.IsTutorial);
            Assert.IsFalse(tutorial.Continued);

            for (int day = 1; day <= tutorial.Lab.Plan.Length; day++)
            {
                tutorial.Lab.BeginDay();
                tutorial.Lab.EndDay();
            }
            Assert.IsTrue(tutorial.Lab.IsRunOver, "Fixture: the tutorial has to actually finish.");

            Assert.IsTrue(RunSaveSlot.Store.Exists,
                "The tutorial deleted the player's save on finishing.");
            Assert.IsTrue(RunSaveSlot.TryReadHeadline(out var after));
            Assert.AreEqual(before.Day, after.Day, "The tutorial wrote over the player's run.");
            Assert.AreEqual(before.ContractName, after.ContractName,
                "The save in the slot is no longer the contract that was in it.");
        }

        /// <summary>
        /// Promise: CONTINUE wins over a leftover TUTORIAL. Both latches survive a scene load and both
        /// are read in the same place, so the one case worth pinning is a save being rebuilt while a
        /// stale tutorial request is still set — the player must get their run, not a two-day one.
        /// </summary>
        [Test]
        public void AContinuedRun_IsNotTurnedIntoATutorialByALeftoverLatch()
        {
            LabRuntime.SimulatesLocally = true;

            var real = Spawn();
            real.Lab.BeginDay();
            real.Lab.EndDay();
            Object.DestroyImmediate(host);
            host = null;

            RunSaveSlot.RequestContinue();
            TutorialRun.Request();

            var continued = Spawn();

            Assert.IsTrue(continued.Continued, "The save was not picked back up.");
            Assert.AreEqual(ContractPlan.DefaultId, continued.Lab.Plan.Id,
                "A stale tutorial request rebuilt a saved run against the wrong contract.");
            Assert.IsFalse(TutorialRun.TakeRequest(),
                "The latch was left set, so the next NEW SHIFT would silently be a tutorial.");
        }

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
        /// Somebody for <c>LabCommands.Send</c> to be asking on behalf of. It is never validated
        /// against anything here — the router in these tests answers without consulting a lab, because
        /// what is under test is the announcement rather than the rules.
        /// </summary>
        private sealed class StubActor : ILabActor
        {
            public ulong ClientId => 0;
            public string DisplayName => "test";
            public bool HasPosition => true;
            public Vector3 Position => Vector3.zero;
            public LabGrip Grip { get; private set; } = LabGrip.Empty;
            public void SetGrip(LabGrip grip) => Grip = grip;
        }
    }
}
