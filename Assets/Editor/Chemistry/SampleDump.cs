using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Residue.Chemistry;
using Residue.Data;
using Residue.Editor.Content;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Editor.Chemistry
{
    /// <summary>
    /// Generates one sample and renders its ground truth beside what every instrument in the
    /// catalogue would report from it (§8, M1: "debug console command to dump a generated sample's
    /// ground truth").
    /// <para>
    /// <b>The report is built around instrument blindness</b>, because that is the part of the
    /// chemistry nothing else makes legible. A fault whose signature only moves cooling-curve
    /// quantities leaves six of the seven instruments reading a perfectly clean panel, and the
    /// headline says so in one line before any numbers appear. Balancing a signature means knowing
    /// which gear could ever have found it.
    /// </para>
    /// <para>
    /// <b>Editor only, and it has to stay that way.</b> This is the only code outside the host that
    /// reads <see cref="SampleGroundTruth"/>, and it is legitimate solely because
    /// <c>Residue.Editor</c> never ships. Nothing here may move into a runtime assembly, and nothing
    /// in a runtime assembly was widened to make it writable (hard rule 2).
    /// </para>
    /// <para>
    /// Generation is deliberately separate from display: this returns a string, the window merely
    /// shows it, and <c>SampleDumpTests</c> asserts the same request produces the same string twice.
    /// A determinism criterion that needed a GUI to check would not stay true (hard rule 1).
    /// </para>
    /// </summary>
    public static class SampleDump
    {
        /// <summary>
        /// Flag on an instrument that would report an entirely Normal panel from a faulted sample.
        /// A const so the suite can assert on it without pinning the rest of the layout.
        /// </summary>
        public const string CleanMarker = "<<< WOULD CALL THIS SAMPLE CLEAN >>>";

        /// <summary>Headline label listing the only instruments that can see what is wrong.</summary>
        public const string VisibleOnlyToLabel = "visible only to";

        /// <summary>An abnormal element no instrument in the catalogue reports. A content fault, not a trap.</summary>
        public const string InvisibleLabel = "!! NO INSTRUMENT CAN SEE THIS !!";

        private const int Rule = 88;

        /// <summary>
        /// Build a report against the current <c>ContentTables</c>, projected fresh into memory.
        /// <para>
        /// The tables rather than the generated <c>.asset</c> files, because this is a balancing tool:
        /// you edit a signature and re-dump, without a rebuild in between and without the risk of
        /// reading an asset somebody edited in the Inspector (hard rule 5).
        /// </para>
        /// </summary>
        public static string Build(in SampleDumpRequest request)
        {
            ContentSet content = null;
            try
            {
                content = ContentBuilder.BuildInMemory();
                return Build(content, request);
            }
            catch (Exception e)
            {
                return $"Content failed to build from ContentTables:\n{e}";
            }
            finally
            {
                if (content != null) DestroyContent(content);
            }
        }

        /// <summary>Render against an already-built set. The caller owns the definitions.</summary>
        public static string Build(ContentSet content, in SampleDumpRequest request)
        {
            if (content == null) return "No content set.";

            var profile = content.Profile(request.ProfileId);
            if (profile == null)
                return $"Unknown profile id '{request.ProfileId}'.\nKnown: {ProfileIdList()}";

            FaultDef forced = null;
            if (!string.IsNullOrEmpty(request.FaultId))
            {
                forced = content.Fault(request.FaultId);
                if (forced == null)
                    return $"Unknown fault id '{request.FaultId}'.\nKnown: {FaultIdList()}";
            }

            var generator = new SampleGenerator(FaultPool(content));
            var rng = new Rng(request.Seed);

            var gen = GenerationRequest.Default(profile, request.EquipmentTag, request.Day);
            gen.HoursSinceOilChange = request.HoursSinceOilChange;
            gen.ForcedFault = forced;
            gen.ForcedSeverity01 = request.ForceSeverity ? Mathf.Clamp01(request.Severity01) : -1f;
            gen.ForceHealthy = request.ForceHealthy;
            gen.ForceBorderline = request.ForceBorderline;
            gen.HealthyChance = request.HealthyChance;
            gen.CascadeChance = request.CascadeChance;

            var sample = generator.Generate(gen, ref rng);
            if (sample == null) return "Generator returned nothing - the profile was null.";

            var elements = ScoreElements(content, profile, sample.Truth);
            var outcomes = RunEveryInstrument(content, profile, sample, elements, request);

            var sb = new StringBuilder(8192);
            WriteHeader(sb, request, profile, forced);
            WriteHeadline(sb, elements, outcomes, sample.Truth);
            WriteFaults(sb, sample.Truth);
            WriteElements(sb, elements, sample.Truth);
            WriteInstruments(sb, content, profile, outcomes);
            WriteBlindSpots(sb, elements, outcomes);
            return sb.ToString();
        }

        // -- Model ------------------------------------------------------------------------------------

        /// <summary>One element of ground truth, scored against the profile the sample belongs to.</summary>
        private sealed class ElementRow
        {
            public string Id;
            public string Unit;
            public float TrueValue;
            public bool Tracked;
            public Threshold Threshold;
            public ReadingSeverity Severity;
            public string MovedBy;

            public bool Abnormal => Tracked && Severity != ReadingSeverity.Normal;
        }

        /// <summary>What one instrument would report, and what it could never have reported.</summary>
        private sealed class InstrumentOutcome
        {
            public MachineDef Def;
            public TestResult Result;

            /// <summary>Element ids that appear on this instrument's printout, in panel order.</summary>
            public readonly List<string> Reported = new();

            /// <summary>Severity of each reported value against the sample's profile.</summary>
            public readonly Dictionary<string, ReadingSeverity> ReportedSeverity = new();

            /// <summary>Everything on the instrument's CANNOT DETECT page, present in the vial or not.</summary>
            public readonly List<string> BlindTo = new();

            /// <summary>
            /// Abnormal elements this instrument does not put on its printout — the blindness the
            /// whole report is built around. Blind and off-panel are both here because the player
            /// cannot tell them apart from a slip either; only the reason differs.
            /// </summary>
            public readonly List<string> MissedAbnormal = new();

            /// <summary>The same set as <see cref="MissedAbnormal"/>, as bare ids, for the blind list markers.</summary>
            public readonly HashSet<string> MissedIds = new(StringComparer.Ordinal);

            public ReadingSeverity Verdict = ReadingSeverity.Normal;

            public ReadingSeverity SeverityFor(string elementId) =>
                ReportedSeverity.TryGetValue(elementId, out var s) ? s : ReadingSeverity.Normal;
        }

        // -- Generation-side scoring --------------------------------------------------------------------

        private static List<ElementRow> ScoreElements(
            ContentSet content, EquipmentProfileDef profile, SampleGroundTruth truth)
        {
            var rows = new List<ElementRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // Threshold order first, then anything a fault signature dragged in that the profile does
            // not score. Both are stable lists, so the report never depends on dictionary ordering.
            foreach (var t in profile.Thresholds)
            {
                if (t?.Element == null) continue;
                if (!seen.Add(t.Element.Id)) continue;
                rows.Add(MakeRow(content, profile, truth, t.Element.Id));
            }

            var extras = new List<string>();
            foreach (var kv in truth.TrueValues)
            {
                if (!seen.Contains(kv.Key)) extras.Add(kv.Key);
            }
            extras.Sort(StringComparer.Ordinal);
            foreach (string id in extras) rows.Add(MakeRow(content, profile, truth, id));

            return rows;
        }

        private static ElementRow MakeRow(
            ContentSet content, EquipmentProfileDef profile, SampleGroundTruth truth, string id)
        {
            bool tracked = profile.TryGetThreshold(id, out var threshold);
            float value = truth.GetTrue(id);

            return new ElementRow
            {
                Id = id,
                Unit = content.Element(id)?.Unit ?? string.Empty,
                TrueValue = value,
                Tracked = tracked,
                Threshold = threshold,
                Severity = tracked ? threshold.Evaluate(value) : ReadingSeverity.Normal,
                MovedBy = MovedBy(truth, id)
            };
        }

        /// <summary>Which active fault's signature touches this element, so a movement has a name.</summary>
        private static string MovedBy(SampleGroundTruth truth, string elementId)
        {
            var names = new List<string>();
            foreach (var fault in truth.ActualFaults)
            {
                if (fault == null) continue;
                foreach (var delta in fault.Signature)
                {
                    if (delta?.Element == null || delta.Element.Id != elementId) continue;
                    names.Add(fault.Id);
                    break;
                }
            }
            return names.Count == 0 ? null : string.Join("+", names);
        }

        // -- Measurement side ---------------------------------------------------------------------------

        /// <summary>
        /// Push the sample through every instrument in the catalogue, each on its own clean, freshly
        /// calibrated machine.
        /// <para>
        /// Residue and drift are deliberately excluded. They are real and they matter, but they are a
        /// property of how the lab was run rather than of the chemistry being balanced, and mixing them
        /// in would make two dumps of the same seed differ by which instrument happened to go first.
        /// Instrument noise is left in, because a signature that only clears the threshold on a quiet
        /// run is not a signature.
        /// </para>
        /// </summary>
        private static List<InstrumentOutcome> RunEveryInstrument(
            ContentSet content,
            EquipmentProfileDef profile,
            GeneratedSample sample,
            List<ElementRow> elements,
            in SampleDumpRequest request)
        {
            // Derived from the run seed rather than sharing the generation stream, so adding an
            // instrument to the tables cannot shift the sample the report is about.
            var rng = new Rng(unchecked(request.Seed * 31 + 7919));

            float fullVial = sample.State.VolumeMl;
            var outcomes = new List<InstrumentOutcome>();

            foreach (var row in ContentTables.Machines)
            {
                var def = content.Machine(row.Id);
                if (def == null) continue;

                var outcome = new InstrumentOutcome { Def = def };

                foreach (var e in def.CannotDetect)
                {
                    if (e != null) outcome.BlindTo.Add(e.Id);
                }

                sample.State.VolumeMl = fullVial;
                var machine = new MachineRuntimeState { InstanceId = $"dump:{def.Id}", Def = def };
                outcome.Result = MeasurementPipeline.Run(
                    sample.State, sample.Truth, machine, request.Day, ref rng);

                if (outcome.Result != null)
                {
                    foreach (var e in def.Measures)
                    {
                        if (e == null || def.IsBlindTo(e.Id)) continue;
                        if (!outcome.Result.Values.ContainsKey(e.Id)) continue;
                        if (outcome.ReportedSeverity.ContainsKey(e.Id)) continue;

                        var severity = profile.Evaluate(e.Id, outcome.Result.Values[e.Id]);
                        outcome.Reported.Add(e.Id);
                        outcome.ReportedSeverity[e.Id] = severity;
                        if (severity > outcome.Verdict) outcome.Verdict = severity;
                    }
                }

                // What is wrong with this vial and would not appear on this instrument's slip.
                foreach (var element in elements)
                {
                    if (!element.Abnormal) continue;
                    if (def.CanMeasure(element.Id)) continue;
                    outcome.MissedIds.Add(element.Id);
                    outcome.MissedAbnormal.Add(
                        def.IsBlindTo(element.Id) ? element.Id + " (blind)" : element.Id + " (off-panel)");
                }

                outcomes.Add(outcome);
            }

            sample.State.VolumeMl = fullVial;
            return outcomes;
        }

        // -- Rendering ----------------------------------------------------------------------------------

        private static void WriteHeader(
            StringBuilder sb, in SampleDumpRequest request, EquipmentProfileDef profile, FaultDef forced)
        {
            sb.Append("OILED UP - sample ground truth dump\n");
            sb.Append("Residue > Chemistry > Sample Dump   (Editor only; never reaches a client)\n");
            sb.Append(Line());

            Field(sb, "seed", request.Seed.ToString(CultureInfo.InvariantCulture));
            Field(sb, "profile", $"{profile.Id}  ({profile.DisplayName}, {profile.BaseOilGrade})");
            Field(sb, "equipment tag", string.IsNullOrEmpty(request.EquipmentTag) ? "UNTAGGED" : request.EquipmentTag);
            Field(sb, "day", request.Day.ToString(CultureInfo.InvariantCulture));
            Field(sb, "hours on oil", $"{Num(request.HoursSinceOilChange)} of {Num(profile.DefaultOilChangeHours)}");
            Field(sb, "forced fault", forced == null ? "(rolled from the pool)" : $"{forced.Id}  ({forced.DisplayName})");
            Field(sb, "forced severity", request.ForceSeverity ? Num(Mathf.Clamp01(request.Severity01)) : "(rolled)");
            Field(sb, "force healthy", request.ForceHealthy ? "yes" : "no");
            Field(sb, "force borderline", request.ForceBorderline ? "yes" : "no");
            Field(sb, "healthy chance", Num(request.HealthyChance));
            Field(sb, "cascade chance", Num(request.CascadeChance));
            sb.Append('\n');
        }

        /// <summary>
        /// The answer to "could anyone have found this?", before a single number. Everything below is
        /// the working; this is the finding.
        /// </summary>
        private static void WriteHeadline(
            StringBuilder sb, List<ElementRow> elements, List<InstrumentOutcome> outcomes, SampleGroundTruth truth)
        {
            var worst = ReadingSeverity.Normal;
            ElementRow worstRow = null;
            foreach (var row in elements)
            {
                if (!row.Tracked || row.Severity <= worst) continue;
                worst = row.Severity;
                worstRow = row;
            }

            Section(sb, "HEADLINE");

            Field(sb, "ground truth", truth.IsHealthy
                ? "HEALTHY - no fault present"
                : $"{truth.ActualFaults.Count} fault(s): {FaultIds(truth)}");

            Field(sb, "worst true reading", worstRow == null
                ? Sev(ReadingSeverity.Normal)
                : $"{Sev(worst)}  {worstRow.Id} = {Num(worstRow.TrueValue)} {worstRow.Unit}");

            int clean = 0;
            foreach (var o in outcomes)
            {
                if (o.Verdict == ReadingSeverity.Normal) clean++;
            }

            if (worst == ReadingSeverity.Normal)
            {
                // Nothing is being hidden, so a machine that does not read Normal here is instrument
                // noise clipping a threshold, which is worth seeing in a balancing tool.
                Field(sb, "instruments",
                    $"{clean} of {outcomes.Count} read an entirely Normal panel, and nothing is wrong");
            }
            else
            {
                Field(sb, "BLIND INSTRUMENTS",
                    $"{clean} of {outcomes.Count} would report this sample as entirely NORMAL");

                // Capability, not this run's numbers: an instrument that puts an abnormal element on
                // its printout could have found the fault, whether or not noise nudged the reading
                // over the line. That is the question a signature is balanced against.
                int abnormal = 0;
                foreach (var row in elements)
                {
                    if (row.Abnormal) abnormal++;
                }

                var seers = new List<string>();
                foreach (var o in outcomes)
                {
                    if (o.MissedAbnormal.Count < abnormal) seers.Add(o.Def.Id);
                }

                Field(sb, VisibleOnlyToLabel, seers.Count == 0
                    ? "NOTHING. No instrument in the catalogue reports anything that is wrong here."
                    : string.Join(", ", seers));
            }

            sb.Append('\n');
        }

        private static void WriteFaults(StringBuilder sb, SampleGroundTruth truth)
        {
            Section(sb, "FAULTS");

            if (truth.IsHealthy)
            {
                sb.Append("  none. Every value below is a healthy baseline for this equipment class.\n\n");
                return;
            }

            for (int i = 0; i < truth.ActualFaults.Count; i++)
            {
                var fault = truth.ActualFaults[i];
                if (fault == null) continue;

                float severity = i < truth.FaultSeverities.Count ? truth.FaultSeverities[i] : 0f;
                string role = i == 0 ? "primary" : "cascade";

                sb.Append("  ").Append(Pad(fault.Id, 24)).Append(Pad(fault.DisplayName, 34))
                  .Append(Pad(fault.Severity.ToString(), 12))
                  .Append("progression ").Append(Num(severity)).Append("  (").Append(role).Append(")\n");

                foreach (var delta in fault.Signature)
                {
                    if (delta?.Element == null) continue;
                    sb.Append("      signature  ").Append(Pad(delta.Element.Id, 10))
                      .Append("x").Append(Num(delta.Multiplier));
                    if (!Mathf.Approximately(delta.FlatAdd, 0f))
                        sb.Append("  ").Append(delta.FlatAdd >= 0f ? "+" : "").Append(Num(delta.FlatAdd));
                    sb.Append('\n');
                }

                if (fault.RootCause != null)
                    sb.Append("      root cause ").Append(fault.RootCause.Id).Append('\n');
            }

            sb.Append('\n');
        }

        private static void WriteElements(StringBuilder sb, List<ElementRow> elements, SampleGroundTruth truth)
        {
            Section(sb, "TRUE VALUES  (before any instrument touches them)");

            sb.Append("  ").Append(Pad("ELEMENT", 10)).Append(PadLeft("TRUE", 11)).Append("  ")
              .Append(Pad("UNIT", 7)).Append(PadLeft("BASELINE", 10)).Append(PadLeft("xBASE", 8))
              .Append("  ").Append(Pad("LIMITS", 30)).Append("SEVERITY\n");

            foreach (var row in elements)
            {
                sb.Append("  ").Append(Pad(row.Id, 10)).Append(PadLeft(Num(row.TrueValue), 11)).Append("  ")
                  .Append(Pad(row.Unit, 7));

                if (row.Tracked)
                {
                    float baseline = row.Threshold.Baseline;
                    string factor = Mathf.Approximately(baseline, 0f) ? "-" : Num(row.TrueValue / baseline);
                    sb.Append(PadLeft(Num(baseline), 10)).Append(PadLeft(factor, 8)).Append("  ")
                      .Append(Pad(Limits(row.Threshold), 30)).Append(Sev(row.Severity));
                }
                else
                {
                    sb.Append(PadLeft("-", 10)).Append(PadLeft("-", 8)).Append("  ")
                      .Append(Pad("(this profile does not score it)", 30)).Append("-");
                }

                if (row.MovedBy != null) sb.Append("   <- ").Append(row.MovedBy);
                sb.Append('\n');
            }

            if (truth.Contamination.Count > 0)
                sb.Append("  (vial also carries contamination; presented values are higher than the above)\n");

            sb.Append('\n');
        }

        private static void WriteInstruments(
            StringBuilder sb, ContentSet content, EquipmentProfileDef profile, List<InstrumentOutcome> outcomes)
        {
            Section(sb, "INSTRUMENTS  (each on a clean, freshly calibrated machine, full vial)");

            foreach (var outcome in outcomes)
            {
                sb.Append("  ").Append(Pad(outcome.Def.Id, 16)).Append(outcome.Def.DisplayName).Append('\n');

                if (outcome.Result == null)
                {
                    sb.Append("    (no run: the sample has less than ")
                      .Append(Num(outcome.Def.SampleVolumeMl)).Append(" ml left)\n\n");
                    continue;
                }

                if (outcome.Reported.Count == 0)
                {
                    sb.Append("    reports    nothing at all on this profile\n");
                }
                else
                {
                    for (int i = 0; i < outcome.Reported.Count; i++)
                    {
                        string id = outcome.Reported[i];
                        sb.Append("    ").Append(Pad(i == 0 ? "reports" : string.Empty, 11))
                          .Append(Pad(id, 10))
                          .Append(PadLeft(Num(outcome.Result.Values[id]), 11)).Append("  ")
                          .Append(Pad(content.Element(id)?.Unit ?? string.Empty, 7))
                          .Append(profile.TryGetThreshold(id, out _)
                              ? Sev(outcome.SeverityFor(id))
                              : "-  (this profile does not score it)")
                          .Append('\n');
                    }
                }

                sb.Append("    ").Append(Pad("blind to", 11))
                  .Append(outcome.BlindTo.Count == 0 ? "nothing" : string.Join(", ", BlindDisplay(outcome)));
                if (AnyStarred(outcome)) sb.Append("   (* = abnormal in this vial)");
                sb.Append('\n');

                if (outcome.MissedAbnormal.Count > 0)
                {
                    sb.Append("    ").Append(Pad("MISSES", 11))
                      .Append(string.Join(", ", outcome.MissedAbnormal))
                      .Append("   (abnormal in this vial, absent from this report)\n");
                }

                sb.Append("    ").Append(Pad("verdict", 11)).Append(Sev(outcome.Verdict));
                if (outcome.Verdict == ReadingSeverity.Normal && outcome.MissedAbnormal.Count > 0)
                    sb.Append("   ").Append(CleanMarker);
                sb.Append("\n\n");
            }
        }

        private static void WriteBlindSpots(
            StringBuilder sb, List<ElementRow> elements, List<InstrumentOutcome> outcomes)
        {
            Section(sb, "BLIND SPOTS  (which gear could ever have found this)");

            var abnormal = new List<ElementRow>();
            foreach (var row in elements)
            {
                if (row.Abnormal) abnormal.Add(row);
            }

            if (abnormal.Count == 0)
            {
                sb.Append("  Nothing is abnormal, so nothing is being missed.\n");
                sb.Append("  Per-instrument blind lists above still apply to any fault you force.\n");
                return;
            }

            foreach (var row in abnormal)
            {
                var seenBy = new List<string>();
                foreach (var o in outcomes)
                {
                    if (o.Def.CanMeasure(row.Id)) seenBy.Add(o.Def.Id);
                }

                sb.Append("  ").Append(Pad(row.Id, 10)).Append(Pad(Sev(row.Severity), 10))
                  .Append(seenBy.Count == 0 ? InvisibleLabel : "reported by: " + string.Join(", ", seenBy))
                  .Append('\n');
            }

            var blindMachines = new List<string>();
            foreach (var o in outcomes)
            {
                if (o.MissedAbnormal.Count > 0 && o.Verdict == ReadingSeverity.Normal)
                    blindMachines.Add(o.Def.Id);
            }

            sb.Append('\n');
            if (blindMachines.Count == 0)
            {
                sb.Append("  Every instrument that is blind to something abnormal still flags this sample\n");
                sb.Append("  on some other element. Nothing here can be missed by running the wrong test.\n");
            }
            else
            {
                sb.Append("  Would report an entirely NORMAL panel (")
                  .Append(blindMachines.Count.ToString(CultureInfo.InvariantCulture)).Append(" of ")
                  .Append(outcomes.Count.ToString(CultureInfo.InvariantCulture)).Append("):\n    ")
                  .Append(string.Join(", ", blindMachines)).Append('\n');
            }
        }

        // -- Helpers ------------------------------------------------------------------------------------

        /// <summary>
        /// The instrument's CANNOT DETECT page, with a star against anything that is actually
        /// abnormal in this vial. The stars are the trap: they are what the slip cannot tell you.
        /// </summary>
        private static IEnumerable<string> BlindDisplay(InstrumentOutcome outcome)
        {
            foreach (string id in outcome.BlindTo)
                yield return outcome.MissedIds.Contains(id) ? id + "*" : id;
        }

        private static bool AnyStarred(InstrumentOutcome outcome)
        {
            foreach (string id in outcome.BlindTo)
            {
                if (outcome.MissedIds.Contains(id)) return true;
            }
            return false;
        }

        private static List<FaultDef> FaultPool(ContentSet content)
        {
            // Built in table order rather than from ContentSet's dictionary, so the fault the pool
            // rolls for a given seed is pinned by the tables and not by enumeration order.
            var pool = new List<FaultDef>(ContentTables.Faults.Length);
            foreach (var row in ContentTables.Faults)
            {
                var fault = content.Fault(row.Id);
                if (fault != null) pool.Add(fault);
            }
            return pool;
        }

        private static string FaultIds(SampleGroundTruth truth)
        {
            var ids = new List<string>(truth.ActualFaults.Count);
            foreach (var f in truth.ActualFaults)
            {
                if (f != null) ids.Add(f.Id);
            }
            return string.Join(", ", ids);
        }

        /// <summary>Every profile id in the tables, for an error message that can be acted on.</summary>
        public static string ProfileIdList()
        {
            var ids = new List<string>(ContentTables.Profiles.Length);
            foreach (var row in ContentTables.Profiles) ids.Add(row.Id);
            return string.Join(", ", ids);
        }

        /// <summary>Every fault id in the tables.</summary>
        public static string FaultIdList()
        {
            var ids = new List<string>(ContentTables.Faults.Length);
            foreach (var row in ContentTables.Faults) ids.Add(row.Id);
            return string.Join(", ", ids);
        }

        private static void DestroyContent(ContentSet set)
        {
            foreach (var o in set.Elements.Values) Object.DestroyImmediate(o);
            foreach (var o in set.Causes.Values) Object.DestroyImmediate(o);
            foreach (var o in set.Profiles.Values) Object.DestroyImmediate(o);
            foreach (var o in set.Faults.Values) Object.DestroyImmediate(o);
            foreach (var o in set.Machines.Values) Object.DestroyImmediate(o);
            foreach (var o in set.Customers.Values) Object.DestroyImmediate(o);
        }

        private static string Limits(Threshold t) => t.Mode switch
        {
            ThresholdMode.UpperLimit => $"normal <={Num(t.NormalLimit)}, crit >={Num(t.CautionLimit)}",
            ThresholdMode.LowerLimit => $"normal >={Num(t.NormalLimit)}, crit <={Num(t.CautionLimit)}",
            ThresholdMode.DeviationBand =>
                $"band +/-{Num(t.NormalLimit * 100f)}%, crit +/-{Num(t.CautionLimit * 100f)}%",
            _ => "-"
        };

        private static string Sev(ReadingSeverity s) => s switch
        {
            ReadingSeverity.Caution => "CAUTION",
            ReadingSeverity.Critical => "CRITICAL",
            _ => "normal"
        };

        /// <summary>
        /// Invariant culture, always. A German editor would otherwise render 1.5 as "1,5" and two
        /// dumps of one seed taken on two machines would not compare — which is the determinism
        /// criterion failing in the one place nobody would look.
        /// </summary>
        private static string Num(float v)
        {
            float a = Mathf.Abs(v);
            string format =
                a >= 1000f ? "0.#" :
                a >= 100f ? "0.0" :
                a >= 10f ? "0.00" :
                a >= 1f ? "0.000" : "0.0000";
            return v.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string Pad(string s, int width)
        {
            s ??= string.Empty;
            return s.Length >= width ? s + " " : s + new string(' ', width - s.Length);
        }

        private static string PadLeft(string s, int width)
        {
            s ??= string.Empty;
            return s.Length >= width ? " " + s : new string(' ', width - s.Length) + s;
        }

        private static string Line() => new string('-', Rule) + "\n";

        private static void Section(StringBuilder sb, string title)
        {
            sb.Append(title).Append('\n').Append(Line());
        }

        private static void Field(StringBuilder sb, string label, string value)
        {
            sb.Append("  ").Append(Pad(label, 22)).Append(value).Append('\n');
        }
    }
}
