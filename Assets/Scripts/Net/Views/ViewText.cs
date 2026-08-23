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
    }
}
