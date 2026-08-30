using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Residue.Gameplay.World;
using UnityEngine;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The reference book's typesetting.
    ///
    /// <para>
    /// Everything here is a pure function of a string and a measure, so the page a player actually
    /// reads is pinned without a scene, a texture or an Editor. That matters more for the book than
    /// for most screens: a layout regression on paper is silent — the text is still present, still
    /// legible, and simply says less than it did, or says it half a millimetre into the margin where
    /// nobody notices until a screenshot.
    /// </para>
    ///
    /// <para>
    /// These deliberately assert <b>inequalities and invariants</b>, never glyph counts.
    /// <see cref="BookFont"/>'s table grows; a test that pinned "fifty-six characters a line" would
    /// fail every time a letter was drawn slightly narrower, which is not a regression in anything.
    /// </para>
    /// </summary>
    public sealed class BookLayoutTests
    {
        private const string Prose =
            "A quench oil that has oxidised reads high on acid number and low on flash point.\n" +
            "Water ingress shows as a collapse in flash point with an unchanged additive package.\n" +
            "\n" +
            "Carryover is about four per cent of whatever went through last. Push a solvent blank " +
            "to see it, and hold the flush button to clear it. One charge per instrument.\n" +
            "\n" +
            "Calibration drifts in a direction that is re-rolled each day. Run a certified " +
            "reference sample if you suspect it, because a drifting instrument reports a clean " +
            "sample and a dirty one with the same confidence.";

        private const string Tabular =
            "ELEMENT   NORMAL          CRITICAL\n" +
            "\n" +
            "fe        <= 40           >= 120\n" +
            "water     <= 300          >= 1200\n" +
            "visc40    46.0 +/-8%      +/-20%\n";

        private static List<BookLine> Set(string heading, string body) =>
            BookLayout.Typeset(heading, body);

        private static List<TypesetPage> Paginated(string heading, string body) =>
            BookLayout.Paginate(Set(heading, body), heading);

        // -------------------------------------------------------------------------------------------
        // Measurement. Everything else rests on this being the same number twice.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: the layout measures a string exactly as the face paints it.
        /// <para>
        /// Two measurements of one string differing by a pixel is how text ends up in a margin, and
        /// the book has one measure function and the font has another. They must agree, so this
        /// checks them against each other rather than against a number typed here.
        /// </para>
        /// </summary>
        [Test]
        public void Measure_AgreesWithTheFacesOwnMeasure()
        {
            foreach (string text in new[] { "", "a", "iii", "Water ingress", "46.0 +/-8%", "Grundöl" })
            foreach (int glyph in new[] { BookLayout.SmallGlyph, BookLayout.BodyGlyph, BookLayout.HeadingGlyph })
            {
                Assert.AreEqual(BookFont.Measure(BookLayout.Spell(text), glyph),
                                BookLayout.Measure(text, glyph),
                                $"'{text}' at {glyph} measures differently in the layout than in the face.");
            }
        }

        /// <summary>
        /// Promise: letterspacing is charged for. A running head is set in tracked capitals, and
        /// tracking that is drawn but not measured is tracking that runs off the page.
        /// </summary>
        [Test]
        public void Measure_ChargesForExtraTracking_ExceptAfterTheLastGlyph()
        {
            const string text = "OPERATOR MANUAL";
            const int tracking = 4;

            int plain = BookLayout.Measure(text, BookLayout.SmallGlyph);
            int tracked = BookLayout.Measure(text, BookLayout.SmallGlyph, tracking);

            Assert.AreEqual(plain + tracking * (BookLayout.Spell(text).Length - 1), tracked,
                "Tracking sits between glyphs, so a string of n glyphs carries n-1 of it.");
        }

        // -------------------------------------------------------------------------------------------
        // Wrapping. The book's own, because PixelText measures in columns and this face is
        // proportional — see BookLayout's class remarks.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: **no wrapped line is wider than the measure it was wrapped to.**
        /// <para>
        /// The one thing wrapping exists to guarantee. Asserted on the book's real column, at every
        /// size type is set at, and on an indent — a hanging indent narrows the available width, and
        /// a wrapper that measures against the full column instead paints the overhang into the
        /// gutter.
        /// </para>
        /// </summary>
        [Test]
        public void Wrap_NeverProducesALineWiderThanTheMeasure()
        {
            foreach (int glyph in new[] { BookLayout.BodyGlyph, BookLayout.HeadingGlyph })
            foreach (int indent in new[] { 0, 100, 400 })
            {
                var lines = new List<BookLine>();
                BookLayout.WrapProse(Prose.Replace("\n", " "), glyph, BookLayout.ColumnWidth,
                                     indent, 0, lines);

                Assert.IsNotEmpty(lines);

                foreach (BookLine line in lines)
                {
                    int painted = BookLayout.Measure(line.Text, glyph);
                    Assert.LessOrEqual(painted + line.Indent, BookLayout.ColumnWidth,
                        $"'{line.Text}' paints {painted}px into a {BookLayout.ColumnWidth - indent}px measure.");
                    Assert.AreEqual(line.Text.Trim(), line.Text,
                        $"'{line.Text}' carries whitespace into the margin.");
                }
            }
        }

        /// <summary>
        /// Promise: a word too long for a line of its own is broken, not allowed to overflow.
        /// <para>
        /// German compounds and element ids do this routinely, and the old wrapper's answer was to
        /// truncate — the tail of the word simply vanished. A break keeps it.
        /// </para>
        /// </summary>
        [Test]
        public void Wrap_BreaksAWordTooLongForTheMeasure_RatherThanOverflowingOrLosingIt()
        {
            string monster = new string('m', 400);
            var lines = new List<BookLine>();
            BookLayout.WrapProse("tan " + monster + " ok", BookLayout.BodyGlyph,
                                 BookLayout.ColumnWidth, 0, 0, lines);

            Assert.Greater(lines.Count, 2, "A 400 character word cannot be one line.");

            var rebuilt = new StringBuilder();
            foreach (BookLine line in lines)
            {
                Assert.LessOrEqual(BookLayout.Measure(line.Text, BookLayout.BodyGlyph),
                                   BookLayout.ColumnWidth,
                                   $"'{line.Text}' overruns the measure.");
                rebuilt.Append(line.Text.Replace("-", string.Empty).Replace(" ", string.Empty));
            }

            Assert.AreEqual("tan" + monster + "ok", rebuilt.ToString(),
                "Breaking a word is a change of shape, not of content: every letter survives.");
        }

        /// <summary>Promise: wrapping is a change of shape. Every word survives, in order.</summary>
        [Test]
        public void Wrap_KeepsEveryWordInOrder()
        {
            const string sentence = "Oxidation raises the acid number and the viscosity together.";

            var lines = new List<BookLine>();
            BookLayout.WrapProse(sentence, BookLayout.HeadingGlyph, 400, 0, 0, lines);

            var joined = new List<string>();
            foreach (BookLine line in lines) joined.Add(line.Text);

            Assert.Greater(lines.Count, 1, "That sentence does not fit 400px of heading.");
            Assert.AreEqual(sentence, string.Join(" ", joined));
        }

        [Test]
        public void Wrap_OfNothing_ProducesNothing()
        {
            var lines = new List<BookLine>();
            BookLayout.WrapProse(null, BookLayout.BodyGlyph, BookLayout.ColumnWidth, 0, 0, lines);
            BookLayout.WrapProse("", BookLayout.BodyGlyph, BookLayout.ColumnWidth, 0, 0, lines);

            Assert.IsEmpty(lines);
        }

        // -------------------------------------------------------------------------------------------
        // Setting a section.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: hand-aligned columns survive a proportional face.
        /// <para>
        /// The content tables pad threshold rows and instrument figures with spaces. In a
        /// proportional face those columns dissolve, so a line with internal runs of spaces is set on
        /// a fixed advance instead — and never wrapped, because half a table row is not a table row.
        /// </para>
        /// </summary>
        [Test]
        public void Typeset_SetsHandAlignedColumnsOnAFixedAdvance_AndNeverWrapsThem()
        {
            var lines = Set(null, Tabular);

            int tabular = 0;
            foreach (BookLine line in lines)
            {
                if (line.Style != BookLineStyle.Tabular) continue;

                tabular++;
                Assert.LessOrEqual(BookLayout.MeasureMono(line.Text, BookLayout.BodyGlyph),
                                   BookLayout.ColumnWidth,
                                   $"'{line.Text}' overruns the measure it was cut to fit.");
            }

            Assert.AreEqual(4, tabular,
                "The column header and the three threshold rows are all tabular matter.");
        }

        /// <summary>
        /// Promise: a single newline re-flows, a blank line does not.
        /// <para>
        /// The breaks in the content tables were authored against a 28-column page and would leave
        /// every other line half empty at this measure — and German, running about a third longer,
        /// would break differently again. Only a blank line is structure.
        /// </para>
        /// </summary>
        [Test]
        public void Typeset_TreatsASingleNewlineAsASpaceAndABlankLineAsAParagraph()
        {
            var joined = Set(null, "one\ntwo");
            Assert.AreEqual(1, joined.Count);
            Assert.AreEqual("one two", joined[0].Text);

            var split = Set(null, "one\n\ntwo");
            Assert.AreEqual(3, split.Count);
            Assert.AreEqual(BookLineStyle.Blank, split[1].Style,
                "A paragraph is marked by space, not by an indent — the book uses one or the other.");
            Assert.AreEqual("two", split[2].Text);
            Assert.AreEqual(0, split[2].Indent, "Spacing and indenting together is belt and braces.");
        }

        /// <summary>
        /// Promise: a heading never runs off the paper.
        /// <para>
        /// It used to be neither wrapped nor cut, so a section name longer than the page simply ran
        /// out past the margin. It now sets over two lines and is cut after that.
        /// </para>
        /// </summary>
        [Test]
        public void Typeset_KeepsALongHeadingOnThePaper()
        {
            var lines = Set("Rotationsviskosimeter und Kaltabschreckoel — Betriebsanleitung", "body");

            int headings = 0;
            foreach (BookLine line in lines)
            {
                if (line.Style != BookLineStyle.Heading) continue;

                headings++;
                Assert.LessOrEqual(BookLayout.Measure(line.Text, BookLayout.HeadingGlyph),
                                   BookLayout.ColumnWidth,
                                   $"The heading line '{line.Text}' paints past the measure.");
                Assert.IsTrue(line.KeepWithNext, "A heading is glued to the text below it.");
            }

            Assert.GreaterOrEqual(headings, 1);
            Assert.LessOrEqual(headings, BookLayout.MaxHeadingLines);
        }

        // -------------------------------------------------------------------------------------------
        // Pagination.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: **a page never holds more lines than fit on it.**
        /// <para>
        /// Measured the way the page is drawn: every line advances the next one, and the last line
        /// has to fit its own ink — descenders included — inside the text block. Counting advances
        /// alone would let the final line's tail hang into the foot margin, over the folio.
        /// </para>
        /// </summary>
        [Test]
        public void APage_NeverHoldsMoreLinesThanFit()
        {
            var body = new StringBuilder();
            for (int i = 0; i < 40; i++) body.Append(Prose).Append("\n\n").Append(Tabular).Append('\n');

            var pages = Paginated("Operation", body.ToString());
            Assert.Greater(pages.Count, 3, "That much prose is several pages.");

            foreach (TypesetPage page in pages)
            {
                if (page.Lines.Count == 0) continue;

                int used = 0;
                for (int i = 0; i < page.Lines.Count - 1; i++) used += page.Lines[i].Advance;
                used += page.Lines[page.Lines.Count - 1].Height;

                Assert.LessOrEqual(used, BookLayout.TextHeight,
                    $"A page of {page.Lines.Count} lines is {used}px deep in a " +
                    $"{BookLayout.TextHeight}px text block.");
            }
        }

        /// <summary>
        /// Promise: the page is actually filled. A regression that halved the text block would still
        /// satisfy the test above, and would silently double the length of every manual.
        /// </summary>
        [Test]
        public void AFullPage_CarriesAProperPageOfText()
        {
            var body = new StringBuilder();
            for (int i = 0; i < 40; i++) body.Append(Prose).Append("\n\n");

            var pages = Paginated("Operation", body.ToString());

            int deepest = 0;
            foreach (TypesetPage page in pages) deepest = Mathf.Max(deepest, page.Lines.Count);

            Assert.GreaterOrEqual(deepest, 20,
                "A page of a book is a couple of dozen lines. Anything much less is a leaflet.");
        }

        /// <summary>
        /// Promise: **pagination is stable.** The same content always breaks in the same places, or a
        /// folio means nothing and a player told "page 6" finds something else there.
        /// </summary>
        [Test]
        public void Pagination_IsStableForTheSameContent()
        {
            var first = Paginated("Blind spots", Prose + "\n\n" + Tabular + "\n\n" + Prose);
            var second = Paginated("Blind spots", Prose + "\n\n" + Tabular + "\n\n" + Prose);

            Assert.AreEqual(first.Count, second.Count);

            for (int p = 0; p < first.Count; p++)
            {
                Assert.AreEqual(first[p].Lines.Count, second[p].Lines.Count, $"Page {p} changed depth.");
                for (int i = 0; i < first[p].Lines.Count; i++)
                    Assert.AreEqual(first[p].Lines[i].Text, second[p].Lines[i].Text,
                        $"Page {p} line {i} changed.");
            }
        }

        /// <summary>
        /// Promise: a heading is never left alone at the foot of a page, and a paragraph never leaves
        /// a single line stranded at the top of the next one.
        /// <para>
        /// The two most obvious typesetting faults there are. Both are fixed by pulling the break
        /// back a line, so both are cheap, which is the standard the brief sets for them.
        /// </para>
        /// </summary>
        [Test]
        public void Pagination_LeavesNoStrandedHeadingAndNoWidow()
        {
            // Many short sections, so headings land at every depth on the page and the breaks are
            // forced into every awkward position in turn.
            var lines = new List<BookLine>();
            for (int i = 0; i < 30; i++) lines.AddRange(Set($"Section {i}", Prose));

            var pages = BookLayout.Paginate(lines, "Sections");

            for (int p = 0; p < pages.Count; p++)
            {
                var page = pages[p];
                if (page.Lines.Count == 0) continue;

                BookLine last = page.Lines[page.Lines.Count - 1];
                Assert.IsFalse(last.KeepWithNext,
                    $"Page {p} ends on a heading with nothing under it.");

                if (p + 1 >= pages.Count || pages[p + 1].Lines.Count != 1) continue;

                Assert.AreNotEqual(last.Paragraph, pages[p + 1].Lines[0].Paragraph,
                    $"Page {p + 1} is a single line of the paragraph page {p} ends with — a widow.");
            }
        }

        /// <summary>A book with nothing in it still has a page, so a folio can be printed on it.</summary>
        [Test]
        public void Paginating_Nothing_StillYieldsAPage()
        {
            Assert.AreEqual(1, BookLayout.Paginate(new List<BookLine>(), "Empty").Count);
            Assert.AreEqual(1, BookLayout.Paginate(null, "Empty").Count);
        }

        [Test]
        public void TheTitlePage_CarriesNoRunningHeadAndNoFolio()
        {
            TypesetPage title = BookLayout.TitlePage("Elements & Sources");

            Assert.IsTrue(title.IsTitlePage);
            Assert.IsNotEmpty(title.Lines);

            foreach (BookLine line in title.Lines)
            {
                Assert.IsTrue(line.Centred, "A title page is centred on its measure.");
                Assert.LessOrEqual(BookLayout.Measure(line.Text, BookLayout.HeadingGlyph),
                                   BookLayout.ColumnWidth);
            }
        }

        // -------------------------------------------------------------------------------------------
        // Page geometry. The numbers the drawing and the pagination both read.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: the text block clears the foot margin, where the folio and the page-corner
        /// controls live. If it did not, a descender on the last line would sit on the page number.
        /// </summary>
        [Test]
        public void TheTextBlock_ClearsTheFootMarginItSharesThePageWith()
        {
            Assert.Less(BookLayout.TextTop + BookLayout.TextHeight, BookLayout.FolioTop,
                "The text block runs into the folio.");

            Assert.Less(BookLayout.FolioTop + BookFont.Height * BookLayout.SmallGlyph,
                        BookLayout.SsPageHeight,
                        "The folio hangs off the bottom of the paper.");

            RectInt corner = InspectableBookSurface.CornerRectOnPage(left: true);
            Assert.GreaterOrEqual(corner.y * BookLayout.Supersample,
                                  BookLayout.TextTop + BookLayout.TextHeight,
                                  "The page-corner control overlaps the text block.");
            Assert.LessOrEqual(corner.yMax, BookLayout.PageHeight,
                               "The page-corner control hangs off the paper.");
        }

        /// <summary>
        /// Promise: the measure is a readable one. Between about 45 and 75 characters a line is the
        /// range prose is read at; much narrower and the eye jumps every second word.
        /// </summary>
        [Test]
        public void TheMeasure_IsAReadableLineLength()
        {
            var lines = new List<BookLine>();
            BookLayout.WrapProse(Prose.Replace("\n", " "), BookLayout.BodyGlyph,
                                 BookLayout.ColumnWidth, 0, 0, lines);

            int longest = 0;
            foreach (BookLine line in lines) longest = Mathf.Max(longest, line.Text.Length);

            Assert.GreaterOrEqual(longest, 40,
                $"{longest} characters is a newspaper column, not a page of a book.");
        }

        /// <summary>
        /// Promise: type is set at a scale that is <b>not</b> a whole number of final pixels.
        /// <para>
        /// This is the entire anti-aliasing mechanism, and it is invisible if it breaks: set the type
        /// at a multiple of the supersample factor and every glyph edge is fully covered or fully
        /// empty, the downsample averages nothing, and the page silently goes back to looking like a
        /// terminal at a larger size.
        /// </para>
        /// </summary>
        [Test]
        public void EverySizeTypeIsSetAt_LandsBetweenFinalPixels()
        {
            foreach (int glyph in new[] { BookLayout.SmallGlyph, BookLayout.BodyGlyph, BookLayout.HeadingGlyph })
                Assert.AreNotEqual(0, glyph % BookLayout.Supersample,
                    $"Type set at {glyph} supersample pixels per font pixel is a whole " +
                    $"{glyph / BookLayout.Supersample} final pixels, so its edges never anti-alias.");
        }

        // -------------------------------------------------------------------------------------------
        // The page-corner controls. One rectangle, or they drift apart again.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: **what can be pressed is what is drawn.**
        /// <para>
        /// These were once two independent sets of magic numbers that disagreed — the printed control
        /// sat in the outer corner while the hit test accepted most of the page, so a paragraph could
        /// be clicked to turn the page. Both now come from <c>CornerRectOnPage</c>; this checks they
        /// still do.
        /// </para>
        /// </summary>
        [Test]
        public void TheCornerHitbox_AgreesWithTheDrawnRectangle()
        {
            foreach (bool left in new[] { true, false })
            {
                RectInt rect = InspectableBookSurface.CornerButtonRect(left);

                Assert.IsTrue(Hits(left, rect.center.x, rect.center.y),
                    "The middle of the printed control does not press it.");

                foreach (var corner in new[]
                         {
                             (rect.x + 1f, rect.y + 1f), (rect.xMax - 1f, rect.y + 1f),
                             (rect.x + 1f, rect.yMax - 1f), (rect.xMax - 1f, rect.yMax - 1f)
                         })
                {
                    Assert.IsTrue(Hits(left, corner.Item1, corner.Item2),
                        "A corner of the printed control does not press it.");
                }

                Assert.IsFalse(Hits(left, BookLayout.SpreadWidth / 2f, BookLayout.TextTop),
                    "A press in the body text turns the page.");
            }
        }

        /// <summary>
        /// Promise: the grab room around the control is symmetric, so the hitbox stays centred on
        /// what is drawn rather than drifting off one side of it.
        /// </summary>
        [Test]
        public void TheCornerHitbox_ForgivesTheEdgeEqually_OnEverySide()
        {
            RectInt rect = InspectableBookSurface.CornerButtonRect(left: true);
            float y = rect.center.y;
            float x = rect.center.x;

            for (int d = 1; d < 40; d++)
            {
                Assert.AreEqual(Hits(true, rect.x - d, y), Hits(true, rect.xMax + d, y),
                    $"{d}px outside the control is a press on one side and not the other.");
                Assert.AreEqual(Hits(true, x, rect.y - d), Hits(true, x, rect.yMax + d),
                    $"{d}px above the control is a press but {d}px below it is not.");
            }

            Assert.IsFalse(Hits(true, rect.x - 200, y), "The hitbox has no outer edge at all.");
        }

        /// <summary>
        /// Promise: the two controls are the same control facing opposite ways. They stopped being
        /// that once already, when one arrow was placed by mirroring the other's offset.
        /// </summary>
        [Test]
        public void TheTwoCornerControls_AreMirrorImages()
        {
            RectInt previous = InspectableBookSurface.CornerButtonRect(left: true);
            RectInt next = InspectableBookSurface.CornerButtonRect(left: false);

            Assert.AreEqual(previous.width, next.width);
            Assert.AreEqual(previous.height, next.height);
            Assert.AreEqual(previous.y, next.y);
            Assert.AreEqual(previous.x, BookLayout.SpreadWidth - next.xMax,
                "One control sits further from its own edge than the other.");
        }

        private static bool Hits(bool left, float x, float y)
        {
            Vector2 page = InspectableBookSurface.PageToLocal(x, y);
            return InspectableBookSurface.HitsCornerButton(left, new Vector3(page.x, 0f, page.y));
        }

        // -------------------------------------------------------------------------------------------
        // German (#55). PixelFont.Transliterate is not applied to this face; the book has its own
        // fallback, and it is conditional on what the face actually carries.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: no German letter reaches the page as a blank.
        /// <para>
        /// <see cref="BookFont"/> falls back to a blank of average width for a character it has no
        /// glyph for, so before this an untranslated umlaut drew as a hole in the middle of a word —
        /// the exact fault <c>PixelFont.Transliterate</c> exists to prevent on the instrument
        /// screens. The book cannot use that one: it folds to capitals, which is wrong in a face with
        /// real lowercase, and it fires unconditionally, which would keep firing after the face
        /// gained a proper diaeresis.
        /// </para>
        /// </summary>
        [Test]
        public void Spell_LeavesNoGermanLetterTheFaceCannotSet()
        {
            const string german = "Grundöl, Prüfgerät, Messgröße — Abschreckhärte";

            foreach (char c in BookLayout.Spell(german))
            {
                if (BookFont.Has(c)) continue;

                Assert.IsFalse("äöüÄÖÜß—".IndexOf(c) >= 0,
                    $"'{c}' has no glyph and no fallback spelling, so it draws as a hole in a word.");
            }
        }

        /// <summary>
        /// Promise: spelling is idempotent, because it happens both when a line is measured and again
        /// when it is drawn. A second pass that changed anything would draw a different string from
        /// the one that was fitted to the measure.
        /// </summary>
        [Test]
        public void Spell_IsIdempotent()
        {
            foreach (string text in new[] { "Grundöl", "plain ascii", "", "Messgröße" })
                Assert.AreEqual(BookLayout.Spell(text), BookLayout.Spell(BookLayout.Spell(text)));

            Assert.AreSame("plain ascii", BookLayout.Spell("plain ascii"),
                "Text with nothing to change must not be rebuilt — that is every English string.");
        }

        /// <summary>
        /// Promise: German still fits. It runs about a third longer than English, and a running head
        /// is cut to the measure rather than allowed to set into the gutter.
        /// </summary>
        [Test]
        public void ARunningHead_IsCutToTheMeasure_InEitherLanguage()
        {
            foreach (string title in new[]
                     {
                         "OPERATOR MANUAL",
                         "BETRIEBSANLEITUNG FÜR DAS ROTATIONSVISKOSIMETER UND DIE ZUBEHÖRTEILE"
                     })
            {
                string head = BookLayout.Truncate(title, BookLayout.ColumnWidth,
                                                  BookLayout.SmallGlyph, BookLayout.RunningHeadTracking);

                Assert.IsNotEmpty(head);
                Assert.LessOrEqual(
                    BookLayout.Measure(head, BookLayout.SmallGlyph, BookLayout.RunningHeadTracking),
                    BookLayout.ColumnWidth,
                    $"'{head}' sets past the measure and into the gutter.");
            }
        }
    }
}
