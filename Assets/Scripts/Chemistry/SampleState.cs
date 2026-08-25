using System.Collections.Generic;
using Residue.Data;

namespace Residue.Chemistry
{
    /// <summary>
    /// Everything about a sample that a client is allowed to know. Safe to replicate in full.
    /// Ground truth lives in <see cref="SampleGroundTruth"/>, which the host holds separately.
    /// </summary>
    public sealed class SampleState
    {
        public SampleId Id;

        /// <summary>
        /// Printed on the vial label, e.g. "RIG-7 COMPRESSOR B". This is also what the lab files the
        /// sample under: the tag arrives with the bottle rather than being transcribed (#73).
        /// </summary>
        public string EquipmentTag;

        public EquipmentProfileDef Profile;

        public float HoursSinceOilChange;

        /// <summary>Free text from the field. May be wrong, vague, or absent — that is the point.</summary>
        public string FieldTechNote;

        public int CollectedDay;

        /// <summary>
        /// The earlier sample this is a re-draw of, when the player filed MONITOR and the unit
        /// stayed in service (§5.4). <see cref="SampleId.None"/> for a first draw.
        /// <para>
        /// Equipment tags repeat legitimately across a contract, so identity has to be explicit —
        /// and §6.1 wants the player to be able to see a unit's history rather than infer it from
        /// a label they may have transcribed wrong.
        /// </para>
        /// </summary>
        public SampleId ResampleOf = SampleId.None;

        public bool IsResample => ResampleOf.IsValid;

        // ---- Physical ----

        /// <summary>Remaining volume. Starts at 100 ml; the full panel costs more than that (§4.5).</summary>
        public float VolumeMl = 100f;

        public float TemperatureC = 20f;

        /// <summary>False until agitated. Running an unsettled sample skews heavy particulates.</summary>
        public bool IsSettled;

        public SampleLocation Location;

        // ---- Player-facing ----

        /// <summary>
        /// Results the player has actually filed at the terminal, by carrying the printout there.
        /// <para>
        /// An instrument finishing a run does NOT put anything here. A reading exists physically on
        /// the machine and on its slip; it becomes part of the record when someone walks it to the
        /// desk. A slip left on a bench is a test you paid for and cannot use.
        /// </para>
        /// </summary>
        public readonly List<TestResult> Results = new();

        public Verdict? FiledVerdict;

        /// <summary>Optional. A correct root cause pays a significant bonus (§5.4).</summary>
        public RootCauseDef FiledRootCause;

        public int FiledOnDay = -1;

        /// <summary>True once the consequence has been resolved and reported back to the player.</summary>
        public bool ConsequenceResolved;

        /// <summary>
        /// How far along the §5.1 chain this sample is. Derived from the fields above rather than
        /// stored — see <see cref="SampleLifecycle"/> for why.
        /// </summary>
        public SampleStage Stage => SampleLifecycle.StageOf(this);

        /// <summary>
        /// What the lab calls this sample, on a screen, on a printout and in an end-of-day report.
        /// <para>
        /// The same string as <see cref="EquipmentTag"/> since #73 removed booking-in: the tag comes
        /// off the label and there is no second, player-typed name it could disagree with. It stays
        /// a distinct member because "what the record is filed under" is what every caller actually
        /// means, and because a sample with no label still has to be nameable in a refusal.
        /// </para>
        /// </summary>
        public string RecordTag =>
            string.IsNullOrEmpty(EquipmentTag) ? $"UNLABELLED {Id}" : EquipmentTag;

        public bool HasVolumeFor(MachineDef machine) => machine != null && VolumeMl >= machine.SampleVolumeMl;

        /// <summary>Most recent measured value for an element across all runs, newest first.</summary>
        public bool TryGetLatest(string elementId, out float value, out TestResult source)
        {
            for (int i = Results.Count - 1; i >= 0; i--)
            {
                var r = Results[i];
                if (r.IsBlank) continue;
                if (r.TryGet(elementId, out value))
                {
                    source = r;
                    return true;
                }
            }
            value = 0f;
            source = null;
            return false;
        }

        /// <summary>
        /// Score every element this sample has a reading for against its profile.
        /// This is what the terminal colours red/amber/green.
        /// </summary>
        public ReadingSeverity WorstReading()
        {
            var worst = ReadingSeverity.Normal;
            if (Profile == null) return worst;

            foreach (var t in Profile.Thresholds)
            {
                if (t?.Element == null) continue;
                if (!TryGetLatest(t.Element.Id, out float v, out _)) continue;
                var s = t.Evaluate(v);
                if (s > worst) worst = s;
            }
            return worst;
        }

        public override string ToString() => $"{Id} [{EquipmentTag}]";
    }
}
