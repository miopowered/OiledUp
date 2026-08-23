namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// What a recalibration took out of an instrument, and how far back into already-filed work that
    /// error reaches (§5.3).
    /// <para>
    /// §5.3 calls this the dread: finding an instrument drifted 18% high means every verdict filed
    /// since it started drifting is suspect. The moment is over in a second and the player is usually
    /// standing at the machine when it happens, so the count is kept rather than announced and
    /// forgotten — the terminal has to still be able to show it after the walk back to the desk.
    /// </para>
    /// </summary>
    public readonly struct CalibrationOutcome
    {
        public readonly int Day;

        /// <summary>Signed error removed. 0.18 means the instrument had been reading 18% high.</summary>
        public readonly float CorrectedDrift;

        /// <summary>Individual runs newly marked suspect.</summary>
        public readonly int FlaggedResults;

        /// <summary>Samples with at least one run inside the drift window.</summary>
        public readonly int AffectedSamples;

        /// <summary>Of those, the ones that already have a verdict on file. This is the list that hurts.</summary>
        public readonly int AffectedArchived;

        public CalibrationOutcome(int day, float correctedDrift, int flaggedResults,
                                  int affectedSamples, int affectedArchived)
        {
            Day = day;
            CorrectedDrift = correctedDrift;
            FlaggedResults = flaggedResults;
            AffectedSamples = affectedSamples;
            AffectedArchived = affectedArchived;
        }

        /// <summary>True when the correction was large enough to put filed work in doubt.</summary>
        public bool CastsDoubt => FlaggedResults > 0;
    }
}
