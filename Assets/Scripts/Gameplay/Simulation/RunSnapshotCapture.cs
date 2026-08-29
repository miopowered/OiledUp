using System;
using System.Collections.Generic;
using Residue.Chemistry;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// Reads a live <see cref="LabState"/> into a <see cref="RunSnapshot"/> (#49).
    /// <para>
    /// <b>Host-only by signature.</b> It takes a <see cref="LabState"/>, and a client never builds
    /// one — <c>LabRuntime.SimulatesLocally</c> is what stops it, and stopping it is the same rule
    /// that keeps a truth-bearing simulation out of every player's process. There is no overload
    /// that reads a replicated view, so a client has nothing to hand this and nothing to save.
    /// </para>
    /// <para>
    /// <b>Called at a day boundary and nowhere else.</b> Not out of thrift: <see cref="LabState"/>
    /// is only ever quiescent between <c>EndDay</c> and the next <c>BeginDay</c>. Anywhere else an
    /// instrument is mid-run, a vial is in somebody's hand and a slip is halfway to a desk, and a
    /// snapshot taken there would be a picture of a moving room.
    /// </para>
    /// </summary>
    public static class RunSnapshotCapture
    {
        /// <summary>Flatten a run. Returns null for a null lab so a caller cannot save nothing over something.</summary>
        public static RunSnapshot Of(LabState lab)
        {
            if (lab == null) return null;

            var snapshot = new RunSnapshot
            {
                Schema = RunSnapshot.SchemaVersion,
                SavedUtcTicks = DateTime.UtcNow.Ticks,

                ContractId = lab.Plan != null ? lab.Plan.Id : null,
                ContractName = lab.Plan != null ? lab.Plan.DisplayName : null,

                Day = lab.Day,
                DayInProgress = lab.DayInProgress,
                DaySecondsRemaining = lab.DaySecondsRemaining,

                Seed = lab.Seed,
                NextSampleId = lab.NextSampleId,

                Money = lab.Economy.Money,
                Reputation = lab.Economy.Reputation,
                SolventUnits = lab.Economy.SolventUnits,
                ReferenceStandards = lab.Economy.ReferenceStandards,
                TotalEarned = lab.Economy.TotalEarned,
                TotalLost = lab.Economy.TotalLost,

                NextSlipTicket = lab.Slips.NextTicket
            };

            lab.CaptureRng(out snapshot.RngA, out snapshot.RngB, out snapshot.RngC, out snapshot.RngD);

            foreach (var sample in lab.Samples.All) snapshot.Samples.Add(Record(sample));
            snapshot.Samples.Sort((a, b) => a.Id.CompareTo(b.Id));

            // The vault flattens its own truth — see SampleRegistry.CaptureTruths. Nothing here ever
            // holds a SampleGroundTruth, which is what keeps hard rule 2 a structural fact.
            lab.Samples.CaptureTruths(snapshot.Truths);

            foreach (var pending in lab.Samples.Pending)
            {
                snapshot.Pending.Add(new RunSnapshot.PendingRecord
                {
                    Sample = pending.Sample.Value,
                    ResolveOnDay = pending.ResolveOnDay
                });
            }

            // Sorted, because the registry's own list is kept in due-date order by an unstable sort:
            // two verdicts landing on the same day can sit either way round. That is harmless in
            // play and fatal to a round-trip test, which is the only thing that can tell you the
            // format lost something.
            snapshot.Pending.Sort((a, b) => a.ResolveOnDay == b.ResolveOnDay
                ? a.Sample.CompareTo(b.Sample)
                : a.ResolveOnDay.CompareTo(b.ResolveOnDay));

            foreach (var id in lab.Samples.PendingRequeues) snapshot.Requeues.Add(id.Value);

            foreach (var machine in lab.Machines) snapshot.Machines.Add(Record(machine));

            var slips = new List<ResultSlips.Slip>();
            lab.Slips.CollectInto(slips);
            foreach (var slip in slips)
            {
                snapshot.Slips.Add(new RunSnapshot.SlipRecord
                {
                    Ticket = slip.Ticket,
                    Sample = slip.Sample.Value,
                    MachineInstanceId = slip.MachineInstanceId,
                    Result = Record(slip.Result),
                    Location = Place(slip.Location)
                });
            }

            foreach (var bottle in lab.Solvent.All)
            {
                snapshot.Bottles.Add(new RunSnapshot.BottleRecord
                {
                    Id = bottle.Id,
                    Capacity = bottle.Capacity,
                    Charges = bottle.Charges,
                    Location = Place(bottle.Location)
                });
            }

            foreach (var report in lab.LastReports) snapshot.LastReports.Add(Record(report));

            return snapshot;
        }

        // -- Records --------------------------------------------------------------------------------

        internal static RunSnapshot.PlaceRecord Place(SampleLocation location) => new()
        {
            Kind = (int)location.Kind,
            HolderClientId = location.HolderClientId,
            ContainerId = location.ContainerId,
            SlotIndex = location.SlotIndex
        };

        internal static RunSnapshot.ResultRecord Record(TestResult result)
        {
            if (result == null) return null;

            var record = new RunSnapshot.ResultRecord
            {
                MachineId = result.MachineId,
                DayRun = result.DayRun,
                MachineRunIndex = result.MachineRunIndex,
                VolumeConsumedMl = result.VolumeConsumedMl,
                Cost = result.Cost,
                Suspect = result.Suspect,
                IsBlank = result.IsBlank,
                IsReference = result.IsReference
            };

            foreach (var kv in result.Values)
                record.Values.Add(new RunSnapshot.Reading { ElementId = kv.Key, Value = kv.Value });

            return record;
        }

        private static RunSnapshot.SampleRecord Record(SampleState sample)
        {
            var record = new RunSnapshot.SampleRecord
            {
                Id = sample.Id.Value,
                EquipmentTag = sample.EquipmentTag,
                ProfileId = sample.Profile != null ? sample.Profile.Id : null,
                CustomerId = sample.Customer != null ? sample.Customer.Id : null,
                JobNumber = sample.JobNumber,
                HoursSinceOilChange = sample.HoursSinceOilChange,
                FieldTechNote = sample.FieldTechNote,
                CollectedDay = sample.CollectedDay,
                ResampleOf = sample.ResampleOf.Value,

                VolumeMl = sample.VolumeMl,
                TemperatureC = sample.TemperatureC,
                IsSettled = sample.IsSettled,
                Location = Place(sample.Location),

                FiledVerdict = sample.FiledVerdict.HasValue ? (int)sample.FiledVerdict.Value : -1,
                FiledRootCauseId = sample.FiledRootCause != null ? sample.FiledRootCause.Id : null,
                FiledOnDay = sample.FiledOnDay,
                ConsequenceResolved = sample.ConsequenceResolved,

                Ambiguity = (int)sample.Ambiguity,
                RegisteredLine = sample.RegisteredLine,
                RegisteredTag = sample.RegisteredTag
            };

            foreach (var result in sample.Results) record.Results.Add(Record(result));
            return record;
        }

        private static RunSnapshot.MachineRecord Record(MachineInstance machine)
        {
            var runtime = machine.Runtime;

            var record = new RunSnapshot.MachineRecord
            {
                InstanceId = machine.InstanceId,
                DefId = machine.Def != null ? machine.Def.Id : null,

                LoadedSample = machine.LoadedSample.Value,
                ActiveRun = (int)machine.ActiveRun,
                SecondsRemaining = machine.SecondsRemaining,
                RunDuration = machine.RunDuration,
                HasResultWaiting = machine.HasResultWaiting,

                DriftPercent = runtime.DriftPercent,
                DriftSign = runtime.DriftSign,
                RunIndex = runtime.RunIndex,
                RunsSinceClean = runtime.RunsSinceClean,
                RunsSinceCalibration = runtime.RunsSinceCalibration,
                DriftStartedAtRunIndex = runtime.DriftStartedAtRunIndex,
                LastCalibratedDay = runtime.LastCalibratedDay,

                LastResult = Record(machine.LastResult),
                LastBlank = Record(machine.LastBlank),
                LastBlankDay = machine.LastBlankDay,

                // The check itself is rebuilt on load from this run and the house certificate — see
                // RunSnapshot.MachineRecord.LastCheckResult.
                LastCheckResult = machine.LastCheck != null ? Record(machine.LastCheckRun) : null,
                LastCheckDay = machine.LastCheck != null ? machine.LastCheck.Day : -1
            };

            foreach (var kv in runtime.Residue)
                record.Residue.Add(new RunSnapshot.Reading { ElementId = kv.Key, Value = kv.Value });

            if (machine.LastCalibration.HasValue)
            {
                var outcome = machine.LastCalibration.Value;
                record.HasLastCalibration = true;
                record.CalibrationDay = outcome.Day;
                record.CalibrationCorrectedDrift = outcome.CorrectedDrift;
                record.CalibrationFlaggedResults = outcome.FlaggedResults;
                record.CalibrationAffectedSamples = outcome.AffectedSamples;
                record.CalibrationAffectedArchived = outcome.AffectedArchived;
            }

            return record;
        }

        private static RunSnapshot.ReportRecord Record(ConsequenceReport report) => new()
        {
            Sample = report.Sample.Value,
            RecordTag = report.RecordTag,
            Filed = (int)report.Filed,
            Outcome = (int)report.Outcome,
            MoneyDelta = report.MoneyDelta,
            ReputationDelta = report.ReputationDelta,
            RootCauseCorrect = report.RootCauseCorrect,
            FaultName = report.FaultName,
            ActualRootCause = report.ActualRootCause,
            RequeueSample = report.RequeueSample,
            Headline = report.Headline,
            Registration = (int)report.Registration
        };
    }
}
