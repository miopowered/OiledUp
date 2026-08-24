using Unity.Collections;

namespace Residue.Net.Views
{
    /// <summary>
    /// Folds a managed string into the fixed-size form the wire takes.
    /// <para>
    /// NGO's serializer will not accept a <see cref="string"/> at all, so every piece of text in a
    /// view is a <c>FixedString</c>. The implicit <c>string</c> conversion those types offer throws
    /// when the source does not fit, which would turn a player typing a long tank tag at the terminal
    /// into a host-side exception mid-replication. Truncating instead is the right failure: a tag is
    /// something the player reads back off a screen, and a clipped one is still legible.
    /// </para>
    /// Internal because it is a wire detail. Nothing outside <c>Residue.Net</c> should be thinking
    /// about byte budgets.
    /// </summary>
    internal static class ViewText
    {
        /// <summary>
        /// 61 bytes of payload, against a longest real equipment tag of about 24 characters
        /// ("HALLE-3 SEALED QUENCH 1") and a longest profile id of 24 ("corrosion_protection_oil").
        /// The next size up would more than double every sample row on the wire to buy headroom
        /// nothing in the content tables needs.
        /// </summary>
        public static FixedString64Bytes Fixed64(string value)
        {
            var packed = new FixedString64Bytes();
            if (!string.IsNullOrEmpty(value)) packed.CopyFromTruncated(value);
            return packed;
        }

        /// <summary>
        /// 29 bytes of payload, for the ids the content tables mint rather than the text a player
        /// types: the longest element id is 6 characters ("TCRmax"), the longest machine id 12
        /// ("karl_fischer"), the longest placed id 14 ("karl_fischer-0"). Readings are the one thing
        /// on this wire there are thousands of, so the size that fits an id with room to double is
        /// worth having as its own budget — <see cref="Fixed64"/> would cost 32 bytes a reading to
        /// carry nothing.
        /// </summary>
        public static FixedString32Bytes Fixed32(string value)
        {
            var packed = new FixedString32Bytes();
            if (!string.IsNullOrEmpty(value)) packed.CopyFromTruncated(value);
            return packed;
        }

        /// <summary>
        /// 125 bytes of payload, against a longest field note of 62 characters. Notes are free text
        /// written in the content tables rather than typed by a player, so this is headroom against
        /// an author, not against an attacker — and it still truncates rather than throwing, for the
        /// reason the type doc gives.
        /// </summary>
        public static FixedString128Bytes Fixed128(string value)
        {
            var packed = new FixedString128Bytes();
            if (!string.IsNullOrEmpty(value)) packed.CopyFromTruncated(value);
            return packed;
        }

        /// <summary>
        /// 509 bytes of payload, for a whole sentence rather than a name: the end-of-day headline
        /// (<see cref="ReportView"/>) runs to about 260 characters at its worst — a tank tag, a
        /// fault, and the <c>MissedConsequence</c> the content tables write for it — and the
        /// consequence text is authored, so the budget has to leave an author room to be vivid.
        /// <para>
        /// Reports are the one list on this wire that exists only between shifts and is a dozen rows
        /// long, so a generous row is cheap here in a way it would not be for a reading.
        /// </para>
        /// </summary>
        public static FixedString512Bytes Fixed512(string value)
        {
            var packed = new FixedString512Bytes();
            if (!string.IsNullOrEmpty(value)) packed.CopyFromTruncated(value);
            return packed;
        }
    }
}
