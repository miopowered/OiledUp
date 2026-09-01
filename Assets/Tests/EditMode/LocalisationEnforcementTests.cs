using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The third of #55's three asks: something that catches a new literal reaching a draw call.
    ///
    /// <para>
    /// The first two — a lookup with ids, and structured arguments — are a one-off sweep. This is
    /// what makes them survive. Without it the count of inline literals starts climbing again with
    /// the next feature, and a year from now the sweep has to be done a second time; the issue says
    /// as much, and it is the reason it was filed during F1 rather than after it.
    /// </para>
    ///
    /// <para>
    /// <b>A test rather than an <c>AssetPostprocessor</c>.</b> <c>StyleEnforcer</c> can be an
    /// importer because it acts on assets as they land. Nothing imports a <c>.cs</c> file in a way
    /// that can reject it, so the only place a source rule can actually bite is a check that runs and
    /// fails — which is the suite. A menu item nobody remembers to run is a convention, and the
    /// convention is what already failed.
    /// </para>
    ///
    /// <para>
    /// <b>It reads source text, not IL.</b> A literal handed to <c>UiKit.Body("…")</c> compiles to
    /// exactly the same thing as one that came from a <c>LocKey</c>, so by the time it is IL the
    /// distinction this test exists to make is gone. Reading source is therefore not laziness; it is
    /// the only stage at which the question can be asked. The cost is that it sees text rather than
    /// syntax, so it deliberately looks only for a literal sitting <i>directly</i> in a call to a
    /// known drawing sink — the overwhelmingly common shape, and one with no false positives worth
    /// the complexity of parsing C# to avoid.
    /// </para>
    ///
    /// <para>
    /// It does not claim to catch everything, and saying so is better than implying otherwise. A
    /// literal assigned to a variable and passed a line later gets through. So does text that
    /// reaches the player without passing a drawing call at all — a sentence built into a field and
    /// drawn much later somewhere else. This is a ratchet against the easy mistake, not a proof, and
    /// there is deliberately no exemption list: every sink it does check is clean, so a failure here
    /// is always a new regression rather than a known debt somebody has to read past.
    /// </para>
    /// </summary>
    public sealed class LocalisationEnforcementTests
    {
        /// <summary>
        /// The calls that put words in front of a player. A literal in any of these is the mistake
        /// #55 is about.
        /// <para>
        /// <c>Say</c> is matched on the method name alone because it is reached through a
        /// <c>PlayerInteractor</c> held under half a dozen different field names.
        /// </para>
        /// </summary>
        private static readonly string[] DrawingSinks =
        {
            @"UiKit\.Title", @"UiKit\.Heading", @"UiKit\.Body", @"UiKit\.Hint", @"UiKit\.Value",
            @"UiKit\.ActionButton", @"UiKit\.QuietButton", @"UiKit\.DangerButton",
            @"HudStyle\.Text", @"HudStyle\.Caption",
            @"\.Say"
        };

        /// <summary>
        /// A literal as the first argument of a drawing sink. <c>@?"</c> so verbatim strings count,
        /// and <c>\$?</c> so an interpolated one is caught too — an interpolated literal is worse
        /// than a plain one, since it is also the concatenation #55 asks callers to stop writing.
        /// </summary>
        private static readonly Regex LiteralAtSink = new(
            @"(?:" + string.Join("|", DrawingSinks) + @")\(\s*\$?@?""",
            RegexOptions.Compiled);

        /// <summary>
        /// Promise: no new inline literal reaches a drawing call.
        /// <para>
        /// The failure message names the file, the line and the text, because a check that says only
        /// "something is wrong" gets suppressed rather than fixed.
        /// </para>
        /// </summary>
        [Test]
        public void NoPlayerFacingLiteral_SitsDirectlyInADrawCall()
        {
            var offenders = new List<string>();

            foreach (string path in SourceFiles())
            {
                string file = Path.GetFileName(path);
                string[] lines = File.ReadAllLines(path);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    // Comments and XML docs are not drawn.
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") ||
                        trimmed.StartsWith("*")) continue;

                    if (!LiteralAtSink.IsMatch(line)) continue;

                    offenders.Add($"{file}:{i + 1}  {trimmed}");
                }
            }

            Assert.IsEmpty(offenders,
                "These draw a literal straight at the player. Give each one a LocKey (see " +
                "PromptStrings / ScreenStrings / MenuStrings / LabStrings) so it can be translated, " +
                "and keep its arguments named rather than concatenated:\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Promise: the check is looking at something.
        /// <para>
        /// A scanner pointed at the wrong folder finds no offenders and passes, which is
        /// indistinguishable from a clean codebase right up until the day it matters. So assert it
        /// actually read the source tree.
        /// </para>
        /// </summary>
        [Test]
        public void TheCheck_ActuallyReadsTheSource()
        {
            var files = SourceFiles().ToList();

            Assert.Greater(files.Count, 50,
                $"Only {files.Count} source files were found under {ScriptsRoot()}. The scan is " +
                "pointed somewhere wrong, and a check that reads nothing passes for the wrong reason.");

            // And the pattern matches the thing it claims to match, or the sweep above is a no-op.
            Assert.IsTrue(LiteralAtSink.IsMatch("column.Add(UiKit.Body(\"Hello\"));"),
                "The sink pattern no longer matches a literal at a draw call, so this whole check " +
                "has quietly stopped working.");

            Assert.IsFalse(LiteralAtSink.IsMatch("column.Add(UiKit.Body(MenuStrings.Back));"),
                "The sink pattern is flagging a LocKey, which would make the check unusable.");
        }

        private static string ScriptsRoot() =>
            Path.Combine(Application.dataPath, "Scripts");

        private static IEnumerable<string> SourceFiles() =>
            Directory.Exists(ScriptsRoot())
                ? Directory.EnumerateFiles(ScriptsRoot(), "*.cs", SearchOption.AllDirectories)
                    .Where(path => !path.EndsWith(".Generated.cs", StringComparison.Ordinal))
                : Enumerable.Empty<string>();
    }
}
