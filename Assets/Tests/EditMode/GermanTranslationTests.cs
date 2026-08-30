using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Residue.Data;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The German table. These are the checks that only exist once a second language does — every
    /// one of them guards a failure that is invisible to anybody reading the game in English.
    /// </summary>
    public sealed class GermanTranslationTests
    {
        [TearDown]
        public void TearDown() => Loc.UseEnglish();

        /// <summary>
        /// Promise: **a translated line keeps every placeholder its English had.**
        ///
        /// <para>
        /// This is the important one. A translator who drops <c>{tag}</c> produces a sentence that
        /// reads perfectly and has lost the only thing identifying which vial it is about — and one
        /// who mistypes it as <c>{Tag}</c> ships a literal "{Tag}" to the screen, because
        /// <see cref="Loc.Fill"/> deliberately leaves an unmatched placeholder visible. Neither shows
        /// up in English, neither is a compile error, and neither is findable by anyone who does not
        /// read German. So it is checked here rather than discovered by a player.
        /// </para>
        ///
        /// <para>
        /// Extra placeholders are caught too: <c>{maschine}</c> where the caller supplies
        /// <c>machine</c> draws as a brace-wrapped word forever. The argument names are part of the
        /// contract, not part of the language.
        /// </para>
        /// </summary>
        [Test]
        public void EveryTranslatedLine_KeepsThePlaceholdersOfItsEnglish()
        {
            var problems = new List<string>();

            foreach (var (owner, key) in EnglishKeys())
            {
                if (!German.Table.TryGetValue(key.Id, out string german)) continue;

                var expected = Placeholders(key.English);
                var actual = Placeholders(german);

                foreach (string missing in expected.Except(actual))
                    problems.Add($"{owner} ('{key.Id}'): German drops {{{missing}}}");

                foreach (string extra in actual.Except(expected))
                    problems.Add($"{owner} ('{key.Id}'): German adds {{{extra}}}, which nothing supplies");
            }

            Assert.IsEmpty(problems,
                "A placeholder mismatch draws a literal brace on screen, or silently loses the value " +
                "that said which sample the sentence was about:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Promise: the table translates lines that exist.
        /// <para>
        /// An id that matches nothing is dead weight that looks like coverage — it inflates the count
        /// in the test below while translating nothing. It is also what a renamed key leaves behind,
        /// and a rename is a deletion plus an addition (see <see cref="LocKey.Id"/>), so this is how
        /// the leftover half gets noticed.
        /// </para>
        /// </summary>
        [Test]
        public void TheGermanTable_HasNoEntriesForLinesThatDoNotExist()
        {
            var known = EnglishKeys().Select(entry => entry.Key.Id).ToHashSet(StringComparer.Ordinal);

            var orphans = German.Table.Keys.Where(id => !known.Contains(id)).OrderBy(id => id).ToList();

            Assert.IsEmpty(orphans,
                "These German entries match no LocKey. Either the key was renamed and this is the " +
                "half left behind, or the id is a typo and the line is not actually translated:\n  " +
                string.Join("\n  ", orphans));
        }

        /// <summary>
        /// Promise: the translation is complete.
        /// <para>
        /// An untranslated line falls back to English rather than breaking, which is the right
        /// behaviour and also the reason this needs asserting: a half-translated game runs perfectly
        /// and simply looks unfinished, and nothing else would ever fail.
        /// </para>
        /// </summary>
        [Test]
        public void EveryLine_HasAGermanTranslation()
        {
            var missing = EnglishKeys()
                .Where(entry => !German.Table.ContainsKey(entry.Key.Id))
                .Select(entry => $"{entry.Owner} ('{entry.Key.Id}')  {entry.Key.English}")
                .OrderBy(line => line)
                .ToList();

            Assert.IsEmpty(missing,
                $"{missing.Count} lines still read English with German selected:\n  " +
                string.Join("\n  ", missing.Take(40)));
        }

        /// <summary>
        /// Promise: nothing was left in English by accident.
        /// <para>
        /// A copy-paste that never got translated is indistinguishable from a deliberate one at a
        /// glance. Some entries genuinely should match — a bare number format, a proper noun — so
        /// this reports rather than forbids, and only fails if the overlap is large enough to mean
        /// somebody pasted a block and moved on.
        /// </para>
        /// </summary>
        [Test]
        public void TheTranslation_IsNotMostlyEnglishCopiedAcross()
        {
            var identical = EnglishKeys()
                .Where(entry => German.Table.TryGetValue(entry.Key.Id, out string german) &&
                                string.Equals(german, entry.Key.English, StringComparison.Ordinal))
                .Select(entry => entry.Key.Id)
                .ToList();

            int total = EnglishKeys().Count();

            Assert.Less(identical.Count, Math.Max(12, total / 10),
                $"{identical.Count} of {total} German entries are byte-identical to the English. A " +
                "few are legitimate; this many means a block was pasted and not translated:\n  " +
                string.Join("\n  ", identical.Take(30)));
        }

        /// <summary>
        /// Promise: selecting German actually changes what a line reads as, end to end.
        /// <para>
        /// Every test above works on the table. This one goes through <see cref="Loc"/> the way the
        /// game does, so it fails if the table is correct but never installed.
        /// </para>
        /// </summary>
        [Test]
        public void SelectingGerman_ChangesWhatALineReadsAs()
        {
            var sample = EnglishKeys().First(entry => German.Table.ContainsKey(entry.Key.Id)).Key;
            string english = sample.Text;

            Loc.Use(German.Code, German.Table);

            Assert.AreEqual(German.Code, Loc.Language);
            Assert.AreEqual(German.Table[sample.Id], sample.Text,
                "The table has this line but Loc is not resolving through it.");

            Loc.UseEnglish();
            Assert.AreEqual(english, sample.Text, "Switching back has to restore English.");
        }

        private static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);

        private static IEnumerable<string> Placeholders(string text) =>
            text == null
                ? Enumerable.Empty<string>()
                : Placeholder.Matches(text).Select(match => match.Groups[1].Value).Distinct();

        /// <summary>
        /// Every <see cref="LocKey"/> declared anywhere in <c>Residue.Data</c>. Discovered rather than
        /// listed, so a table added later is covered without anyone extending this.
        /// </summary>
        private static IEnumerable<(string Owner, LocKey Key)> EnglishKeys()
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
            }
        }
    }
}
