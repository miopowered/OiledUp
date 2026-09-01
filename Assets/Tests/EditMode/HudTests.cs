using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Residue.Data;
using Residue.Gameplay.UI;
using Residue.Gameplay.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The HUD, as far as it can be checked without a panel.
    ///
    /// <para>
    /// <see cref="LabHud"/> itself cannot be built here — a <c>UIDocument</c> owns no
    /// <c>rootVisualElement</c> until it is enabled with panel settings, and the test runner has none
    /// to give it (the same limitation <c>PlayerScreenTests</c> records). <see cref="HudHeader"/> can:
    /// it is a plain class owning a subtree, so the bar the player reads first is constructible,
    /// refreshable and walkable in an EditMode test.
    /// </para>
    ///
    /// <para>
    /// Two kinds of check live here, and they are complements. The first walks the built tree and
    /// asserts what is actually drawn. The second reads the HUD's source, because the tree only proves
    /// things about the states a fake lab can be put into — a verdict colour added on a branch nothing
    /// here reaches would pass the first and fail the second.
    /// </para>
    /// </summary>
    public sealed class HudTests
    {
        [TearDown]
        public void TearDown() => Loc.UseEnglish();

        /// <summary>A lab with whatever readings a test wants. Nothing here simulates anything.</summary>
        private sealed class FakeLab : ILabView
        {
            public int Day { get; set; } = 1;
            public float DaySecondsRemaining { get; set; } = 600f;
            public bool DayInProgress { get; set; } = true;
            public bool ShiftOver { get; set; }
            public bool IsRunOver { get; set; }
            public float Money { get; set; } = 1240f;
            public float Reputation { get; set; } = 72f;
            public float SolventUnits { get; set; } = 8f;
            public int ReferenceStandards { get; set; } = 3;
            public float CalibrationCost { get; set; } = 140f;
            public int OpenSampleCount { get; set; } = 4;

            public IMachineView Machine(string instanceId) => null;
        }

        private static Label Find(HudHeader header, string name)
        {
            var label = header.Root.Q<Label>(name);
            Assert.IsNotNull(label, $"The header no longer has an element named '{name}'.");
            return label;
        }

        // -- Hard rule 4 ---------------------------------------------------------------------------
        //
        // Red, amber and green mean verdict state and nothing else, and a HUD carrying money and
        // reputation is the single most likely place in this project for that to be spent on chrome.
        // These are the checks that stop it.

        /// <summary>
        /// Promise: <b>the header never draws a signal colour, in any state.</b>
        /// <para>
        /// Walked over the whole subtree — text, backgrounds and all four borders — in the states a
        /// player actually reaches: a healthy shift, an overdrawn account, an empty workload and the
        /// end of the day. Each of those is a place someone would reach for red or green, and each of
        /// them has to say what it means some other way.
        /// </para>
        /// </summary>
        [Test]
        public void TheHeader_NeverDrawsAVerdictColour()
        {
            var header = new HudHeader();

            var states = new[]
            {
                new FakeLab(),
                new FakeLab { Money = -820f, Reputation = 4f, OpenSampleCount = 0 },
                new FakeLab { ShiftOver = true, DaySecondsRemaining = 0f },
                new FakeLab { Day = 2, ShiftOver = true, Money = -1f, OpenSampleCount = 11 }
            };

            foreach (var lab in states)
            {
                header.Refresh(lab);

                var drawn = ExplicitColoursIn(header.Root).ToList();

                // A walk that inspects nothing passes for the wrong reason, and would keep passing
                // for ever after someone put red on the balance. Assert it is actually looking at
                // colours before asserting which ones they are.
                Assert.Greater(drawn.Count, 8,
                    "The colour walk found almost nothing set on the header. Either the tree is not " +
                    "built or inline styles are no longer readable this way, and this whole check " +
                    "has quietly stopped working.");

                var offenders = drawn
                    .Where(found => IsSignal(found.Colour, out _))
                    .Select(found => $"{found.Where}: {found.What} is SignalPalette." +
                                     $"{(IsSignal(found.Colour, out string which) ? which : "?")}")
                    .ToList();

                Assert.IsEmpty(offenders,
                    "The HUD header is drawing a verdict colour. Row 4 is reserved (hard rule 4): if " +
                    "red only ever means CRITICAL, a player reads a results table before they read " +
                    "it, and spending it on a balance or a clock is what takes that away. Say it " +
                    "with a word, a sign, a weight or a position instead:\n  " +
                    string.Join("\n  ", offenders));
            }
        }

        /// <summary>
        /// Promise: <b>money says "this is bad" without changing hue at all.</b>
        /// <para>
        /// Not merely "not red" — the balance cell is the one place the temptation is strongest, so it
        /// is pinned to exactly zero colour change and three non-hue channels instead: the caption
        /// becomes a different word, the figure gains a minus sign, and the figure goes bold. That is
        /// §2.2's redundant-encoding rule applied where there is no severity to encode.
        /// </para>
        /// </summary>
        [Test]
        public void AnOverdrawnBalance_ChangesItsWordAndItsWeight_NeverItsColour()
        {
            var header = new HudHeader();

            header.Refresh(new FakeLab { Money = 1240f });
            var balance = Find(header, HudHeader.BalanceName);
            var caption = Find(header, HudHeader.BalanceCaptionName);

            var healthyColour = balance.style.color.value;
            string healthyCaption = caption.text;
            string healthyValue = balance.text;
            var healthyWeight = balance.style.unityFontStyleAndWeight.value;

            header.Refresh(new FakeLab { Money = -820f });

            Assert.AreEqual(healthyColour, balance.style.color.value,
                "The balance changed colour when it went negative. Whatever colour that is, it is a " +
                "hue-only channel on a value with no severity — and one keystroke away from being red.");

            Assert.AreNotEqual(healthyCaption, caption.text,
                "An overdrawn balance has to change the word naming it, or the only thing telling a " +
                "player they are in trouble is a minus sign they were not looking at.");

            Assert.AreNotEqual(healthyValue, balance.text);
            StringAssert.Contains("−", balance.text,
                "The figure has to carry an explicit sign — the same U+2212 the terminal's report uses.");

            Assert.AreNotEqual(healthyWeight, balance.style.unityFontStyleAndWeight.value,
                "Weight is the third channel. Two are the minimum §2.2 asks for; this cell has no " +
                "colour channel at all, so it needs all three it has.");
        }

        /// <summary>
        /// Promise: the end of the shift is legible without hue, and is not amber.
        /// <para>
        /// The old readout tinted the whole status block <c>SignalPalette.Caution</c> when the clock
        /// ran out. §2.2 arguably licenses that as "alarm state", but hard rule 4 does not, and a HUD
        /// that spends amber on a clock is a HUD where amber has stopped meaning CAUTION. It is now a
        /// word in a band, at a weight, in the warm-family orange the menus already use.
        /// </para>
        /// </summary>
        [Test]
        public void TheEndOfTheShift_IsAWordAndNotASignalColour()
        {
            var header = new HudHeader();

            header.Refresh(new FakeLab());
            var alert = header.Root.Q(HudHeader.AlertName);
            Assert.IsNotNull(alert);
            Assert.AreEqual(DisplayStyle.None, alert.style.display.value,
                "The shift-over band must not be up during an ordinary shift.");

            header.Refresh(new FakeLab { ShiftOver = true, DaySecondsRemaining = 0f });

            Assert.AreEqual(DisplayStyle.Flex, alert.style.display.value,
                "Nothing on screen said the shift had ended.");

            var words = alert.Query<Label>().ToList();
            Assert.IsNotEmpty(words);
            Assert.IsNotEmpty(words[0].text,
                "The band is the word channel. An empty one is a coloured bar and nothing else.");

            Assert.AreEqual("00:00", Find(header, HudHeader.ClockName).text,
                "The clock is the third channel and has to agree with the band.");
        }

        /// <summary>
        /// Promise: no verdict colour is named anywhere in the HUD's source.
        /// <para>
        /// The tree walk above can only see states a fake lab can produce. This sees every branch,
        /// including one added next year on a path no test reaches. It reads source text rather than
        /// IL for the reason <c>LocalisationEnforcementTests</c> gives — and it skips comments, since
        /// half the files below explain at length why they do not do this.
        /// </para>
        /// </summary>
        [Test]
        public void NoHudFile_ReachesForTheSignalPalette()
        {
            var offenders = new List<string>();

            foreach (string file in HudSources())
            {
                string[] lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;
                    if (!VerdictColour.IsMatch(trimmed)) continue;

                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {trimmed}");
                }
            }

            Assert.IsEmpty(offenders,
                "These spend a verdict colour on HUD chrome (hard rule 4). The HUD has no severity to " +
                "show — a balance, a clock, a selected slot and a ticked objective are all states, not " +
                "readings — so use HudStyle.Accent, HudStyle.Warn, a word, a sign or a weight:\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// The three reserved colours and the two lookups that return them. <c>SignalPalette.Ink</c>,
        /// <c>Dim</c>, <c>Off</c>, <c>Panel</c> and <c>Accent</c> are neutral chrome and are deliberately
        /// not matched — <see cref="HudStyle"/> aliases them precisely so the HUD and the menus cannot
        /// drift on what grey means.
        /// </summary>
        private static readonly Regex VerdictColour = new(
            @"SignalPalette\.(Critical|Caution|Normal|For)\b", RegexOptions.Compiled);

        /// <summary>
        /// The files that draw the overlay. Listed rather than globbed, and asserted to exist, because
        /// a scan that silently finds nothing passes for the wrong reason.
        /// </summary>
        private static IEnumerable<string> HudSources()
        {
            string[] names =
            {
                "LabHud.cs", "HudHeader.cs", "HudStyle.cs", "TutorialCard.cs", "TutorialCompass.cs"
            };

            string folder = Path.Combine(Application.dataPath, "Scripts", "Gameplay", "World");

            foreach (string name in names)
            {
                string path = Path.Combine(folder, name);
                Assert.IsTrue(File.Exists(path),
                    $"'{name}' is not where this check expects it. A renamed or moved HUD file that " +
                    "nobody adds back here is a file nothing is guarding.");
                yield return path;
            }
        }

        /// <summary>
        /// Every colour the subtree explicitly sets, and where. Only inline styles carrying a real
        /// value are collected: an unset property reports a <see cref="StyleKeyword"/> and a default
        /// <c>Color</c> that would otherwise be compared as if somebody had chosen it.
        /// </summary>
        private static IEnumerable<(string Where, string What, Color Colour)> ExplicitColoursIn(
            VisualElement element)
        {
            string label = string.IsNullOrEmpty(element.name)
                ? element.GetType().Name
                : element.name;

            if (element is Label text && !string.IsNullOrEmpty(text.text))
                label += $" (\"{text.text}\")";

            var properties = new (string What, StyleColor Style)[]
            {
                ("colour", element.style.color),
                ("background", element.style.backgroundColor),
                ("top border", element.style.borderTopColor),
                ("bottom border", element.style.borderBottomColor),
                ("left border", element.style.borderLeftColor),
                ("right border", element.style.borderRightColor)
            };

            foreach (var (what, style) in properties)
            {
                if (style.keyword != StyleKeyword.Undefined) continue;

                yield return (label, what, style.value);
            }

            foreach (var child in element.Children())
                foreach (var found in ExplicitColoursIn(child))
                    yield return found;
        }

        private static bool IsSignal(Color colour, out string which)
        {
            if (Same(colour, SignalPalette.Critical)) { which = "Critical"; return true; }
            if (Same(colour, SignalPalette.Caution)) { which = "Caution"; return true; }
            if (Same(colour, SignalPalette.Normal)) { which = "Normal"; return true; }

            which = null;
            return false;
        }

        /// <summary>Compared on RGB only: an alpha-faded verdict colour is still a verdict colour.</summary>
        private static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.002f &&
            Mathf.Abs(a.g - b.g) < 0.002f &&
            Mathf.Abs(a.b - b.b) < 0.002f;

        // -- Hierarchy -----------------------------------------------------------------------------

        /// <summary>
        /// Promise: the header's type sizes rank the way the design says they do.
        /// <para>
        /// The order is not decoration. Cells are ranked by how often the number changes what the
        /// player does next: the clock moves every second and is the pressure the game runs on (§6.1),
        /// the workload is what the clock is being spent on, the ledger moves a few times a day, and
        /// the drum rarely moves at all. A later change that quietly makes the drum as loud as the
        /// clock is exactly the regression this pins.
        /// </para>
        /// </summary>
        [Test]
        public void TheHeader_RanksItsCellsByHowOftenTheyMatter()
        {
            var header = new HudHeader();
            header.Refresh(new FakeLab());

            float clock = Find(header, HudHeader.ClockName).style.fontSize.value.value;
            float open = Find(header, HudHeader.OpenName).style.fontSize.value.value;
            float balance = Find(header, HudHeader.BalanceName).style.fontSize.value.value;
            float drum = Find(header, HudHeader.DrumName).style.fontSize.value.value;

            Assert.Greater(clock, open, "The clock is the one hero-sized thing on the screen.");
            Assert.GreaterOrEqual(open, balance);
            Assert.Greater(balance, drum,
                "The drum level changes on a flush and is checked deliberately. It must not be as " +
                "loud as a number that moves every time a verdict is filed.");

            Assert.AreEqual(FontStyle.Bold,
                Find(header, HudHeader.OpenName).style.unityFontStyleAndWeight.value,
                "Open samples and the ledger share a size, so weight is what separates them.");
        }

        /// <summary>
        /// Promise: a HUD with no lab draws nothing rather than a bar of zeroes.
        /// <para>
        /// Null is a real answer — there is a window during scene load where a client has a HUD and no
        /// replicated view yet — and a header showing DAY 0 and £0 during it would be the interface
        /// telling the player something false.
        /// </para>
        /// </summary>
        [Test]
        public void TheHeader_WithNoLab_DrawsNothing()
        {
            var header = new HudHeader();

            Assert.DoesNotThrow(() => header.Refresh(null));
            Assert.AreEqual(DisplayStyle.None, header.Root.style.display.value);
        }

        // -- Localisation (#55) --------------------------------------------------------------------

        /// <summary>
        /// Promise: every string the header draws goes through the table.
        /// <para>
        /// <c>LocalisationEnforcementTests</c> catches a literal sitting in a draw call; this catches
        /// the other shape — a line assembled correctly from a <see cref="LocKey"/> at build time and
        /// then never resolved again, or one quietly rebuilt from a field. The test installs a
        /// translation for every new header key and insists the drawn text follows it.
        /// </para>
        /// <para>
        /// The captions are deliberately reordered and lengthened here: the header lays its cells out
        /// against a flexible spacer precisely because German runs roughly 30% longer, and a caption
        /// that only fits its English is a caption sized to the wrong language.
        /// </para>
        /// </summary>
        [Test]
        public void EveryHeaderLine_FollowsTheActiveLanguage()
        {
            var lab = new FakeLab { Day = 2, Money = 1240f, OpenSampleCount = 4 };

            var header = new HudHeader();
            header.Refresh(lab);

            Assert.AreEqual("DAY 2 · TIME LEFT", Find(header, HudHeader.ShiftCaptionName).text);

            Loc.Use("test", new Dictionary<string, string>
            {
                [ScreenStrings.HudShiftCaption.Id] = "TAG {day} · VERBLEIBENDE SCHICHTZEIT",
                [ScreenStrings.HudBalanceCaption.Id] = "KONTOSTAND",
                [ScreenStrings.HudBalanceOverdrawnCaption.Id] = "KONTO ÜBERZOGEN",
                [ScreenStrings.HudBalanceValue.Id] = "{sign}{amount} £",
                [ScreenStrings.HudOpenSamplesMany.Id] = "offene Proben: {count}"
            });

            // The same readings, deliberately: the header caches what it last drew and skips a cell
            // whose number has not moved, so this is also the check that a language change is not one
            // of the things that cache silently swallows.
            header.Refresh(lab);

            Assert.AreEqual("TAG 2 · VERBLEIBENDE SCHICHTZEIT",
                Find(header, HudHeader.ShiftCaptionName).text,
                "Switching language left the header in the old one. Its cells are cached on the value " +
                "they drew, and the language is not a value.");

            Assert.AreEqual("KONTOSTAND", Find(header, HudHeader.BalanceCaptionName).text);

            Assert.AreEqual("offene Proben: 4", Find(header, HudHeader.OpenName).text,
                "The count has to be movable — a translation that puts the number last still has to " +
                "read as a sentence.");

            // The currency symbol moves with the language, which is the whole reason it is inside the
            // template rather than concatenated at the call site.
            StringAssert.EndsWith("£", Find(header, HudHeader.BalanceName).text);

            header.Refresh(new FakeLab { Day = 2, Money = -820f });
            Assert.AreEqual("KONTO ÜBERZOGEN", Find(header, HudHeader.BalanceCaptionName).text);
        }

        /// <summary>
        /// Promise: every line the HUD gained has a German entry with the same placeholders.
        /// <para>
        /// <c>GermanTranslationTests</c> asserts this over every key in the project, which is the right
        /// place for it. This one is here so a failure names the HUD rather than arriving as one line
        /// in a list of five hundred — and because the header's captions have a constraint the general
        /// test cannot know about: they sit in a fixed-width row and a caption that wrapped would push
        /// its own number out of the bar.
        /// </para>
        /// </summary>
        [Test]
        public void EveryNewHudLine_IsTranslated()
        {
            var keys = new[]
            {
                ScreenStrings.HudShiftCaption, ScreenStrings.HudBalanceCaption,
                ScreenStrings.HudBalanceOverdrawnCaption, ScreenStrings.HudBalanceValue,
                ScreenStrings.HudReputationCaption, ScreenStrings.HudDrumCaption,
                ScreenStrings.HudStandardsCaption, ScreenStrings.HudSlotEmpty,
                ScreenStrings.HudControlsEssential, ScreenStrings.HudControlsHeading
            };

            foreach (var key in keys)
            {
                Assert.IsTrue(German.Table.ContainsKey(key.Id),
                    $"'{key.Id}' still reads English with German selected.");

                CollectionAssert.AreEquivalent(
                    Placeholders(key.English), Placeholders(German.Table[key.Id]),
                    $"'{key.Id}' does not carry the same arguments in both languages, which draws a " +
                    "literal brace on screen or silently loses the value.");
            }
        }

        private static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);

        private static string[] Placeholders(string text) =>
            Placeholder.Matches(text ?? string.Empty)
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .OrderBy(name => name)
                .ToArray();

        // -- Layout invariants ---------------------------------------------------------------------

        /// <summary>
        /// Promise: nothing anchored to the top of the screen can land on the header.
        /// <para>
        /// Both corner cards and the debug readout hang off <see cref="HudStyle.ContentTop"/>, and the
        /// header can grow by its alert band at the end of a shift. The constant has to clear both, or
        /// the one line telling a player the day is over ends up under the standing-orders card.
        /// </para>
        /// </summary>
        [Test]
        public void ContentBelowTheHeader_ClearsItEvenWhenItGrows()
        {
            Assert.Greater(HudStyle.ContentTop,
                HudStyle.HeaderHeight + HudStyle.HeaderAlertHeight,
                "ContentTop no longer clears the header plus its alert band.");
        }

        /// <summary>
        /// Promise: every offset the HUD uses is on the spacing scale.
        /// <para>
        /// A scale nobody checks becomes a suggestion, and the failure it prevents is not a crash — it
        /// is an overlay that has eleven different gaps in it and reads as five widgets that each
        /// chose a corner, which is what this work replaced.
        /// </para>
        /// </summary>
        [Test]
        public void EverySpacingConstant_IsAMultipleOfFour()
        {
            foreach (float step in new[]
                     {
                         HudStyle.S1, HudStyle.S2, HudStyle.S3, HudStyle.S4, HudStyle.S6, HudStyle.S8,
                         HudStyle.Inset, HudStyle.HeaderHeight, HudStyle.HeaderAlertHeight,
                         HudStyle.ContentTop
                     })
            {
                Assert.AreEqual(0f, step % 4f, $"{step} is not on the four-pixel grid.");
            }
        }

        /// <summary>
        /// Promise: the type scale stays a scale — five steps, strictly increasing.
        /// <para>
        /// Adding a sixth is how two widgets end up picking different sizes for the same job, which is
        /// the argument <c>UiKit</c> makes about its own scale and the reason this one is not simply
        /// that one: a HUD is read in peripheral vision over live geometry and needs a bigger, more
        /// separated set than a modal card does.
        /// </para>
        /// </summary>
        [Test]
        public void TheTypeScale_IsStrictlyIncreasing_AndBiggerThanTheMenuKit()
        {
            Assert.Less(HudStyle.CaptionSize, HudStyle.BodySize);
            Assert.Less(HudStyle.BodySize, HudStyle.MetricSize);
            Assert.Less(HudStyle.MetricSize, HudStyle.HeadingSize);
            Assert.Less(HudStyle.HeadingSize, HudStyle.HeroSize);

            Assert.Greater(HudStyle.BodySize, UiKit.BodySize,
                "HUD body text is read while walking, over a low-contrast room, at whatever distance " +
                "the player is sitting. It is deliberately a step above the menu kit's.");
        }
    }
}
