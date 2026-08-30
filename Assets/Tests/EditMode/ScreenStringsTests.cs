using System.Collections.Generic;
using NUnit.Framework;
using Residue.Data;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The terminal, HUD and in-world screen lines (#55).
    /// <para>
    /// <c>LocalisationTests</c> already pins the mechanism — fallback, named arguments, unique
    /// ids — over every table in <c>Residue.Data</c>, including this one. What is left to guard is the
    /// shape of the lines that used to be assembled in branches, because folding one of those pairs
    /// back into a stem plus a fragment compiles, reads correctly in English, and is only discovered
    /// by someone translating into a language nobody on the team speaks.
    /// </para>
    /// </summary>
    public sealed class ScreenStringsTests
    {
        [TearDown]
        public void TearDown() => Loc.UseEnglish();

        /// <summary>
        /// Promise: a line that counts something is a whole phrase per count, so a translator can
        /// inflect the noun and move the number.
        /// <para>
        /// These three were <c>$"{n} run{(n == 1 ? "" : "s")}"</c> and friends. The English is
        /// indistinguishable either way, which is exactly the problem: the fragment handed over is
        /// the letter "s", and no amount of translating it produces a Polish plural or a Japanese
        /// counter. The test is that each count has its own key and that a translation of the many
        /// case can put the number somewhere English does not.
        /// </para>
        /// </summary>
        [Test]
        public void ACountedLine_IsAWholePhrasePerCount()
        {
            var pairs = new (LocKey One, LocKey Many)[]
            {
                (ScreenStrings.TerminalRunCountOne, ScreenStrings.TerminalRunCountMany),
                (ScreenStrings.HudOpenSamplesOne, ScreenStrings.HudOpenSamplesMany)
            };

            foreach (var (one, many) in pairs)
            {
                Assert.AreNotEqual(one.Id, many.Id,
                    "The singular and the plural have to be separate lines to be translated apart.");

                Assert.IsFalse(one.English.Contains("{"),
                    $"'{one.Id}' counts one thing, so it should spell the number out rather than " +
                    "take it as an argument — that is what lets a translator drop it entirely.");

                StringAssert.Contains("{count}", many.English,
                    $"'{many.Id}' has to carry the number as a named argument, or it cannot be moved.");
            }

            // A translation that counts after the noun still reads as a sentence.
            Loc.Use("test", new Dictionary<string, string>
            {
                [ScreenStrings.HudOpenSamplesMany.Id] = "samples open: {count}"
            });

            Assert.AreEqual("samples open: 4",
                ScreenStrings.HudOpenSamplesMany.Format(("count", 4)));
        }

        /// <summary>
        /// Promise: a line with two outcomes is two whole sentences, not one stem with an ending
        /// appended.
        /// <para>
        /// Each of these replaced a concatenation — a calibration line that had <c>"in tolerance"</c>
        /// or <c>"OUT OF TOLERANCE"</c> stuck on the end, and a refusal that had either a job number
        /// or the words <c>"this vial"</c> dropped into one slot. A translator handed the tail cannot
        /// see the clause it qualifies and cannot move it in front of the subject; one handed a bare
        /// noun phrase cannot give it the article or the case the sentence around it wants.
        /// </para>
        /// </summary>
        [Test]
        public void ABranchedLine_IsAWholeSentencePerBranch()
        {
            var branches = new (LocKey A, LocKey B)[]
            {
                (ScreenStrings.TerminalCheckInTolerance, ScreenStrings.TerminalCheckOutOfTolerance),
                (ScreenStrings.TerminalNoNoteForJob, ScreenStrings.TerminalNoNoteForVial),
                (ScreenStrings.TerminalRetestNeeds, ScreenStrings.TerminalRetestImpossible),
                (ScreenStrings.HudInspectHelp, ScreenStrings.HudInspectHelpWithHint)
            };

            foreach (var (a, b) in branches)
            {
                Assert.AreNotEqual(a.Id, b.Id);

                // Neither branch may be a prefix of the other: that is what a stem plus an appended
                // fragment looks like from here, and it is the shape this issue exists to remove.
                Assert.IsFalse(b.English.StartsWith(a.English),
                    $"'{b.Id}' begins with the whole of '{a.Id}', which means one of them is a stem " +
                    "and the difference is a fragment. Write both out in full.");
            }
        }

        /// <summary>
        /// Promise: an untranslated screen still reads, and a translated one is what is drawn.
        /// <para>
        /// The in-world instrument screens are the case worth naming. Their lines are cut to a column
        /// count at the glass rather than sized to the English here (see
        /// <c>MachineDisplay.Columns</c>), so a longer translation is clipped rather than pushing the
        /// number underneath it off the panel.
        /// </para>
        /// </summary>
        [Test]
        public void AScreenLine_FollowsTheActiveLanguage()
        {
            Assert.AreEqual("READY", ScreenStrings.ScreenReady.Text);

            Loc.Use("de", new Dictionary<string, string>
            {
                [ScreenStrings.ScreenReady.Id] = "BETRIEBSBEREIT"
            });

            Assert.AreEqual("BETRIEBSBEREIT", ScreenStrings.ScreenReady.Text);
        }
    }
}
