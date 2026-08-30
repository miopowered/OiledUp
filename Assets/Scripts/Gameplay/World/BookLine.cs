namespace Residue.Gameplay.World
{
    /// <summary>
    /// How a typeset line is set, which decides its size, its vertical advance and how it may be
    /// broken across a page.
    /// </summary>
    public enum BookLineStyle
    {
        /// <summary>Prose, set proportionally at <see cref="BookLayout.BodyGlyph"/>.</summary>
        Body,

        /// <summary>A chapter heading: larger, emboldened, and glued to the text below it.</summary>
        Heading,

        /// <summary>
        /// Tabular matter — a row whose columns were aligned with spaces by whoever wrote it.
        /// Set on a fixed advance, because a proportional face turns those columns into a ragged
        /// mess, and never wrapped, because half a table row is not a table row.
        /// </summary>
        Tabular,

        /// <summary>Paragraph space. Carries no ink and less height than a full line.</summary>
        Blank
    }

    /// <summary>
    /// One line of set type, already broken to the measure — what the page draws and what the
    /// paginator counts.
    ///
    /// <para>
    /// A line is not just a string. It remembers which paragraph it came from, because widow and
    /// orphan control is the question "is the line on the other side of this page break part of the
    /// same thought?", and a bare list of strings cannot answer it. It remembers its indent, because
    /// an indent that lived at the draw call would have to be re-derived from the text every time it
    /// was measured — and measuring and drawing disagreeing is the whole class of bug this book has
    /// already had once.
    /// </para>
    ///
    /// <para>
    /// Everything is in <b>supersample pixels</b> (see <see cref="BookLayout.Supersample"/>). Type is
    /// laid out at the resolution it is rasterised at, so the arithmetic stays integer and the glyph
    /// grid is deliberately not a whole number of final pixels — that misalignment is what gives the
    /// downsample something to average, and is where the anti-aliasing comes from.
    /// </para>
    /// </summary>
    public readonly struct BookLine
    {
        public string Text { get; }
        public BookLineStyle Style { get; }

        /// <summary>Left inset from the text column, in supersample pixels.</summary>
        public int Indent { get; }

        /// <summary>
        /// Which paragraph of the section this came from, or -1 for a line that belongs to none.
        /// Only the paginator reads it.
        /// </summary>
        public int Paragraph { get; }

        /// <summary>Centred on the measure rather than set flush left. Title pages only.</summary>
        public bool Centred { get; }

        /// <summary>
        /// A page may not break after this line. A heading stranded at the foot of a page is the
        /// most obvious possible typesetting fault, and the cheapest to refuse.
        /// </summary>
        public bool KeepWithNext { get; }

        public BookLine(string text, BookLineStyle style, int indent = 0, int paragraph = -1,
                        bool centred = false, bool keepWithNext = false)
        {
            Text = text ?? string.Empty;
            Style = style;
            Indent = indent;
            Paragraph = paragraph;
            Centred = centred;
            KeepWithNext = keepWithNext;
        }

        /// <summary>Supersample pixels per font pixel, for whatever size this line is set at.</summary>
        public int GlyphPixel =>
            Style == BookLineStyle.Heading ? BookLayout.HeadingGlyph : BookLayout.BodyGlyph;

        /// <summary>
        /// Ink extent below this line's own top, descenders included. The last line on a page has to
        /// fit <i>this</i>, not its advance — otherwise every page throws away the space between its
        /// final baseline and the foot margin.
        /// </summary>
        public int Height => Style == BookLineStyle.Blank ? 0 : BookFont.Height * GlyphPixel;

        /// <summary>Top of this line to top of the next.</summary>
        public int Advance => Style switch
        {
            BookLineStyle.Blank => BookLayout.ParagraphSpace,
            BookLineStyle.Heading => BookLayout.HeadingLine,
            _ => BookLayout.BodyLine
        };
    }
}
