using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Whoever is asking, from the validating side's point of view: what they are holding and where
    /// they are standing.
    /// <para>
    /// The point of the abstraction is that <see cref="LabCommandExecutor"/> must not care whether
    /// the request came from the player sitting at this machine or from somebody four hundred
    /// milliseconds away. Single player supplies the local <see cref="PlayerInteractor"/>; a host
    /// validating a client's request supplies an adapter over that client's session. The rules are
    /// then written once, and there is no "trusted" path through them.
    /// </para>
    /// <para>
    /// <b>None of this is a security boundary, and it is not meant to be.</b> §3.1 gives a client
    /// authority over its own transform, so a modified client can put itself anywhere it likes and
    /// <see cref="Position"/> will faithfully report the lie. What the checks buy is that a client
    /// cannot act on state it does not own: hands are tracked by the host, instrument occupancy is
    /// the host's, and every mutation still goes through the same gateway. Reach is there to stop a
    /// stale prompt or a lagged click operating an instrument in another room, not to stop a
    /// determined cheat in a four-player co-op game.
    /// </para>
    /// </summary>
    public interface ILabActor
    {
        /// <summary>
        /// The NGO client id this player is connected on, which is what
        /// <c>SampleLocation.Held</c> records. Zero for the local player, because NGO always gives
        /// the host id 0 and single player is a host with nobody connected.
        /// </summary>
        ulong ClientId { get; }

        /// <summary>Roster label, for log lines. Never shown as part of a refusal.</summary>
        string DisplayName { get; }

        /// <summary>
        /// False when this player has never reported a pose — a client mid-join. Reach is then not
        /// checked at all, because refusing everything until the first transform update lands would
        /// make a joining player's first few seconds silently broken.
        /// </summary>
        bool HasPosition { get; }

        Vector3 Position { get; }

        /// <summary>What the host believes is in their hands.</summary>
        LabGrip Grip { get; }

        /// <summary>
        /// The host has decided their hands now hold this. Called only from
        /// <see cref="LabCommandExecutor"/>, and only after the change has actually been made.
        /// </summary>
        void SetGrip(LabGrip grip);
    }
}
