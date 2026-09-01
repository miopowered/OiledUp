using System.Collections.Generic;

namespace Residue.Data
{
    /// <summary>
    /// The German table, assembled from one file per area.
    ///
    /// <para>
    /// <b>Duzen, not siezen.</b> The English voice is terse and direct — "You are not standing at the
    /// terminal." — and the formal address would put a register between the lab and the player that
    /// the original does not have. It is also shorter, which matters on a 32-column instrument screen.
    /// Applied consistently: a table that switches halfway reads as two people wrote it, because two
    /// people did.
    /// </para>
    ///
    /// <para>
    /// <b>Split by area, one class per file, for the same reason the English tables are.</b> A single
    /// 526-entry file is unreviewable and unmergeable, and the areas are what a translator actually
    /// works through — every prompt, then every screen, rather than an alphabetical list that jumps
    /// between the two.
    /// </para>
    ///
    /// <para>
    /// <b>What is not translated, and must not be.</b> Equipment tags, element ids, machine instance
    /// ids and sample ids travel through these lines as arguments and are never looked up. Content
    /// from <c>ContentTables.cs</c> — fault names, element names, root causes — is balance data with
    /// its own pipeline, and hard rule 1 means a fault that read differently in two languages would
    /// be the chemistry lying in one of them. The credits licence bodies are excluded outright: they
    /// have to appear exactly as the licence was granted.
    /// </para>
    ///
    /// <para>
    /// Anything missing falls back to English rather than to an id, so this being incomplete is a
    /// partial translation and never a broken build. <c>GermanTranslationTests</c> is what stops it
    /// staying incomplete quietly.
    /// </para>
    /// </summary>
    public static class German
    {
        /// <summary>BCP 47, and what <see cref="Loc.Language"/> reports while this is installed.</summary>
        public const string Code = "de";

        /// <summary>What the language picker shows, in the language itself — never "German".</summary>
        public const string EndonymLabel = "Deutsch";

        private static Dictionary<string, string> table;

        /// <summary>
        /// Built once and kept. Five hundred odd entries is nothing to construct, but the table is
        /// asked for on every language change and there is no reason to rebuild it each time.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Table
        {
            get
            {
                if (table != null) return table;

                table = new Dictionary<string, string>(600);
                GermanPrompts.AddTo(table);
                GermanScreens.AddTo(table);
                GermanMenu.AddTo(table);
                GermanLab.AddTo(table);
                GermanTutorial.AddTo(table);
                GermanConsequences.AddTo(table);
                return table;
            }
        }
    }
}
