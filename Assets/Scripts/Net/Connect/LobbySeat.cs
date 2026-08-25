namespace Residue.Net.Connect
{
    /// <summary>
    /// One row of the lobby roster, as everybody in the lobby sees it.
    /// <para>
    /// A snapshot rather than a live handle. <see cref="LobbyRoom"/> rebuilds the whole list every
    /// time anything moves and raises <c>Changed</c>, so a screen can hold the list it was given for
    /// a frame without it changing underneath the draw. That is the same whole-snapshot argument
    /// <c>LabNetwork.PublishAll</c> makes: a roster is four rows, and incremental invalidation is
    /// where two screens start disagreeing about who is in the room.
    /// </para>
    /// <para>
    /// <b>What is deliberately not here:</b> the stable id. A display name is cosmetic and belongs on
    /// screen; a stable id is what a seat in the lab is keyed on, and no client has any use for
    /// another player's. The host keeps that mapping to itself — see
    /// <see cref="LobbyRoom.StableIdOf"/>.
    /// </para>
    /// </summary>
    public readonly struct LobbySeat
    {
        /// <summary>The NGO connection this row describes. The host is always <c>0</c>.</summary>
        public readonly ulong ClientId;

        /// <summary>Roster label, never an identity. Two players may perfectly well both be "Dave".</summary>
        public readonly string Name;

        public readonly bool Ready;

        /// <summary>True for the one player who can start the shift.</summary>
        public readonly bool IsHost;

        public LobbySeat(ulong clientId, string name, bool ready, bool isHost)
        {
            ClientId = clientId;
            Name = string.IsNullOrWhiteSpace(name) ? "Player" : name;
            Ready = ready;
            IsHost = isHost;
        }

        /// <summary>
        /// Field-wise, so <see cref="LobbyRoom"/> can tell a rebuild that changed something from one
        /// that changed nothing and skip the <c>Changed</c> event. A roster that raises an event at
        /// 4 Hz forever is a screen that rebuilds at 4 Hz forever.
        /// </summary>
        public bool Matches(in LobbySeat other) =>
            ClientId == other.ClientId &&
            Ready == other.Ready &&
            IsHost == other.IsHost &&
            string.Equals(Name, other.Name, System.StringComparison.Ordinal);

        public override string ToString() =>
            $"{Name} [{ClientId}]{(IsHost ? " host" : "")}{(Ready ? " ready" : "")}";
    }
}
