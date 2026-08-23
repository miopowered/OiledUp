using System.Collections.Generic;
using UnityEngine;

namespace Residue.Data
{
    /// <summary>
    /// A laboratory instrument (§4.5). The <see cref="CannotDetect"/> list is load-bearing:
    /// it is how the gear-spalling trap works — the ICP is blind to ferrous particles too large
    /// for the plasma, so a clean ICP result is not a clean sample.
    /// </summary>
    [CreateAssetMenu(menuName = "Residue/Machine", fileName = "Machine_")]
    public sealed class MachineDef : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Header("Run cost")]
        [SerializeField] private float runTimeSeconds = 180f;

        [Tooltip("Millilitres consumed per run. A 100 ml sample cannot take the full panel — " +
                 "test ordering is a real decision (§4.5).")]
        [SerializeField] private float sampleVolumeMl = 5f;

        [SerializeField] private float costPerRun;

        [Header("Capability")]
        [SerializeField] private List<ElementDef> measures = new();

        [Tooltip("Elements this machine physically cannot see even when present. ICP: particles >8um.")]
        [SerializeField] private List<ElementDef> cannotDetect = new();

        [Header("Accuracy")]
        [Tooltip("Random measurement spread as a fraction of the true value.")]
        [SerializeField, Range(0f, 0.5f)] private float baseNoisePercent = 0.03f;

        [Tooltip("Calibration error accumulated per run, as a fraction. Sign is randomised per machine per day.")]
        [SerializeField, Range(0f, 0.1f)] private float calibrationDriftPerRun = 0.004f;

        [Tooltip("Fraction of a run's true values left behind in the machine as residue (§5.2).")]
        [SerializeField, Range(0f, 1f)] private float contaminationCarryoverPercent = 0.05f;

        [Header("Placement")]
        [SerializeField] private bool requiresFumeHood;
        [SerializeField] private bool requiresPreheat;
        [SerializeField] private float preheatTargetC = 100f;

        [Tooltip("Concurrent samples. Autosampler upgrade raises this.")]
        [SerializeField, Min(1)] private int slots = 1;

        [SerializeField] private int purchaseCost;

        [Tooltip("Footprint in 0.5 m bench grid cells (§5.5).")]
        [SerializeField] private Vector2Int footprint = Vector2Int.one;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public float RunTimeSeconds => runTimeSeconds;
        public float SampleVolumeMl => sampleVolumeMl;
        public float CostPerRun => costPerRun;
        public IReadOnlyList<ElementDef> Measures => measures;
        public IReadOnlyList<ElementDef> CannotDetect => cannotDetect;
        public float BaseNoisePercent => baseNoisePercent;
        public float CalibrationDriftPerRun => calibrationDriftPerRun;
        public float ContaminationCarryoverPercent => contaminationCarryoverPercent;
        public bool RequiresFumeHood => requiresFumeHood;
        public bool RequiresPreheat => requiresPreheat;
        public float PreheatTargetC => preheatTargetC;
        public int Slots => slots;
        public int PurchaseCost => purchaseCost;
        public Vector2Int Footprint => footprint;

        /// <summary>True if this machine reports a value for the element at all.</summary>
        public bool CanMeasure(string elementId)
        {
            if (IsBlindTo(elementId)) return false;
            foreach (var e in measures)
            {
                if (e != null && e.Id == elementId) return true;
            }
            return false;
        }

        /// <summary>True if the element is present in the sample but invisible to this instrument.</summary>
        public bool IsBlindTo(string elementId)
        {
            foreach (var e in cannotDetect)
            {
                if (e != null && e.Id == elementId) return true;
            }
            return false;
        }

        public override string ToString() => $"Machine:{Id}";
    }
}
