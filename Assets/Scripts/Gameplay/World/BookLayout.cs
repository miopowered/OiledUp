using System;
using System.Collections.Generic;
using System.Text;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The book's typesetting: page geometry, measured wrapping, and pagination.
    ///
    /// <para>
    /// <b>Why not <see cref="PixelText"/>.</b> That measures in <i>columns</i> — a character count
    /// times one fixed advance — which is exactly right for an instrument readout set in a monospaced
    /// face and simply arithmetic nonsense for <see cref="BookFont"/>, where an "i" is one cell wide
    /// and an "m" is five. A column count applied to a proportional face is wrong by a different
    /// amount for every string, so a line "within the column" can still paint into the margin and a
    /// line that would have fitted gets broken early. Everything here therefore breaks on
    /// <i>measured width</i>. <see cref="PixelText"/> keeps the screens; nothing here touches it.
    /// </para>
    ///
    /// <para>
    /// <b>Everything is in supersample pixels.</b> The page is rasterised at
    /// <see cref="Supersample"/>x and box-filtered down, and type is laid out at that resolution
    /// rather than at the final one. That keeps the arithmetic integer and — the point — makes the
    /// glyph grid land on fractions of a final pixel, which is what gives the downsample partial
    /// coverage to average. Set the type at a whole number of final pixels and every edge is fully
    /// covered or fully empty again, and the supersampling buys nothing at all.
    /// </para>
    ///
    /// <para>
    /// Pure string and integer maths, no <c>UnityEngine</c> types, so the page a player reads is
    /// pinned by <c>BookLayoutTests</c> without a scene, a texture or an Editor.
    /// </para>
    /// </summary>
    public static class BookLayout
    {
        // -- The raster ---------------------------------------------------------------------------

        /// <summary>
        /// Samples per final pixel, per axis. Three, not two: a 2x2 box gives five grey levels on a
        /// glyph edge and 3x3 gives ten, which is the difference between visibly stepped diagonals
        /// and smooth ones at the size body text is set here.
        /// </summary>
        public const int Supersample = 3;

        /// <summary>
        /// Final page size in texture pixels. The spread is two of these, and its 3:2 aspect matches
        /// the physical 0.36 x 0.24 m book exactly, so a texel is square — the old 512x384 spread
        /// stretched every glyph by a ninth horizontally for no reason anybody wrote down.
        /// </summary>
        public const int PageWidth = 576;

        public const int PageHeight = 768;
        public const int SpreadWidth = PageWidth * 2;
        public const int SpreadHeight = PageHeight;

        public const int SsPageWidth = PageWidth * Supersample;
        public const int SsPageHeight = PageHeight * Supersample;

        // -- Type sizes, in supersample pixels per font pixel --------------------------------------
        //
        // None of these is a multiple of Supersample, deliberately. See the class remarks.

        /// <summary>Body text. 5 gives a cap height of about 11.7 final pixels.</summary>
        public const int BodyGlyph = 5;

        /// <summary>Chapter headings.</summary>
        public const int HeadingGlyph = 8;

        /// <summary>Running heads and folios — smaller than the text they head, as they should be.</summary>
        public const int SmallGlyph = 4;

        /// <summary>
        /// Extra space between the letters of a running head. Letterspaced capitals are the
        /// conventional running-head treatment, and capitals set solid at this size close up.
        /// </summary>
        public const int RunningHeadTracking = 4;

        /// <summary>
        /// Supersample pixels a heading's stems are widened by. Two thirds of a final pixel on an
        /// eight-wide stem — a semibold, not a second font.
        /// </summary>
        public const int HeadingWeight = 2;

        // -- Vertical rhythm ----------------------------------------------------------------------

        /// <summary>Body baseline to body baseline.</summary>
        public const int BodyLine = (BookFont.Height + BookFont.Leading) * BodyGlyph;

        /// <summary>
        /// A heading and the air beneath it. More than the heading's own cell, so the space belongs
        /// to the heading rather than being a blank line somebody has to remember to emit.
        /// </summary>
        public const int HeadingLine = 130;

        /// <summary>
        /// Space between paragraphs. Less than a full line: §"a first-line indent or a blank line
        /// between paragraphs but not both" — this book uses spacing, because its content is
        /// reference matter full of lists and tables, where an indent reads as an outline level
        /// rather than as a new paragraph.
        /// </summary>
        public const int ParagraphSpace = 39;

        // -- Margins, in supersample pixels --------------------------------------------------------
        //
        // The gutter margin is the wide one. On an open book the paper turns into the binding, so the
        // inner margin has to carry both the text's own air and the part of the page the reader
        // cannot flatten; the drawn gutter shading occupies most of it.

        public const int OuterMargin = 138;
        public const int GutterMargin = 189;

        /// <summary>The measure. About 56 characters of body text — inside the 45-75 that reads.</summary>
        public const int ColumnWidth = SsPageWidth - OuterMargin - GutterMargin;

        public const int RunningHeadTop = 84;
        public const int RunningHeadRuleY = 138;
        public const int RunningHeadRuleThickness = 3;

        public const int TextTop = 210;

        /// <summary>
        /// Depth of the text block: exactly twenty-eight body lines, descenders included, clearing
        /// the foot margin the folio and the page-corner control live in.
        /// </summary>
        public const int TextHeight = 1800;

        public const int FolioTop = 2136;

        /// <summary>
        /// A long chapter title sets over two lines rather than being cut on the first, which is what
        /// a printed book does. Beyond that it is cut — a title occupying a third of its own opening
        /// page is not a title any more.
        /// </summary>
        public const int MaxHeadingLines = 2;

        // -- Spelling -------------------------------------------------------------------------------

        /// <summary>
        /// Spell text in characters <see cref="BookFont"/> actually has (#55).
        ///
        /// <para>
        /// Deliberately <i>not</i> <see cref="PixelFont.Transliterate"/>. That folds an umlaut to
        /// "AE" unconditionally and in capitals, which is right for a 3x5 uppercase CRT font and
        /// wrong twice over here: this face has real lowercase, so "Grundöl" must fall back to
        /// "Grundoel" rather than "GrundOEl", and it has two blank rows above the x-height, so it can
        /// carry a real diaeresis. Every substitution below is therefore conditional on
        /// <see cref="BookFont.Has"/> — the moment the face gains ä, the fallback stops firing and
        /// the German is set properly, with no change here.
        /// </para>
        ///
        /// <para>
        /// Applied before anything is measured <i>and</i> inside the glyph blit, so the string that
        /// is measured is byte-for-byte the string that is drawn. Idempotent, and returns the
        /// original instance when there is nothing to change — which is every English string.
        /// </para>
        /// </summary>
        public static string Spell(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            bool needed = false;
            foreach (char c in text)
            {
                if (Substitute(c) == null) continue;
                needed = true;
                break;
            }

            if (!needed) return text;

            var built = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                string replacement = Substitute(c);
                if (replacement != null) built.Append(replacement);
                else built.Append(c);
            }

            return built.ToString();
        }

        /// <summary>
        /// What a character the face lacks should be spelled as, or null to leave it alone (and be
        /// drawn as <see cref="BookFont.Missing"/>, a blank of average width, so the line still
        /// measures sanely).
        /// <para>
        /// Only substitutions with a single unambiguous answer. AE/OE/UE/SS is the standard German
        /// fallback and reads as old-fashioned rather than as broken; an em dash is a hyphen; a
        /// curly quote is a straight one. Nothing invents a spelling for a symbol that has none —
        /// "£" has no ASCII equivalent that is not worse than the gap.
        /// </para>
        /// </summary>
        private static string Substitute(char c)
        {
            if (BookFont.Has(c)) return null;

            switch (c)
            {
                case 'ä': return "ae";
                case 'Ä': return "Ae";
                case 'ö': return "oe";
                case 'Ö': return "Oe";
                case 'ü': return "ue";
                case 'Ü': return "Ue";
                case 'ß': return "ss";

                case '—': // em dash
                case '–': // en dash
                case '‑': // non-breaking hyphen
                case '−': // minus
                    return "-";

                case '‘':
                case '’':
                    return "'";

                case '“':
                case '”':
                    return "\"";

                case '…': return "...";
                case '×': return "x";

                case ' ':
                case ' ':
                case ' ':
                case ' ':
                    return " ";

                default: return null;
            }
        }

        // -- Measurement ----------------------------------------------------------------------------

        /// <summary>Painted width, in supersample pixels. Proportional; see <see cref="BookFont.Measure"/>.</summary>
        public static int Measure(string text, int glyphPixel) => Measure(text, glyphPixel, 0);

        /// <summary>
        /// Painted width with <paramref name="extraTracking"/> supersample pixels added between
        /// every pair of glyphs — letterspacing, for running heads. With no extra tracking this is
        /// <see cref="BookFont.Measure"/> exactly, which <c>BookLayoutTests</c> asserts, because two
        /// measurements of the same string differing by a pixel is how text ends up in a margin.
        /// </summary>
        public static int Measure(string text, int glyphPixel, int extraTracking)
        {
            text = Spell(text);
            if (string.IsNullOrEmpty(text)) return 0;

            int width = 0;
            foreach (char c in text)
                width += (BookFont.Glyph(c).Width + BookFont.Tracking) * glyphPixel + extraTracking;

            return width - BookFont.Tracking * glyphPixel - extraTracking;
        }

        /// <summary>One cell of tabular matter, glyph and trailing tracking together.</summary>
        public static int MonoAdvance(int glyphPixel) =>
            (BookFont.CellWidth + BookFont.Tracking) * glyphPixel;

        /// <summary>Painted width of a line set on a fixed advance.</summary>
        public static int MeasureMono(string text, int glyphPixel)
        {
            text = Spell(text);
            if (string.IsNullOrEmpty(text)) return 0;

            return text.Length * MonoAdvance(glyphPixel) - BookFont.Tracking * glyphPixel;
        }

        /// <summary>How many characters of tabular matter fit the measure.</summary>
        public static int MonoColumns(int width, int glyphPixel) =>
            Math.Max(1, (width + BookFont.Tracking * glyphPixel) / MonoAdvance(glyphPixel));

        /// <summary>
        /// Cut to <paramref name="width"/> supersample pixels, keeping the head and never returning
        /// nothing. A hard cut with no ellipsis, for the same reason
        /// <see cref="PixelText.Truncate"/> makes one: the marker would cost more of the name than it
        /// saves, and everything truncated here is printed in full somewhere else.
        /// </summary>
        public static string Truncate(string text, int width, int glyphPixel, int extraTracking = 0)
        {
            text = Spell(text);
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (Measure(text, glyphPixel, extraTracking) <= width) return text;

            int trailing = BookFont.Tracking * glyphPixel + extraTracking;
            int used = 0;
            int kept = 0;

            for (int i = 0; i < text.Length; i++)
            {
                int advance = (BookFont.Glyph(text[i]).Width + BookFont.Tracking) * glyphPixel +
                              extraTracking;
                if (used + advance - trailing > width) break;

                used += advance;
                kept++;
            }

            return text.Substring(0, Math.Max(1, kept));
        }

        /// <summary>As <see cref="Truncate"/>, for a line set on a fixed advance.</summary>
        public static string TruncateMono(string text, int width, int glyphPixel)
        {
            text = Spell(text);
            if (string.IsNullOrEmpty(text)) return string.Empty;

            int columns = MonoColumns(width, glyphPixel);
            return text.Length <= columns ? text : text.Substring(0, columns);
        }

        // -- Wrapping --------------------------------------------------------------------------------

        /// <summary>
        /// Break prose to the measure and append it to <paramref name="output"/>.
        ///
        /// <para>
        /// Breaks on painted width, not on a character count — see the class remarks. A word that
        /// cannot fit a line of its own is broken rather than allowed to overflow, and takes a hyphen
        /// when the face has one, because a word split across two lines with no hyphen reads as two
        /// words. The hyphen is measured before the break is chosen, so the hyphen itself never
        /// pushes the line over.
        /// </para>
        ///
        /// <para>
        /// <paramref name="indent"/> applies to every line, not just the first: it comes from leading
        /// whitespace in the source, which marks a sub-entry, and a sub-entry whose continuation
        /// lines run back out to the margin stops looking like one.
        /// </para>
        /// </summary>
        public static void WrapProse(string text, int glyphPixel, int width, int indent,
                                     int paragraph, List<BookLine> output)
        {
            if (output == null) return;

            text = Spell(text);
            if (string.IsNullOrEmpty(text)) return;

            int tracking = BookFont.Tracking * glyphPixel;
            int spaceWidth = Measure(" ", glyphPixel);
            int available = Math.Max(MonoAdvance(glyphPixel), width - indent);

            var line = new StringBuilder();
            int lineWidth = 0;

            foreach (string token in text.Split(' '))
            {
                if (token.Length == 0) continue;

                string word = token;
                while (true)
                {
                    int joined = line.Length == 0
                        ? 0
                        : lineWidth + tracking + spaceWidth + tracking;
                    int total = joined + Measure(word, glyphPixel);

                    if (total <= available)
                    {
                        if (line.Length > 0) line.Append(' ');
                        line.Append(word);
                        lineWidth = total;
                        break;
                    }

                    if (line.Length > 0)
                    {
                        output.Add(new BookLine(line.ToString(), BookLineStyle.Body, indent, paragraph));
                        line.Clear();
                        lineWidth = 0;
                        continue;
                    }

                    output.Add(new BookLine(BreakWord(word, glyphPixel, available, out word),
                                            BookLineStyle.Body, indent, paragraph));
                    if (word.Length == 0) break;
                }
            }

            if (line.Length > 0)
                output.Add(new BookLine(line.ToString(), BookLineStyle.Body, indent, paragraph));
        }

        /// <summary>
        /// The largest head of <paramref name="word"/> that fits, hyphen included. Always takes at
        /// least one character, so the caller cannot loop for ever on a measure narrower than a
        /// glyph.
        /// </summary>
        private static string BreakWord(string word, int glyphPixel, int available, out string rest)
        {
            int tracking = BookFont.Tracking * glyphPixel;
            bool hyphenate = BookFont.Has('-');
            int hyphenAdvance = hyphenate
                ? (BookFont.Glyph('-').Width + BookFont.Tracking) * glyphPixel
                : 0;

            int used = 0;
            int kept = 0;

            // Never break after the last character: that is not a break, it is the whole word.
            for (int i = 0; i < word.Length - 1; i++)
            {
                int advance = (BookFont.Glyph(word[i]).Width + BookFont.Tracking) * glyphPixel;
                if (used + advance + hyphenAdvance - tracking > available) break;

                used += advance;
                kept++;
            }

            if (kept < 1) kept = 1;

            string head = word.Substring(0, kept);
            rest = word.Substring(kept);

            // One forced character plus a hyphen can still overrun a pathologically narrow measure.
            // Losing the hyphen is better than painting into the margin.
            if (hyphenate && Measure(head + "-", glyphPixel) <= available) head += "-";

            return head;
        }

        // -- Setting a section -----------------------------------------------------------------------

        /// <summary>
        /// Set one chapter — its heading and its prose — as a stream of lines.
        ///
        /// <para>
        /// <b>A single newline is a soft break; a blank line is a paragraph.</b> The content tables
        /// carry breaks that were authored against a 28-column page, and honouring them at this
        /// measure would leave every third line half empty. It also would not survive translation:
        /// German runs about a third longer, so a hard-broken line becomes a full line plus a
        /// two-word orphan. Re-flowing is what lets one source serve two languages and any page size.
        /// </para>
        ///
        /// <para>
        /// Two kinds of source line are exempt, because for them the breaks really are the content:
        /// a line with <b>internal runs of spaces</b> is tabular matter whose columns were aligned by
        /// hand, and a line with <b>leading whitespace</b> is a sub-entry. Both stay on their own
        /// line; the first is additionally set on a fixed advance so its columns still line up in a
        /// proportional face.
        /// </para>
        /// </summary>
        public static List<BookLine> Typeset(string heading, string body)
        {
            var lines = new List<BookLine>();
            int paragraph = 0;

            if (!string.IsNullOrEmpty(heading))
            {
                var wrapped = new List<BookLine>();
                WrapProse(heading, HeadingGlyph, ColumnWidth, 0, paragraph, wrapped);

                for (int i = 0; i < wrapped.Count && i < MaxHeadingLines; i++)
                    lines.Add(new BookLine(wrapped[i].Text, BookLineStyle.Heading, 0, paragraph,
                                           centred: false, keepWithNext: true));
                paragraph++;
            }

            if (!string.IsNullOrEmpty(body))
            {
                var prose = new StringBuilder();

                void FlushProse()
                {
                    if (prose.Length == 0) return;
                    WrapProse(prose.ToString(), BodyGlyph, ColumnWidth, 0, paragraph++, lines);
                    prose.Clear();
                }

                void AddParagraphSpace()
                {
                    if (lines.Count == 0) return;
                    if (lines[lines.Count - 1].Style == BookLineStyle.Blank) return;
                    lines.Add(new BookLine(string.Empty, BookLineStyle.Blank));
                }

                foreach (string source in body.Replace("\r", string.Empty).Split('\n'))
                {
                    string trimmed = source.Trim();

                    if (trimmed.Length == 0)
                    {
                        FlushProse();
                        AddParagraphSpace();
                        continue;
                    }

                    if (IsTabular(trimmed))
                    {
                        FlushProse();
                        lines.Add(new BookLine(TruncateMono(source.TrimEnd(), ColumnWidth, BodyGlyph),
                                               BookLineStyle.Tabular, 0, paragraph++));
                        continue;
                    }

                    if (char.IsWhiteSpace(source[0]))
                    {
                        FlushProse();
                        WrapProse(trimmed, BodyGlyph, ColumnWidth, LeadingIndent(source),
                                  paragraph++, lines);
                        continue;
                    }

                    if (prose.Length > 0) prose.Append(' ');
                    prose.Append(trimmed);
                }

                FlushProse();
            }

            while (lines.Count > 0 && lines[lines.Count - 1].Style == BookLineStyle.Blank)
                lines.RemoveAt(lines.Count - 1);

            return lines;
        }

        /// <summary>
        /// Columns aligned with spaces by whoever wrote the line. Two spaces is enough to say so —
        /// the content tables pad with <c>{id,-9}</c> and with hand-counted runs, and both land here.
        /// A stray double space in prose sets one line on a fixed advance, which looks wide rather
        /// than broken, and is fixable in the string table where it belongs.
        /// </summary>
        private static bool IsTabular(string trimmed) => trimmed.Contains("  ");

        /// <summary>Leading whitespace, measured in spaces of body text.</summary>
        private static int LeadingIndent(string source)
        {
            int spaces = 0;
            foreach (char c in source)
            {
                if (!char.IsWhiteSpace(c)) break;
                spaces += c == '\t' ? 4 : 1;
            }

            return spaces * (BookFont.Glyph(' ').Width + BookFont.Tracking) * BodyGlyph;
        }

        // -- Pagination ------------------------------------------------------------------------------

        /// <summary>
        /// Fill pages with a stream of lines, refusing the breaks a printer would refuse.
        ///
        /// <para>
        /// Three of them, in order of how bad they look: a heading alone at the foot of a page, a
        /// two-line paragraph split down the middle, and a widow — one last line of a paragraph
        /// stranded at the top of the next page — or its mirror, an orphaned first line at the foot.
        /// Each is fixed by pulling the break back a line, and the search gives up after two, because
        /// a visibly short page is worse than the widow it was avoiding.
        /// </para>
        ///
        /// <para>
        /// Deterministic: the same lines and the same measure always produce the same pages, which is
        /// what lets a folio mean anything.
        /// </para>
        /// </summary>
        public static List<TypesetPage> Paginate(IReadOnlyList<BookLine> lines, string section)
        {
            var pages = new List<TypesetPage>();
            if (lines == null || lines.Count == 0)
            {
                pages.Add(new TypesetPage(section, new List<BookLine>()));
                return pages;
            }

            int index = 0;
            while (index < lines.Count)
            {
                // Paragraph space at the head of a page is space nobody asked for.
                while (index < lines.Count && lines[index].Style == BookLineStyle.Blank) index++;
                if (index >= lines.Count) break;

                int start = index;
                int used = 0;
                int end = start;

                while (end < lines.Count)
                {
                    if (used + lines[end].Height > TextHeight) break;
                    used += lines[end].Advance;
                    end++;
                }

                if (end == start) end = start + 1;
                if (end < lines.Count) end = PullBackBadBreak(lines, start, end);

                var page = new List<BookLine>();
                for (int i = start; i < end; i++) page.Add(lines[i]);
                while (page.Count > 0 && page[page.Count - 1].Style == BookLineStyle.Blank)
                    page.RemoveAt(page.Count - 1);

                pages.Add(new TypesetPage(section, page));
                index = end;
            }

            if (pages.Count == 0) pages.Add(new TypesetPage(section, new List<BookLine>()));
            return pages;
        }

        /// <summary>The title page: the book's own name, and nothing else on the paper.</summary>
        public static TypesetPage TitlePage(string title)
        {
            var lines = new List<BookLine>();
            var wrapped = new List<BookLine>();
            WrapProse(title, HeadingGlyph, ColumnWidth, 0, 0, wrapped);

            for (int i = 0; i < wrapped.Count && i < 3; i++)
                lines.Add(new BookLine(wrapped[i].Text, BookLineStyle.Heading, 0, 0, centred: true));

            return new TypesetPage(title, lines, isTitlePage: true);
        }

        private static int PullBackBadBreak(IReadOnlyList<BookLine> lines, int start, int end)
        {
            int floor = Math.Max(start + 1, end - 2);

            int candidate = end;
            while (candidate > floor && IsBadBreak(lines, candidate)) candidate--;

            return IsBadBreak(lines, candidate) ? end : candidate;
        }

        /// <summary><paramref name="index"/> is the first line of the following page.</summary>
        private static bool IsBadBreak(IReadOnlyList<BookLine> lines, int index)
        {
            if (index <= 0 || index >= lines.Count) return false;

            BookLine previous = lines[index - 1];
            BookLine next = lines[index];

            if (previous.KeepWithNext) return true;
            if (previous.Style == BookLineStyle.Blank) return false;
            if (previous.Paragraph < 0 || previous.Paragraph != next.Paragraph) return false;

            if (ParagraphLength(lines, next.Paragraph) < 3) return true;

            bool widow = index + 1 >= lines.Count || lines[index + 1].Paragraph != next.Paragraph;
            bool orphan = index - 2 < 0 || lines[index - 2].Paragraph != previous.Paragraph;

            return widow || orphan;
        }

        private static int ParagraphLength(IReadOnlyList<BookLine> lines, int paragraph)
        {
            int count = 0;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Paragraph == paragraph) count++;

            return count;
        }
    }
}
