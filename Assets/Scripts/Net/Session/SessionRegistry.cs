using System;
using System.Collections.Generic;
using UnityEngine;

namespace Residue.Net.Session
{
    /// <summary>
    /// The host's roster: one <see cref="PlayerSession"/> per stable identity, and a second index
    /// from the NGO client id that identity is currently connected on.
    ///
    /// <para>
    /// <b>Two indexes, and only one of them is authoritative.</b> Sessions are keyed on the stable
    /// id; the client-id map is a lookup that is torn down the instant a connection ends. That
    /// asymmetry is the entire point of the type. NGO allocates client ids per connection and reuses
    /// them freely, so a registry keyed on the client id would hand the second player to occupy
    /// client 2 the first player's hands, pose and half-finished flush. Everything below is arranged
    /// so that cannot happen: <see cref="Disconnect"/> removes the client-id entry before it does
    /// anything else, and nothing ever writes a client-id entry except <see cref="Bind"/>.
    /// </para>
    ///
    /// <para>
    /// A plain C# class with no <c>NetworkBehaviour</c> and no Unity lifecycle, for the same reason
    /// <c>LabState</c> is one: the whole join/drop/rejoin sequence has to be steppable in an edit-mode
    /// test, where there is no transport and no frame loop. The wiring to NGO's callbacks lives
    /// outside; see the remarks on <see cref="Join"/> and <see cref="Disconnect"/> for what to call
    /// and when.
    /// </para>
    ///
    /// <para>
    /// It never sees the chemistry. Sessions name a <c>SampleId</c> at most, so no path from here
    /// reaches <c>SampleGroundTruth</c> (hard rule 2).
    /// </para>
    /// </summary>
    public sealed class SessionRegistry
    {
        /// <summary>§M4 acceptance is four players.</summary>
        public const int DefaultCapacity = 4;

        private readonly Dictionary<string, PlayerSession> byStableId =
            new(StringComparer.Ordinal);

        private readonly Dictionary<ulong, PlayerSession> byClientId = new();

        /// <summary>
        /// Seats in the lab. Counted against <i>all</i> sessions, not just connected ones — an absent
        /// player's seat is held for them. Without that, the fourth player to drop out gets locked
        /// out of their own run by a stranger who joined during the reconnect, which is the failure
        /// rejoin exists to prevent. <see cref="Forget"/> is how a seat is given up.
        /// </summary>
        public int Capacity { get; }

        public SessionRegistry(int capacity = DefaultCapacity)
        {
            Capacity = Math.Max(1, capacity);
        }

        /// <summary>A player has connected for the first time this run.</summary>
        public event Action<PlayerSession> Joined;

        /// <summary>A previously absent player is back. Restore pose and show them the release note.</summary>
        public event Action<PlayerSession> Rejoined;

        /// <summary>A player has dropped. Their session is still here, marked absent.</summary>
        public event Action<PlayerSession> Left;

        /// <summary>
        /// Something has just left a disconnected player's hands and needs putting back in the world.
        /// Raised before <see cref="Left"/>, so a handler for both sees the release first.
        /// <para>
        /// The registry knows nothing about racks or trays, so it announces rather than acts. See
        /// <see cref="PlayerSession"/> for the argument about where the vial should land.
        /// </para>
        /// </summary>
        public event Action<PlayerSession, HeldItem> ItemReleased;

        // -- Reading the roster -------------------------------------------------------------------------

        /// <summary>Everyone, connected or not. Enumeration order is unspecified.</summary>
        public IReadOnlyCollection<PlayerSession> All => byStableId.Values;

        /// <summary>
        /// Only the players who are actually here. This is the collection to iterate for anything
        /// that sends — an absent session has no client id to send to.
        /// </summary>
        public IReadOnlyCollection<PlayerSession> Connected => byClientId.Values;

        public int ConnectedCount => byClientId.Count;

        public int SeatsTaken => byStableId.Count;

        public bool HasFreeSeat => byStableId.Count < Capacity;

        /// <summary>Players with a session but no connection — the ones rejoin is holding a seat for.</summary>
        public List<PlayerSession> Absent()
        {
            var absent = new List<PlayerSession>();
            foreach (var session in byStableId.Values)
            {
                if (!session.IsConnected) absent.Add(session);
            }
            return absent;
        }

        /// <summary>
        /// The session on a live connection. False for a client id that has disconnected, <i>even if
        /// the same number is later reissued</i> — the caller then gets the new occupant, never the
        /// old one.
        /// </summary>
        public bool TryGet(ulong clientId, out PlayerSession session) =>
            byClientId.TryGetValue(clientId, out session);

        public PlayerSession Get(ulong clientId) =>
            byClientId.TryGetValue(clientId, out var s) ? s : null;

        /// <summary>The session for an identity, connected or absent.</summary>
        public bool TryGetByStableId(string stableId, out PlayerSession session)
        {
            session = null;
            string key = Normalise(stableId);
            return key != null && byStableId.TryGetValue(key, out session);
        }

        // -- Connection lifecycle -----------------------------------------------------------------------

        /// <summary>
        /// Admit a connection. Call this from NGO's connection-approval callback, or from
        /// <c>OnClientConnectedCallback</c> once the identity has arrived in the connection payload —
        /// but call it exactly once per connection, before anything spawns a body for that client.
        /// <para>
        /// The identity must come from the client's <see cref="IPlayerIdentity"/> over the connection
        /// payload. It is not verified here; a stable id is a convenience, not a credential, and
        /// pretending otherwise would be security theatre in a four-player co-op game.
        /// </para>
        /// <para>
        /// <see cref="JoinOutcome.Displaced"/> raises neither <see cref="Joined"/> nor
        /// <see cref="Rejoined"/>: the roster did not change, only which socket one of its entries is
        /// answering on. The host acts on <see cref="JoinResult.DisplacedClientId"/> instead.
        /// </para>
        /// </summary>
        /// <param name="stableId">
        /// The <see cref="IPlayerIdentity.StableId"/> the client presented. Empty is refused rather
        /// than defaulted, because every client that failed to sign in would otherwise share one
        /// session.
        /// </param>
        /// <param name="clientId">The NGO client id this connection was allocated.</param>
        /// <param name="nowSeconds">
        /// Any monotonic clock the host controls — <c>Time.realtimeSinceStartupAsDouble</c> in the
        /// game, a plain counter in a test. Passed in rather than read so the registry stays
        /// steppable off a frame loop.
        /// </param>
        public JoinResult Join(string stableId, ulong clientId, double nowSeconds)
        {
            string key = Normalise(stableId);
            if (key == null) return JoinResult.Rejected(JoinOutcome.RejectedNoIdentity);

            if (byStableId.TryGetValue(key, out var existing))
            {
                // Same identity, second connection. The newest one wins: the old connection is by
                // definition the one that is not talking to us any more (a duplicate sign-in, or a
                // drop the transport has not noticed yet), and leaving it bound would give one
                // player two bodies and two pairs of hands.
                if (existing.IsConnected)
                {
                    ulong stale = existing.ClientId;
                    if (stale == clientId) return JoinResult.Displaced(existing, PlayerSession.NoClientId);

                    byClientId.Remove(stale);
                    Bind(existing, clientId, nowSeconds);
                    return JoinResult.Displaced(existing, stale);
                }

                Bind(existing, clientId, nowSeconds);
                Rejoined?.Invoke(existing);
                return JoinResult.Restored(existing);
            }

            if (!HasFreeSeat) return JoinResult.Rejected(JoinOutcome.RejectedLabFull);

            var session = new PlayerSession(key);
            byStableId[key] = session;
            Bind(session, clientId, nowSeconds);
            Joined?.Invoke(session);
            return JoinResult.Created(session);
        }

        /// <summary>
        /// A connection has ended. Call from <c>OnClientDisconnectCallback</c>, before despawning
        /// anything the player owned — the released item is announced from here and the handler needs
        /// the world to still be intact when it puts the vial back.
        /// <para>
        /// The session survives; only the binding to the client id goes. Returns the session, or null
        /// if the client id was never bound (which is the normal case for a connection refused at
        /// approval).
        /// </para>
        /// </summary>
        public PlayerSession Disconnect(ulong clientId, double nowSeconds)
        {
            if (!byClientId.TryGetValue(clientId, out var session)) return null;

            // First, and unconditionally. Everything downstream of a reused client id depends on
            // there being no stale entry left pointing at this session.
            byClientId.Remove(clientId);

            var released = session.Unbind(nowSeconds);
            if (!released.IsEmpty) ItemReleased?.Invoke(session, released);

            Left?.Invoke(session);
            return session;
        }

        /// <summary>
        /// Give up a seat for good — a deliberate quit, or a host decision that someone is not coming
        /// back. Anything still in that session's hands is released first, so leaving cannot strand a
        /// vial any more than dropping can.
        /// </summary>
        public bool Forget(string stableId)
        {
            string key = Normalise(stableId);
            if (key == null || !byStableId.TryGetValue(key, out var session)) return false;

            // Routed through Disconnect so the release rule has one implementation. The timestamp is
            // moot — the record is about to stop existing, so nothing will ever read its absence.
            if (session.IsConnected) Disconnect(session.ClientId, session.LastJoinedAtSeconds);

            byStableId.Remove(key);
            return true;
        }

        /// <summary>Wipe the roster. For ending a run, not for ending a day.</summary>
        public void Clear()
        {
            byStableId.Clear();
            byClientId.Clear();
        }

        private void Bind(PlayerSession session, ulong clientId, double nowSeconds)
        {
            // Defensive, and loud. Reaching here means a disconnect was missed: the old session is
            // about to become unreachable by client id, and anything the transport sends on that
            // number would land on the wrong player. Silently evicting it would hide exactly the
            // class of bug this type exists to make impossible.
            if (byClientId.TryGetValue(clientId, out var squatter) && squatter != session)
            {
                Debug.LogWarning(
                    $"[SessionRegistry] Client {clientId} was still bound to {squatter.StableId} when " +
                    $"{session.StableId} joined on it. A disconnect callback was missed.");

                var stranded = squatter.Unbind(nowSeconds);
                if (!stranded.IsEmpty) ItemReleased?.Invoke(squatter, stranded);
                Left?.Invoke(squatter);
            }

            session.Bind(clientId, nowSeconds);
            byClientId[clientId] = session;
        }

        private static string Normalise(string stableId) =>
            string.IsNullOrWhiteSpace(stableId) ? null : stableId.Trim();
    }
}
