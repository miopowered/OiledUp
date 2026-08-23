using System;
using UnityEngine;

namespace Residue.Net.Session
{
    /// <summary>
    /// A hold-to-act that was part way through when the connection went. §M4 names this as the third
    /// thing a session has to keep, alongside the hands and the pose.
    /// <para>
    /// It is on the list because of how long some of these are. A flush is 20 seconds of standing
    /// still; §9 wants that time to be the design rather than filler, which means losing it hurts
    /// exactly as much as it was supposed to. Twelve seconds into a flush is not a rounding error,
    /// it is most of the cost already paid.
    /// </para>
    /// <b>Resuming is offered, not applied.</b> The registry stores the progress; whether it may be
    /// picked up again is a question about the <i>target</i> — has the instrument been flushed by
    /// somebody else, has it started a run, is the vial still in it — and only the host's machine
    /// state can answer that. Restoring progress against a target that has since changed underneath
    /// it would give away a flush that never happened, which is the same class of unfairness hard
    /// rule 3 forbids in the other direction.
    /// </summary>
    [Serializable]
    public readonly struct HeldAction : IEquatable<HeldAction>
    {
        /// <summary>What was being done — "flush", "agitate", "calibrate". Null when nothing was.</summary>
        public readonly string ActionId;

        /// <summary>
        /// What it was being done to: a machine instance id, a sample id's string form, whatever the
        /// caller's action vocabulary uses. Compared verbatim when deciding whether a resume is
        /// still pointed at the same thing.
        /// </summary>
        public readonly string TargetId;

        public readonly float ElapsedSeconds;
        public readonly float RequiredSeconds;

        public HeldAction(string actionId, string targetId, float elapsedSeconds, float requiredSeconds)
        {
            ActionId = string.IsNullOrEmpty(actionId) ? null : actionId;
            TargetId = string.IsNullOrEmpty(targetId) ? null : targetId;
            ElapsedSeconds = Mathf.Max(0f, elapsedSeconds);
            RequiredSeconds = Mathf.Max(0f, requiredSeconds);
        }

        public static readonly HeldAction None = default;

        public bool IsEmpty => ActionId == null;

        /// <summary>Progress for the HUD ring, 0..1. Zero for an empty action.</summary>
        public float Progress01 =>
            RequiredSeconds > 0f ? Mathf.Clamp01(ElapsedSeconds / RequiredSeconds) : 0f;

        /// <summary>
        /// True if this progress is still about the same job on the same thing. The host asks this
        /// before handing the seconds back; a mismatch means the world moved on and the hold starts
        /// from zero.
        /// </summary>
        public bool Matches(string actionId, string targetId) =>
            !IsEmpty &&
            string.Equals(ActionId, actionId, StringComparison.Ordinal) &&
            string.Equals(TargetId, targetId, StringComparison.Ordinal);

        public bool Equals(HeldAction other) =>
            string.Equals(ActionId, other.ActionId, StringComparison.Ordinal) &&
            string.Equals(TargetId, other.TargetId, StringComparison.Ordinal) &&
            Mathf.Approximately(ElapsedSeconds, other.ElapsedSeconds) &&
            Mathf.Approximately(RequiredSeconds, other.RequiredSeconds);

        public override bool Equals(object obj) => obj is HeldAction o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ActionId != null ? ActionId.GetHashCode() : 0;
                hash = (hash * 397) ^ (TargetId != null ? TargetId.GetHashCode() : 0);
                hash = (hash * 397) ^ ElapsedSeconds.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(HeldAction a, HeldAction b) => a.Equals(b);
        public static bool operator !=(HeldAction a, HeldAction b) => !a.Equals(b);

        public override string ToString() => IsEmpty
            ? "no held action"
            : $"{ActionId}({TargetId}) {ElapsedSeconds:F1}/{RequiredSeconds:F1}s";
    }
}
