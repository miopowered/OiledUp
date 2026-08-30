using System;
using System.Collections.Generic;
using System.Text;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Text layout for the in-world screens — the instrument readouts and the reference book — which
    /// rasterise <see cref="PixelFont"/> into a <c>Texture2D</c> rather than building a panel.
    /// <para>
    /// Deliberately not part of <c>UiKit</c> (#50). The overlay kit assembles <c>VisualElement</c>
    /// trees and hands wrapping to the layout engine; there is no layout engine behind a texture, so
    /// a screen in the world has to decide for itself where a line breaks. Those are two different
    /// problems that happen to share a vocabulary, and merging them would put a UI Toolkit dependency
    /// behind every instrument panel in the lab to buy nothing.
    /// </para>
    /// <para>
    /// <b>Wrapping and truncation are not interchangeable, and both callers are right.</b> The book
    /// reflows: a page is twenty-odd lines of prose and losing the tail of a sentence loses the
    /// sentence. An instrument readout does not: each line is a labelled field at a fixed y, so a
    /// long caption that wrapped would push the value under it off the glass. Hence
    /// <see cref="Wrap"/> for one and <see cref="Truncate"/> for the other, rather than one function
    /// with a flag. What they genuinely share is the arithmetic — how many characters fit, how far
    /// apart two baselines sit, and where a hard cut falls — which is what lives here.
    /// </para>
    /// <para>
    /// Pure string and integer maths, no <c>UnityEngine</c> types, so every promise below is pinned
    /// by <c>PixelTextTests</c> without a scene or a texture.
    /// </para>
    /// </summary>
    public static class PixelText
    {
        /// <summary>
        /// Blank pixel rows between one line of glyphs and the next, before scaling. Two: at 3x5 a
        /// single row of leading lets a descender-free font's rows visually merge across a table.
        /// </summary>
        public const int LineGap = 2;

        /// <summary>Baseline-to-baseline distance in texture pixels at the given glyph scale.</summary>
        public static int LineHeight(int scale) => (PixelFont.GlyphHeight + LineGap) * scale;

        /// <summary>
        /// How many characters fit across <paramref name="availableWidth"/> texture pixels.
        /// <para>
        /// Charges every character the full <see cref="PixelFont.Advance"/> including its trailing
        /// gap, so the answer is up to one scaled pixel short of what
        /// <see cref="PixelFont.MeasureWidth"/> would actually paint. That slack is wanted: it is what
        /// guarantees a full-width line still clears the margin it was measured against. Both screens
        /// already counted it this way; do not "fix" it into an off-by-one against the margin.
        /// </para>
        /// Never returns zero. A field too narrow for a single glyph is a mis-configured screen, and
        /// one clipped character on it beats an empty-string loop or a division that never terminates.
        /// </summary>
        public static int Columns(int availableWidth, int scale) =>
            Math.Max(1, availableWidth / (PixelFont.Advance * scale));

        /// <summary>
        /// Cut <paramref name="text"/> down to <paramref name="columns"/> characters, keeping the
        /// head.
        /// <para>
        /// A hard cut with no ellipsis, which is what both callers already did. An ellipsis would
        /// cost three of the fourteen columns a titrator's caption line has, and
        /// <see cref="PixelFont"/> has no single-character one to spend instead — so the marker would
        /// eat more of the name than it saves. The screens that use this print the same tag elsewhere
        /// in full (the terminal's sample list, the book's own heading), so a clipped name is
        /// recoverable; a name reduced to four characters and a marker is not.
        /// </para>
        /// </summary>
        public static string Truncate(string text, int columns)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // Cut what will actually be drawn, not what was passed in: an umlaut becomes two
            // characters on this font (see PixelFont.Transliterate), so measuring the original would
            // let a German caption overrun the column it was cut to fit (#55).
            text = PixelFont.Transliterate(text);

            return text.Length <= columns ? text : text.Substring(0, Math.Max(1, columns));
        }

        /// <summary>
        /// Break <paramref name="text"/> into lines of at most <paramref name="columns"/> characters,
        /// appending them to <paramref name="output"/>.
        /// <para>
        /// Paragraphs are separated by newlines and survive as blank lines, because a wall of pixel
        /// text with no paragraph breaks is unreadable at this size. Runs of spaces collapse: the
        /// content tables are hand-written prose and a stray double space would otherwise print as a
        /// visible hole or, at a line break, as a phantom empty line.
        /// </para>
        /// <para>
        /// A word longer than the column is measured <i>before</i> it is cut. It therefore still
        /// forces the break that its full length demands and then takes a whole line of its own,
        /// rather than being silently packed onto the end of the previous one — which is what keeps a
        /// truncated word looking truncated instead of looking like a short word.
        /// </para>
        /// </summary>
        public static void Wrap(string text, int columns, List<string> output)
        {
            if (output == null || string.IsNullOrEmpty(text)) return;

            // Wrap the drawn spelling, so a German word that grows by a character still breaks in the
            // right place rather than one column late (#55).
            text = PixelFont.Transliterate(text);

            foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(paragraph)) { output.Add(string.Empty); continue; }

                var line = new StringBuilder();
                foreach (string word in paragraph.Trim().Split(' '))
                {
                    if (word.Length == 0) continue;

                    if (line.Length > 0 && line.Length + 1 + word.Length > columns)
                    {
                        output.Add(line.ToString());
                        line.Clear();
                    }

                    if (line.Length > 0) line.Append(' ');
                    line.Append(Truncate(word, columns));
                }

                if (line.Length > 0) output.Add(line.ToString());
            }
        }

        /// <summary>
        /// The x offset that centres <paramref name="text"/> in a field
        /// <paramref name="fieldWidth"/> pixels across, relative to the field's own left edge.
        /// <para>
        /// Centred on the string's painted width rather than on its column count. Mirroring an offset
        /// taken from the other edge and forgetting the glyph's own width is exactly how the book's
        /// two page-corner arrows stopped looking like the same control facing opposite ways.
        /// </para>
        /// Text wider than its field returns a negative offset — it overhangs evenly instead of being
        /// pinned left, so an overflow is visibly an overflow rather than a line that merely looks
        /// long.
        /// </summary>
        /// <param name="text">The string to centre.</param>
        /// <param name="scale">Glyph scale it will be drawn at.</param>
        /// <param name="fieldWidth">Width of the field, in texture pixels.</param>
        public static int CentreOffset(string text, int scale, int fieldWidth) =>
            (fieldWidth - PixelFont.MeasureWidth(text, scale)) / 2;
    }
}
