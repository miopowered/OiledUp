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

        // ---- Provenance (#32) ----
        //
        // Where the oil actually came from, which is hidden information for exactly the reason the
        // chemistry is: the player has to work it out from the note and the readings, and a client
        // that could read it would be reading the answer. It rides in this type rather than in a
        // second host-only map so there is still only one vault to keep shut — Residue.Net references
        // Residue.Gameplay and could name a public dictionary on LabState, but there is no expression
        // in any assembly that yields one of these.
        //
        // None of it is chemistry and none of it touches a reading. Hard rule 1 is not bent here: a
        // careless customer is a customer whose paperwork and drum discipline are worth checking, not
        // one whose instruments lie.

        /// <summary>
        /// The tank the oil in this vial was really drawn from, whatever the label survived to say.
        /// Equal to <see cref="SampleState.EquipmentTag"/> for every vial with a legible label.
        /// </summary>
        public string TrueTankTag;

        /// <summary>
        /// The line of its delivery note this vial really answers, or -1 for one the note never
        /// mentioned — #32's "unlisted sample". Indices are into
        /// <c>DeliveryNote.Lines</c> as it was printed.
        /// </summary>
        public int TrueNoteLine = -1;

        /// <summary>
        /// The other vial this one shares a drum with (§6.1), or <see cref="SampleId.None"/>.
        /// <para>
        /// Symmetric: both halves of a split draw name each other. The pair are the same oil down to
        /// the last true value, so the tell is that they measure the same — which is something the
        /// player can run twice and compare, not something they have to intuit.
        /// </para>
        /// </summary>
        public SampleId SameDrumAs = SampleId.None;

        /// <summary>One draw was bottled twice and booked as two. The trap §6.1 names outright.</summary>
        public bool DrawnFromOneDrum => SameDrumAs.IsValid;

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
