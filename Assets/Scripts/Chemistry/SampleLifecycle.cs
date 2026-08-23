using System.Collections.Generic;
using System.Text;
using Residue.Data;
using UnityEngine;

namespace Residue.Chemistry
{
    /// <summary>
    /// The §5.1 lifecycle as an explicit state machine: one table of legal transitions, and one
    /// gateway per step that consults it before touching a field.
    /// <para>
    /// <b>The stage is derived, never stored.</b> Location, <see cref="SampleState.IsSettled"/>,
    /// <see cref="SampleState.Results"/>, <see cref="SampleState.FiledVerdict"/> and
    /// <see cref="SampleState.ConsequenceResolved"/> already exist and are already written from half
    /// a dozen places. A stored <c>Stage</c> field would be a second source of truth that any of
    /// those writes could silently desync from, and a lifecycle that disagrees with the data it
    /// guards is worse than no lifecycle at all. Deriving it means the stage cannot lie: it is a
    /// reading of the record, not a parallel set of books. It also costs nothing at M4 — a client
    /// computes the stage from the <see cref="SampleState"/> it already has, with no extra field to
    /// replicate and nothing to keep in sync across the wire.
    /// </para>
    /// Server-side by construction: this type never sees <see cref="SampleGroundTruth"/>, so no
    /// amount of lifecycle plumbing can put ground truth on a path to a client.
    /// </summary>
    public static class SampleLifecycle
    {
        /// <summary>
        /// Every legal transition, indexed by the stage being left. This is the only place the shape
        /// of the lifecycle is written down; call sites ask, they do not assume.
        /// <para>
        /// Four entries deserve their reasons in the open:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <b>Every stage before <see cref="SampleStage.Archived"/> can reach it.</b> Filing a
        /// verdict is always available, including on a vial still in the crate. The game's tension is
        /// that you <i>can</i> call a tank without testing it; §5.6 requires blanket-verdict
        /// strategies to be playable and to lose money. The lifecycle constrains analysis, not the
        /// player's right to make a call.
        /// </description></item>
        /// <item><description>
        /// <b><see cref="SampleStage.Logged"/> repeats.</b> The record can be amended while it is
        /// still only a record. Hard rule 3 — a mis-log the player spots must be fixable, or the
        /// punishment is for a typo rather than for not checking.
        /// </description></item>
        /// <item><description>
        /// <b><see cref="SampleStage.Prepped"/> and <see cref="SampleStage.Measured"/> repeat.</b>
        /// A sample is agitated more than once and runs on more than one instrument.
        /// </description></item>
        /// <item><description>
        /// <b><see cref="SampleStage.Archived"/> goes back to <see cref="SampleStage.Measured"/>.</b>
        /// §5.3 puts a certified reference sample in the player's hands and then makes it say the
        /// instrument was reading 18% high — which means every verdict filed since it started
        /// drifting was filed on numbers the lab got wrong. The spec names the remedy in the same
        /// breath: show the affected archived samples and let them be re-opened. Hard rule 3 cuts
        /// both ways here. The player checked, found the fault, and has to be able to act on it, or
        /// the check was theatre. The edge lands on Measured rather than Logged because a re-opening
        /// withdraws the <i>verdict</i> and nothing else: the tag stays fixed, and the results stay
        /// on file — suspect flags included — because the point is to run them again and compare.
        /// <see cref="TryReopen"/> is the only gateway that uses it; <see cref="TryFileResult"/>
        /// refuses it by hand, since appending a slip to a record that still carries a verdict would
        /// quietly change what the player was looking at when they made the call.
        /// </description></item>
        /// </list>
        /// </summary>
        private static readonly SampleStage[][] Table =
        {
            /* InCrate  */ new[] { SampleStage.Unpacked, SampleStage.Archived },
            /* Unpacked */ new[] { SampleStage.Logged, SampleStage.Archived },
            /* Logged   */ new[] { SampleStage.Logged, SampleStage.Prepped, SampleStage.Archived },
            /* Prepped  */ new[] { SampleStage.Prepped, SampleStage.Measured, SampleStage.Archived },
            /* Measured */ new[] { SampleStage.Measured, SampleStage.Archived },
            /* Archived */ new[] { SampleStage.Measured, SampleStage.Resolved },
            /* Resolved */ new SampleStage[0]
        };

        /// <summary>
        /// Read the stage off the record. Ordered most-advanced first, so the answer is always the
        /// furthest point the sample has actually reached.
        /// </summary>
        public static SampleStage StageOf(SampleState state)
        {
            if (state == null) return SampleStage.InCrate;
            if (state.ConsequenceResolved) return SampleStage.Resolved;
            if (state.FiledVerdict.HasValue) return SampleStage.Archived;
            if (state.Results.Count > 0) return SampleStage.Measured;

            // Settling only counts as prep once the vial is booked in. Without the IsLogged guard,
            // anything that sets IsSettled without going through TryPrep derives Prepped — a stage
            // the chain says is unreachable except via Logged — and TryLog then refuses forever with
            // "already been worked on". That strands the vial: unloggable, so never legally
            // preppable, with no way back. Deriving a stage the table cannot produce is the one
            // failure mode a derived stage has that a stored one does not.
            if (state.IsSettled && state.IsLogged) return SampleStage.Prepped;
            if (state.IsLogged) return SampleStage.Logged;

            return state.Location.Kind == SampleLocationKind.InCrate
                ? SampleStage.InCrate
                : SampleStage.Unpacked;
        }

        public static bool IsLegal(SampleStage from, SampleStage to)
        {
            foreach (var next in Table[(int)from])
            {
                if (next == to) return true;
            }
            return false;
        }

        public static IReadOnlyList<SampleStage> LegalNext(SampleStage from) => Table[(int)from];

        /// <summary>
        /// Why a transition is refused, as a sentence fragment that reads after a sample's name.
        /// Null when the transition is legal.
        /// <para>
        /// Pure: it does not log. Prompts ask this every frame to explain a greyed-out action, and
        /// §9 wants that explanation without a console full of the player looking at something.
        /// </para>
        /// </summary>
        public static string Explain(SampleStage from, SampleStage to)
        {
            if (IsLegal(from, to)) return null;

            return to switch
            {
                SampleStage.Unpacked => "is already out of the delivery crate.",

                SampleStage.Logged when from == SampleStage.InCrate =>
                    "is still in the delivery crate — unload it before booking it in.",
                SampleStage.Logged when from >= SampleStage.Archived =>
                    "has been filed. An archived record cannot be re-tagged.",
                SampleStage.Logged =>
                    "has already been worked on; the tag is fixed once analysis starts.",

                SampleStage.Prepped when from < SampleStage.Logged =>
                    "is not booked in — register the tank tag at the terminal first (§5.1).",
                SampleStage.Prepped when from >= SampleStage.Archived =>
                    "has been filed and archived.",
                SampleStage.Prepped => "has already been through an instrument.",

                SampleStage.Measured when from < SampleStage.Prepped =>
                    "has not been prepped, so nothing can have measured it.",
                SampleStage.Measured =>
                    "has already been resolved — the consequence has landed, so there is nothing " +
                    "left to re-test.",

                SampleStage.Archived when from == SampleStage.Archived =>
                    "already has a verdict on file.",
                SampleStage.Archived => "has already been resolved.",

                SampleStage.Resolved when from == SampleStage.Resolved =>
                    "has already been resolved.",
                SampleStage.Resolved => "has no verdict filed, so there is nothing to resolve.",

                _ => "does not go back in the delivery crate."
            };
        }

        /// <summary>The refusal for a specific sample, ready to show the player. Null when legal.</summary>
        public static string Refusal(SampleState state, SampleStage to) =>
            state == null ? "No such sample." : Compose(state, StageOf(state), to);

        /// <summary>
        /// Gate a transition and report it. Every rejection goes to the console with the sample, the
        /// stages and the reason — an illegal transition is a bug in a call site, and swallowing it
        /// leaves a sample that quietly stops progressing with nothing to search for.
        /// </summary>
        public static bool CanAdvance(SampleState state, SampleStage to, out string refusal)
        {
            if (state == null)
            {
                refusal = "No such sample.";
                Debug.LogWarning($"[SampleLifecycle] Refused -> {to} on a null sample.");
                return false;
            }

            var from = StageOf(state);
            if (IsLegal(from, to))
            {
                refusal = null;
                return true;
            }

            refusal = Compose(state, from, to);
            Debug.LogWarning($"[SampleLifecycle] {state.Id} refused {from} -> {to}: {refusal}");
            return false;
        }

        private static string Compose(SampleState state, SampleStage from, SampleStage to)
        {
            string reason = Explain(from, to);
            return reason == null ? null : $"{state.RecordTag} {reason}";
        }

        // -- The steps ----------------------------------------------------------------------------

        /// <summary>
        /// Put the vial somewhere. Only one move is a lifecycle step — the one that takes it out of
        /// the delivery crate, which is §5.1's unload. Every later move is a shelf change: which of
        /// <c>[fridge | bench]</c> a sample is sitting on is a location, not progress, and an
        /// archived vial is still a bottle you can pick up (§5.3 re-opens archived samples).
        /// </summary>
        public static bool TryMove(SampleState state, SampleLocation destination, out string refusal)
        {
            refusal = null;
            if (state == null) { refusal = "No such sample."; return false; }

            bool leavingTheCrate = StageOf(state) == SampleStage.InCrate &&
                                   destination.Kind != SampleLocationKind.InCrate;

            if (leavingTheCrate && !CanAdvance(state, SampleStage.Unpacked, out refusal)) return false;

            state.Location = destination;
            return true;
        }

        /// <summary>
        /// Register the vial against a tank tag the player has typed.
        /// <para>
        /// Nothing here checks the tag against the label. That is the whole mechanic: §5.1 makes
        /// mis-logging a real failure mode, and it is only a failure mode if a wrong tag is accepted
        /// exactly as readily as a right one. What the player typed is what the terminal shows from
        /// here on, so the tell is walking back to the vial and reading the paper label — which is
        /// what keeps hard rule 3 satisfied.
        /// </para>
        /// </summary>
        public static bool TryLog(SampleState state, string typedTag, out string refusal)
        {
            refusal = null;
            if (state == null) { refusal = "No such sample."; return false; }

            string tag = NormaliseTag(typedTag);
            if (string.IsNullOrEmpty(tag))
            {
                refusal = "Type the tank tag printed on the vial label.";
                return false;
            }

            if (!CanAdvance(state, SampleStage.Logged, out refusal)) return false;

            state.LoggedTag = tag;
            return true;
        }

        /// <summary>Agitate back to homogeneous. Instruments refuse anything that has not had this (§4.5).</summary>
        public static bool TryPrep(SampleState state, out string refusal)
        {
            if (!CanAdvance(state, SampleStage.Prepped, out refusal)) return false;
            state.IsSettled = true;
            return true;
        }

        /// <summary>
        /// Transcribe a printout into the record. Instruments do not call this — the player does, by
        /// carrying the slip to the terminal (§5.1).
        /// </summary>
        public static bool TryFileResult(SampleState state, TestResult result, out string refusal)
        {
            refusal = null;
            if (state == null) { refusal = "No such sample."; return false; }

            if (result == null) { refusal = "That slip is blank."; return false; }

            // Archived -> Measured is a legal edge, but it belongs to TryReopen. Filing a slip
            // against a record that still carries a verdict would rewrite the evidence behind a call
            // the player has already made, so this gateway refuses what the table allows.
            var stage = StageOf(state);
            if (stage >= SampleStage.Archived)
            {
                refusal = stage == SampleStage.Archived
                    ? $"{state.RecordTag} has a verdict on file. Re-open the record before adding to it."
                    : $"{state.RecordTag} has been resolved. Results cannot be added to a closed record.";
                return false;
            }

            if (!CanAdvance(state, SampleStage.Measured, out refusal)) return false;

            if (state.Results.Contains(result)) { refusal = "Already on file."; return false; }

            state.Results.Add(result);
            return true;
        }

        /// <summary>
        /// File the verdict and close the record. Queuing the consequence is the caller's job — that
        /// needs ground truth for the delay, so it lives in the registry and not here.
        /// </summary>
        public static bool TryArchive(SampleState state, Verdict verdict, RootCauseDef rootCause,
                                      int day, out string refusal)
        {
            if (!CanAdvance(state, SampleStage.Archived, out refusal)) return false;

            state.FiledVerdict = verdict;
            state.FiledRootCause = rootCause;
            state.FiledOnDay = day;
            state.Location = SampleLocation.Archived();
            return true;
        }

        /// <summary>
        /// Withdraw a filed verdict and put the record back in play (§5.3).
        /// <para>
        /// Only ever reached because the player proved the instrument was lying: a certified
        /// reference run revealed drift, and this record's numbers were taken inside that window.
        /// Everything measured stays on file, suspect flags and all — re-opening is an invitation to
        /// re-run and compare, not an eraser. What comes off is the verdict, the root cause and the
        /// day it was filed, because those are the claims that were made on the bad numbers.
        /// </para>
        /// The vial goes back on the archive shelf as a bottle rather than a record, because the
        /// whole point is that it can be carried to an instrument again.
        /// <para>
        /// Whether there is enough oil left to be worth re-testing is not asked here. The lifecycle
        /// knows about records; volume is the caller's to weigh, and refusing on it is the sharper
        /// half of the mechanic.
        /// </para>
        /// </summary>
        public static bool TryReopen(SampleState state, out string refusal)
        {
            refusal = null;
            if (state == null) { refusal = "No such sample."; return false; }

            if (!CanAdvance(state, SampleStage.Measured, out refusal)) return false;

            if (!state.FiledVerdict.HasValue)
            {
                refusal = $"{state.RecordTag} has no verdict on file to withdraw.";
                return false;
            }

            if (state.Results.Count == 0)
            {
                refusal = $"{state.RecordTag} has nothing on file to re-test.";
                return false;
            }

            state.FiledVerdict = null;
            state.FiledRootCause = null;
            state.FiledOnDay = -1;
            state.Location = SampleLocation.OnSurface("archive", -1);
            return true;
        }

        /// <summary>The consequence has landed and been shown to the player. Terminal state.</summary>
        public static bool TryResolve(SampleState state, out string refusal)
        {
            if (!CanAdvance(state, SampleStage.Resolved, out refusal)) return false;
            state.ConsequenceResolved = true;
            return true;
        }

        // -- Tags ---------------------------------------------------------------------------------

        /// <summary>
        /// Fold a typed tag onto the form the labels are printed in: trimmed, single-spaced, upper
        /// case. Case and stray spaces are transcription noise, and punishing those would make the
        /// mechanic about typing rather than about attention. Naming the wrong tank still is.
        /// </summary>
        public static string NormaliseTag(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var builder = new StringBuilder(raw.Length);
            bool pendingSpace = false;

            foreach (char c in raw)
            {
                if (char.IsWhiteSpace(c)) { pendingSpace = builder.Length > 0; continue; }
                if (pendingSpace) builder.Append(' ');
                pendingSpace = false;
                builder.Append(char.ToUpperInvariant(c));
            }

            return builder.ToString();
        }
    }
}
