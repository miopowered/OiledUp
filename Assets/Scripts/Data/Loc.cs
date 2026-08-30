using System.Collections.Generic;
using System.Text;

namespace Residue.Data
{
    /// <summary>
    /// The active language, and the formatter behind <see cref="LocKey"/> (#55).
    /// <para>
    /// This is deliberately not <c>com.unity.localization</c>. That package authors its tables as
    /// binary-ish assets, which is exactly what <c>CLAUDE.md</c> rules out for anything that has to be
    /// reviewable in a diff, and it would put a package dependency behind every prompt in the game to
    /// buy a lookup that is thirty lines. The same argument <c>AudioBus</c> makes against an
    /// <c>AudioMixer</c>.
    /// </para>
    /// <para>
    /// No language is shipped but English. #55 is explicitly about making translation a project
    /// rather than a rewrite, and the font work is out of scope — the in-world screens draw with a
    /// pixel font that has no glyph coverage past Latin. The plumbing does not commit anyone to
    /// solving that.
    /// </para>
    /// </summary>
    public static class Loc
    {
        private static IReadOnlyDictionary<string, string> table;

        /// <summary>The language code in force, for a screen that wants to say so. Empty for English.</summary>
        public static string Language { get; private set; } = string.Empty;

        /// <summary>
        /// Install a translation. Anything the table omits falls back to English, so a partial
        /// translation is a partial translation rather than a broken build.
        /// </summary>
        public static void Use(string language, IReadOnlyDictionary<string, string> translations)
        {
            Language = language ?? string.Empty;
            table = translations;
        }

        /// <summary>Back to English.</summary>
        public static void UseEnglish()
        {
            Language = string.Empty;
            table = null;
        }

        public static string Resolve(LocKey key)
        {
            if (table != null && key.Id != null &&
                table.TryGetValue(key.Id, out string translated) &&
                !string.IsNullOrEmpty(translated))
            {
                return translated;
            }

            return key.English ?? string.Empty;
        }

        /// <summary>
        /// Replace every <c>{name}</c> in <paramref name="template"/> from
        /// <paramref name="arguments"/>.
        ///
        /// <para>
        /// Hand-rolled rather than <c>string.Format</c> or a regex, for two reasons. Named
        /// placeholders are the point (see <see cref="LocKey"/>) and <c>string.Format</c> only does
        /// positional ones. And this runs inside <c>Prompt()</c>, which is called every frame the
        /// player is looking at anything — a regex there would allocate a match collection sixty
        /// times a second per object under the crosshair.
        /// </para>
        ///
        /// <para>
        /// A template with no placeholder returns without allocating at all, which is most of them.
        /// </para>
        /// </summary>
        public static string Fill(string template, params (string Name, object Value)[] arguments)
        {
            if (string.IsNullOrEmpty(template) || arguments == null || arguments.Length == 0)
                return template;

            if (template.IndexOf('{') < 0) return template;

            var built = new StringBuilder(template.Length + 16);

            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] != '{') { built.Append(template[i]); continue; }

                int close = template.IndexOf('}', i + 1);
                if (close < 0) { built.Append(template, i, template.Length - i); break; }

                string name = template.Substring(i + 1, close - i - 1);
                if (TryFind(arguments, name, out object value))
                    built.Append(value);
                else
                    built.Append('{').Append(name).Append('}');

                i = close;
            }

            return built.ToString();
        }

        private static bool TryFind((string Name, object Value)[] arguments, string name,
                                    out object value)
        {
            foreach (var argument in arguments)
            {
                if (!string.Equals(argument.Name, name, System.StringComparison.Ordinal)) continue;

                value = argument.Value;
                return true;
            }

            value = null;
            return false;
        }
    }
}
