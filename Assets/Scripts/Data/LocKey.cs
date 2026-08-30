using System.Collections.Generic;
using System.Text;

namespace Residue.Data
{
    /// <summary>
    /// One player-facing string: a stable id, and the English it reads as (#55).
    ///
    /// <para>
    /// <b>The id and the English live together on purpose.</b> The obvious shape is an id at the call
    /// site and a dictionary of English somewhere else, and it rots immediately: the call site stops
    /// saying what it draws, so reviewing a prompt means opening two files, and a key deleted in one
    /// place survives in the other for ever. Here the declaration <i>is</i> the English table — there
    /// is no second copy to fall out of step, and a diff that changes what the player reads shows the
    /// new sentence rather than an id.
    /// </para>
    ///
    /// <para>
    /// A translation is then an override keyed on <see cref="Id"/>, layered on by <see cref="Loc"/>.
    /// English is the fallback for anything a table does not carry, so a half-finished translation
    /// shows English rather than an id — a player who sees "prompt.take_printout" has been handed a
    /// bug report to read, and a translator who has not reached a line yet has not broken the game.
    /// </para>
    ///
    /// <para>
    /// <b>Arguments are named, never positional.</b> <c>"Take printout — {tag}"</c> rather than a
    /// concatenation or a <c>{0}</c>, because word order is not universal: a language that puts the
    /// object first cannot be served by a translator handed the fragment <c>"Take printout — "</c>,
    /// and a translator handed <c>{0}</c> cannot tell what it will be. This is the half of #55 that
    /// is hard to retrofit later, which is why it is in the primitive rather than left to callers.
    /// </para>
    ///
    /// <para>
    /// <b>Ids are not data.</b> Equipment tags, element ids, machine instance ids and
    /// <c>SampleId</c> formatting never come through here. Running an id through a translation table
    /// is a bug whose symptom is a lookup failing in one language only.
    /// </para>
    /// </summary>
    public readonly struct LocKey
    {
        /// <summary>
        /// Stable, lowercase, dotted, grouped by where it is drawn — <c>prompt.</c>, <c>terminal.</c>,
        /// <c>menu.</c>, <c>hud.</c>, <c>book.</c>. It is the translator's primary key, so renaming one
        /// silently drops that line back to English in every shipped translation. Treat a rename as a
        /// deletion plus an addition, because that is what it is.
        /// </summary>
        public string Id { get; }

        /// <summary>What it reads as with no translation loaded, and the source text a translator works from.</summary>
        public string English { get; }

        public LocKey(string id, string english)
        {
            Id = id;
            English = english;
        }

        /// <summary>The active translation of this line, or its English.</summary>
        public string Text => Loc.Resolve(this);

        /// <summary>
        /// Fill the named placeholders. Missing arguments are left as their <c>{name}</c> so a
        /// mistake shows up as a visible placeholder in one line rather than as a silently empty
        /// sentence — the difference between "obviously wrong" and "quietly wrong".
        /// </summary>
        public string Format(params (string Name, object Value)[] arguments) =>
            Loc.Fill(Text, arguments);

        public override string ToString() => Text;

        /// <summary>So a key can be handed straight to anything taking a string.</summary>
        public static implicit operator string(LocKey key) => key.Text;
    }
}
