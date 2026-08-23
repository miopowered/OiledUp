using System.Collections.Generic;
using Residue.Data;

namespace Residue.Chemistry
{
    /// <summary>
    /// Live state of one placed instrument. Server owned. Holds the two things that quietly
    /// corrupt results: leftover residue from the previous sample (§5.2) and calibration drift (§5.3).
    /// </summary>
    public sealed class MachineRuntimeState
    {
        public string InstanceId;
        public MachineDef Def;

        /// <summary>
        /// What the previous samples left behind, keyed by element id. Transfers into the next
        /// sample's readings. Zeroed only by <see cref="Clean"/>, which costs solvent and player time.
        /// </summary>
        public readonly Dictionary<string, float> Residue = new();

        /// <summary>Signed calibration error as a fraction. 0.18 means every reading comes out 18% high.</summary>
        public float DriftPercent;

        /// <summary>Direction drift walks today. Re-rolled each day so a machine is not predictably biased.</summary>
        public int DriftSign = 1;

        public int RunIndex;
        public int RunsSinceClean;
        public int RunsSinceCalibration;

        /// <summary>Run index at which the current drift episode began — the retroactive suspicion window (§5.3).</summary>
        public int DriftStartedAtRunIndex;

        public int LastCalibratedDay = -1;

        /// <summary>Solvent units consumed by a clean. Purchasable consumable, so skipping it is tempting.</summary>
        public const float SolventPerClean = 1f;

        /// <summary>How much of the machine's residue transfers into the next sample's readings.</summary>
        public const float ResidueTransferRate = 1f;

        public float GetResidue(string elementId) => Residue.TryGetValue(elementId, out var v) ? v : 0f;

        /// <summary>Call at the start of each in-game day. Re-rolls which way drift walks.</summary>
        public void BeginDay(ref Rng rng)
        {
            DriftSign = rng.Chance(0.5f) ? 1 : -1;
        }

        /// <summary>Wash station action. Zeroes carryover. 20-40 s of player time plus solvent.</summary>
        public void Clean()
        {
            Residue.Clear();
            RunsSinceClean = 0;
        }

        /// <summary>
        /// Recalibrate after measuring error with a certified reference sample.
        /// Returns the drift that was corrected, so the UI can flag every result since
        /// <see cref="DriftStartedAtRunIndex"/> as suspect.
        /// </summary>
        public float Calibrate(int day)
        {
            float corrected = DriftPercent;
            DriftPercent = 0f;
            RunsSinceCalibration = 0;
            DriftStartedAtRunIndex = RunIndex;
            LastCalibratedDay = day;
            return corrected;
        }

        /// <summary>Advance wear counters after a run. Called by <see cref="MeasurementPipeline"/>.</summary>
        internal void RegisterRun()
        {
            if (DriftPercent == 0f) DriftStartedAtRunIndex = RunIndex;
            RunIndex++;
            RunsSinceClean++;
            RunsSinceCalibration++;
            DriftPercent += Def.CalibrationDriftPerRun * DriftSign;
        }
    }
}
