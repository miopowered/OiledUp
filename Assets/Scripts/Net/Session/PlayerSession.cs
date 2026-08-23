using System;

namespace Residue.Net.Session
{
    /// <summary>
    /// The host's record of one player, keyed on their <see cref="IPlayerIdentity.StableId"/> and
    /// outliving any single connection. This is the whole of what §M4's rejoin restores.
    ///
    /// <para>
    /// <b>It holds only what is not already shared.</b> The vault, instrument occupancy, the economy,
    /// the contract and the day clock live on the host in <c>LabState</c> regardless of who is
    /// connected, so a reconnecting player does not restore them — they are simply still there. What
    /// dies with a connection, and only that, is on this record:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="Held"/> — the one thing in their hands (§2.6).</description></item>
    /// <item><description><see cref="Pose"/> — where they stood and looked, the one thing §3.1 gives
    /// a client authority over.</description></item>
    /// <item><description><see cref="PendingAction"/> — a part-finished hold.</description></item>
    /// </list>
    ///
    /// <para>
    /// Deliberately absent, because it is shared state with exactly one owner elsewhere: sample state
    /// and locations (<c>SampleRegistry</c>), what is loaded in which instrument and how far through
    /// a run it is (<c>MachineInstance</c>), money, reputation and consumables (<c>Economy</c>), the
    /// day number and the shift clock (<c>LabState</c>), and every verdict and consequence. Copying
    /// any of those here would create a second set of books that a disconnect could desync, which is
    /// the failure the derived-stage argument in <c>SampleLifecycle</c> already makes at length.
    /// </para>
    ///
    /// <para>
    /// Absent by hard rule 2: <c>SampleGroundTruth</c>. This type is in <c>Residue.Net</c> and is
    /// therefore on the client's side of the wall by construction; it names a <c>SampleId</c> and
    /// nothing else about a sample.
    /// </para>
    ///
    /// <para>
    /// <b>What happens to a carried vial when its holder drops.</b>
    /// </para>
    /// <para>
    /// <b>It goes straight back to the sample rack, and the session keeps a note of where it went.</b>
    /// The note is <see cref="ReleasedOnDisconnect"/>; the move itself is the host's to perform, via
    /// <c>SessionRegistry.ItemReleased</c>.
    /// </para>
    /// <para>
    /// The alternative that looks kindest — reserving it to them — is the one that softlocks the lab.
    /// Everyone has one pair of hands and the instruments block, so a vial nobody is allowed to touch
    /// is a sample nobody can run, and a reservation has no natural expiry: a player who never comes
    /// back holds it for the rest of the contract. If it happens to be the sample a due verdict turns
    /// on, or the last one a customer is waiting for, three players lose a day's work to a fourth
    /// player's router. Hard rule 3 settles it — the remaining players could not have checked for
    /// that and cannot clear it.
    /// </para>
    /// <para>
    /// Dropping it on the floor keeps it reachable in principle and unreachable in practice.
    /// <c>Carryable.AttachTo</c> parents a carried prop to the hand socket with its colliders off and
    /// its rigidbody kinematic; handing it back to physics at whatever transform the disconnecting
    /// player last replicated means it can land inside a machine, behind the fume hood, or — if the
    /// drop happened during a load — at the world origin. A vial under the floor is the same softlock
    /// as a reserved one, minus any way for the player to reason about it.
    /// </para>
    /// <para>
    /// The rack is deterministic, always reachable, and already where an idle vial belongs.
    /// <c>SampleLocation.OnSurface</c> describes it, <c>SampleRack</c> exists, and §5.1 makes the move
    /// free of lifecycle consequence — only leaving the delivery crate is a stage change, every later
    /// move is a shelf change — so returning a vial can never corrupt a record whatever stage it had
    /// reached. The cost to the dropped player is the walk back from wherever they respawn to the
    /// rack, which is precisely the §5.5 cost the game already charges everyone for moving things
    /// around a room.
    /// </para>
    /// <para>
    /// Printouts and manuals follow the same rule to the same fixture logic — tray and shelf
    /// respectively — so there is one sentence to remember rather than three.
    /// </para>
    /// <para>
    /// <b>The rejoining player is told, not handed.</b> <see cref="ReleasedOnDisconnect"/> restores a
    /// note ("your ES-4471 is back in the rack"), never the object. Between the drop and the return
    /// another player may perfectly legitimately have taken that vial, run it, or filed on it, and
    /// re-materialising it in the returner's hands would either duplicate the sample or take it out
    /// of somebody else's grip mid-task. Neither is recoverable; a sentence is.
    /// </para>
    /// </summary>
    public sealed class PlayerSession
    {
        /// <summary>Sentinel for "no connection". NGO client ids start at 0, so 0 cannot be it.</summary>
        public const ulong NoClientId = ulong.MaxValue;

        /// <summary>The identity this record is keyed on. Never changes for the life of the session.</summary>
        public string StableId { get; }

        /// <summary>
        /// The connection currently bound to this record, or <see cref="NoClientId"/> while absent.
        /// Rebound on every reconnect, because NGO hands out a different one each time — and reuses
        /// the old one for whoever connects next.
        /// </summary>
        public ulong ClientId { get; private set; } = NoClientId;

        public bool IsConnected => ClientId != NoClientId;

        /// <summary>How many times this identity has connected. 1 on a first join, 2+ on a rejoin.</summary>
        public int JoinCount { get; private set; }

        /// <summary>Roster label. Cosmetic — see <see cref="IPlayerIdentity.DisplayName"/>.</summary>
        public string DisplayName { get; set; }

        public double LastJoinedAtSeconds { get; private set; }

        /// <summary>When the connection was lost, or -1 if it never has been.</summary>
        public double DisconnectedAtSeconds { get; private set; } = -1d;

        // -- The three things a connection takes with it ----------------------------------------------

        public PlayerPose Pose { get; set; } = PlayerPose.Unknown;

        /// <summary>
        /// What is in their hands right now. Cleared on disconnect — see the type doc; the item is
        /// returned to the world and the memory of it moves to <see cref="ReleasedOnDisconnect"/>.
        /// </summary>
        public HeldItem Held { get; set; } = HeldItem.None;

        public HeldAction PendingAction { get; set; } = HeldAction.None;

        /// <summary>
        /// What was taken out of their hands when they dropped, kept so the host can tell them where
        /// it went. A note, not a claim: nothing here reserves anything.
        /// </summary>
        public HeldItem ReleasedOnDisconnect { get; private set; } = HeldItem.None;

        public PlayerSession(string stableId, string displayName = null)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException("A session needs a stable id.", nameof(stableId));

            StableId = stableId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName;
        }

        /// <summary>Seconds since the connection was lost, or 0 while connected.</summary>
        public double AbsentSeconds(double nowSeconds) =>
            IsConnected || DisconnectedAtSeconds < 0d
                ? 0d
                : Math.Max(0d, nowSeconds - DisconnectedAtSeconds);

        /// <summary>
        /// Clear the "here is where your vial went" note once the player has been shown it, so the
        /// message does not follow them through every subsequent reconnect.
        /// </summary>
        public void AcknowledgeRelease() => ReleasedOnDisconnect = HeldItem.None;

        // -- Connection state. Registry-only, so the two indexes cannot drift apart. -------------------

        internal void Bind(ulong clientId, double nowSeconds)
        {
            ClientId = clientId;
            LastJoinedAtSeconds = nowSeconds;
            DisconnectedAtSeconds = -1d;
            JoinCount++;
        }

        /// <summary>
        /// Mark absent and empty the hands. Returns what was released so the registry can announce
        /// it; the caller is what actually puts the vial back in the rack.
        /// </summary>
        internal HeldItem Unbind(double nowSeconds)
        {
            ClientId = NoClientId;
            DisconnectedAtSeconds = nowSeconds;

            var released = Held;
            Held = HeldItem.None;
            if (!released.IsEmpty) ReleasedOnDisconnect = released;

            // A hold is not resumable across a gap by itself — see HeldAction. The seconds are kept
            // so the host can decide; only the hands are emptied here.
            return released;
        }

        public override string ToString() =>
            $"{DisplayName} [{StableId}] " +
            (IsConnected ? $"client {ClientId}" : "absent") +
            $", holding {Held}";
    }
}
