using System.Collections.Generic;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Where the bottles come from on a process that does not simulate.
    /// <para>
    /// The third static seam, and the same shape as the two that came before it:
    /// <see cref="LabCommands.Router"/> is how an action leaves, <see cref="LabView.Replicated"/> is
    /// how the instruments are read, and this is how the physical bottles are. In every case
    /// <c>Residue.Gameplay</c> declares the shape and <c>Residue.Net</c> fills it in, because the
    /// assembly dependency runs the other way and that direction is what keeps ground truth off the
    /// wire (CLAUDE.md's assembly diagram).
    /// </para>
    /// <para>
    /// <b>Pull, not push.</b> The host republishes on a 4 Hz clock and again on every accepted
    /// command, so a push would land at times the world layer has no say over — mid-callback, or
    /// between a player releasing a vial and the socket it is going into being decided.
    /// <see cref="VialReconciler"/> asks once per frame from <see cref="LabRuntime"/>'s own
    /// <c>Update</c>, which is one ordering to reason about instead of two.
    /// </para>
    /// Both fields are null in single player and on a host: a process that simulates spawns its own
    /// props from its own <c>LabState</c>, and reading a snapshot of what it just published back would
    /// be a second prop system fighting the first.
    /// </summary>
    public static class VialFeed
    {
        /// <summary>
        /// Fill <paramref name="into"/> with every bottle this process can see and return true, or
        /// return false when there are none to read — a host, single player, or a client whose session
        /// has not spawned yet.
        /// <para>
        /// False and "an empty list" are different answers on purpose. An empty list means the lab
        /// genuinely has no bottles in it and anything left over should be destroyed; false means this
        /// process is not the one being told, and nothing should be touched at all.
        /// </para>
        /// </summary>
        public delegate bool Snapshot(List<VialPlacement> into);

        /// <summary>Installed by <c>Residue.Net</c> at startup. Null in an Editor-only test run.</summary>
        public static Snapshot Source;

        /// <summary>
        /// How to find another player's hands, for a bottle whose location says somebody is holding it.
        /// </summary>
        public static IPlayerHands Hands;
    }
}
