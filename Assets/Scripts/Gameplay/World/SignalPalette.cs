using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The three verdict colours, matching palette row 4 (§2.2) so the screen and the world agree —
    /// and the glyph and the word that travel with each of them.
    /// <para>
    /// These are the <b>only</b> place red, amber and green are allowed. If red only ever means
    /// critical, a player glancing at a results table reads it before they read it. Using them for
    /// chrome anywhere else spends that for nothing.
    /// </para>
    /// <para>
    /// <b>Hue is never the only carrier (§2.2, #41).</b> Reserving row 4 for verdict state
    /// concentrates the most important information in the game onto the exact axis red-green
    /// colourblindness removes — roughly one man in twelve could not tell CRITICAL from NORMAL. Hard
    /// rule 3 says nobody is punished for something they could not have checked, and an unreadable
    /// verdict is precisely that. So every severity here ships as three things that must be used
    /// together: a colour, a <see cref="Glyph(ReadingSeverity)"/> and a <see cref="Label"/>. Draw at
    /// least two. <see cref="Marked(ReadingSeverity)"/> is the shorthand for "glyph and word", and is
    /// what a border, a light or a tint should be paired with — those carry no text of their own.
    /// </para>
    /// <para>
    /// The colours are separated in <see cref="Luminance"/> as well as in hue, so a greyscale reading
    /// of a results table still ranks them. Amber and green used to sit 0.08 apart, which is nothing:
    /// desaturate the old table and CAUTION and NORMAL were the same grey. They are now at least
    /// <see cref="MinimumLuminanceSeparation"/> apart pairwise, which
    /// <c>SignalEncodingTests</c> checks — that test is the greyscale screenshot, computed rather
    /// than eyeballed.
    /// </para>
    /// <para>
    /// <b>Keep the atlas in step.</b> <c>PaletteBootstrap.SignalRow</c> holds the same three colours
    /// at row 4 columns 2, 6 and 10. Changing a value here means changing it there and running
    /// <c>Residue > Art > Rebuild Palette</c>, or a geometry emissive and a screen label stop
    /// agreeing about what amber is.
    /// </para>
    /// </summary>
    public static class SignalPalette
    {
        public static readonly Color Critical = new(1.00f, 0.22f, 0.18f); // luminance 0.383
        public static readonly Color Caution = new(1.00f, 0.80f, 0.20f);  // luminance 0.799
        public static readonly Color Normal = new(0.16f, 0.68f, 0.34f);   // luminance 0.545

        /// <summary>
        /// Not a severity — "this has never been measured". Left where it was on purpose: it is
        /// aliased as <c>UiPalette.InkFaint</c> and is already near the floor of what reads as text
        /// on a panel, so it cannot be pushed further from <see cref="Critical"/> in luminance
        /// without becoming unreadable. Anything drawn in it therefore has to say
        /// <see cref="UnknownMark"/> as well.
        /// </summary>
        public static readonly Color Off = new(0.34f, 0.36f, 0.38f);

        // Neutral chrome. Everything that is not a verdict lives here.
        public static readonly Color Ink = new(0.88f, 0.89f, 0.90f);
        public static readonly Color Dim = new(0.58f, 0.60f, 0.62f);
        public static readonly Color Panel = new(0.11f, 0.12f, 0.13f, 0.96f);
        public static readonly Color PanelSoft = new(0.16f, 0.17f, 0.19f, 0.96f);
        public static readonly Color Accent = new(0.32f, 0.62f, 0.70f);

        /// <summary>
        /// How far apart any two signal colours must sit on <see cref="Luminance"/>. Chosen as the
        /// point where three greys are plainly ordered rather than merely unequal; the closest
        /// pair — NORMAL against CRITICAL — clears it by a little, which is the constraint that fixes
        /// the green. Raising this means re-picking a colour, not relaxing the test.
        /// </summary>
        public const float MinimumLuminanceSeparation = 0.15f;

        /// <summary>
        /// What a severity looks like with the colour taken away. One character, so a table column
        /// stays aligned, and drawn from the set <see cref="PixelFont"/> can raster — an instrument
        /// screen has no other font, and a marker that only renders in the UI kit would be missing
        /// exactly where a player is standing at the machine.
        /// </summary>
        public static string Glyph(ReadingSeverity severity) => severity switch
        {
            ReadingSeverity.Critical => "X",
            ReadingSeverity.Caution => "!",
            _ => "="
        };

        public static string Glyph(Verdict verdict) => verdict switch
        {
            Verdict.Critical => "X",
            Verdict.Monitor => "!",
            _ => "="
        };

        /// <summary>Nothing has been measured, so there is no severity to show. See <see cref="Off"/>.</summary>
        public const string UnknownGlyph = "?";

        /// <summary>The <see cref="UnknownGlyph"/> and its word, for a row with no runs behind it.</summary>
        public const string UnknownMark = "? UNTESTED";

        public static Color For(ReadingSeverity severity) => severity switch
        {
            ReadingSeverity.Critical => Critical,
            ReadingSeverity.Caution => Caution,
            _ => Normal
        };

        public static Color For(Verdict verdict) => verdict switch
        {
            Verdict.Critical => Critical,
            Verdict.Monitor => Caution,
            _ => Normal
        };

        public static string Label(ReadingSeverity severity) => severity switch
        {
            ReadingSeverity.Critical => "CRITICAL",
            ReadingSeverity.Caution => "CAUTION",
            _ => "NORMAL"
        };

        /// <summary>
        /// A filed call, in the words the terminal's own buttons use. Separate from
        /// <see cref="Label(ReadingSeverity)"/> because MONITOR is a decision the player made and
        /// CAUTION is a number the instrument produced, and conflating them on a screen would tell
        /// the player they filed something they did not.
        /// </summary>
        public static string Label(Verdict verdict) => verdict switch
        {
            Verdict.Critical => "CRITICAL",
            Verdict.Monitor => "MONITOR",
            _ => "NORMAL"
        };

        /// <summary>Glyph and word together — the two non-colour channels, for anything drawing one string.</summary>
        public static string Marked(ReadingSeverity severity) => $"{Glyph(severity)} {Label(severity)}";

        public static string Marked(Verdict verdict) => $"{Glyph(verdict)} {Label(verdict)}";

        /// <summary>
        /// Relative luminance, sRGB coefficients. Deliberately over the raw channel values rather
        /// than gamma-linearised ones: this is a ranking of how bright three swatches look beside
        /// each other, not a photometric measurement, and the ordering is what the greyscale check
        /// in <c>SignalEncodingTests</c> depends on.
        /// </summary>
        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }
}
