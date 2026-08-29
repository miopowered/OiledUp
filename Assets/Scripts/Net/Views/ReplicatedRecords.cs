using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;
using Residue.Gameplay.World;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// <see cref="IRecordFeed"/> over the rows <see cref="LabNetwork"/> replicates. The client half of
    /// the results seam, and the thing that lets a joined player use the terminal at all.
    /// <para>
    /// <b>It rebuilds the record, it does not compute one.</b> Every number here arrived measured;
    /// nothing in this file multiplies, adds noise or consults a threshold, because §3.1 is explicit
    /// that a client must never produce a test result and <c>MeasurementPipeline</c> is host-only.
    /// What it does do is put those numbers back into the shapes the screens already know how to draw
    /// — <see cref="SampleState"/>, <see cref="TestResult"/>, <see cref="CalibrationCheck"/> — so the
    /// terminal has one set of drawing code rather than a host version and a client version that can
    /// quietly disagree about what the player is looking at.
    /// </para>
    /// <para>
    /// Those types are safe to hold by construction. <see cref="SampleGroundTruth"/> is a separate
    /// class that no view can express, and <see cref="SampleLifecycle"/> already expects a client to
    /// derive a sample's stage from a <see cref="SampleState"/> of its own. What is missing here is
    /// missing on purpose: no <see cref="SampleState.EquipmentTag"/>, because the paper label reaches
    /// the bottle and no screen (see <see cref="VialView"/>), and no location, because bottles are not
    /// this list's business.
    /// </para>
    /// <para>
    /// The one exception to "nothing here computes" is what it refuses to rebuild. A
    /// <see cref="ConsequenceReport"/> comes back with its outcome, its money and its sentence and
    /// with both of its diagnosis fields left null — see <see cref="ReadReports"/>. That is the same
    /// argument the rest of the file makes, pointed the other way: the host already decided what a
    /// client may know, and rebuilding more of the shape than arrived would be this file inventing it.
    /// </para>
    /// </summary>
    public sealed class ReplicatedRecords : IRecordFeed
    {
        private readonly LabNetwork network;

        /// <summary>
        /// The house certificate, blended from this process's own tables. Not replicated because it
        /// is a reading of content both sides ship — §5.3 turns on the player being able to look every
        /// certified figure up in the manual, so deriving it is what keeps the certificate and the
        /// limits it is measured against from disagreeing.
        /// </summary>
        private ReferenceStandard standard;

        private ContentCatalog blendedFrom;

        public ReplicatedRecords(LabNetwork network) => this.network = network;

        /// <summary>
        /// The day's reckoning, as published rows (§4.3, §5.4).
        /// <para>
        /// Installed rather than read off <see cref="LabNetwork"/> like the other lists, and the
        /// difference is deliberate: this is the one list whose contents are only safe because of
        /// <i>when</i> they are published, so it is handed to the reader by the writer, at the moment
        /// the writer decides it exists. Null until then, and a desk with no rows simply has no
        /// summary to draw — which is the honest answer before the first day has ended.
        /// </para>
        /// </summary>
        public NetworkList<ReportView> Reports { get; set; }

        private static ContentCatalog Catalog =>
            LabRuntime.Instance != null ? LabRuntime.Instance.Catalog : null;

        public LabRecords ReadLab()
        {
            var catalog = Catalog;
            if (catalog == null || network == null) return null;

            var sampleRows = network.Samples;
            var machineRows = network.Machines;
            if (sampleRows == null || machineRows == null) return null;

            var runs = ReadRuns(out var rows);

            var byId = new Dictionary<int, SampleState>(sampleRows.Count);
            var open = new List<SampleState>();
            var inDoubt = new List<SampleState>();

            for (int i = 0; i < sampleRows.Count; i++)
            {
                var view = sampleRows[i];
                byId[view.Id] = Rebuild(view, catalog);
            }

            // In row order, which is the order the host filed them. A record's runs are the ones
            // walked to the desk; a slip still sitting in an instrument's tray is not on it (§5.1).
            foreach (var row in rows)
            {
                if (!row.Filed || row.Sample == 0) continue;
                if (!byId.TryGetValue(row.Sample, out var state)) continue;
                if (!runs.TryGetValue(row.Key, out var run)) continue;

                state.Results.Add(run);
            }

            foreach (var state in byId.Values)
            {
                if (!state.FiledVerdict.HasValue) { open.Add(state); continue; }
                if (state.Stage == SampleStage.Archived && HasSuspectRun(state)) inDoubt.Add(state);
            }

            open.Sort((a, b) => a.Id.CompareTo(b.Id));
            inDoubt.Sort((a, b) => a.Id.CompareTo(b.Id));

            var instruments = new List<InstrumentRecord>(machineRows.Count);
            for (int i = 0; i < machineRows.Count; i++)
                instruments.Add(Rebuild(machineRows[i], rows, runs, catalog));

            var day = network.Day;
            var economy = network.Economy;

            return new LabRecords
            {
                Day = day.Day,
                IsRunOver = day.IsRunOver,
                DayInProgress = day.DayInProgress,
                ContractName = day.ContractName.ToString(),
                ContractLength = day.ContractLength,
                Money = economy.Money,
                Reputation = economy.Reputation,
                SolventUnits = economy.SolventUnits,
                ReferenceStandards = economy.ReferenceStandards,
                StartingMoney = economy.StartingMoney,
                TotalEarned = economy.TotalEarned,
                TotalLost = economy.TotalLost,
                SolventUnitCost = economy.SolventUnitCost,
                ReferenceStandardUnitCost = economy.ReferenceStandardUnitCost,
                StandardId = Standard(catalog).Id,
                Open = open,
                InDoubt = inDoubt,
                Instruments = instruments,
                Reports = ReadReports(day.Day),
                Causes = catalog.Causes,

                // The paperwork that came in the boxes (#32, #80). Rebuilt from the lines the host
                // published rather than from anything this file works out, and rebuilt by the same
                // code that types the paper prop — the desk and the page in the player's hand have to
                // number the rows identically, because a registration is filed by row number.
                Notes = ReadNotes()
            };
        }

        /// <summary>
        /// The delivery notes this client has been told about.
        /// <para>
        /// Empty until <c>LabNetwork</c> spawns, and the panel that reads it says so rather than
        /// pretending the note is blank. Nothing else on the screen depends on it.
        /// </para>
        /// </summary>
        private List<DeliveryNote> ReadNotes()
        {
            var notes = new List<DeliveryNote>();
            ReplicatedCartons.ReadNotes(network, notes);
            return notes;
        }

        /// <summary>
        /// Rebuild the day's reckoning from the rows the host published (§4.3, §5.4).
        /// <para>
        /// Same rule as everything else in this file: it puts the numbers back into the shape the
        /// screen already draws — a <see cref="ConsequenceReport"/> — rather than teaching the
        /// terminal a second way to render a day end. What it cannot rebuild is what the host chose
        /// not to send: <see cref="ConsequenceReport.FaultName"/> and
        /// <see cref="ConsequenceReport.ActualRootCause"/> stay null on this side, because a client
        /// gets whatever it may know already worded, in the headline, and a spare copy of the answer
        /// in a field nothing draws is the shape a leak takes six months later.
        /// </para>
        /// Rows are dropped unless they belong to the day on the clock. The report list and
        /// <see cref="DayView"/> are separate writes and can arrive a frame apart, and a summary
        /// titled with one day and filled with another's is the one way a day end can lie.
        /// </summary>
        private List<ConsequenceReport> ReadReports(int day)
        {
            var reports = new List<ConsequenceReport>();

            var rows = Reports;
            if (rows == null) return reports;

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Day != day) continue;

                reports.Add(new ConsequenceReport
                {
                    Sample = row.SampleId,
                    Outcome = row.Outcome,
                    MoneyDelta = row.MoneyDelta,
                    ReputationDelta = row.ReputationDelta,
                    RootCauseCorrect = row.RootCauseCorrect,
                    Headline = row.Headline.ToString()
                });
            }

            return reports;
        }

        public bool TryLastReading(string machineInstanceId, out TestResult reading, out SampleId sample)
        {
            reading = null;
            sample = SampleId.None;

            var list = network != null ? network.Results : null;
            if (list == null || string.IsNullOrEmpty(machineInstanceId)) return false;

            var packed = ViewText.Fixed32(machineInstanceId);
            var newest = default(ResultView);
            bool found = false;

            // Keys are handed out in publish order, so the highest one on this instrument is the last
            // thing it produced — the same answer MachineInstance.LastResult gives on the host.
            for (int i = 0; i < list.Count; i++)
            {
                var row = list[i];
                if (!row.MachineInstanceId.Equals(packed)) continue;
                if (found && row.Key <= newest.Key) continue;

                newest = row;
                found = true;
            }

            if (!found) return false;

            reading = Rebuild(newest);
            Fill(reading, newest.Key);
            sample = newest.SampleId;
            return true;
        }

        // -- Rebuilding ------------------------------------------------------------------------------

        /// <summary>Every published run, by key, with its numbers already in it.</summary>
        private Dictionary<int, TestResult> ReadRuns(out List<ResultView> rows)
        {
            rows = new List<ResultView>();

            var resultRows = network.Results;
            var readingRows = network.Readings;
            var runs = new Dictionary<int, TestResult>();

            if (resultRows == null) return runs;

            for (int i = 0; i < resultRows.Count; i++)
            {
                var row = resultRows[i];
                rows.Add(row);
                runs[row.Key] = Rebuild(row);
            }

            if (readingRows == null) return runs;

            for (int i = 0; i < readingRows.Count; i++)
            {
                var reading = readingRows[i];

                // A reading whose run has not arrived is dropped rather than guessed at. The two
                // lists are written in one pass, so this is the frame-boundary case and it resolves
                // itself on the next publish — with nothing drawn under the wrong heading meanwhile.
                if (!runs.TryGetValue(reading.ResultKey, out var run)) continue;

                run.Values[reading.ElementId.ToString()] = reading.Value;
            }

            return runs;
        }

        private static TestResult Rebuild(ResultView row) => new()
        {
            MachineId = row.MachineDefId.ToString(),
            DayRun = row.DayRun,
            VolumeConsumedMl = row.VolumeConsumedMl,
            Cost = row.Cost,
            IsBlank = row.IsBlank,
            IsReference = row.IsReference,
            Suspect = row.Suspect
        };

        private void Fill(TestResult run, int key)
        {
            var readingRows = network != null ? network.Readings : null;
            if (readingRows == null) return;

            for (int i = 0; i < readingRows.Count; i++)
            {
                var reading = readingRows[i];
                if (reading.ResultKey != key) continue;

                run.Values[reading.ElementId.ToString()] = reading.Value;
            }
        }

        /// <summary>
        /// Rebuild the record of a sample.
        /// <para>
        /// The fields are set so that <see cref="SampleLifecycle.StageOf"/> derives the stage the host
        /// published, rather than storing it: a stored stage would be a second source of truth that
        /// could disagree with the record it describes, which is the argument that type makes for
        /// deriving it in the first place.
        /// </para>
        /// </summary>
        private static SampleState Rebuild(SampleView view, ContentCatalog catalog)
        {
            var note = view.FieldTechNote.ToString();
            var job = view.JobNumber.ToString();

            return new SampleState
            {
                Id = view.SampleId,
                EquipmentTag = view.RecordTag.ToString(),
                Profile = catalog.Profile(view.ProfileId.ToString()),

                // Resolved against the client's own catalog, exactly as the profile above is. Without
                // these two the sender crosses the wire and is dropped on arrival: the view carries it
                // and the rebuilt state does not, so a client screen asking who sent a vial gets null
                // while the host's identical screen answers. That divergence is what this whole layer
                // exists to prevent.
                Customer = catalog.Customer(view.CustomerId.ToString()),
                JobNumber = string.IsNullOrEmpty(job) ? null : job,
                VolumeMl = view.VolumeMl,
                HoursSinceOilChange = view.HoursSinceOilChange,
                FieldTechNote = string.IsNullOrEmpty(note) ? null : note,
                ResampleOf = new SampleId(view.ResampleOf),
                FiledVerdict = view.HasVerdict ? view.FiledVerdict : (Verdict?)null,
                FiledOnDay = view.FiledOnDay,
                ConsequenceResolved = view.Stage == SampleStage.Resolved,
                IsSettled = view.Stage >= SampleStage.Prepped,

                // Enough location for the stage to derive, and no more: where the bottle actually is
                // travels in VialView, which screens must not read.
                Location = view.Stage == SampleStage.InCrate
                    ? SampleLocation.InCrate("intake", -1)
                    : SampleLocation.OnSurface("bench", -1)
            };
        }

        private InstrumentRecord Rebuild(MachineView view, List<ResultView> rows,
                                         Dictionary<int, TestResult> runs, ContentCatalog catalog)
        {
            string instanceId = view.InstanceId.ToString();
            var def = catalog.Machine(view.DefId.ToString());

            var record = new InstrumentRecord
            {
                InstanceId = instanceId,
                Def = def,
                RunsSinceFlush = view.RunsSinceFlush,
                LastBlankDay = view.LastBlankDay
            };

            if (view.HasBlank) record.LastBlank = Newest(rows, runs, instanceId, blank: true, day: -1);

            // Only while a certificate is on file. A recalibration consumes it, and a client still
            // showing yesterday's reference run would be offering a tell the host has spent.
            if (view.HasCalibrationCheck && def != null)
            {
                var readout = Newest(rows, runs, instanceId, blank: false, day: view.CalibrationCheckDay);
                record.Check = CalibrationCheck.From(Standard(catalog), readout, def,
                                                     view.CalibrationCheckDay);
            }

            if (view.HasRecalibration)
            {
                record.LastCalibration = new CalibrationOutcome(
                    view.RecalibratedDay, view.RecalibrationCorrected, view.RecalibrationFlaggedRuns,
                    view.RecalibrationAffectedSamples, view.RecalibrationAffectedRecords);
            }

            return record;
        }

        /// <summary>
        /// The newest blank on an instrument, or its reference readout from a given day. Newest by
        /// key, which is publish order.
        /// </summary>
        private static TestResult Newest(List<ResultView> rows, Dictionary<int, TestResult> runs,
                                         string instanceId, bool blank, int day)
        {
            TestResult found = null;
            int newest = 0;
            var packed = ViewText.Fixed32(instanceId);

            foreach (var row in rows)
            {
                if (!row.MachineInstanceId.Equals(packed)) continue;
                if (blank ? !row.IsBlank : !(row.IsReference && row.DayRun == day)) continue;
                if (found != null && row.Key <= newest) continue;
                if (!runs.TryGetValue(row.Key, out var run)) continue;

                found = run;
                newest = row.Key;
            }

            return found;
        }

        private static bool HasSuspectRun(SampleState state)
        {
            foreach (var run in state.Results)
            {
                if (run.Suspect) return true;
            }
            return false;
        }

        private ReferenceStandard Standard(ContentCatalog catalog)
        {
            if (standard != null && blendedFrom == catalog) return standard;

            standard = ReferenceStandard.FromProfiles(catalog.Profiles);
            blendedFrom = catalog;
            return standard;
        }
    }
}
