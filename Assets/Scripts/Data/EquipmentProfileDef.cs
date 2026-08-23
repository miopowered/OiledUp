using System;
using System.Collections.Generic;
using UnityEngine;

namespace Residue.Data
{
    /// <summary>
    /// Per-element limits for one equipment type. Thresholds are per profile on purpose (§4.2):
    /// 60 ppm iron is routine in a haul truck engine and alarming in a hydraulic system.
    /// This is the single largest source of legitimate difficulty in the game.
    /// </summary>
    [Serializable]
    public sealed class Threshold
    {
        [SerializeField] private ElementDef element;
        [SerializeField] private ThresholdMode mode = ThresholdMode.UpperLimit;

        [Tooltip("Typical reading on a HEALTHY unit of this type. Fault multipliers scale this value.\n" +
                 "For LowerLimit elements (TBN) this is the new-oil value. For DeviationBand (viscosity) " +
                 "it is the grade's nominal value.")]
        [SerializeField] private float baseline;

        [Tooltip("Run-to-run spread on a healthy unit, as a fraction of Baseline. Drives sample noise.")]
        [SerializeField, Range(0f, 1f)] private float baselineVariance = 0.25f;

        [Tooltip("UpperLimit: Normal while <= this. LowerLimit: Normal while >= this. " +
                 "DeviationBand: Normal while |value-Baseline|/Baseline <= this (as a fraction, 0.05 = 5%).")]
        [SerializeField] private float normalLimit;

        [Tooltip("Beyond this is Critical. Between NormalLimit and this is Caution.")]
        [SerializeField] private float cautionLimit;

        public ElementDef Element => element;
        public ThresholdMode Mode => mode;
        public float Baseline => baseline;
        public float BaselineVariance => baselineVariance;
        public float NormalLimit => normalLimit;
        public float CautionLimit => cautionLimit;

        /// <summary>Score a measured value against this threshold.</summary>
        public ReadingSeverity Evaluate(float value)
        {
            switch (mode)
            {
                case ThresholdMode.UpperLimit:
                    if (value <= normalLimit) return ReadingSeverity.Normal;
                    return value < cautionLimit ? ReadingSeverity.Caution : ReadingSeverity.Critical;

                case ThresholdMode.LowerLimit:
                    if (value >= normalLimit) return ReadingSeverity.Normal;
                    return value > cautionLimit ? ReadingSeverity.Caution : ReadingSeverity.Critical;

                case ThresholdMode.DeviationBand:
                {
                    if (Mathf.Approximately(baseline, 0f)) return ReadingSeverity.Normal;
                    float deviation = Mathf.Abs(value - baseline) / Mathf.Abs(baseline);
                    if (deviation <= normalLimit) return ReadingSeverity.Normal;
                    return deviation < cautionLimit ? ReadingSeverity.Caution : ReadingSeverity.Critical;
                }

                default:
                    return ReadingSeverity.Normal;
            }
        }
    }

    /// <summary>A class of monitored equipment: heavy diesel engine, industrial gearbox, hydraulic system.</summary>
    [CreateAssetMenu(menuName = "Residue/Equipment Profile", fileName = "Profile_")]
    public sealed class EquipmentProfileDef : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private List<Threshold> thresholds = new();
        [SerializeField] private float defaultOilChangeHours = 500f;

        [Tooltip("e.g. '15W-40'. Used by the wrong-oil-added fault and the reference book.")]
        [SerializeField] private string baseOilGrade;

        private Dictionary<string, Threshold> lookup;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public IReadOnlyList<Threshold> Thresholds => thresholds;
        public float DefaultOilChangeHours => defaultOilChangeHours;
        public string BaseOilGrade => baseOilGrade;

        public bool TryGetThreshold(string elementId, out Threshold threshold)
        {
            BuildLookup();
            return lookup.TryGetValue(elementId, out threshold);
        }

        public bool TryGetThreshold(ElementDef element, out Threshold threshold)
        {
            if (element == null)
            {
                threshold = null;
                return false;
            }
            return TryGetThreshold(element.Id, out threshold);
        }

        /// <summary>Score a measured value, or <see cref="ReadingSeverity.Normal"/> if this profile does not track the element.</summary>
        public ReadingSeverity Evaluate(string elementId, float value)
            => TryGetThreshold(elementId, out var t) ? t.Evaluate(value) : ReadingSeverity.Normal;

        private void BuildLookup()
        {
            if (lookup != null && lookup.Count == thresholds.Count) return;
            lookup = new Dictionary<string, Threshold>(thresholds.Count);
            foreach (var t in thresholds)
            {
                if (t?.Element == null) continue;
                lookup[t.Element.Id] = t;
            }
        }

        private void OnValidate() => lookup = null;
    }
}
