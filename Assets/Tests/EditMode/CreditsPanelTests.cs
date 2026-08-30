using System.IO;
using NUnit.Framework;
using Residue.Data;
using Residue.Net.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The credits screen (#53) exists because some of the project's art licences require
    /// attribution in the shipped product, not only in the repository. The one thing that actually
    /// matters is that a build shows the same text <c>Assets/Art/Imported/CREDITS.md</c> says — a
    /// hand-transcribed copy in <see cref="CreditsPanel"/> could drift from it silently and nobody
    /// would notice until it mattered.
    /// </summary>
    public sealed class CreditsPanelTests
    {
        /// <summary>
        /// <c>CreditsContent.Generated.cs</c> is produced by <c>Residue/Content/Rebuild Credits</c>
        /// from CREDITS.md (see <c>Residue.Editor.Content.CreditsBuilder</c>). If someone edits the
        /// licence file and forgets to rebuild, the game keeps shipping the old text — this is the
        /// test that catches exactly that before it reaches main.
        /// </summary>
        [Test]
        public void ThirdPartyArtCredits_MatchTheSourceFile()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string path = Path.Combine(projectRoot, "Assets", "Art", "Imported", "CREDITS.md");

            Assert.IsTrue(File.Exists(path),
                "Assets/Art/Imported/CREDITS.md is missing. It is the source of truth for art " +
                "licences, and CreditsContent.ThirdPartyArt is generated from it.");

            string expected = File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd();

            Assert.AreEqual(expected, CreditsContent.ThirdPartyArt,
                "CreditsContent.ThirdPartyArt has drifted from CREDITS.md — run " +
                "Residue/Content/Rebuild Credits and commit the regenerated file.");
        }

        /// <summary>
        /// Not empty by accident: a generator that silently found zero package notices is more
        /// likely broken than a project with no third-party code shipping inside it — this project
        /// pulls in Netcode, URP and several Unity services, all of which carry a notice file.
        /// </summary>
        [Test]
        public void PackageNotices_AreNotEmpty()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(CreditsContent.PackageNotices));
        }

        /// <summary>
        /// <see cref="CreditsPanel"/> is meant to split <c>PackageNotices</c> back into one label per
        /// package using the exact separator <c>CreditsBuilder</c> joined them with (see the comment
        /// on <see cref="CreditsContent.PackageNoticeSeparator"/>). If the two ever disagree, the
        /// split silently stops working and every notice collapses into one unreadable label.
        /// </summary>
        [Test]
        public void PackageNotices_SplitOnTheGeneratedSeparator_YieldsMoreThanOneEntry()
        {
            string[] parts = CreditsContent.PackageNotices.Split(
                new[] { CreditsContent.PackageNoticeSeparator }, System.StringSplitOptions.None);

            Assert.Greater(parts.Length, 1,
                "Expected more than one package notice — either the generator or the separator it " +
                "shares with CreditsPanel has drifted.");
        }

        /// <summary>The art section has to reproduce CREDITS.md verbatim, not a summary of it — a
        /// screen that rewords a licence is no longer displaying the licence.</summary>
        [Test]
        public void Panel_ShowsTheArtCreditsVerbatim()
        {
            var panel = new CreditsPanel(() => { });

            Assert.IsTrue(ContainsExactLabel(panel.Root, CreditsContent.ThirdPartyArt));
        }

        /// <summary>
        /// Every page in this shell has a way back to the title screen — a credits screen with no
        /// BACK button is a dead end a player has to alt-F4 out of. Built with
        /// <c>UiKit.QuietButton</c> like every other BACK button in the shell, so wiring the click
        /// itself through to the callback is <c>UiKit</c>'s contract, not this screen's — see
        /// <c>SettingsPanel</c> and <c>TitlePanel</c>, which rely on the same guarantee without
        /// re-proving it.
        /// </summary>
        [Test]
        public void Panel_HasABackButton()
        {
            var panel = new CreditsPanel(() => { });

            // Asked through the key rather than through the word (#55). The English is still "BACK",
            // but a test that hunts for a literal is a test that fails in every language but one —
            // which would make it a test of the translation rather than of the button being there.
            Assert.IsNotNull(FindButton(panel.Root, MenuStrings.Back.Text));
        }

        private static bool ContainsExactLabel(VisualElement root, string text)
        {
            if (root is Label label && label.text == text) return true;

            foreach (var child in root.Children())
            {
                if (ContainsExactLabel(child, text)) return true;
            }
            return false;
        }

        private static Button FindButton(VisualElement root, string text)
        {
            if (root is Button button && button.text == text) return button;

            foreach (var child in root.Children())
            {
                var found = FindButton(child, text);
                if (found != null) return found;
            }
            return null;
        }
    }
}
