using System.Collections.Generic;
using NUnit.Framework;
using Residue.Gameplay.World;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The in-world screens can spell German (#55).
    /// <para>
    /// <see cref="PixelFont"/> holds 3x5 glyphs for <c>0-9 A-Z</c> and a few symbols, and anything
    /// else falls back to a space. Before <see cref="PixelFont.Transliterate"/> that meant German drew
    /// "MESSGERÄT" as <c>MESSGER T</c> — a hole in the middle of a word, on the one surface a player
    /// stands in front of to read a number. It is exactly the class of bug nobody notices in English.
    /// </para>
    /// </summary>
    public sealed class PixelFontGermanTests
    {
        /// <summary>
        /// Promise: every German letter reaches the glass as something, and never as a gap.
        /// <para>
        /// Asserted through <see cref="PixelFont.Glyph"/> rather than by eye, because "it renders" and
        /// "it renders a blank" look identical in a screenshot of a dark panel.
        /// </para>
        /// </summary>
        [Test]
        public void EveryGermanLetter_HasSomethingToDrawWith()
        {
            const string german = "ÄÖÜäöüß";
            string drawn = PixelFont.Transliterate(german);

            Assert.AreEqual("AEOEUEAEOEUESS", drawn);

            string blank = PixelFont.Glyph(' ');
            foreach (char c in drawn)
            {
                Assert.AreNotEqual(blank, PixelFont.Glyph(c),
                    $"'{c}' still falls back to the space glyph, so it draws as a hole in the word.");
            }
        }

        /// <summary>
        /// Promise: a word that grows is measured after it grows.
        /// <para>
        /// This is the half that would have been easy to get wrong. Transliterating only at draw time
        /// leaves every width — wrapping, truncation, centring — computed on the shorter original, so
        /// a German caption overruns the column it was cut to fit and pushes the value beside it off
        /// the panel.
        /// </para>
        /// </summary>
        [Test]
        public void AWordThatGrows_IsMeasuredAtItsDrawnWidth()
        {
            Assert.AreEqual(PixelFont.MeasureWidth("MESSGERAET", 1), PixelFont.MeasureWidth("MESSGERÄT", 1),
                "An umlaut costs a character on this font, so the two have to measure the same.");

            Assert.Greater(PixelFont.MeasureWidth("MESSGERÄT", 1), PixelFont.MeasureWidth("MESSGERT", 1));
        }

        /// <summary>Promise: truncation cuts to the column count in drawn characters, not source ones.</summary>
        [Test]
        public void Truncation_CountsWhatWillBeDrawn()
        {
            // "ÄÖÜ" is six drawn characters, so eight columns keeps six of them and no more.
            Assert.AreEqual("AEOEUE", PixelText.Truncate("ÄÖÜ", 8));
            Assert.AreEqual("AEOE", PixelText.Truncate("ÄÖÜ", 4));
        }

        /// <summary>
        /// Promise: wrapping breaks on the drawn width too.
        /// <para>
        /// A line allowed to run one column long is the difference between a readout that fits its
        /// glass and one whose last character is silently clipped.
        /// </para>
        /// </summary>
        [Test]
        public void Wrapping_BreaksOnTheDrawnWidth()
        {
            var lines = new List<string>();
            PixelText.Wrap("GRÖSSE PRÜFEN", 8, lines);

            foreach (string line in lines)
                Assert.LessOrEqual(line.Length, 8, $"'{line}' is wider than the glass it was wrapped for.");

            Assert.IsNotEmpty(lines);
        }

        /// <summary>
        /// Promise: English is untouched, and costs nothing.
        /// <para>
        /// The same instance comes back rather than a copy. An instrument screen redraws its whole
        /// readout on a timer, so a transliteration pass that allocated a new string per line per
        /// redraw would be pure garbage for the language that never needs it.
        /// </para>
        /// </summary>
        [Test]
        public void EnglishIsUnchanged_AndNotEvenCopied()
        {
            const string english = "NO READING";

            Assert.AreSame(english, PixelFont.Transliterate(english));
        }

        /// <summary>Promise: running it twice changes nothing the second time.</summary>
        [Test]
        public void Transliteration_IsIdempotent()
        {
            string once = PixelFont.Transliterate("PRÜFEN");

            Assert.AreEqual(once, PixelFont.Transliterate(once));
        }
    }
}
