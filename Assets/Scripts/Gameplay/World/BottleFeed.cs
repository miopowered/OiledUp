using System.Collections.Generic;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Where the solvent bottles come from on a process that does not simulate.
    /// <para>
    /// The fourth static seam, and the same shape as the three before it:
    /// <see cref="LabCommands.Router"/> is how an action leaves, <see cref="LabView.Replicated"/> is
    /// how the instruments are read, <see cref="VialFeed"/> is how the sample bottles are, and this is
    /// how the solvent ones are. In every case <c>Residue.Gameplay</c> declares the shape and
    /// <c>Residue.Net</c> fills it in, because the assembly dependency runs the other way and that
    /// direction is what keeps ground truth off the wire.
    /// </para>
    /// <para>
    /// <b>Kept apart from <see cref="VialFeed"/> rather than folded into it.</b> The two lists have
    /// nothing in common but a location: a vial carries a label, a volume and a record behind it, a
    /// solvent bottle carries a charge count. One list would mean a null half in every row and a
    /// "which kind is this" branch in every reader. <see cref="VialFeed.Hands"/> is shared, because
    /// whose hands are whose is genuinely one question.
    /// </para>
    /// Null in single player and on a host: a process that simulates reads its own
    /// <c>SolventStore</c> — see <see cref="BottleReconciler"/>, which is the one place that choice
    /// is made.
    /// </summary>
    public static class BottleFeed
    {
        /// <summary>
        /// Fill <paramref name="into"/> with every bottle this process can see and return true, or
        /// return false when this process is not the one being told.
        /// <para>
        /// False and "an empty list" are different answers, for the reason
        /// <see cref="VialFeed.Snapshot"/> gives: empty means the lab has no bottles and anything left
        /// over should go, false means nothing here should be touched at all.
        /// </para>
        /// </summary>
        public delegate bool Snapshot(List<BottlePlacement> into);

        /// <summary>Installed by <c>Residue.Net</c> at startup. Null in an Editor-only test run.</summary>
        public static Snapshot Source;
    }
}
