namespace Residue.Chemistry
{
    /// <summary>
    /// Why a vial cannot say for itself which line of its delivery note it answers (#32).
    ///
    /// <para>
    /// <b>This is the only reason a sample is ever registered by hand.</b> #73 removed booking-in
    /// because it stopped the loop dead at a keyboard: nothing could be prepped or run until it had
    /// been typed in. A vial with a legible tank tag still needs no typing at all — it carries the tag
    /// printed on it from the moment it exists, and reconciling it against the paper in the box is
    /// reading rather than data entry. What is left here are the two cases where reading cannot
    /// finish the job, because the bottle itself is ambiguous.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing is gated on it.</b> An ambiguous vial agitates, loads, runs, prints and files
    /// exactly like any other. A player who never registers one simply has an unidentified sample,
    /// and the cost lands where §5.4 lands every other cost — at resolution, days later, when the
    /// report turns out to have named the wrong tank.
    /// </para>
    ///
    /// <para>
    /// Client-safe, and deliberately so: both values state something the player can see with their own
    /// eyes on the bench — a smudged label, or two bottles carrying one tag. Neither says whether the
    /// customer was careless, which is the part that has to be measured.
    /// </para>
    /// </summary>
    public enum SampleAmbiguity
    {
        /// <summary>The label says what it is. The overwhelming majority of vials.</summary>
        None = 0,

        /// <summary>
        /// The tank tag is smudged, torn or was never written. The note still lists the tank, so the
        /// vial can be identified by elimination against the other bottles in the box — or by ringing
        /// the customer, which costs shift time and is certain.
        /// </summary>
        UnreadableLabel = 1,

        /// <summary>
        /// Two vials in one carton carry the same tank tag, against a note that books two draws from
        /// that tank. Either the plant genuinely drew it twice, or somebody filled both bottles out of
        /// one drum and wrote the label twice (§6.1). The paper reads identically either way; only the
        /// readings tell them apart.
        /// </summary>
        DuplicateClaim = 2
    }
}
