using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// Turns a <see cref="RunSnapshot"/> into the text <see cref="RunSaveStore"/> carries, and back
    /// (#49).
    ///
    /// <para>
    /// <b>Hand-rolled, and floats are written at nine significant digits.</b> Unity's
    /// <c>JsonUtility</c> is the obvious tool and it is the wrong one twice over: it cannot express a
    /// dictionary — residue, true values and every measured reading are dictionaries — and it rounds
    /// floats on the way out. Rounding is not cosmetic here. A restored true value that differs in
    /// the seventh digit is a sample that measures fractionally differently from the one that was
    /// saved, and hard rule 1 says a loaded run behaves exactly like the run that was saved. Nine
    /// digits is the documented shortest round-trip precision for a 32-bit float, so
    /// <c>G9</c> in and <c>float.Parse</c> out is exact rather than nearly exact.
    /// </para>
    ///
    /// <para>
    /// <b>Refuses rather than repairs.</b> A schema this build does not know is rejected with both
    /// version numbers in the message. There is no migration path yet and inventing one would mean
    /// guessing at fields an older writer never wrote — which ends as a run that loads and silently
    /// drops whatever the guess got wrong. When the first shipped save is worth migrating, the seam
    /// is <see cref="TryDecode"/>: read the version, branch, and keep this reader as the newest
    /// branch. Until then "this save is from a different version" is the honest answer and it is one
    /// a player can act on.
    /// </para>
    ///
    /// <para>
    /// The format is one record per line, fields separated by tabs, so a corrupt or truncated file
    /// fails on a line rather than on the whole document — although in practice
    /// <see cref="RunSaveStore"/>'s checksum has already caught it.
    /// </para>
    /// </summary>
    public static class RunSnapshotCodec
    {
        private const char Separator = '\t';

        /// <summary>Stands in for a null string. Unambiguous because backslashes are escaped first.</summary>
        private const string NullToken = "\\0";

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        // -- Writing ---------------------------------------------------------------------------------

        public static string Encode(RunSnapshot snapshot)
        {
            if (snapshot == null) return string.Empty;

            var text = new StringBuilder(4096);

            Line(text, "schema", Int(RunSnapshot.SchemaVersion));
            Line(text, "saved", snapshot.SavedUtcTicks.ToString(Invariant));
            Line(text, "contract", Text(snapshot.ContractId), Text(snapshot.ContractName));
            Line(text, "day", Int(snapshot.Day), Bool(snapshot.DayInProgress),
                 Float(snapshot.DaySecondsRemaining));
            Line(text, "seed", Int(snapshot.Seed));
            Line(text, "rng", UInt(snapshot.RngA), UInt(snapshot.RngB), UInt(snapshot.RngC),
                 UInt(snapshot.RngD));
            Line(text, "nextsample", Int(snapshot.NextSampleId));
            Line(text, "books", Float(snapshot.Money), Float(snapshot.Reputation),
                 Float(snapshot.SolventUnits), Int(snapshot.ReferenceStandards),
                 Float(snapshot.TotalEarned), Float(snapshot.TotalLost));
            Line(text, "nextslip", Int(snapshot.NextSlipTicket));

            foreach (var sample in snapshot.Samples)
            {
                Line(text, "sample",
                     Int(sample.Id), Text(sample.EquipmentTag), Text(sample.ProfileId),
                     Float(sample.HoursSinceOilChange), Text(sample.FieldTechNote),
                     Int(sample.CollectedDay), Int(sample.ResampleOf),
                     Float(sample.VolumeMl), Float(sample.TemperatureC), Bool(sample.IsSettled),
                     Int(sample.FiledVerdict), Text(sample.FiledRootCauseId), Int(sample.FiledOnDay),
                     Bool(sample.ConsequenceResolved));
                WritePlace(text, "sample.at", sample.Location);
                foreach (var result in sample.Results) WriteResult(text, "sample.result", result);
            }

            foreach (var truth in snapshot.Truths)
            {
                Line(text, "truth", Int(truth.Id));
                for (int i = 0; i < truth.FaultIds.Count; i++)
                {
                    Line(text, "truth.fault", Text(truth.FaultIds[i]),
                         Float(i < truth.Severities.Count ? truth.Severities[i] : 0.5f));
                }
                WriteReadings(text, "truth.value", truth.TrueValues);
                WriteReadings(text, "truth.contamination", truth.Contamination);
            }

            foreach (var machine in snapshot.Machines)
            {
                Line(text, "machine",
                     Text(machine.InstanceId), Text(machine.DefId), Int(machine.LoadedSample),
                     Int(machine.ActiveRun), Float(machine.SecondsRemaining), Float(machine.RunDuration),
                     Bool(machine.HasResultWaiting), Float(machine.DriftPercent), Int(machine.DriftSign),
                     Int(machine.RunIndex), Int(machine.RunsSinceClean), Int(machine.RunsSinceCalibration),
                     Int(machine.DriftStartedAtRunIndex), Int(machine.LastCalibratedDay),
                     Int(machine.LastBlankDay), Int(machine.LastCheckDay));

                WriteReadings(text, "machine.residue", machine.Residue);
                WriteResult(text, "machine.last", machine.LastResult);
                WriteResult(text, "machine.blank", machine.LastBlank);
                WriteResult(text, "machine.check", machine.LastCheckResult);

                if (machine.HasLastCalibration)
                {
                    Line(text, "machine.calibration", Int(machine.CalibrationDay),
                         Float(machine.CalibrationCorrectedDrift), Int(machine.CalibrationFlaggedResults),
                         Int(machine.CalibrationAffectedSamples), Int(machine.CalibrationAffectedArchived));
                }
            }

            foreach (var slip in snapshot.Slips)
            {
                Line(text, "slip", Int(slip.Ticket), Int(slip.Sample), Text(slip.MachineInstanceId));
                WritePlace(text, "slip.at", slip.Location);
                WriteResult(text, "slip.result", slip.Result);
            }

            foreach (var bottle in snapshot.Bottles)
            {
                Line(text, "bottle", Text(bottle.Id), Int(bottle.Capacity), Int(bottle.Charges));
                WritePlace(text, "bottle.at", bottle.Location);
            }

            foreach (var pending in snapshot.Pending)
                Line(text, "pending", Int(pending.Sample), Int(pending.ResolveOnDay));

            foreach (int requeue in snapshot.Requeues) Line(text, "requeue", Int(requeue));

            foreach (var report in snapshot.LastReports)
            {
                Line(text, "report", Int(report.Sample), Text(report.RecordTag), Int(report.Filed),
                     Int(report.Outcome), Float(report.MoneyDelta), Float(report.ReputationDelta),
                     Bool(report.RootCauseCorrect), Text(report.FaultName), Text(report.ActualRootCause),
                     Bool(report.RequeueSample), Text(report.Headline));
            }

            return text.ToString();
        }

        private static void WritePlace(StringBuilder text, string tag, RunSnapshot.PlaceRecord place) =>
            Line(text, tag, Int(place.Kind), place.HolderClientId.ToString(Invariant),
                 Text(place.ContainerId), Int(place.SlotIndex));

        private static void WriteReadings(StringBuilder text, string tag, List<RunSnapshot.Reading> readings)
        {
            if (readings == null || readings.Count == 0) return;

            text.Append(tag);
            foreach (var reading in readings)
            {
                text.Append(Separator).Append(Text(reading.ElementId));
                text.Append(Separator).Append(Float(reading.Value));
            }
            text.Append('\n');
        }

        private static void WriteResult(StringBuilder text, string tag, RunSnapshot.ResultRecord result)
        {
            if (result == null) return;

            Line(text, tag, Text(result.MachineId), Int(result.DayRun), Int(result.MachineRunIndex),
                 Float(result.VolumeConsumedMl), Float(result.Cost), Bool(result.Suspect),
                 Bool(result.IsBlank), Bool(result.IsReference));
            WriteReadings(text, tag + ".value", result.Values);
        }

        // -- Reading ---------------------------------------------------------------------------------

        /// <summary>
        /// Rebuild a snapshot, or say why not.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public static bool TryDecode(string payload, out RunSnapshot snapshot, out string refusal)
        {
            snapshot = null;
            refusal = null;

            if (string.IsNullOrEmpty(payload))
            {
                refusal = "That save is empty.";
                return false;
            }

            var reading = new RunSnapshot();

            RunSnapshot.SampleRecord sample = null;
            RunSnapshot.TruthRecord truth = null;
            RunSnapshot.MachineRecord machine = null;
            RunSnapshot.SlipRecord slip = null;
            RunSnapshot.BottleRecord bottle = null;
            RunSnapshot.ResultRecord result = null;

            bool sawSchema = false;

            foreach (string raw in payload.Split('\n'))
            {
                if (raw.Length == 0) continue;

                string[] parts = raw.Split(Separator);
                switch (parts[0])
                {
                    case "schema":
                        reading.Schema = Int(parts, 1);
                        sawSchema = true;
                        if (reading.Schema != RunSnapshot.SchemaVersion)
                        {
                            refusal = $"That save is in format {reading.Schema} and this build reads " +
                                      $"format {RunSnapshot.SchemaVersion}. It cannot be continued.";
                            return false;
                        }
                        break;

                    case "saved": reading.SavedUtcTicks = Long(parts, 1); break;

                    case "contract":
                        reading.ContractId = Text(parts, 1);
                        reading.ContractName = Text(parts, 2);
                        break;

                    case "day":
                        reading.Day = Int(parts, 1);
                        reading.DayInProgress = Bool(parts, 2);
                        reading.DaySecondsRemaining = Float(parts, 3);
                        break;

                    case "seed": reading.Seed = Int(parts, 1); break;

                    case "rng":
                        reading.RngA = UInt(parts, 1);
                        reading.RngB = UInt(parts, 2);
                        reading.RngC = UInt(parts, 3);
                        reading.RngD = UInt(parts, 4);
                        break;

                    case "nextsample": reading.NextSampleId = Int(parts, 1); break;
                    case "nextslip": reading.NextSlipTicket = Int(parts, 1); break;

                    case "books":
                        reading.Money = Float(parts, 1);
                        reading.Reputation = Float(parts, 2);
                        reading.SolventUnits = Float(parts, 3);
                        reading.ReferenceStandards = Int(parts, 4);
                        reading.TotalEarned = Float(parts, 5);
                        reading.TotalLost = Float(parts, 6);
                        break;

                    case "sample":
                        sample = new RunSnapshot.SampleRecord
                        {
                            Id = Int(parts, 1),
                            EquipmentTag = Text(parts, 2),
                            ProfileId = Text(parts, 3),
                            HoursSinceOilChange = Float(parts, 4),
                            FieldTechNote = Text(parts, 5),
                            CollectedDay = Int(parts, 6),
                            ResampleOf = Int(parts, 7),
                            VolumeMl = Float(parts, 8),
                            TemperatureC = Float(parts, 9),
                            IsSettled = Bool(parts, 10),
                            FiledVerdict = Int(parts, 11),
                            FiledRootCauseId = Text(parts, 12),
                            FiledOnDay = Int(parts, 13),
                            ConsequenceResolved = Bool(parts, 14)
                        };
                        reading.Samples.Add(sample);
                        break;

                    case "sample.at":
                        if (sample != null) sample.Location = Place(parts);
                        break;

                    case "sample.result":
                        result = Result(parts);
                        sample?.Results.Add(result);
                        break;

                    case "truth":
                        truth = new RunSnapshot.TruthRecord { Id = Int(parts, 1) };
                        reading.Truths.Add(truth);
                        break;

                    case "truth.fault":
                        truth?.FaultIds.Add(Text(parts, 1));
                        truth?.Severities.Add(Float(parts, 2));
                        break;

                    case "truth.value":
                        if (truth != null) Readings(parts, truth.TrueValues);
                        break;

                    case "truth.contamination":
                        if (truth != null) Readings(parts, truth.Contamination);
                        break;

                    case "machine":
                        machine = new RunSnapshot.MachineRecord
                        {
                            InstanceId = Text(parts, 1),
                            DefId = Text(parts, 2),
                            LoadedSample = Int(parts, 3),
                            ActiveRun = Int(parts, 4),
                            SecondsRemaining = Float(parts, 5),
                            RunDuration = Float(parts, 6),
                            HasResultWaiting = Bool(parts, 7),
                            DriftPercent = Float(parts, 8),
                            DriftSign = Int(parts, 9),
                            RunIndex = Int(parts, 10),
                            RunsSinceClean = Int(parts, 11),
                            RunsSinceCalibration = Int(parts, 12),
                            DriftStartedAtRunIndex = Int(parts, 13),
                            LastCalibratedDay = Int(parts, 14),
                            LastBlankDay = Int(parts, 15),
                            LastCheckDay = Int(parts, 16)
                        };
                        reading.Machines.Add(machine);
                        break;

                    case "machine.residue":
                        if (machine != null) Readings(parts, machine.Residue);
                        break;

                    case "machine.last":
                        result = Result(parts);
                        if (machine != null) machine.LastResult = result;
                        break;

                    case "machine.blank":
                        result = Result(parts);
                        if (machine != null) machine.LastBlank = result;
                        break;

                    case "machine.check":
                        result = Result(parts);
                        if (machine != null) machine.LastCheckResult = result;
                        break;

                    case "machine.last.value":
                    case "machine.blank.value":
                    case "machine.check.value":
                    case "sample.result.value":
                    case "slip.result.value":
                        if (result != null) Readings(parts, result.Values);
                        break;

                    case "machine.calibration":
                        if (machine != null)
                        {
                            machine.HasLastCalibration = true;
                            machine.CalibrationDay = Int(parts, 1);
                            machine.CalibrationCorrectedDrift = Float(parts, 2);
                            machine.CalibrationFlaggedResults = Int(parts, 3);
                            machine.CalibrationAffectedSamples = Int(parts, 4);
                            machine.CalibrationAffectedArchived = Int(parts, 5);
                        }
                        break;

                    case "slip":
                        slip = new RunSnapshot.SlipRecord
                        {
                            Ticket = Int(parts, 1),
                            Sample = Int(parts, 2),
                            MachineInstanceId = Text(parts, 3)
                        };
                        reading.Slips.Add(slip);
                        break;

                    case "slip.at":
                        if (slip != null) slip.Location = Place(parts);
                        break;

                    case "slip.result":
                        result = Result(parts);
                        if (slip != null) slip.Result = result;
                        break;

                    case "bottle":
                        bottle = new RunSnapshot.BottleRecord
                        {
                            Id = Text(parts, 1),
                            Capacity = Int(parts, 2),
                            Charges = Int(parts, 3)
                        };
                        reading.Bottles.Add(bottle);
                        break;

                    case "bottle.at":
                        if (bottle != null) bottle.Location = Place(parts);
                        break;

                    case "pending":
                        reading.Pending.Add(new RunSnapshot.PendingRecord
                        {
                            Sample = Int(parts, 1),
                            ResolveOnDay = Int(parts, 2)
                        });
                        break;

                    case "requeue":
                        reading.Requeues.Add(Int(parts, 1));
                        break;

                    case "report":
                        reading.LastReports.Add(new RunSnapshot.ReportRecord
                        {
                            Sample = Int(parts, 1),
                            RecordTag = Text(parts, 2),
                            Filed = Int(parts, 3),
                            Outcome = Int(parts, 4),
                            MoneyDelta = Float(parts, 5),
                            ReputationDelta = Float(parts, 6),
                            RootCauseCorrect = Bool(parts, 7),
                            FaultName = Text(parts, 8),
                            ActualRootCause = Text(parts, 9),
                            RequeueSample = Bool(parts, 10),
                            Headline = Text(parts, 11)
                        });
                        break;

                    // Anything unrecognised is a line a newer writer added. The schema check above has
                    // already refused a version this build does not know, so reaching here means the
                    // versions agree and the line is noise — dropping it silently would be the quiet
                    // data loss this whole file exists to avoid, but there is no such line to reach.
                    default:
                        refusal = $"That save contains a “{parts[0]}” record this build does not " +
                                  "understand. It cannot be continued.";
                        return false;
                }
            }

            if (!sawSchema)
            {
                refusal = "That save has no format version in it, so it cannot be read safely.";
                return false;
            }

            snapshot = reading;
            return true;
        }

        /// <summary>
        /// Read only what the main menu needs to offer CONTINUE, without resolving a single
        /// definition. Deliberately tolerant of a schema this build cannot load: the menu's job is to
        /// say <i>that</i> a save exists and what it is, and
        /// <see cref="RunSnapshotRestore.TryRebuild"/>'s job is to refuse it with a reason.
        /// </summary>
        public static bool TryReadHeadline(string payload, out RunSaveHeadline headline)
        {
            headline = default;
            if (string.IsNullOrEmpty(payload)) return false;

            int schema = 0;
            long saved = 0;
            string contract = null;
            int day = 0;
            float money = 0f;
            bool sawDay = false;

            foreach (string raw in payload.Split('\n'))
            {
                if (raw.Length == 0) continue;

                string[] parts = raw.Split(Separator);
                switch (parts[0])
                {
                    case "schema": schema = Int(parts, 1); break;
                    case "saved": saved = Long(parts, 1); break;
                    case "contract": contract = Text(parts, 2); break;
                    case "day": day = Int(parts, 1); sawDay = true; break;
                    case "books": money = Float(parts, 1); break;

                    // Everything else is a record, and a headline has no opinion about records. Not
                    // stopping at the first one is deliberate: it would tie this to the order
                    // Encode happens to write the header in, and a menu reads a save once.
                    default: break;
                }
            }

            if (!sawDay) return false;

            headline = new RunSaveHeadline(schema, day, contract, money, saved);
            return true;
        }

        // -- Fields ----------------------------------------------------------------------------------

        private static void Line(StringBuilder text, string tag, params string[] fields)
        {
            text.Append(tag);
            foreach (string field in fields) text.Append(Separator).Append(field);
            text.Append('\n');
        }

        private static string Int(int value) => value.ToString(Invariant);
        private static string UInt(uint value) => value.ToString(Invariant);
        private static string Bool(bool value) => value ? "1" : "0";

        /// <summary>Nine significant digits — the shortest that round-trips a 32-bit float exactly.</summary>
        private static string Float(float value) => value.ToString("G9", Invariant);

        private static string Text(string value)
        {
            if (value == null) return NullToken;

            return value
                .Replace("\\", "\\\\")
                .Replace("\t", "\\t")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private static string Raw(string[] parts, int index) =>
            index >= 0 && index < parts.Length ? parts[index] : null;

        private static string Text(string[] parts, int index)
        {
            string raw = Raw(parts, index);
            if (raw == null || raw == NullToken) return null;

            var text = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] != '\\' || i + 1 >= raw.Length)
                {
                    text.Append(raw[i]);
                    continue;
                }

                i++;
                text.Append(raw[i] switch
                {
                    't' => '\t',
                    'r' => '\r',
                    'n' => '\n',
                    _ => raw[i]
                });
            }
            return text.ToString();
        }

        private static int Int(string[] parts, int index) =>
            int.TryParse(Raw(parts, index), NumberStyles.Integer, Invariant, out int value) ? value : 0;

        private static long Long(string[] parts, int index) =>
            long.TryParse(Raw(parts, index), NumberStyles.Integer, Invariant, out long value) ? value : 0L;

        private static uint UInt(string[] parts, int index) =>
            uint.TryParse(Raw(parts, index), NumberStyles.Integer, Invariant, out uint value) ? value : 0u;

        private static ulong ULong(string[] parts, int index) =>
            ulong.TryParse(Raw(parts, index), NumberStyles.Integer, Invariant, out ulong value) ? value : 0UL;

        private static float Float(string[] parts, int index) =>
            float.TryParse(Raw(parts, index), NumberStyles.Float, Invariant, out float value) ? value : 0f;

        private static bool Bool(string[] parts, int index) => Raw(parts, index) == "1";

        private static RunSnapshot.PlaceRecord Place(string[] parts) => new()
        {
            Kind = Int(parts, 1),
            HolderClientId = ULong(parts, 2),
            ContainerId = Text(parts, 3),
            SlotIndex = Int(parts, 4)
        };

        private static RunSnapshot.ResultRecord Result(string[] parts) => new()
        {
            MachineId = Text(parts, 1),
            DayRun = Int(parts, 2),
            MachineRunIndex = Int(parts, 3),
            VolumeConsumedMl = Float(parts, 4),
            Cost = Float(parts, 5),
            Suspect = Bool(parts, 6),
            IsBlank = Bool(parts, 7),
            IsReference = Bool(parts, 8)
        };

        private static void Readings(string[] parts, List<RunSnapshot.Reading> into)
        {
            for (int i = 1; i + 1 < parts.Length; i += 2)
            {
                into.Add(new RunSnapshot.Reading
                {
                    ElementId = Text(parts, i),
                    Value = Float(parts, i + 1)
                });
            }
        }
    }
}
