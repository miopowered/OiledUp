namespace Residue.Data
{
    /// <summary>
    /// What the lab is told about a verdict once it comes due (§5.4, #55).
    ///
    /// <para>
    /// These are the most consequential sentences in the game. A verdict is filed on one day and
    /// answered days later, and this is the entire answer — whether the call was right, what it cost,
    /// and what the customer did with it. If any line here is wrong or unreadable, the delayed
    /// consequence that the whole design rests on lands as a shrug.
    /// </para>
    ///
    /// <para>
    /// <b>Two sentences, not one built in halves.</b> A report gets a diagnosis line and, when #32's
    /// paperwork was wrong, a second line saying so. The old code appended the second onto the first
    /// with <c>+=</c>, which is fine in English and unusable to a translator: the separator, the
    /// order and the join are all decisions the language should get to make. Each half is now a whole
    /// sentence, and <see cref="HeadlineWithNote"/> is what puts them together — so a language that
    /// wants the paperwork failure first, or wants a different separator between them, can say so.
    /// </para>
    ///
    /// <para>
    /// <b>Fault names, root causes and missed-consequence text are arguments, never keys.</b> They
    /// come from <c>ContentTables.cs</c>, which is balance data with its own pipeline, and hard rule 1
    /// says the chemistry never lies — a fault that read differently in two languages would be the
    /// chemistry lying in one of them.
    /// </para>
    /// </summary>
    public static class ConsequenceStrings
    {
        /// <summary>
        /// Diagnosis and paperwork note, joined. A template rather than <c>a + " " + b</c> because the
        /// separator is a language's decision — and so is which of the two goes first.
        /// </summary>
        public static readonly LocKey HeadlineWithNote = new(
            "consequence.headline_with_note", "{diagnosis} {note}");

        // -- The diagnosis ------------------------------------------------------------------------

        public static readonly LocKey CorrectCriticalCauseConfirmed = new(
            "consequence.correct_critical_cause_confirmed",
            "{tag}: taken out of service in time. Cause confirmed as {cause}. Full payout plus " +
            "diagnostic bonus.");

        /// <summary>
        /// Right call, wrong cause. A separate line from <see cref="CorrectCriticalNoCause"/> rather
        /// than a shared stem with a swapped tail: naming the wrong cause and naming none are
        /// different mistakes, and the player should be able to tell which one they made.
        /// </summary>
        public static readonly LocKey CorrectCriticalWrongCause = new(
            "consequence.correct_critical_wrong_cause",
            "{tag}: taken out of service in time — {fault}. Filed cause was wrong; it was {cause}.");

        public static readonly LocKey CorrectCriticalNoCause = new(
            "consequence.correct_critical_no_cause",
            "{tag}: taken out of service in time — {fault}. No root cause filed, so no diagnostic " +
            "bonus.");

        public static readonly LocKey FalsePositive = new(
            "consequence.false_positive",
            "{tag}: tank dumped and recharged on your call. The oil tested serviceable. Line " +
            "downtime and the fresh charge are ours.");

        public static readonly LocKey MonitorOnImminent = new(
            "consequence.monitor_on_imminent",
            "{tag}: kept quenching on your advice and it should not have been. {fault}. {consequence}");

        public static readonly LocKey MonitorDeveloping = new(
            "consequence.monitor_developing",
            "{tag}: kept in service and scheduled for another draw. The numbers are worse this cycle.");

        public static readonly LocKey MonitorUnnecessary = new(
            "consequence.monitor_unnecessary", "{tag}: redrawn at your request, still within spec.");

        /// <summary>The worst outcome in the game. <c>{consequence}</c> is the fault's own text.</summary>
        public static readonly LocKey MissedFault = new(
            "consequence.missed_fault",
            "{tag}: PASSED AS FIT TO QUENCH. {fault}. {consequence} Named in the incident file.");

        public static readonly LocKey CorrectNormal = new(
            "consequence.correct_normal", "{tag}: cleared as fit for service. Routine payout.");

        // -- The paperwork note (#32) -------------------------------------------------------------

        public static readonly LocKey RegistrationUnregistered = new(
            "consequence.registration_unregistered",
            "The vial it came from was never identified, so the report went out against no tank at " +
            "all. Unbillable.");

        public static readonly LocKey RegistrationWrongTank = new(
            "consequence.registration_wrong_tank",
            "Filed against {filed}. The oil was drawn from {actual}. They have acted on the wrong bath.");

        public static readonly LocKey RegistrationMissedSplitDraw = new(
            "consequence.registration_missed_split_draw",
            "Both vials were bottled from one drum and you certified them as two draws. They paid " +
            "for a cross-check and got one sample counted twice.");

        public static readonly LocKey RegistrationImaginedSplitDraw = new(
            "consequence.registration_imagined_split_draw",
            "You would not separate the two draws. Their dispatch log says the tank was drawn twice, " +
            "and the numbers agree with them.");

        public static readonly LocKey RegistrationSameDrumCaught = new(
            "consequence.registration_same_drum_caught",
            "Both vials read the same because they were the same oil — one drum, booked as two " +
            "draws. Called, and charged for.");
    }
}
