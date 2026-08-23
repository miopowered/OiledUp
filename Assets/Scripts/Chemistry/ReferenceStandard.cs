using System.Collections.Generic;
using Residue.Data;

namespace Residue.Chemistry
{
    /// <summary>
    /// A certified reference material: an oil blended to published values, bought by the ampoule and
    /// pushed through an instrument so that the gap between the certificate and the readout measures
    /// the instrument's calibration error (§5.3).
    /// <para>
    /// The certified figures are derived from the healthy baselines the profiles already publish
    /// rather than authored beside them. That is what makes drift fair under hard rule 3: a standard
    /// is only a tell if the player can check the answer against something, and the something has to
    /// be a number they can already look up in the manual. Authoring a second set would let the
    /// certificate and the limits it is measured against quietly disagree — the one failure a
    /// reference sample must not have.
    /// </para>
    /// Deliberately not a <c>ScriptableObject</c>. This is not balance data; it is a reading of the
    /// balance data, and it should be rebuilt whenever that changes rather than remembered.
    /// </summary>
    public sealed class ReferenceStandard
    {
        private readonly Dictionary<string, float> certified = new();

        /// <summary>Printed on the ampoule and on the certificate the terminal shows.</summary>
        public string Id { get; }

        public IReadOnlyDictionary<string, float> Certified => certified;

        private ReferenceStandard(string id) => Id = id;

        public bool TryGet(string elementId, out float value) => certified.TryGetValue(elementId, out value);

        /// <summary>
        /// Blend the house standard from every profile's healthy baselines. An element several
        /// equipment types track is certified at the mean of their baselines, so one bottle serves
        /// the whole bench — which is what a multi-element check standard is for.
        /// </summary>
        public static ReferenceStandard FromProfiles(IReadOnlyList<EquipmentProfileDef> profiles,
                                                     string id = "CRM-1")
        {
            var standard = new ReferenceStandard(id);
            if (profiles == null) return standard;

            var totals = new Dictionary<string, float>();
            var counts = new Dictionary<string, int>();

            foreach (var profile in profiles)
            {
                if (profile == null) continue;

                foreach (var threshold in profile.Thresholds)
                {
                    // A certified value of zero has no error to express — measured over certified is
                    // undefined — so an element with no positive baseline is simply not on this
                    // certificate and no instrument is judged on it.
                    if (threshold?.Element == null || threshold.Baseline <= 0f) continue;

                    totals.TryGetValue(threshold.Element.Id, out float sum);
                    counts.TryGetValue(threshold.Element.Id, out int seen);
                    totals[threshold.Element.Id] = sum + threshold.Baseline;
                    counts[threshold.Element.Id] = seen + 1;
                }
            }

            foreach (var kv in totals) standard.certified[kv.Key] = kv.Value / counts[kv.Key];
            return standard;
        }
    }
}
