using System.Collections.Generic;

namespace Residue.Chemistry
{
    /// <summary>
    /// One completed machine run against one sample. This is the player-facing record —
    /// it holds MEASURED values (true value plus residue carryover, plus noise, scaled by
    /// calibration drift), never ground truth.
    /// </summary>
    public sealed class TestResult
    {
        /// <summary><see cref="Residue.Data.MachineDef.Id"/> of the instrument that produced this.</summary>
        public string MachineId;

        /// <summary>In-game day the run completed. Used by retroactive drift suspicion (§5.3).</summary>
        public int DayRun;

        /// <summary>Monotonic per-machine run counter, so drift at time-of-run can be reconstructed.</summary>
        public int MachineRunIndex;

        /// <summary>Measured values keyed by <see cref="Residue.Data.ElementDef.Id"/>.</summary>
        public Dictionary<string, float> Values = new();

        /// <summary>Millilitres this run consumed from the sample.</summary>
        public float VolumeConsumedMl;

        /// <summary>Consumables cost charged for this run.</summary>
        public float Cost;

        /// <summary>
        /// Set true when a later calibration check reveals the machine was drifting when this ran (§5.3).
        /// Drives the "every verdict you filed since it started drifting is suspect" list.
        /// </summary>
        public bool Suspect;

        /// <summary>True if this was a solvent blank rather than a real sample — it reads residue directly (§5.2).</summary>
        public bool IsBlank;

        /// <summary>
        /// True if this run measured a certified reference standard rather than a customer's oil.
        /// The values are known in advance, so the difference is the instrument's error (§5.3).
        /// </summary>
        public bool IsReference;

        public bool TryGet(string elementId, out float value) => Values.TryGetValue(elementId, out value);
    }
}
