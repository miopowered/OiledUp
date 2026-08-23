namespace Residue.Data
{
    /// <summary>What kind of thing a reading measures. Drives grouping in the reference book UI.</summary>
    public enum ElementCategory
    {
        WearMetal,
        Contaminant,
        Additive,
        FluidProperty
    }

    /// <summary>
    /// How a <see cref="Threshold"/> decides whether a reading is bad.
    /// <para>
    /// The spec (§4.2) modelled this as <c>normalMax</c>/<c>cautionMax</c> plus an <c>inverted</c> flag,
    /// but that cannot express viscosity, which is scored as a +/- band around the oil grade's nominal
    /// value (§4.2 lists "Visc@100C: +/-5% / +/-12%"). Both directions are bad, so neither a max nor an
    /// inverted max fits. <see cref="DeviationBand"/> covers it and subsumes the old flag.
    /// </para>
    /// </summary>
    public enum ThresholdMode
    {
        /// <summary>Higher is worse. Wear metals, contaminants, soot, water.</summary>
        UpperLimit,

        /// <summary>Lower is worse. TBN, additive depletion.</summary>
        LowerLimit,

        /// <summary>Distance from <see cref="Threshold.Baseline"/> in either direction is worse. Viscosity.</summary>
        DeviationBand
    }

    /// <summary>Severity of a single reading against its equipment profile.</summary>
    public enum ReadingSeverity
    {
        Normal,
        Caution,
        Critical
    }

    /// <summary>
    /// How far along a fault is. Drives the consequence table (§5.4): filing MONITOR on an
    /// <see cref="Imminent"/> fault fails the equipment, filing it on <see cref="Developing"/> does not.
    /// </summary>
    public enum FaultSeverity
    {
        Benign,
        Developing,
        Imminent
    }

    /// <summary>The three verdicts a player can file on a sample (§1).</summary>
    public enum Verdict
    {
        Normal,
        Monitor,
        Critical
    }
}
