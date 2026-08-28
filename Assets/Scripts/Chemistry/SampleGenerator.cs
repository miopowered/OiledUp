using System.Collections.Generic;
using Residue.Data;
using UnityEngine;

namespace Residue.Chemistry
{
    /// <summary>Paired output of <see cref="SampleGenerator"/>: what the player sees, and what is actually true.</summary>
    public sealed class GeneratedSample
    {
        public SampleState State;
        public SampleGroundTruth Truth;
    }

    /// <summary>Inputs for one sample. Defaults produce a routine sample with a modest chance of a fault.</summary>
    public struct GenerationRequest
    {
        public EquipmentProfileDef Profile;
        public string EquipmentTag;
        public int CollectedDay;
        public float HoursSinceOilChange;

        /// <summary>Force this exact fault. Null rolls from the pool.</summary>
        public FaultDef ForcedFault;

        /// <summary>0..1 to pin progression. Negative rolls from the fault's severity band.</summary>
        public float ForcedSeverity01;

        /// <summary>Guarantee a clean sample regardless of the pool.</summary>
        public bool ForceHealthy;

        /// <summary>
        /// Ambiguity budget (§6.3). Tunes progression so the sample lands in the Caution band —
        /// the "iron elevated, silicon borderline, 8 ml left" decision that stays hard even for a
        /// player who knows the table cold.
        /// </summary>
        public bool ForceBorderline;

        /// <summary>Probability of no fault at all when nothing is forced.</summary>
        public float HealthyChance;

        /// <summary>Probability that a fault also drags in one of its <see cref="FaultDef.CanCause"/> entries.</summary>
        public float CascadeChance;

        public static GenerationRequest Default(EquipmentProfileDef profile, string tag, int day) => new()
        {
            Profile = profile,
            EquipmentTag = tag,
            CollectedDay = day,
            HoursSinceOilChange = 250f,
            ForcedSeverity01 = -1f,
            HealthyChance = 0.35f,
            CascadeChance = 0.25f
        };
    }

    /// <summary>
    /// Builds samples from an equipment profile plus a fault (§4.3). Fully deterministic given the
    /// same <see cref="Rng"/> state, so a run seed reproduces a whole contract and tests are stable.
    /// <para><b>Server only.</b></para>
    /// </summary>
    public sealed class SampleGenerator
    {
        private readonly List<FaultDef> pool = new();
        private int nextId;

        /// <summary>How far a LowerLimit element (TBN, additives) falls over a full oil-change interval.</summary>
        private const float EndOfLifeRetention = 0.62f;

        public SampleGenerator(IEnumerable<FaultDef> faultPool, int firstSampleId = 1)
        {
            if (faultPool != null)
            {
                foreach (var f in faultPool)
                {
                    if (f != null) pool.Add(f);
                }
            }
            nextId = Mathf.Max(1, firstSampleId);
        }

        /// <summary>
        /// The id the next generated sample will carry.
        /// <para>
        /// Saved with a run (#49) and handed back through <c>firstSampleId</c> on load. Without it a
        /// continued contract would start counting from 1 again and mint ids that records already in
        /// the vault are filed under — two samples with one identity, which the host's registry
        /// resolves by silently overwriting the older one.
        /// </para>
        /// </summary>
        public int NextSampleId => nextId;

        public GeneratedSample Generate(in GenerationRequest request, ref Rng rng)
        {
            var profile = request.Profile;
            if (profile == null) return null;

            var truth = new SampleGroundTruth { Id = new SampleId(nextId) };
            var state = new SampleState
            {
                Id = truth.Id,
                EquipmentTag = string.IsNullOrEmpty(request.EquipmentTag) ? "UNTAGGED" : request.EquipmentTag,
                Profile = profile,
                CollectedDay = request.CollectedDay,
                HoursSinceOilChange = request.HoursSinceOilChange,
                VolumeMl = 100f,
                TemperatureC = 20f,
                IsSettled = false,
                Location = SampleLocation.InCrate("inbound", -1)
            };
            nextId++;

            BuildHealthyBaseline(profile, request.HoursSinceOilChange, truth.TrueValues, ref rng);

            var fault = ChooseFault(in request, profile, ref rng);
            if (fault != null)
            {
                float severity = ResolveSeverity(in request, fault, profile, truth.TrueValues, ref rng);
                ApplyFault(fault, severity, truth, profile);

                if (request.CascadeChance > 0f && rng.Chance(request.CascadeChance))
                {
                    var secondary = PickCascade(fault, profile, ref rng);
                    if (secondary != null)
                    {
                        // A consequence runs behind its cause, so it reads weaker.
                        ApplyFault(secondary, severity * rng.Range(0.4f, 0.8f), truth, profile);
                    }
                }
            }

            return new GeneratedSample { State = state, Truth = truth };
        }

        /// <summary>Healthy readings for this equipment class, spread by each threshold's natural variance.</summary>
        private static void BuildHealthyBaseline(
            EquipmentProfileDef profile,
            float hoursSinceOilChange,
            Dictionary<string, float> into,
            ref Rng rng)
        {
            float oilLife = profile.DefaultOilChangeHours > 0f
                ? Mathf.Clamp01(hoursSinceOilChange / profile.DefaultOilChangeHours)
                : 0f;

            foreach (var t in profile.Thresholds)
            {
                if (t?.Element == null) continue;

                float value = t.Baseline * (1f + rng.NextGaussian(0f, t.BaselineVariance));

                switch (t.Mode)
                {
                    case ThresholdMode.LowerLimit:
                        // TBN and additive packs deplete as the oil ages. Healthy oil at end of
                        // interval should sit just above NormalLimit, not comfortably above it.
                        value *= Mathf.Lerp(1f, EndOfLifeRetention, oilLife);
                        break;

                    case ThresholdMode.UpperLimit:
                        // Wear metals accumulate with hours on the oil.
                        value *= Mathf.Lerp(0.55f, 1f, oilLife);
                        break;
                }

                into[t.Element.Id] = ClampToNormalBand(t, Mathf.Max(0f, value));
            }
        }

        /// <summary>
        /// Keep a healthy reading inside its Normal band.
        /// <para>
        /// A healthy unit must read Normal — that is what "healthy" means, and §5.6 asserts it. Without
        /// this, a three-sigma tail on a tight threshold (viscosity's +/-5% band, or a depleted additive
        /// pack near end of oil life) would occasionally flag a perfectly good machine, and the player
        /// would be punished for something no test could have revealed.
        /// </para>
        /// Faults then scale <i>this</i> value, so they still push out of the band normally.
        /// </summary>
        private static float ClampToNormalBand(Threshold t, float value)
        {
            const float margin = 0.92f;

            switch (t.Mode)
            {
                case ThresholdMode.UpperLimit:
                    // Floor as well as cap. A three-sigma low tail can drive a small baseline to zero,
                    // and a fault whose signature is mostly a multiplier would then barely register —
                    // making an Imminent fault silently undetectable on an unlucky roll.
                    return Mathf.Clamp(value, t.Baseline * 0.35f, t.NormalLimit * margin);

                case ThresholdMode.LowerLimit:
                    return Mathf.Max(value, t.NormalLimit / margin);

                case ThresholdMode.DeviationBand:
                {
                    if (Mathf.Approximately(t.Baseline, 0f)) return value;
                    float maxDeviation = Mathf.Abs(t.Baseline) * t.NormalLimit * margin;
                    return Mathf.Clamp(value, t.Baseline - maxDeviation, t.Baseline + maxDeviation);
                }

                default:
                    return value;
            }
        }

        private FaultDef ChooseFault(in GenerationRequest request, EquipmentProfileDef profile, ref Rng rng)
        {
            if (request.ForceHealthy) return null;
            if (request.ForcedFault != null) return request.ForcedFault;
            if (rng.Chance(request.HealthyChance)) return null;

            var candidates = new List<FaultDef>();
            foreach (var f in pool)
            {
                if (f.IsValidOn(profile)) candidates.Add(f);
            }
            return candidates.Count == 0 ? null : candidates[rng.Range(0, candidates.Count)];
        }

        private static FaultDef PickCascade(FaultDef cause, EquipmentProfileDef profile, ref Rng rng)
        {
            var options = new List<FaultDef>();
            foreach (var f in cause.CanCause)
            {
                if (f != null && f.IsValidOn(profile)) options.Add(f);
            }
            return options.Count == 0 ? null : options[rng.Range(0, options.Count)];
        }

        private static float ResolveSeverity(
            in GenerationRequest request,
            FaultDef fault,
            EquipmentProfileDef profile,
            Dictionary<string, float> healthy,
            ref Rng rng)
        {
            if (request.ForcedSeverity01 >= 0f) return Mathf.Clamp01(request.ForcedSeverity01);
            if (request.ForceBorderline) return FindBorderlineSeverity(fault, profile, healthy, ref rng);

            return fault.Severity switch
            {
                FaultSeverity.Benign => rng.Range(0.10f, 0.30f),
                FaultSeverity.Developing => rng.Range(0.35f, 0.65f),
                FaultSeverity.Imminent => rng.Range(0.72f, 1.00f),
                _ => rng.Range(0.3f, 0.7f)
            };
        }

        /// <summary>
        /// Scan progression for the band where this fault reads Caution but not Critical, and land
        /// inside it. If no such band exists (the fault jumps straight past caution), fall back to a
        /// mid-range roll rather than silently producing a Critical.
        /// </summary>
        private static float FindBorderlineSeverity(
            FaultDef fault,
            EquipmentProfileDef profile,
            Dictionary<string, float> healthy,
            ref Rng rng)
        {
            const int steps = 24;
            float lowest = -1f, highest = -1f;
            var scratch = new Dictionary<string, float>(healthy.Count);

            for (int i = 1; i <= steps; i++)
            {
                float sev = i / (float)steps;

                scratch.Clear();
                foreach (var kv in healthy) scratch[kv.Key] = kv.Value;
                ApplySignatureTo(scratch, fault, sev, profile);

                if (WorstOf(profile, scratch) != ReadingSeverity.Caution) continue;
                if (lowest < 0f) lowest = sev;
                highest = sev;
            }

            if (lowest < 0f) return rng.Range(0.35f, 0.65f);
            return rng.Range(lowest, Mathf.Max(lowest, highest));
        }

        private static void ApplyFault(FaultDef fault, float severity01, SampleGroundTruth truth, EquipmentProfileDef profile)
        {
            ApplySignatureTo(truth.TrueValues, fault, severity01, profile);
            truth.ActualFaults.Add(fault);
            truth.FaultSeverities.Add(severity01);
        }

        /// <summary>
        /// Apply a fault signature in place. Each delta scales the element's CURRENT value, so
        /// stacked faults compound the way a cascade should — dirt ingress raising iron, then
        /// abrasive wear raising it again.
        /// </summary>
        private static void ApplySignatureTo(
            Dictionary<string, float> values,
            FaultDef fault,
            float severity01,
            EquipmentProfileDef profile)
        {
            foreach (var delta in fault.Signature)
            {
                if (delta?.Element == null) continue;
                string id = delta.Element.Id;

                // Elements outside the profile's threshold list (large ferrous debris, flashpoint)
                // start at zero and are carried entirely by FlatAdd.
                values.TryGetValue(id, out float current);
                if (current <= 0f && !profile.TryGetThreshold(id, out _)) current = 0f;

                values[id] = Mathf.Max(0f, delta.Apply(current, severity01));
            }
        }

        private static ReadingSeverity WorstOf(EquipmentProfileDef profile, Dictionary<string, float> values)
        {
            var worst = ReadingSeverity.Normal;
            foreach (var kv in values)
            {
                var s = profile.Evaluate(kv.Key, kv.Value);
                if (s > worst) worst = s;
            }
            return worst;
        }
    }
}
