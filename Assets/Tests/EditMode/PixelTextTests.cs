using System.Collections.Generic;
using NUnit.Framework;
using Residue.Gameplay.World;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The in-world text layout behind the instrument screens (#50). The reference book used to
    /// share it and no longer does — <see cref="BookLayout"/> measures a proportional face on
    /// painted width, where this counts columns of a monospaced one.
    /// <para>
    /// Wrapping and truncation are pure functions of a string and a column count, so the thing a
    /// player actually reads on a machine panel is pinned here rather than by looking at it. These
    /// screens are the only place a lot of information exists — a fresh reading appears on the glass
    /// before it is anywhere else — and a layout regression there is silent: the text is still
    /// present, still legible, and simply says less than it did.
    /// </para>
    /// <para>
    /// The awkward inputs are the point. A word longer than the column, an empty paragraph, a
    /// trailing space and a string that is exactly the column width are all things the content
    /// tables contain, and each of them is a different branch.
    /// </para>
    /// </summary>
    public sealed class PixelTextTests
    {
        // Two real fields, so the cases below are the shape callers actually pass rather than round
        // numbers. The first was the reference book's page before it moved to BookFont and
        // BookLayout — it is kept because it is a wide field at scale 2 and the arithmetic here has
        // no other caller that shape. The book itself no longer measures in columns at all: its face
        // is proportional, so a column count is the wrong question for it (see BookLayout).
        private const int WideField = 256;
        private const int WideFieldMargin = 14;
        private const int WideFieldScale = 2;

        private const int PanelWidth = 128;      // MachineDisplay's default glass
        private const int PanelScale = 2;

        private static List<string> Wrapped(string text, int columns)
        {
            var lines = new List<string>();
            PixelText.Wrap(text, columns, lines);
            return lines;
        }

        // -------------------------------------------------------------------------------------------
        // Fitting a field.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// A full-width line has to still clear the margin it was measured against. The column count
        /// charges every character its trailing gap even though the last one is never painted, which
        /// is the slack that guarantees it.
        /// </summary>
        [Test]
        public void AFullWidthLine_FitsTheFieldItWasMeasuredFor()
        {
            var fields = new (string Name, int Width, int Scale)[]
            {
                ("wide field", WideField - WideFieldMargin * 2, WideFieldScale),
                ("instrument panel", PanelWidth - 4, PanelScale),
                ("one glyph exactly", PixelFont.Advance * 3, 3)
            };

            foreach (var field in fields)
            {
                int columns = PixelText.Columns(field.Width, field.Scale);
                int painted = PixelFont.MeasureWidth(new string('W', columns), field.Scale);

                Assert.LessOrEqual(painted, field.Width,
                    $"{columns} columns of text paint {painted}px into the {field.Name}'s " +
                    $"{field.Width}px, so a full line runs into the margin.");

                Assert.Greater(PixelFont.MeasureWidth(new string('W', columns + 1), field.Scale),
                    field.Width,
                    $"The {field.Name} could fit another column. Losing one is losing a character " +
                    "off every truncated caption in the game.");
            }
        }

        /// <summary>
        /// A field narrower than a single glyph is a mis-configured screen, not a reason to hand back
        /// zero — every caller divides a line into columns and a zero-column screen either loops or
        /// prints nothing at all.
        /// </summary>
        [Test]
        public void AFieldTooNarrowForAGlyph_StillReportsOneColumn()
        {
            Assert.AreEqual(1, PixelText.Columns(0, 2));
            Assert.AreEqual(1, PixelText.Columns(3, 2));
            Assert.AreEqual(1, PixelText.Columns(-40, 2));
        }

        /// <summary>The instrument's own numbers, so a change to the shared maths is noticed here.</summary>
        [Test]
        public void TheShippedScreens_KeepTheColumnCountsTheyWereLaidOutAgainst()
        {
            Assert.AreEqual(28, PixelText.Columns(WideField - WideFieldMargin * 2, WideFieldScale),
                "A 228px field at scale 2 is 28 characters wide.");

            Assert.AreEqual(15, PixelText.Columns(PanelWidth - 4, PanelScale),
                "An instrument's 128px glass at scale 2 is 15 characters wide.");
        }

        [Test]
        public void LineHeight_LeavesTheFontsOwnLeadingAtEveryScale()
        {
            Assert.AreEqual(PixelFont.GlyphHeight + PixelText.LineGap, PixelText.LineHeight(1));
            Assert.AreEqual((PixelFont.GlyphHeight + PixelText.LineGap) * 2, PixelText.LineHeight(2));
            Assert.AreEqual((PixelFont.GlyphHeight + PixelText.LineGap) * 4, PixelText.LineHeight(4));

            Assert.Greater(PixelText.LineHeight(2), PixelFont.GlyphHeight * 2,
                "Baselines a glyph apart make two rows of a table read as one block of pixels.");
        }

        // -------------------------------------------------------------------------------------------
        // Truncation. A hard cut, on purpose — see PixelText.Truncate.
        // -------------------------------------------------------------------------------------------

        [Test]
        public void Truncate_LeavesAnythingThatAlreadyFits()
        {
            Assert.AreEqual("WERK-1", PixelText.Truncate("WERK-1", 15));
            Assert.AreEqual("", PixelText.Truncate("", 15));
            Assert.AreEqual("", PixelText.Truncate(null, 15),
                "A screen with no caption yet draws an empty line, not a null reference.");
        }

        /// <summary>
        /// The boundary the instrument captions live on. A tag that is exactly the column width is
        /// complete and must not lose its last character to an off-by-one.
        /// </summary>
        [Test]
        public void Truncate_KeepsAStringThatIsExactlyTheColumnWidth()
        {
            const string tag = "HALLE-3 MARTEMP"; // 15 characters, an instrument panel's full width
            Assert.AreEqual(15, tag.Length);
            Assert.AreEqual(tag, PixelText.Truncate(tag, 15));
        }

        /// <summary>
        /// Keeps the head, adds nothing. An ellipsis would spend three of an instrument's fifteen
        /// columns on saying something the clipped word already says.
        /// </summary>
        [Test]
        public void Truncate_CutsTheTailAndDoesNotSpendColumnsOnAMarker()
        {
            string cut = PixelText.Truncate("HALLE-3 MARTEMPER 2", 15);

            Assert.AreEqual("HALLE-3 MARTEMP", cut);
            Assert.AreEqual(15, cut.Length, "A truncated line must occupy exactly the columns it was given.");
            Assert.IsFalse(cut.EndsWith("..."), "PixelFont has no ellipsis to spend a column on.");
        }

        /// <summary>
        /// A nonsense column count comes from a mis-configured screen. One character is a visible
        /// wrongness; an empty string is a screen that looks like it has nothing to say.
        /// </summary>
        [Test]
        public void Truncate_StillYieldsACharacterWhenTheFieldIsNonsense()
        {
            Assert.AreEqual("H", PixelText.Truncate("HALLE-3", 0));
            Assert.AreEqual("H", PixelText.Truncate("HALLE-3", -5));
        }

        // -------------------------------------------------------------------------------------------
        // Wrapping. The book's branch.
        // -------------------------------------------------------------------------------------------

        [Test]
        public void Wrap_OfNothing_ProducesNothing()
        {
            Assert.AreEqual(0, Wrapped(null, 20).Count);
            Assert.AreEqual(0, Wrapped("", 20).Count);
        }

        [Test]
        public void Wrap_BreaksOnWordsAndNeverExceedsTheColumn()
        {
            var lines = Wrapped(
                "A QUENCH OIL THAT HAS OXIDISED READS HIGH ON ACID NUMBER AND LOW ON FLASH POINT.",
                20);

            Assert.Greater(lines.Count, 1, "That sentence does not fit on one 20 column line.");

            foreach (string line in lines)
            {
                Assert.LessOrEqual(line.Length, 20, $"'{line}' overruns the page.");
                Assert.AreEqual(line.Trim(), line, $"'{line}' carries whitespace into the margin.");
            }

            Assert.AreEqual("A QUENCH OIL THAT HAS OXIDISED READS HIGH ON ACID NUMBER AND LOW ON " +
                            "FLASH POINT.", string.Join(" ", lines),
                "Wrapping is a change of shape, not of content: every word survives, in order.");
        }

        /// <summary>
        /// A line that lands exactly on the column boundary is full, not overfull, and must not be
        /// broken a word early — that is a whole line of the page thrown away on every paragraph.
        /// </summary>
        [Test]
        public void Wrap_FillsALineThatLandsExactlyOnTheColumn()
        {
            var lines = Wrapped("ABCD EFGH IJKL", 14);

            Assert.AreEqual(1, lines.Count, "14 characters into 14 columns is one full line.");
            Assert.AreEqual("ABCD EFGH IJKL", lines[0]);
        }

        /// <summary>
        /// The awkward one. A word wider than the page cannot be wrapped, so it is cut — but it is
        /// measured at its full length first, so it still forces the break its real width demands and
        /// then takes a line of its own. Packing the cut remainder onto the previous line would make
        /// a truncated word look like a short one.
        /// </summary>
        [Test]
        public void Wrap_GivesAWordLongerThanThePageItsOwnCutLine()
        {
            var lines = Wrapped("TAN ANTIOXIDANTDEPLETIONRATE OK", 10);

            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual("TAN", lines[0]);
            Assert.AreEqual("ANTIOXIDAN", lines[1]);
            Assert.AreEqual(10, lines[1].Length, "The cut word occupies the full column width.");
            Assert.AreEqual("OK", lines[2],
                "What follows an over-long word starts a fresh line rather than joining its remains.");
        }

        [Test]
        public void Wrap_KeepsParagraphBreaksAndCollapsesStrayWhitespace()
        {
            var lines = Wrapped("FIRST\n\nSECOND", 20);

            CollectionAssert.AreEqual(new[] { "FIRST", "", "SECOND" }, lines,
                "A blank line between paragraphs is the only paragraph mark this font has.");

            CollectionAssert.AreEqual(new[] { "ONE TWO" }, Wrapped("  ONE   TWO  ", 20),
                "Hand-written table prose picks up stray spaces; they must not print as holes.");

            CollectionAssert.AreEqual(new[] { "" }, Wrapped("   ", 20),
                "A whitespace-only paragraph is a paragraph break, not a line of spaces.");
        }

        /// <summary>Windows line endings reach here from the content tables; the CR must not print.</summary>
        [Test]
        public void Wrap_DoesNotPrintACarriageReturnAsAGlyph()
        {
            var lines = Wrapped("FIRST\r\nSECOND", 20);

            CollectionAssert.AreEqual(new[] { "FIRST", "SECOND" }, lines);
            foreach (string line in lines) StringAssert.DoesNotContain("\r", line);
        }

        /// <summary>
        /// The end-to-end promise, against a real field's geometry: nothing the wrapper produces can
        /// paint past the margin it was laid out for.
        /// </summary>
        [Test]
        public void Wrap_AtAFieldsOwnGeometry_NeverPaintsIntoTheMargin()
        {
            const int available = WideField - WideFieldMargin * 2;
            int columns = PixelText.Columns(available, WideFieldScale);

            var lines = Wrapped(
                "OXIDATION RAISES THE ACID NUMBER AND THE VISCOSITY TOGETHER.\n\n" +
                "WATER INGRESS SHOWS AS A COLLAPSE IN FLASH POINT WITH AN UNCHANGED " +
                "ADDITIVE PACKAGE, WHICH IS WHY A KARLFISCHERTITRATIONRESULT SETTLES IT.",
                columns);

            foreach (string line in lines)
            {
                Assert.LessOrEqual(PixelFont.MeasureWidth(line, WideFieldScale), available,
                    $"'{line}' paints {PixelFont.MeasureWidth(line, WideFieldScale)}px into a " +
                    $"{available}px page.");
            }
        }

        // -------------------------------------------------------------------------------------------
        // Centring.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Centred on painted width, not column count. The book's two page-corner arrows are the same
        /// control facing opposite ways only because both are placed this way; offsetting one from
        /// its own edge is what put the "next" arrow into the body text (#65).
        /// </summary>
        [Test]
        public void CentreOffset_LeavesTheSameGapOnBothSides()
        {
            const int field = 100;
            const int scale = 2;
            const string text = "3/12";

            int offset = PixelText.CentreOffset(text, scale, field);
            int painted = PixelFont.MeasureWidth(text, scale);

            Assert.GreaterOrEqual(offset, 0);
            Assert.LessOrEqual(System.Math.Abs((field - offset - painted) - offset), 1,
                "The gaps either side may differ by the odd pixel of an odd remainder, no more.");
        }

        [Test]
        public void CentreOffset_OfAnArrowInItsCornerControl()
        {
            // The page-corner control is 24px wide and the arrow is drawn at scale 4.
            Assert.AreEqual(6, PixelText.CentreOffset(">", 4, 24));
            Assert.AreEqual(PixelText.CentreOffset(">", 4, 24), PixelText.CentreOffset("<", 4, 24),
                "The two corners must place their arrows identically or they stop reading as a pair.");
        }

        /// <summary>
        /// Text wider than its field overhangs evenly rather than being pinned left. An overflow that
        /// looks like an overflow gets fixed; one that looks like a long line does not.
        /// </summary>
        [Test]
        public void CentreOffset_OfTextWiderThanItsField_GoesNegative()
        {
            Assert.Less(PixelText.CentreOffset("A VERY LONG FOOTER INDEED", 2, 40), 0);
        }
    }
}
