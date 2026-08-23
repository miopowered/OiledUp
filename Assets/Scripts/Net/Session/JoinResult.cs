namespace Residue.Net.Session
{
    /// <summary>What <see cref="SessionRegistry.Join"/> did. The host branches on this.</summary>
    public enum JoinOutcome
    {
        /// <summary>Never seen before. Spawn them at a spawn point, empty-handed.</summary>
        Created,

        /// <summary>Known identity, previously absent. Put them back where they were (§M4).</summary>
        Restored,

        /// <summary>
        /// Known identity that was already connected on another client id. The new connection wins
        /// and <see cref="JoinResult.DisplacedClientId"/> must be kicked.
        /// </summary>
        Displaced,

        /// <summary>No usable stable id. Refuse the connection rather than key a session on "".</summary>
        RejectedNoIdentity,

        /// <summary>The lab is full and this identity has no seat in it.</summary>
        RejectedLabFull
    }

    /// <summary>
    /// The answer to a connection request, in the form NGO's approval callback wants: a yes/no plus
    /// everything needed to act on a yes.
    /// </summary>
    public readonly struct JoinResult
    {
        public readonly JoinOutcome Outcome;

        /// <summary>Null when the join was refused.</summary>
        public readonly PlayerSession Session;

        /// <summary>
        /// The stale connection to disconnect, valid only for <see cref="JoinOutcome.Displaced"/>.
        /// Leaving it up would give one identity two bodies in the lab.
        /// </summary>
        public readonly ulong DisplacedClientId;

        private JoinResult(JoinOutcome outcome, PlayerSession session, ulong displacedClientId)
        {
            Outcome = outcome;
            Session = session;
            DisplacedClientId = displacedClientId;
        }

        public bool Accepted => Session != null;

        /// <summary>True when the caller must restore pose, tell them where their vial went, and so on.</summary>
        public bool IsRejoin => Outcome == JoinOutcome.Restored;

        internal static JoinResult Created(PlayerSession session) =>
            new(JoinOutcome.Created, session, PlayerSession.NoClientId);

        internal static JoinResult Restored(PlayerSession session) =>
            new(JoinOutcome.Restored, session, PlayerSession.NoClientId);

        internal static JoinResult Displaced(PlayerSession session, ulong displacedClientId) =>
            new(JoinOutcome.Displaced, session, displacedClientId);

        internal static JoinResult Rejected(JoinOutcome reason) =>
            new(reason, null, PlayerSession.NoClientId);

        /// <summary>A sentence for the connection-approval payload, or null when accepted.</summary>
        public string RefusalReason => Outcome switch
        {
            JoinOutcome.RejectedNoIdentity =>
                "Could not establish a player identity. Sign-in has not completed.",
            JoinOutcome.RejectedLabFull =>
                "The lab is full. A seat is held for anyone who dropped out of this run.",
            _ => null
        };

        public override string ToString() =>
            Accepted ? $"{Outcome}: {Session}" : $"{Outcome}: {RefusalReason}";
    }
}
