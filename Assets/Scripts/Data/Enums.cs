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

    /// <summary>
    /// What a customer's plant does. §6.2's four industries, which is what makes a name mean something
    /// beyond a string: a forge and a spring maker run different fluids and fail in different ways, so
    /// knowing the sender narrows the diagnosis before a single instrument has run.
    /// </summary>
    public enum CustomerIndustry
    {
        AutomotiveSupplier,
        FastenerWorks,
        Forge,
        SpringMaker
    }

    /// <summary>
    /// How much a customer's paperwork and drum discipline can be trusted.
    /// <para>
    /// A label the player learns over a contract rather than a number they are shown. It is
    /// deliberately not a difficulty dial: a careless customer does not send worse oil, they send
    /// worse <i>records</i> — a note that disagrees with the carton, or several tanks quietly drawn
    /// from one drum (§6.1). Hard rule 1 stands either way, because the chemistry of what is in the
    /// bottle is untouched, and hard rule 3 stands because both are discoverable from the note and
    /// the readings.
    /// </para>
    /// </summary>
    public enum CustomerReliability
    {
        /// <summary>Paperwork is right. What the note says is what is in the box.</summary>
        Meticulous,

        /// <summary>The default. Occasional honest mistakes, nothing systematic.</summary>
        Routine,

        /// <summary>Notes go wrong often enough to be worth checking every time.</summary>
        Careless,

        /// <summary>§6.1's corner-cutter: the one whose drums are worth suspecting.</summary>
        CutsCorners
    }
}
