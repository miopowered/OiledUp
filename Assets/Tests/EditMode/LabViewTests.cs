using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Data;
using Residue.Editor.Content;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.World;
using Residue.Net.Views;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards the read seam that lets a joined client operate the lab.
    /// <para>
    /// A client has no <see cref="LabState"/> and never will (hard rule 2), so every station in the
    /// room reads an <see cref="IMachineView"/> instead of the host's live instrument. That interface
    /// has two implementations. <b>The failure mode this file exists for is not a crash — it is
    /// drift.</b> Both sides compile, both render, and one of them quietly quotes a different run time
    /// or offers to run a sample somebody has already run, and the two players in the room disagree
    /// about what the machine in front of them is doing.
    /// </para>
    /// So most of what follows is not "does the adapter work" but "do the two adapters give the same
    /// answer for the same instrument".
    /// </summary>
    public sealed class LabViewTests
    {
        private ContentSet content;
        private ContentCatalog catalog;

        private ILabView originalHost;
        private ILabView originalReplicated;

        [SetUp]
        public void SetUp()
        {
            content = ContentBuilder.BuildInMemory();
            catalog = ContentBuilder.BuildCatalogInMemory(content);

            // Static, process-wide, and read by half the world layer. Anything left installed here
            // fails a completely unrelated test somewhere else, which is a miserable thing to debug.
            originalHost = LabView.Host;
            originalReplicated = LabView.Replicated;
        }

        [TearDown]
        public void TearDown()
        {
            LabView.Host = originalHost;
            LabView.Replicated = originalReplicated;

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
        // 1. The two implementations must be indistinguishable to anything that draws an instrument.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: the instrument in front of you looks the same whether or not you are the one
        /// hosting.
        /// <para>
        /// Every readable member of <see cref="IMachineView"/> is compared between the host's adapter
        /// over a live <see cref="MachineInstance"/> and a client's adapter over
        /// <c>MachineView.From</c> of that same instance, across the states a station actually
        /// distinguishes: idle, loaded, running, and holding a finished result.
        /// </para>
        /// Driven by reflection rather than a field list, so a member added to the interface later is
        /// covered the day it lands rather than the day someone remembers this file. The two members
        /// that are <i>deliberately</i> asymmetric — <see cref="IMachineView.CanAccept"/> and
        /// <see cref="IMachineView.HasFreshCheck"/> — are methods, and have tests of their own below.
        /// </summary>
        [Test]
        public void HostAndClientAdapters_DrawTheSameInstrument()
        {
            foreach (var (state, machine) in InstrumentStates())
            {
                var host = new HostMachineView(machine);
                var client = ReplicatedMachineView.Of(MachineView.From(machine), catalog);

                foreach (var property in typeof(IMachineView).GetProperties())
                {
                    Assert.AreEqual(property.GetValue(host), property.GetValue(client),
                        $"IMachineView.{property.Name} disagrees between the host and a client for an " +
                        $"instrument that is {state}. Two players standing at the same machine would " +
                        "be shown different things.");
                }
            }
        }

        /// <summary>
        /// Promise: an instrument that has finished a run offers the vial back to whoever walks up,
        /// not only to the player who pressed the button.
        /// <para>
        /// This used to be a bool on the station component, set in the callback of the player who
        /// started the run. That is right for exactly one person: everybody else's station still said
        /// "RUN", and pressing it spent millilitres on a test that had already been done (§4.5). It is
        /// now read off the instrument, so it replicates and it is the same answer for the room.
        /// </para>
        /// </summary>
        [Test]
        public void AFinishedRun_OffersTheVialBackToEveryPlayer()
        {
            var machine = Instrument();
            Assert.IsFalse(machine.HasResultWaiting, "A fresh instrument is holding nothing.");

            machine.LoadedSample = new SampleId(1);
            Assert.IsTrue(machine.TryBeginRun());
            Assert.IsFalse(machine.HasResultWaiting, "A run in progress is not a result waiting.");

            machine.Tick(10_000f);
            Assert.IsTrue(machine.HasResultWaiting, "The run finished and the vial is still in there.");
            Assert.IsTrue(MachineView.From(machine).HasResultWaiting,
                "A client whose station cannot see this offers to run the sample a second time.");

            machine.Unload();
            Assert.IsFalse(machine.HasResultWaiting, "The vial has been taken; there is nothing to collect.");
        }

        /// <summary>
        /// Promise: a blank and a certified standard leave nothing to pick up.
        /// <para>
        /// Neither takes a vial (§5.2, §5.3), so an instrument that flagged a result waiting after one
        /// would offer the player an empty hand — and, worse, would stop offering the run they meant
        /// to do next.
        /// </para>
        /// </summary>
        [Test]
        public void ABlankOrAStandard_LeavesNothingToCollect()
        {
            var machine = Instrument();

            Assert.IsTrue(machine.TryBeginBlank());
            machine.Tick(10_000f);
            Assert.IsFalse(machine.HasResultWaiting, "A solvent blank consumes no vial and leaves none.");

            Assert.IsTrue(machine.TryBeginReference());
            machine.Tick(10_000f);
            Assert.IsFalse(machine.HasResultWaiting, "The ampoule is the consumable, not a vial in the tray.");
        }

        /// <summary>
        /// Promise: loading a vial clears whatever the last one left behind.
        /// <para>
        /// Otherwise the station offers "take your vial back" the instant a fresh sample goes in, and
        /// the sample never gets run.
        /// </para>
        /// </summary>
        [Test]
        public void LoadingAVial_ClearsTheResultTheLastOneLeft()
        {
            var machine = Instrument();
            machine.HasResultWaiting = true;

            Assert.AreEqual(LoadRefusal.Accepted, machine.TryLoad(ReadySample()));
            Assert.IsFalse(machine.HasResultWaiting);
        }

        /// <summary>
        /// Promise: a client greys out the recalibrate button on exactly the same rule the host
        /// enforces — a certificate from <i>today</i> (§5.3).
        /// <para>
        /// The day is the whole point: <c>MachineRuntimeState.BeginDay</c> re-rolls drift every
        /// morning, so yesterday's standard authorises nothing. A client that only knew a certificate
        /// existed would offer an action the host is about to refuse, which hard rule 3 forbids.
        /// </para>
        /// </summary>
        [Test]
        public void AStaleCertificate_AuthorisesNoCalibrationOnEitherSide()
        {
            var machine = Instrument();
            machine.LastCheck = CertificateFor(machine, day: 3);
            Assert.IsNotNull(machine.LastCheck, "Setup failed: no certified lines matched this instrument.");

            var host = new HostMachineView(machine);
            var client = ReplicatedMachineView.Of(MachineView.From(machine), catalog);

            Assert.IsTrue(host.HasFreshCheck(3));
            Assert.IsTrue(client.HasFreshCheck(3), "Today's certificate is on file and the client cannot see it.");

            Assert.IsFalse(host.HasFreshCheck(4));
            Assert.IsFalse(client.HasFreshCheck(4),
                "A certificate from yesterday has had a whole day of drift walk over it.");
        }

        /// <summary>
        /// Promise: a client that cannot check a rule offers the action anyway and lets the host
        /// refuse, rather than inventing an answer.
        /// <para>
        /// Volume, temperature and settling all live on a <see cref="SampleState"/> the client does not
        /// hold. The house pattern (see <c>LabCommands</c>) is that an optimistic prompt costs the
        /// player a refusal sentence they can read, whereas a pessimistic one greys out a button that
        /// would have worked — and, more importantly, that re-implementing §4.5 here would create a
        /// second copy of the rules to drift from the enforced one. What the client <i>can</i> see is
        /// occupancy, and it must still get that right.
        /// </para>
        /// </summary>
        [Test]
        public void AClientRefusesOnlyWhatItCanActuallySee()
        {
            var machine = Instrument();
            var idle = ReplicatedMachineView.Of(MachineView.From(machine), catalog);
            Assert.AreEqual(LoadRefusal.Accepted, idle.CanAccept(null),
                "Nothing the client can see refuses this load, so it asks and lets the host decide.");

            machine.LoadedSample = new SampleId(1);
            var loaded = ReplicatedMachineView.Of(MachineView.From(machine), catalog);
            Assert.AreEqual(LoadRefusal.MachineOccupied, loaded.CanAccept(null),
                "Occupancy is replicated, so a client that offers a load into a full instrument is " +
                "just wrong rather than optimistic.");

            Assert.IsTrue(machine.TryBeginRun());
            var running = ReplicatedMachineView.Of(MachineView.From(machine), catalog);
            Assert.AreEqual(LoadRefusal.MachineBusy, running.CanAccept(null));
        }

        // -----------------------------------------------------------------------------------------
        // 2. The lab-wide view, and the one thing it is honest about not having.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: the shift clock, the books and the calibration price read the same off
        /// <see cref="HostLabView"/> as they do off the lab it wraps.
        /// <para>
        /// These feed the HUD, the terminal prompt and the £ figure on the recalibrate button. The
        /// price in particular is replicated rather than re-derived from a default
        /// <see cref="EconomyTuning"/>, so that tuning it cannot leave a client quoting last week's
        /// number.
        /// </para>
        /// </summary>
        [Test]
        public void HostLabView_ReportsTheLabItWraps()
        {
            var lab = new LabState(catalog, OneDayPlan(), 1234);
            var view = new HostLabView(lab);

            Assert.AreEqual(0, view.Day);
            Assert.IsFalse(view.DayInProgress);

            lab.BeginDay();
            Assert.AreEqual(1, view.Day);
            Assert.IsTrue(view.DayInProgress);
            Assert.IsFalse(view.ShiftOver);
            Assert.AreEqual(lab.Economy.Money, view.Money, 1e-3f);
            Assert.AreEqual(lab.Economy.SolventUnits, view.SolventUnits, 1e-3f);
            Assert.AreEqual(lab.Economy.ReferenceStandards, view.ReferenceStandards);
            Assert.AreEqual(lab.Tuning.CalibrationCost, view.CalibrationCost, 1e-3f);
            Assert.AreEqual(lab.OpenSamples().Count, view.OpenSampleCount,
                "The terminal prompt and the HUD both count this; they must count what the lab holds.");

            lab.Tick(10_000f);
            Assert.IsTrue(view.ShiftOver, "The clock ran out and every station must stop offering runs.");

            Assert.AreEqual(lab.Tuning.CalibrationCost,
                EconomyView.From(lab.Economy, lab.Tuning).CalibrationCost, 1e-3f,
                "A client reads the price off the wire; if it does not travel, the button lies.");
        }

        /// <summary>
        /// Promise: a client says vials are not here rather than drawing an empty crate.
        /// <para>
        /// §3.2 makes a vial a local prop and nothing replicates <c>SampleLocation</c>, so a joined
        /// client's crate and racks are bare however full the host's are. Hard rule 3 is about the
        /// player always being able to tell what is going on: "crate empty" would be a sentence that
        /// is false, and it would read as a bug rather than as a limitation.
        /// </para>
        /// </summary>
        [Test]
        public void AProcessWithNoVialProps_SaysSoRatherThanShowingAnEmptyShelf()
        {
            var lab = new LabState(catalog, OneDayPlan(), 1234);

            LabView.Host = null;
            LabView.Replicated = new ReplicatedLabView(null);
            Assert.IsFalse(LabView.Current.HasVialProps);
            Assert.IsTrue(LabView.VialsMissingHere,
                "This is what every crate, rack and instrument reads before it offers a vial.");
            Assert.IsNotEmpty(LabView.VialsAreHostOnly, "There has to be something to say to the player.");

            LabView.Host = new HostLabView(lab);
            Assert.IsTrue(LabView.Current.HasVialProps, "A process that simulates spawned the bottles.");
            Assert.IsFalse(LabView.VialsMissingHere);
        }

        /// <summary>
        /// Promise: a host reads its own lab, not a snapshot of it.
        /// <para>
        /// A host holds both — it publishes the views everyone else reads — and reading its own
        /// snapshot back would put its screens one publish behind its own simulation for nothing.
        /// </para>
        /// </summary>
        [Test]
        public void WhereBothViewsExist_TheHostReadsItsOwnLab()
        {
            var host = new HostLabView(new LabState(catalog, OneDayPlan(), 1234));
            var replicated = new ReplicatedLabView(null);

            LabView.Host = host;
            LabView.Replicated = replicated;
            Assert.AreSame(host, LabView.Current);

            LabView.Host = null;
            Assert.AreSame(replicated, LabView.Current);

            LabView.Replicated = null;
            Assert.IsNull(LabView.Current, "No session and no lab is a real state; every caller handles it.");
        }

        // -- helpers ------------------------------------------------------------------------------

        /// <summary>
        /// The states a station actually distinguishes, each as a fresh instrument. Named so a
        /// parity failure says which one broke.
        /// </summary>
        private IEnumerable<(string State, MachineInstance Machine)> InstrumentStates()
        {
            var idle = Instrument();
            yield return ("idle and empty", idle);

            var loaded = Instrument();
            loaded.LoadedSample = new SampleId(1);
            yield return ("loaded, not yet run", loaded);

            var running = Instrument();
            running.LoadedSample = new SampleId(2);
            running.TryBeginRun();
            running.Tick(running.RunSeconds * 0.25f);   // a quarter in, whatever the tables say it costs
            yield return ("running", running);

            var finished = Instrument();
            finished.LoadedSample = new SampleId(3);
            finished.TryBeginRun();
            finished.Tick(10_000f);
            yield return ("holding a finished result", finished);

            var certified = Instrument();
            certified.LastCheck = CertificateFor(certified, day: 2);
            certified.LastBlank = new TestResult { IsBlank = true, Values = { ["water_ppm"] = 120f } };
            certified.LastBlankDay = 2;
            yield return ("carrying the §5.2 and §5.3 tells", certified);
        }

        /// <summary>
        /// One Karl Fischer titrator, scaled the way a test lab is, so the parity check runs against
        /// the awkward end of the range rather than the comfortable one.
        /// </summary>
        private MachineInstance Instrument() =>
            new("karl_fischer-0", content.Machines["karl_fischer"]) { TimeScale = 0.05f };

        /// <summary>A certificate that reads 20% high — comfortably outside the §5.3 tolerance.</summary>
        private CalibrationCheck CertificateFor(MachineInstance machine, int day)
        {
            var standard = ReferenceStandard.FromProfiles(content.Profiles.Values.ToList());
            var readout = new TestResult { MachineId = machine.Def.Id, IsReference = true };

            foreach (var element in machine.Def.Measures)
            {
                if (element == null || machine.Def.IsBlindTo(element.Id)) continue;
                if (!standard.TryGet(element.Id, out float certified)) continue;
                readout.Values[element.Id] = certified * 1.2f;
            }

            return CalibrationCheck.From(standard, readout, machine.Def, day);
        }

        /// <summary>A sample walked far enough along §5.1 that an instrument will take it.</summary>
        private SampleState ReadySample()
        {
            var rng = new Rng(31337);
            var profile = content.Profiles["quench_oil_cold"];
            var generated = new SampleGenerator(content.AllFaults)
                .Generate(GenerationRequest.Default(profile, "WERK-1 QUENCH 1", 1), ref rng);

            Assert.IsNotNull(generated, "Generator produced nothing.");
            Assert.IsTrue(SampleLifecycle.TryMove(generated.State, SampleLocation.OnSurface("bench", 0), out var move), move);
            Assert.IsTrue(SampleLifecycle.TryLog(generated.State, "WERK-1 QUENCH 1", out var log), log);
            Assert.IsTrue(SampleLifecycle.TryPrep(generated.State, out var prep), prep);
            return generated.State;
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
