using Residue.Chemistry;

namespace Residue.Gameplay.Simulation
{
    /// <summary>How the player's registration decision turned out, once the truth was allowed to speak.</summary>
    public enum RegistrationOutcome
    {
        /// <summary>The label said what it was. Nothing was ever in doubt and nothing is scored.</summary>
        NotAmbiguous,

        /// <summary>
        /// The vial was never identified. The customer receives a report on a bottle they cannot
        /// match to a tank, which is worth nothing to them.
        /// </summary>
        Unregistered,

        /// <summary>The player named the right tank, or correctly refused to separate one drum.</summary>
        Correct,

        /// <summary>The report went out against a tank this oil never came from.</summary>
        WrongTank,

        /// <summary>
        /// Two vials off one drum were certified as two independent draws (§6.1). The customer paid
        /// for a cross-check and got one draw counted twice.
        /// </summary>
        MissedSplitDraw,

        /// <summary>
        /// The player declared two genuine draws inseparable. Honest, and wrong: the customer's
        /// paperwork was fine and was called into question anyway.
        /// </summary>
        ImaginedSplitDraw
    }

    /// <summary>
    /// Scores what the player recorded about an ambiguous vial against what was actually in it (#32).
    ///
    /// <para>
    /// <b>It takes the truth as three plain values, not as a <see cref="SampleGroundTruth"/>.</b> That
    /// is deliberate: the vault stays the only thing that can produce one, and this stays a pure
    /// function that a test can drive directly with the four cases it has to get right. The one caller
    /// that has both halves is <see cref="ConsequenceResolver"/>, which already reads truth by
    /// contract.
    /// </para>
    ///
    /// <para>
    /// <b>Every outcome below is reachable by reading the paper in the box.</b> Hard rule 3 is the
    /// whole of this issue: a discrepancy is only fair because the note arrived with the vials. An
    /// unreadable label is settled by elimination against the other bottles, or by ringing the
    /// customer. A duplicated claim is settled by running both vials and comparing — two genuinely
    /// different baths cannot come back identical. Nothing here punishes a player for something they
    /// had no way to check.
    /// </para>
    /// </summary>
    public static class DeliveryReconciliation
    {
        /// <summary>
        /// Score one vial's registration.
        /// </summary>
        /// <param name="state">The player-facing record, including whatever they registered.</param>
        /// <param name="trueTankTag">The tank the oil actually came from.</param>
        /// <param name="drawnFromOneDrum">This vial shares its oil with another in the same carton.</param>
        public static RegistrationOutcome Score(SampleState state, string trueTankTag,
                                                bool drawnFromOneDrum)
        {
            if (state == null || state.Ambiguity == SampleAmbiguity.None)
                return RegistrationOutcome.NotAmbiguous;

            if (state.RegisteredLine == SampleState.Unregistered) return RegistrationOutcome.Unregistered;

            return state.Ambiguity == SampleAmbiguity.DuplicateClaim
                ? ScoreDuplicate(state, trueTankTag, drawnFromOneDrum)
                : ScoreUnreadable(state, trueTankTag);
        }

        /// <summary>
        /// An unreadable label has one right answer and the note contains it.
        /// <para>
        /// Matched on the tank tag rather than on the line index, because a note may legitimately book
        /// the same tank twice — and in that case either line is the right answer. Scoring the index
        /// would fail a player who got the tank right and the row wrong, which is a distinction the
        /// customer's report does not contain.
        /// </para>
        /// <para>
        /// "I cannot tell" scores as <see cref="RegistrationOutcome.Unregistered"/> rather than as a
        /// mistake. It is the truthful answer for a player who ran out of shift before they could ring
        /// the customer, and it lands the same cost as never having looked — which is the honest cost,
        /// because the customer is holding the same unattributable report either way.
        /// </para>
        /// </summary>
        private static RegistrationOutcome ScoreUnreadable(SampleState state, string trueTankTag)
        {
            if (state.RegisteredLine == SampleState.CannotTell) return RegistrationOutcome.Unregistered;

            return string.Equals(state.RegisteredTag, trueTankTag, System.StringComparison.Ordinal)
                ? RegistrationOutcome.Correct
                : RegistrationOutcome.WrongTank;
        }

        /// <summary>
        /// Two vials, one tag, two lines booking the same tank. The question is not which row each
        /// bottle belongs to — both rows name the same tank, so the report reads the same either way.
        /// The question is whether the plant really drew twice.
        /// <para>
        /// Naming a line asserts "these are two independent draws". Recording
        /// <see cref="SampleState.CannotTell"/> asserts "these came out of one container and I will not
        /// certify them separately". Exactly one of those is true, the readings say which, and both
        /// are wrong in the case they do not describe — otherwise "cannot tell" would be a free move
        /// that is never punished, and every player would record it on every duplicate without ever
        /// running the second vial.
        /// </para>
        /// </summary>
        private static RegistrationOutcome ScoreDuplicate(SampleState state, string trueTankTag,
                                                          bool drawnFromOneDrum)
        {
            if (state.RegisteredLine == SampleState.CannotTell)
            {
                return drawnFromOneDrum
                    ? RegistrationOutcome.Correct
                    : RegistrationOutcome.ImaginedSplitDraw;
            }

            // A registration that names a tank the oil never came from is wrong before the drum
            // question is even asked. Unreachable while both duplicate lines name one tank, and kept
            // because the shape on the note is content, not a law.
            if (!string.Equals(state.RegisteredTag, trueTankTag, System.StringComparison.Ordinal))
                return RegistrationOutcome.WrongTank;

            return drawnFromOneDrum
                ? RegistrationOutcome.MissedSplitDraw
                : RegistrationOutcome.Correct;
        }

        /// <summary>
        /// The report went out naming something this oil is not. §5.4's payout is withheld for these
        /// and the reputation cost lands whichever way the diagnosis itself went.
        /// </summary>
        public static bool IsMisattributed(RegistrationOutcome outcome) =>
            outcome is RegistrationOutcome.Unregistered
                or RegistrationOutcome.WrongTank
                or RegistrationOutcome.MissedSplitDraw;
    }
}
