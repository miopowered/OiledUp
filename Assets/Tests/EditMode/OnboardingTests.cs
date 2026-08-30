using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Residue.Editor.Content;
using Residue.Gameplay.Settings;
using Residue.Gameplay.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The first-run shift brief (#47). These guard the bargain onboarding is on both sides of it:
    /// hard rule 3 says never punish something the player could not have checked, and hard rule 1
    /// says a player who understands cause must beat one who memorised a table. The brief has to name
    /// the tools that make contamination and drift checkable, and must never name a symptom.
    /// </summary>
    public sealed class OnboardingTests
    {
        [SetUp]
        public void SetUp() => GameSettings.Load();

        // -----------------------------------------------------------------------------------------
        // What the brief says
        // -----------------------------------------------------------------------------------------

        private static string BriefText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(BookContent.ShiftBriefTitle);
            foreach (var page in BookContent.ShiftBrief())
            {
                sb.AppendLine(page.Title);
                sb.AppendLine(page.Body);
            }
            sb.AppendLine(BookContent.ShiftBriefClosing);
            return sb.ToString();
        }

        /// <summary>
        /// Promise: hard rule 1. The brief teaches where to look and never what you will find, so not
        /// one measurable quantity, fault or root cause may be named in it. A line that said "high
        /// water means ingress" would hand a new player the diagnostic table the whole game is about
        /// building for themselves — and it would do it before they had run anything.
        /// <para>
        /// Checked against the real content tables rather than a hardcoded word list, so a fault
        /// added later is covered the day it is added.
        /// </para>
        /// </summary>
        [Test]
        public void TheShiftBrief_NamesNoElementFaultOrRootCause()
        {
            var content = ContentBuilder.BuildInMemory();
            try
            {
                string brief = BriefText();

                foreach (var element in content.Elements.Values)
                {
                    AssertAbsent(brief, element.DisplayName, "element");

                    // Ids are chemical symbols: "P" and "Ca" as whole words never occur in prose, so
                    // only the ones long enough to be a real collision are worth asserting on.
                    if (element.Id.Length >= 4) AssertAbsent(brief, element.Id, "element id");
                }

                foreach (var cause in content.Causes.Values)
                    AssertAbsent(brief, cause.DisplayName, "root cause");

                foreach (var fault in content.Faults.Values)
                    AssertAbsent(brief, fault.DisplayName, "fault");
            }
            finally
            {
                foreach (var o in AllDefinitions(content)) Object.DestroyImmediate(o);
            }
        }

        /// <summary>
        /// Word-boundary match, but only at an end that is actually a word character — a term like
        /// "Saponification No." ends in a full stop, and a <c>\b</c> pasted after it would produce a
        /// pattern that can never match, which is a test that passes by accident.
        /// </summary>
        private static void AssertAbsent(string brief, string term, string kind)
        {
            if (string.IsNullOrWhiteSpace(term)) return;

            string lead = char.IsLetterOrDigit(term[0]) ? @"\b" : string.Empty;
            string tail = char.IsLetterOrDigit(term[^1]) ? @"\b" : string.Empty;

            Assert.IsFalse(
                Regex.IsMatch(brief, lead + Regex.Escape(term) + tail, RegexOptions.IgnoreCase),
                $"The shift brief names the {kind} \"{term}\". Onboarding says where to look, " +
                "never what the answer is (hard rule 1).");
        }

        private static IEnumerable<Object> AllDefinitions(ContentSet content) =>
            content.Elements.Values.Cast<Object>()
                .Concat(content.Causes.Values)
                .Concat(content.Profiles.Values)
                .Concat(content.Faults.Values)
                .Concat(content.Machines.Values)
                .Concat(content.Customers.Values);

        /// <summary>
        /// Promise: hard rule 3. Contamination and calibration drift are only fair because a blank run
        /// and a certified standard reveal them. Before this brief existed, nothing in the game said
        /// either tool was there — the tell was shipped but unfindable, which is the same as absent.
        /// </summary>
        [Test]
        public void TheShiftBrief_NamesTheBlankAndTheStandard()
        {
            string brief = BriefText().ToLowerInvariant();

            Assert.IsTrue(brief.Contains("blank"),
                "The brief must name the blank run — it is the only tell for carried-over residue.");
            Assert.IsTrue(brief.Contains("standard"),
                "The brief must name the reference standard — it is the only tell for drift.");
        }

        /// <summary>
        /// Promise: the brief is a pointer at the manuals, not a replacement for them. Every reference
        /// on the rack has to be named, or a book nobody knows about is a book nobody reads — which
        /// was #47's last bullet and the whole reason this card exists.
        /// </summary>
        [Test]
        public void TheShiftBrief_PointsAtEveryReferenceOnTheRack()
        {
            string brief = BriefText();

            foreach (var kind in new[]
                     {
                         BookKind.ElementIndex, BookKind.DiagnosticGuide, BookKind.ThresholdTables
                     })
            {
                string title = BookContent.TitleFor(kind, null);
                Assert.IsTrue(brief.Contains(title),
                    $"The brief never mentions \"{title}\", so a new player has no reason to pick " +
                    "that book up.");
            }

            Assert.IsTrue(brief.ToLowerInvariant().Contains("manual"),
                "The brief must point at the per-instrument manuals on the benches.");
        }

        /// <summary>
        /// Promise: it stays short enough to read while the day clock runs. Six steps is the budget;
        /// a brief that grows into a chapter is one nobody finishes, and the shift does not pause for
        /// it.
        /// </summary>
        [Test]
        public void TheShiftBrief_IsShortAndEveryStepHasBothHalves()
        {
            var pages = BookContent.ShiftBrief();

            Assert.IsNotEmpty(pages);
            Assert.LessOrEqual(pages.Count, 8,
                "The brief has grown past what a player will read with a shift clock running.");

            foreach (var page in pages)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(page.Title));
                Assert.IsFalse(string.IsNullOrWhiteSpace(page.Body));
            }
        }

        // -----------------------------------------------------------------------------------------
        // How the flag persists
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: a profile that has never played gets the brief. The key does not exist yet on a
        /// fresh install, and "no key" has to read as "not seen" rather than as a stored true.
        /// </summary>
        [Test]
        public void AProfileWithNoStoredAnswer_IsOwedTheBrief()
        {
            WithStoredFlag(() =>
            {
                PlayerPrefs.DeleteKey(GameSettings.ShiftBriefKey);
                Assert.IsFalse(GameSettings.ReadShiftBriefSeen(),
                    "A player who has never seen the brief must be shown it.");
            });
        }

        /// <summary>
        /// Promise: putting it away sticks. Dismissal writes through to PlayerPrefs immediately rather
        /// than waiting for a Save(), so a player who never opens the settings screen still does not
        /// get the card again next session.
        /// </summary>
        [Test]
        public void PuttingTheBriefAway_SurvivesTheSession()
        {
            WithStoredFlag(() =>
            {
                GameSettings.ShiftBriefSeen = false;
                GameSettings.ShiftBriefSeen = true;

                Assert.IsTrue(GameSettings.ReadShiftBriefSeen(),
                    "Dismissal must reach PlayerPrefs without a separate Save().");
            });
        }

        /// <summary>
        /// Promise: onboarding is recoverable. Someone who resets their settings — the one thing a
        /// confused player reliably tries — is being handed a first run, and must get the brief back
        /// with it rather than being locked out of the only unprompted explanation in the game.
        /// </summary>
        [Test]
        public void ResettingSettings_GivesTheBriefBack()
        {
            // ResetToDefaults wipes the whole profile, including the profile of whoever is running
            // the suite, so this one puts every setting back rather than just the flag under test.
            bool vsync = GameSettings.VSync;
            int quality = GameSettings.QualityLevel;
            float fov = GameSettings.FieldOfView;
            float master = GameSettings.MasterVolume;
            float effects = GameSettings.EffectsVolume;
            float ambience = GameSettings.AmbienceVolume;
            float voice = GameSettings.VoiceVolume;
            float sensitivity = GameSettings.LookSensitivity;
            bool invert = GameSettings.InvertLook;
            float bob = GameSettings.HeadBobScale;
            float shake = GameSettings.CameraShakeScale;
            bool seen = GameSettings.ShiftBriefSeen;

            try
            {
                GameSettings.ShiftBriefSeen = true;

                GameSettings.ResetToDefaults();

                Assert.IsFalse(GameSettings.ShiftBriefSeen);
                Assert.IsFalse(GameSettings.ReadShiftBriefSeen(),
                    "ResetToDefaults must clear the stored key, not just the in-memory copy.");
            }
            finally
            {
                GameSettings.VSync = vsync;
                GameSettings.QualityLevel = quality;
                GameSettings.FieldOfView = fov;
                GameSettings.MasterVolume = master;
                GameSettings.EffectsVolume = effects;
                GameSettings.AmbienceVolume = ambience;
                GameSettings.VoiceVolume = voice;
                GameSettings.LookSensitivity = sensitivity;
                GameSettings.InvertLook = invert;
                GameSettings.HeadBobScale = bob;
                GameSettings.CameraShakeScale = shake;
                GameSettings.ShiftBriefSeen = seen;
                GameSettings.Save();
            }
        }

        /// <summary>
        /// Snapshot and restore rather than delete, so a run of the suite does not cost the person at
        /// the keyboard their own profile — the house pattern from <c>MotionComfortTests</c>.
        /// </summary>
        private static void WithStoredFlag(System.Action body)
        {
            string key = GameSettings.ShiftBriefKey;
            bool had = PlayerPrefs.HasKey(key);
            int saved = PlayerPrefs.GetInt(key, 0);
            bool savedInMemory = GameSettings.ShiftBriefSeen;

            try
            {
                body();
            }
            finally
            {
                GameSettings.ShiftBriefSeen = savedInMemory;
                if (had) PlayerPrefs.SetInt(key, saved);
                else PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
