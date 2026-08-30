using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Residue.Data;
using Residue.Gameplay.World;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The book face (#55). Every failure here is one a screenshot would not show you.
    ///
    /// <para>
    /// <b>Why a bitmap font needs a test at all.</b> Three of its four failure modes are silent.
    /// A <see cref="BookGlyph.Rows"/> string one character short is an
    /// <c>IndexOutOfRangeException</c> at draw time and compiles cleanly. A character with no entry
    /// falls back to <see cref="BookFont.Missing"/>, which is a blank of average width — so a
    /// missing glyph draws as a hole in the middle of a sentence and looks, at a glance, like
    /// spacing. A width wider than <see cref="BookFont.CellWidth"/> silently overlaps the next
    /// letter. Only the fourth — a letter drawn badly — is something an eye catches, and that one is
    /// not testable.
    /// </para>
    /// </summary>
    public sealed class BookFontTests
    {
        /// <summary>
        /// Promise: every glyph fills its cell exactly, so drawing one cannot go out of bounds.
        /// <para>
        /// <see cref="BookFont.IsOn"/> indexes <c>y * CellWidth + x</c> with no length check, which
        /// is right — the check belongs here, once, rather than in the inner loop of a page render.
        /// </para>
        /// </summary>
        [Test]
        public void EveryGlyph_FillsItsCellExactly()
        {
            const int Cells = BookFont.CellWidth * BookFont.Height;
            var wrong = new List<string>();

            foreach (char c in Face())
            {
                var glyph = BookFont.Glyph(c);

                if (glyph.Rows == null || glyph.Rows.Length != Cells)
                {
                    wrong.Add($"'{c}' has {glyph.Rows?.Length ?? 0} cells, not {Cells}");
                    continue;
                }

                foreach (char cell in glyph.Rows)
                {
                    if (cell == '0' || cell == '1') continue;
                    wrong.Add($"'{c}' contains '{cell}', which is neither ink nor paper");
                    break;
                }
            }

            Assert.IsEmpty(wrong,
                "A row string of the wrong length throws when the page is drawn, not when it is " +
                "compiled:\n  " + string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Promise: no glyph is wider than the cell it was written in.
        /// <para>
        /// Columns past <see cref="BookGlyph.Width"/> are ignored, so a width that overruns
        /// <see cref="BookFont.CellWidth"/> does not read past the string — it just advances the pen
        /// further than the ink goes, and the letter after it lands in the gap.
        /// </para>
        /// </summary>
        [Test]
        public void EveryWidth_FitsTheCell()
        {
            var wrong = new List<string>();

            foreach (char c in Face())
            {
                int width = BookFont.Glyph(c).Width;

                if (width < 1 || width > BookFont.CellWidth)
                    wrong.Add($"'{c}' is {width} wide (must be 1..{BookFont.CellWidth})");
            }

            Assert.IsEmpty(wrong, string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Promise: every character the reference books can print has something to print it with.
        /// <para>
        /// Taken from the shipped text rather than from a list somebody remembered to update: the
        /// <c>book.</c> block of <see cref="LabStrings"/>, the same block in German, and the
        /// punctuation <see cref="BookContent"/> assembles pages out of itself. A translator who
        /// reaches for a character the face lacks finds out here, not from a player photographing a
        /// gap in a sentence.
        /// </para>
        /// </summary>
        [Test]
        public void EveryCharacterTheBooksPrint_HasAGlyph()
        {
            var missing = new SortedSet<char>();

            foreach (string line in BookText())
            {
                foreach (char c in line)
                {
                    // Page bodies are built with AppendLine, so the breaks are text, not glyphs.
                    if (c == '\n' || c == '\r' || c == '\t') continue;

                    if (!BookFont.Has(c)) missing.Add(c);
                }
            }

            var named = new List<string>();
            foreach (char c in missing) named.Add($"'{c}' (U+{(int)c:X4})");

            Assert.IsEmpty(named,
                "BookFont.Glyph falls back to a blank of average width, so each of these draws as " +
                "a hole in the middle of a printed sentence:\n  " + string.Join("\n  ", named));
        }

        /// <summary>
        /// Promise: the descenders actually descend.
        /// <para>
        /// This is the reason the cell is nine rows rather than seven. If g, j, p, q and y were
        /// squashed onto the baseline the face would still draw, still measure, and still read as a
        /// dot-matrix label rather than as print — a defect no other assertion here can see.
        /// </para>
        /// </summary>
        [Test]
        public void TheDescenders_HangBelowTheLine()
        {
            foreach (char c in "gjpqy")
            {
                Assert.IsTrue(InksBelowTheBaseline(c),
                    $"'{c}' is a descender with no ink in rows " +
                    $"{BookFont.Baseline}..{BookFont.Height - 1}:\n{Picture(c)}");
            }

            // And the other half: a letter that sits on the line must not dip below it, or the
            // whole distinction is noise.
            foreach (char c in "aeimnorsuvwxzABCEFHIKLMNOPRSTUVWXZ0123456789")
            {
                Assert.IsFalse(InksBelowTheBaseline(c),
                    $"'{c}' sits on the baseline but inks the descender rows:\n{Picture(c)}");
            }
        }

        /// <summary>
        /// Promise: <see cref="BookFont.Measure"/> is the sum of what will actually be drawn.
        /// <para>
        /// The font is proportional, so measuring is the only way to know how wide a line is — and a
        /// measure that disagreed with the draw would wrap text to a width it does not occupy, which
        /// shows up as a last word clipped off the edge of a page rather than as an exception.
        /// Includes an unknown character on purpose: it measures as
        /// <see cref="BookFont.Missing"/>, and the two have to agree about that too.
        /// </para>
        /// </summary>
        [Test]
        public void Measure_IsTheSumOfTheGlyphWidths()
        {
            foreach (string text in new[]
                     {
                         "Elements & Sources",
                         "Cost per run  £120",
                         "Viscosity 46.0 +/-8%",
                         "Fläschchen — Messgerät (§4.5)",
                         "i", "mm", "☃ unknown"
                     })
            {
                int expected = 0;
                foreach (char c in text) expected += BookFont.Glyph(c).Width + BookFont.Tracking;
                expected -= BookFont.Tracking;

                Assert.AreEqual(expected, BookFont.Measure(text, 1), $"'{text}' at scale 1");
                Assert.AreEqual(expected * 3, BookFont.Measure(text, 3), $"'{text}' at scale 3");
            }

            Assert.AreEqual(0, BookFont.Measure("", 1));
            Assert.AreEqual(0, BookFont.Measure(null, 2));
        }

        // -- Helpers ---------------------------------------------------------------------------

        /// <summary>Every character the face actually declares.</summary>
        private static IEnumerable<char> Face()
        {
            for (int i = 0; i <= char.MaxValue; i++)
            {
                char c = (char)i;
                if (BookFont.Has(c)) yield return c;
            }
        }

        private static bool InksBelowTheBaseline(char c)
        {
            var glyph = BookFont.Glyph(c);

            for (int y = BookFont.Baseline; y < BookFont.Height; y++)
                for (int x = 0; x < glyph.Width; x++)
                    if (BookFont.IsOn(glyph, x, y)) return true;

            return false;
        }

        /// <summary>
        /// Everything a reference page can contain that is not content-table data: the
        /// <c>book.</c> lines in both languages, and the connective punctuation
        /// <see cref="BookContent"/> writes itself.
        /// </summary>
        private static IEnumerable<string> BookText()
        {
            var german = new Dictionary<string, string>();
            GermanLab.AddTo(german);

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static;

            foreach (var field in typeof(LabStrings).GetFields(Flags))
            {
                if (field.FieldType != typeof(LocKey)) continue;

                var key = (LocKey)field.GetValue(null);
                if (key.Id == null || !key.Id.StartsWith("book.", StringComparison.Ordinal)) continue;

                yield return key.English;
                if (german.TryGetValue(key.Id, out string translated)) yield return translated;
            }

            // The separators, brackets and units BookContent and the threshold tables compose with,
            // plus the repertoire the face promises regardless of what today's copy happens to use.
            yield return "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            yield return "abcdefghijklmnopqrstuvwxyz";
            yield return "0123456789";
            yield return " .,:;'\"!?-–—()/%+=<>°£·≤≥&";
            yield return "äöüßÄÖÜ§[]{}*_#@\\";
        }

        /// <summary>A picture of a glyph, for reading a failure rather than decoding one.</summary>
        private static string Picture(char c)
        {
            var glyph = BookFont.Glyph(c);
            var sb = new StringBuilder();

            for (int y = 0; y < BookFont.Height; y++)
            {
                for (int x = 0; x < glyph.Width; x++) sb.Append(BookFont.IsOn(glyph, x, y) ? '#' : '.');
                sb.Append('\n');
            }

            return sb.ToString();
        }
    }
}
