using System.Collections.Generic;
using Residue.Data;

namespace Residue.Chemistry
{
    /// <summary>
    /// SERVER ONLY. The actual chemistry of a sample and what is actually wrong with it.
    /// <para>
    /// <b>This type must never be referenced from a replicated payload.</b> The spec (§4.4) kept these
    /// fields on <see cref="SampleState"/> behind a comment, with an editor test to catch leaks. A comment
    /// is not a boundary — one careless <c>[SerializeField]</c> or a reflection-based serializer defeats it.
    /// Splitting ground truth into its own type means the host can hold
    /// <c>Dictionary&lt;SampleId, SampleGroundTruth&gt;</c> in a field that no RPC signature can reach,
    /// and the leak test becomes a structural assertion instead of a string search.
    /// </para>
    /// Nothing in <c>Residue.Net</c> may take this type as a parameter. See ARCHITECTURE.md.
    /// </summary>
    public sealed class SampleGroundTruth
    {
        public SampleId Id;

        /// <summary>What is actually wrong. Empty means a genuinely healthy unit.</summary>
        public readonly List<FaultDef> ActualFaults = new();

        /// <summary>How far along each fault is, 0..1, parallel to <see cref="ActualFaults"/>.</summary>
        public readonly List<float> FaultSeverities = new();

        /// <summary>
        /// The real concentration of every element, before any instrument touches it.
        /// Keyed by <see cref="ElementDef.Id"/>.
        /// </summary>
        public readonly Dictionary<string, float> TrueValues = new();

        /// <summary>
        /// Contamination already carried by the vial itself — from a dirty decant, a reused pipette,
        /// or a prep error. Added on top of true values at measurement time (§5.2).
        /// </summary>
        public readonly Dictionary<string, float> Contamination = new();

        /// <summary>
        /// The fault the generator considers the discriminating one for scoring the root-cause bonus.
        /// Null when the sample is healthy.
        /// </summary>
        public FaultDef PrimaryFault => ActualFaults.Count > 0 ? ActualFaults[0] : null;

        public bool IsHealthy => ActualFaults.Count == 0;

        /// <summary>Worst severity across all present faults, for consequence resolution (§5.4).</summary>
        public FaultSeverity WorstSeverity
        {
            get
            {
                var worst = FaultSeverity.Benign;
                foreach (var f in ActualFaults)
                {
                    if (f != null && f.Severity > worst) worst = f.Severity;
                }
                return worst;
            }
        }

        public float GetTrue(string elementId) => TrueValues.TryGetValue(elementId, out var v) ? v : 0f;

        public float GetContamination(string elementId)
            => Contamination.TryGetValue(elementId, out var v) ? v : 0f;

        /// <summary>True value plus whatever the vial itself is carrying.</summary>
        public float GetPresented(string elementId) => GetTrue(elementId) + GetContamination(elementId);
    }
}
