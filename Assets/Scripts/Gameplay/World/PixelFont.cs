using System.Collections.Generic;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A 3x5 bitmap font, stored as 15 characters per glyph (five rows of three, top to bottom).
    /// <para>
    /// Deliberately not TextMeshPro. TMP needs its Essential Resources imported through a GUI
    /// dialog, which is a step an agent cannot take and a fresh clone would not have — the
    /// instrument displays would silently render nothing. Fifteen bytes a glyph has no such
    /// dependency, and a chunky pixel readout is what a lab instrument looks like anyway.
    /// </para>
    /// </summary>
    public static class PixelFont
    {
        public const int GlyphWidth = 3;
        public const int GlyphHeight = 5;

        /// <summary>Columns advanced per character, including the gap.</summary>
        public const int Advance = 4;

        private static readonly Dictionary<char, string> Glyphs = new()
        {
            { '0', "111101101101111" }, { '1', "010110010010111" }, { '2', "111001111100111" },
            { '3', "111001111001111" }, { '4', "101101111001001" }, { '5', "111100111001111" },
            { '6', "111100111101111" }, { '7', "111001001001001" }, { '8', "111101111101111" },
            { '9', "111101111001111" },

            { 'A', "111101111101101" }, { 'B', "110101110101110" }, { 'C', "111100100100111" },
            { 'D', "110101101101110" }, { 'E', "111100111100111" }, { 'F', "111100111100100" },
            { 'G', "111100101101111" }, { 'H', "101101111101101" }, { 'I', "111010010010111" },
            { 'J', "001001001101111" }, { 'K', "101101110101101" }, { 'L', "100100100100111" },
            { 'M', "111111111101101" }, { 'N', "101111111111101" }, { 'O', "111101101101111" },
            { 'P', "111101111100100" }, { 'Q', "111101101111011" }, { 'R', "111101111110101" },
            { 'S', "111100111001111" }, { 'T', "111010010010010" }, { 'U', "101101101101111" },
            { 'V', "101101101101010" }, { 'W', "101101111111101" }, { 'X', "101101010101101" },
            { 'Y', "101101010010010" }, { 'Z', "111001010100111" },

            { ' ', "000000000000000" }, { '.', "000000000000010" }, { '-', "000000111000000" },
            { '%', "101001010100101" }, { '/', "001001010100100" }, { ':', "000010000010000" },
            { '+', "000010111010000" }, { '(', "001010010010001" }, { ')', "100010010010100" },
            { '<', "001010100010001" }, { '>', "100010001010100" }, { '=', "000111000111000" },
            { '#', "101111101111101" }, { '*', "000101010101000" }, { '!', "010010010000010" },
            { '?', "111001010000010" }, { ',', "000000000010100" }, { '_', "000000000000111" }
        };

        /// <summary>Pixel rows for a character, or null if it has no glyph.</summary>
        public static string Glyph(char c)
        {
            if (c >= 'a' && c <= 'z') c = (char)(c - 'a' + 'A');
            return Glyphs.TryGetValue(c, out var g) ? g : Glyphs[' '];
        }

        public static bool IsOn(string glyph, int x, int y) => glyph[y * GlyphWidth + x] == '1';

        /// <summary>Width in pixels of a string at the given scale, excluding the trailing gap.</summary>
        public static int MeasureWidth(string text, int scale) =>
            text == null || text.Length == 0 ? 0 : (text.Length * Advance - 1) * scale;
    }
}
