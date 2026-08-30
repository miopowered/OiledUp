using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Residue.Data;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The localisation plumbing (#55). Not a test that the game is translated — nothing is, and
    /// that is deliberate — but that translating it later stays a project rather than a rewrite.
    /// </summary>
    public sealed class LocalisationTests
    {
        [TearDown]
        public void TearDown() => Loc.UseEnglish();

        /// <summary>
        /// Promise: an untranslated build reads as English, and a translated one reads as the
        /// translation.
        /// </summary>
        [Test]
        public void ALine_ReadsAsEnglishUntilSomethingSaysOtherwise()
        {
            var key = new LocKey("test.greeting", "Good morning.");

            Assert.AreEqual("Good morning.", key.Text);

            Loc.Use("de", new Dictionary<string, string> { ["test.greeting"] = "Guten Morgen." });
            Assert.AreEqual("Guten Morgen.", key.Text);

            Loc.UseEnglish();
            Assert.AreEqual("Good morning.", key.Text);
        }

        /// <summary>
        /// Promise: a half-finished translation shows English, never an id.
        /// <para>
        /// A player who is shown "prompt.take_printout" has been handed a bug report to read. A
        /// translator who has not reached a line yet has not broken the game, and should not have to
        /// complete the whole file before anyone can run it.
        /// </para>
        /// </summary>
        [Test]
        public void AMissingTranslation_FallsBackToEnglish_NotToTheId()
        {
            var key = new LocKey("test.untranslated", "Take the vial.");

            Loc.Use("de", new Dictionary<string, string> { ["test.other"] = "Etwas anderes." });

            Assert.AreEqual("Take the vial.", key.Text);

            // An entry that exists but is empty is a line the translator has not done either.
            Loc.Use("de", new Dictionary<string, string> { ["test.untranslated"] = "" });
            Assert.AreEqual("Take the vial.", key.Text);
        }

        /// <summary>
        /// Promise: arguments are named, so a translator can move them.
        /// <para>
        /// This is the half of #55 that cannot be retrofitted. Word order is not universal: a
        /// language that puts the object first is unreachable if the English was assembled by
        /// concatenation, because the translator is handed a fragment rather than a sentence. The
        /// test that matters is therefore not that the placeholder fills — it is that a template
        /// which reorders its placeholders still produces a sensible sentence.
        /// </para>
        /// </summary>
        [Test]
        public void ATranslation_CanReorderTheArguments()
        {
            var key = new LocKey("test.load", "Load {sample} into {machine}");

            Assert.AreEqual("Load QT-4471 into the viscometer",
                key.Format(("sample", "QT-4471"), ("machine", "the viscometer")));

            Loc.Use("yoda", new Dictionary<string, string>
            {
                ["test.load"] = "Into {machine}, {sample} you must load"
            });

            Assert.AreEqual("Into the viscometer, QT-4471 you must load",
                key.Format(("sample", "QT-4471"), ("machine", "the viscometer")));
        }

        /// <summary>
        /// Promise: a mistake is visible rather than silent.
        /// <para>
        /// An argument the template does not use, or a placeholder nobody supplied, leaves the
        /// <c>{name}</c> on screen. A formatter that swallowed it would produce a sentence with a
        /// hole in it that reads as finished English — quietly wrong is worse than obviously wrong,
        /// because only one of them gets reported.
        /// </para>
        /// </summary>
        [Test]
        public void AMissingArgument_ShowsItsPlaceholder()
        {
            var key = new LocKey("test.partial", "Take {item} from {place}");

            Assert.AreEqual("Take the note from {place}", key.Format(("item", "the note")));
        }

        /// <summary>Promise: text with no placeholders survives untouched, braces and all.</summary>
        [Test]
        public void TextWithoutPlaceholders_IsLeftAlone()
        {
            Assert.AreEqual("Nothing to decide.",
                Loc.Fill("Nothing to decide.", ("unused", "x")));

            // An unclosed brace is content, not a crash.
            Assert.AreEqual("A { that never closes",
                Loc.Fill("A { that never closes", ("unused", "x")));
        }

        /// <summary>
        /// Promise: no two lines share an id.
        /// <para>
        /// A duplicate is invisible in English — both call sites already read correctly — and shows
        /// up only once somebody translates, at which point one of the two lines silently takes the
        /// other's translation. That is a bug that can only be found in a language the author does
        /// not speak, so it has to be caught here instead.
        /// </para>
        /// <para>
        /// Discovered by reflection over every <see cref="LocKey"/> the assembly declares, so a new
        /// table added later is covered without anyone extending this.
        /// </para>
        /// </summary>
        [Test]
        public void EveryLine_HasItsOwnId()
        {
            var byId = new Dictionary<string, string>();
            var clashes = new List<string>();

            foreach (var (owner, key) in AllKeys())
            {
                if (string.IsNullOrEmpty(key.Id))
                {
                    clashes.Add($"{owner} has no id");
                    continue;
                }

                if (byId.TryGetValue(key.Id, out string first))
                    clashes.Add($"'{key.Id}' is used by both {first} and {owner}");
                else
                    byId[key.Id] = owner;
            }

            Assert.IsEmpty(clashes,
                "Two lines sharing an id read correctly in English and swap places the moment " +
                "anyone translates them:\n  " + string.Join("\n  ", clashes));
        }

        /// <summary>
        /// Promise: every line actually says something in English.
        /// <para>
        /// An empty English is a line that falls back to nothing, which draws as a blank prompt.
        /// </para>
        /// </summary>
        [Test]
        public void EveryLine_HasEnglish()
        {
            var empty = AllKeys()
                .Where(entry => string.IsNullOrWhiteSpace(entry.Key.English))
                .Select(entry => entry.Owner)
                .ToList();

            Assert.IsEmpty(empty,
                "These draw as nothing at all:\n  " + string.Join("\n  ", empty));
        }

        /// <summary>
        /// Promise: ids follow the convention, so a translator can sort the file into screens.
        /// <para>
        /// Lowercase and dotted, with a leading group. Not cosmetic: the id is the translator's
        /// primary key and their working order, and a flat unsorted list of several hundred lines is
        /// what makes a translation a slog rather than a task.
        /// </para>
        /// </summary>
        [Test]
        public void EveryId_IsGroupedAndLowercase()
        {
            var wrong = new List<string>();

            foreach (var (owner, key) in AllKeys())
            {
                if (string.IsNullOrEmpty(key.Id)) continue;

                if (key.Id != key.Id.ToLowerInvariant())
                    wrong.Add($"{owner}: '{key.Id}' is not lowercase");
                else if (!key.Id.Contains('.'))
                    wrong.Add($"{owner}: '{key.Id}' has no group prefix (expected e.g. 'prompt.')");
            }

            Assert.IsEmpty(wrong, string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Every <see cref="LocKey"/> declared as a static field or property anywhere in
        /// <c>Residue.Data</c>, with the member that owns it for the failure message.
        /// </summary>
        private static IEnumerable<(string Owner, LocKey Key)> AllKeys()
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static;

            foreach (var type in typeof(LocKey).Assembly.GetTypes())
            {
                foreach (var field in type.GetFields(Flags))
                {
                    if (field.FieldType != typeof(LocKey)) continue;

                    yield return ($"{type.Name}.{field.Name}", (LocKey)field.GetValue(null));
                }

                foreach (var property in type.GetProperties(Flags))
                {
                    if (property.PropertyType != typeof(LocKey)) continue;
                    if (property.GetMethod == null) continue;

                    LocKey value;
                    try { value = (LocKey)property.GetValue(null); }
                    catch (Exception) { continue; }

                    yield return ($"{type.Name}.{property.Name}", value);
                }
            }
        }
    }
}
