using System.Collections.Generic;
using Residue.Chemistry;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Where the results slips come from on a process that does not simulate.
    /// <para>
    /// The fifth static seam, and the same shape as the four before it:
    /// <see cref="LabCommands.Router"/> is how an action leaves, <see cref="LabView.Replicated"/> is
    /// how the instruments are read, <see cref="VialFeed"/> is where the bottles are,
    /// <see cref="RecordFeed"/> is where the numbers are, and this is where the paper is. In every
    /// case <c>Residue.Gameplay</c> declares the shape and <c>Residue.Net</c> fills it in, because the
    /// assembly dependency runs the other way and that direction is what keeps ground truth off the
    /// wire (CLAUDE.md's assembly diagram).
    /// </para>
    /// <para>
    /// <b>Why this exists at all.</b> A slip is spawned host-side when a run finishes, and nothing
    /// replicated it — so a client's instrument tray was empty. It could read the numbers off the
    /// machine's screen and not carry them to the desk, which made <i>filing a result</i> host-only:
    /// a hole in the middle of the co-op loop, because two players run instruments in parallel and
    /// only one of them could do the paperwork.
    /// </para>
    /// Both members are null in single player and on a host: a process that simulates prints its own
    /// slips as its own runs finish, and reading a snapshot of what it just published back would be a
    /// second prop system fighting the first.
    /// </summary>
    public static class SlipFeed
    {
        /// <summary>
        /// Fill <paramref name="into"/> with every slip this process can see and return true, or
        /// return false when there are none to read — a host, single player, or a client whose
        /// session has not spawned yet.
        /// <para>
        /// False and "an empty list" are different answers on purpose, exactly as in
        /// <see cref="VialFeed.Snapshot"/>. An empty list means the lab has no outstanding paperwork
        /// and any slip still lying around is one somebody filed; false means this process is not the
        /// one being told, and nothing should be touched at all.
        /// </para>
        /// </summary>
        public delegate bool Snapshot(List<SlipPlacement> into);

        /// <summary>Installed by <c>Residue.Net</c> at startup. Null in an Editor-only test run.</summary>
        public static Snapshot Source;

        /// <summary>
        /// Look up the run a slip names, by <c>ResultView.Key</c>.
        /// <para>
        /// Pulled on demand rather than pushed onto the prop, because the only thing that ever asks
        /// is a player glancing at the paper in their hands — and rebuilding a
        /// <see cref="TestResult"/> for every slip in the lab, four times a second, to serve a
        /// keypress nobody may press is the wrong trade by two orders of magnitude.
        /// </para>
        /// </summary>
        public delegate bool Reading(int resultKey, out TestResult result);

        /// <summary>Installed alongside <see cref="Source"/>. Null wherever that is.</summary>
        public static Reading Numbers;
    }
}
