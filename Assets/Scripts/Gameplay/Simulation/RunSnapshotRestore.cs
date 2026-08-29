using System;
using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// Rebuilds a live <see cref="LabState"/> from a <see cref="RunSnapshot"/> (#49).
    ///
    /// <para>
    /// <b>All or nothing.</b> Every definition a save names is resolved against the live
    /// <c>ContentCatalog</c> before anything is built, and one unresolvable id refuses the whole
    /// load with a sentence naming it. The alternative — skip the sample and carry on — is the exact
    /// failure the issue forbids: a save that loads and silently drops a record. It is also worse
    /// than it looks. A sample whose fault archetype has been deleted still carries that fault's
    /// signature in its true values, so dropping the <c>FaultDef</c> leaves a vial that reads
    /// Critical on every instrument and resolves as "no fault found" when the verdict lands. That is
    /// the chemistry lying, which hard rule 1 does not permit even once.
    /// </para>
    ///
    /// <para>
    /// <b>What is not restored, and why.</b> <see cref="EconomyTuning"/> and the
    /// <see cref="ContractPlan"/> rows are balance authored in code; the save carries the plan's id
    /// and nothing else, so a continued run picks up whatever the current build says the payouts and
    /// the arrival curve are. That is the same decision as referencing content by id, and for the
    /// same reason: a save must never pin a copy of the balance tables.
    /// </para>
    /// </summary>
    public static class RunSnapshotRestore
    {
        /// <summary>
        /// Rebuild the run, or explain in one sentence why this build cannot.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public static bool TryRebuild(RunSnapshot snapshot, ContentCatalog catalog,
                                      out LabState lab, out string refusal)
        {
            lab = null;
            refusal = null;

            if (snapshot == null) { refusal = "That save is empty."; return false; }

            if (snapshot.Schema != RunSnapshot.SchemaVersion)
            {
                refusal = $"That save was written by a different version of the game " +
                          $"(save format {snapshot.Schema}, this build reads {RunSnapshot.SchemaVersion}). " +
                          "It cannot be continued.";
                return false;
            }

            if (catalog == null || !catalog.IsComplete)
            {
                refusal = "The lab's reference data is missing, so a saved run cannot be read.";
                return false;
            }

            var plan = ContractPlan.ById(snapshot.ContractId);
            if (plan == null)
            {
                refusal = $"That save is for the contract “{snapshot.ContractId}”, which this build " +
                          "no longer offers.";
                return false;
            }

            var rebuilt = new LabState(catalog, plan, snapshot.Seed);
            rebuilt.RestoreGeneration(
                Rng.FromState(snapshot.RngA, snapshot.RngB, snapshot.RngC, snapshot.RngD),
                snapshot.NextSampleId);

            rebuilt.Economy.Restore(snapshot.Money, snapshot.Reputation, snapshot.SolventUnits,
                                    snapshot.ReferenceStandards, snapshot.TotalEarned, snapshot.TotalLost);

            if (!RestoreMachines(rebuilt, snapshot, catalog, out refusal)) return false;
            if (!RestoreSamples(rebuilt, snapshot, catalog, out refusal)) return false;

            RestoreSlips(rebuilt, snapshot);
            RestoreBottles(rebuilt, snapshot);
            RestoreReports(rebuilt, snapshot);

            rebuilt.RestoreDay(snapshot.Day, snapshot.DayInProgress, snapshot.DaySecondsRemaining);

            lab = rebuilt;
            return true;
        }

        // -- Machines --------------------------------------------------------------------------------

        private static bool RestoreMachines(LabState lab, RunSnapshot snapshot, ContentCatalog catalog,
                                            out string refusal)
        {
            refusal = null;

            foreach (var record in snapshot.Machines)
            {
                var def = catalog.Machine(record.DefId);
                if (def == null)
                {
                    refusal = $"That save has a “{record.DefId}” on the bench and this build has no " +
                              "such instrument. The run cannot be continued.";
                    return false;
                }

                var machine = lab.Install(def, record.InstanceId);
                if (machine == null)
                {
                    refusal = $"Could not install “{record.DefId}” from the save.";
                    return false;
                }

                if (!TryEnum<RunKind>(record.ActiveRun, out var activeRun))
                {
                    refusal = $"That save describes a run “{record.ActiveRun}” on {def.DisplayName} " +
                              "that this build does not have.";
                    return false;
                }

                machine.LoadedSample = new SampleId(record.LoadedSample);
                machine.HasResultWaiting = record.HasResultWaiting;
                machine.RestoreRun(activeRun, record.SecondsRemaining, record.RunDuration);

                var runtime = machine.Runtime;
                runtime.DriftPercent = record.DriftPercent;
                runtime.DriftSign = record.DriftSign;
                runtime.RunIndex = record.RunIndex;
                runtime.RunsSinceClean = record.RunsSinceClean;
                runtime.RunsSinceCalibration = record.RunsSinceCalibration;
                runtime.DriftStartedAtRunIndex = record.DriftStartedAtRunIndex;
                runtime.LastCalibratedDay = record.LastCalibratedDay;

                runtime.Residue.Clear();
                foreach (var reading in record.Residue) runtime.Residue[reading.ElementId] = reading.Value;

                machine.LastResult = Result(record.LastResult);
                machine.LastBlank = Result(record.LastBlank);
                machine.LastBlankDay = record.LastBlankDay;

                // Rebuilt rather than stored — see MachineInstance.LastCheckRun.
                machine.LastCheckRun = Result(record.LastCheckResult);
                machine.LastCheck = machine.LastCheckRun == null
                    ? null
                    : CalibrationCheck.From(lab.Standard, machine.LastCheckRun, def, record.LastCheckDay);

                machine.LastCalibration = record.HasLastCalibration
                    ? new CalibrationOutcome(record.CalibrationDay, record.CalibrationCorrectedDrift,
                                             record.CalibrationFlaggedResults,
                                             record.CalibrationAffectedSamples,
                                             record.CalibrationAffectedArchived)
                    : (CalibrationOutcome?)null;
            }

            return true;
        }

        // -- Samples ---------------------------------------------------------------------------------

        private static bool RestoreSamples(LabState lab, RunSnapshot snapshot, ContentCatalog catalog,
                                           out string refusal)
        {
            refusal = null;

            var truths = new Dictionary<int, RunSnapshot.TruthRecord>(snapshot.Truths.Count);
            foreach (var truth in snapshot.Truths) truths[truth.Id] = truth;

            var faults = new List<FaultDef>();
            var severities = new List<float>();

            foreach (var record in snapshot.Samples)
            {
                var profile = catalog.Profile(record.ProfileId);
                if (profile == null)
                {
                    refusal = $"That save has a sample from a “{record.ProfileId}” and this build has " +
                              "no such equipment profile. The run cannot be continued.";
                    return false;
                }

                // A sender that no longer exists refuses the whole load, like every other content id.
                // Silently dropping it would leave a sample whose paperwork claims a firm the lab has
                // never heard of, and #32 reconciles against exactly that paperwork — so a note with
                // no sender is a discrepancy the player could not have caused and cannot resolve.
                CustomerDef customer = null;
                if (!string.IsNullOrEmpty(record.CustomerId))
                {
                    customer = catalog.Customer(record.CustomerId);
                    if (customer == null)
                    {
                        refusal = $"{record.EquipmentTag} was sent by “{record.CustomerId}”, which " +
                                  "this build no longer has on file. The run cannot be continued.";
                        return false;
                    }
                }

                RootCauseDef filedCause = null;
                if (!string.IsNullOrEmpty(record.FiledRootCauseId))
                {
                    filedCause = catalog.Cause(record.FiledRootCauseId);
                    if (filedCause == null)
                    {
                        refusal = $"{record.EquipmentTag} was filed against the cause " +
                                  $"“{record.FiledRootCauseId}”, which this build no longer has. " +
                                  "The run cannot be continued.";
                        return false;
                    }
                }

                if (record.FiledVerdict >= 0 && !TryEnum<Verdict>(record.FiledVerdict, out _))
                {
                    refusal = $"{record.EquipmentTag} carries a verdict this build does not recognise.";
                    return false;
                }

                if (!truths.TryGetValue(record.Id, out var truthRecord))
                {
                    // A record with no chemistry behind it can never be resolved: the registry would
                    // drop its pending consequence and the player would simply never hear back.
                    refusal = $"That save is incomplete — {record.EquipmentTag} has no chemistry on " +
                              "file. The run cannot be continued.";
                    return false;
                }

                faults.Clear();
                severities.Clear();

                for (int i = 0; i < truthRecord.FaultIds.Count; i++)
                {
                    string faultId = truthRecord.FaultIds[i];
                    var fault = catalog.Fault(faultId);
                    if (fault == null)
                    {
                        refusal = $"That save has a “{faultId}” in it and this build no longer defines " +
                                  "that fault. The run cannot be continued — loading it would leave " +
                                  "samples that read wrong and resolve as healthy.";
                        return false;
                    }

                    faults.Add(fault);
                    severities.Add(i < truthRecord.Severities.Count ? truthRecord.Severities[i] : 0.5f);
                }

                var state = new SampleState
                {
                    Id = new SampleId(record.Id),
                    EquipmentTag = record.EquipmentTag,
                    Profile = profile,
                    Customer = customer,
                    JobNumber = record.JobNumber,
                    HoursSinceOilChange = record.HoursSinceOilChange,
                    FieldTechNote = record.FieldTechNote,
                    CollectedDay = record.CollectedDay,
                    ResampleOf = new SampleId(record.ResampleOf),

                    VolumeMl = record.VolumeMl,
                    TemperatureC = record.TemperatureC,
                    IsSettled = record.IsSettled,
                    Location = Place(record.Location),

                    FiledVerdict = record.FiledVerdict >= 0 ? (Verdict)record.FiledVerdict : (Verdict?)null,
                    FiledRootCause = filedCause,
                    FiledOnDay = record.FiledOnDay,
                    ConsequenceResolved = record.ConsequenceResolved
                };

                foreach (var result in record.Results)
                {
                    var restored = Result(result);
                    if (restored != null) state.Results.Add(restored);
                }

                lab.Samples.RestoreSample(state, faults, severities,
                                          truthRecord.TrueValues, truthRecord.Contamination);
            }

            foreach (var pending in snapshot.Pending)
                lab.Samples.RestorePending(new SampleId(pending.Sample), pending.ResolveOnDay);

            foreach (int requeue in snapshot.Requeues)
                lab.Samples.RestoreRequeue(new SampleId(requeue));

            return true;
        }

        // -- Paperwork and props -----------------------------------------------------------------------

        private static void RestoreSlips(LabState lab, RunSnapshot snapshot)
        {
            foreach (var record in snapshot.Slips)
            {
                lab.Slips.Restore(record.Ticket, new SampleId(record.Sample), record.MachineInstanceId,
                                  Result(record.Result), Place(record.Location));
            }

            lab.Slips.RestoreNextTicket(snapshot.NextSlipTicket);
        }

        private static void RestoreBottles(LabState lab, RunSnapshot snapshot)
        {
            foreach (var record in snapshot.Bottles)
                lab.Solvent.Restore(record.Id, record.Capacity, record.Charges, Place(record.Location));
        }

        private static void RestoreReports(LabState lab, RunSnapshot snapshot)
        {
            foreach (var record in snapshot.LastReports)
            {
                lab.RestoreReport(new ConsequenceReport
                {
                    Sample = new SampleId(record.Sample),
                    RecordTag = record.RecordTag,
                    Filed = TryEnum<Verdict>(record.Filed, out var filed) ? filed : Verdict.Normal,
                    Outcome = TryEnum<ConsequenceOutcome>(record.Outcome, out var outcome)
                        ? outcome
                        : ConsequenceOutcome.CorrectNormal,
                    MoneyDelta = record.MoneyDelta,
                    ReputationDelta = record.ReputationDelta,
                    RootCauseCorrect = record.RootCauseCorrect,
                    FaultName = record.FaultName,
                    ActualRootCause = record.ActualRootCause,
                    RequeueSample = record.RequeueSample,
                    Headline = record.Headline
                });
            }
        }

        // -- Records ----------------------------------------------------------------------------------

        internal static SampleLocation Place(RunSnapshot.PlaceRecord record) => new()
        {
            Kind = TryEnum<SampleLocationKind>(record.Kind, out var kind) ? kind : SampleLocationKind.OnSurface,
            HolderClientId = record.HolderClientId,
            ContainerId = record.ContainerId,
            SlotIndex = record.SlotIndex
        };

        internal static TestResult Result(RunSnapshot.ResultRecord record)
        {
            if (record == null) return null;

            var result = new TestResult
            {
                MachineId = record.MachineId,
                DayRun = record.DayRun,
                MachineRunIndex = record.MachineRunIndex,
                VolumeConsumedMl = record.VolumeConsumedMl,
                Cost = record.Cost,
                Suspect = record.Suspect,
                IsBlank = record.IsBlank,
                IsReference = record.IsReference
            };

            foreach (var reading in record.Values) result.Values[reading.ElementId] = reading.Value;
            return result;
        }

        private static bool TryEnum<T>(int value, out T parsed) where T : struct, Enum
        {
            if (Enum.IsDefined(typeof(T), value))
            {
                parsed = (T)Enum.ToObject(typeof(T), value);
                return true;
            }

            parsed = default;
            return false;
        }
    }
}
