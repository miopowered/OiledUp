using Residue.Chemistry;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Where finished readings come from on a process that does not simulate.
    /// <para>
    /// The fourth static seam, and the same shape as the three before it:
    /// <see cref="LabCommands.Router"/> is how an action leaves, <see cref="LabView.Replicated"/> is
    /// how the instruments are read, <c>VialFeed.Source</c> is where the bottles are, and this is
    /// where the numbers are. <c>Residue.Gameplay</c> declares the shape and <c>Residue.Net</c>
    /// fills it in, because the assembly dependency runs the other way and that direction is what
    /// keeps ground truth off the wire (CLAUDE.md's assembly diagram).
    /// </para>
    /// <para>
    /// Null in single player and on a host, where the terminal reads its own <c>LabState</c> and the
    /// instrument screens are driven by the run-completed event. A process that simulates
    /// reading its own publish back would be a second, later copy of what it already has.
    /// </para>
    /// </summary>
    public interface IRecordFeed
    {
        /// <summary>
        /// Gather what the desk can see, or null when there is nothing to draw yet — a session that
        /// has spawned but not published, or a process with no content catalog to resolve ids against.
        /// Null and an empty lab are different answers: one is "wait", the other is "nothing here".
        /// </summary>
        LabRecords ReadLab();

        /// <summary>
        /// The most recent finished reading on one placed instrument, whatever kind of run produced
        /// it, and the sample it belonged to (<see cref="SampleId.None"/> for a blank or a standard).
        /// <para>
        /// This is what puts numbers back on an instrument's own screen for a player who is not
        /// hosting. It is pulled rather than pushed for the reason <see cref="VialFeed"/> gives: the
        /// host republishes on its own clock, and a screen that redrew on arrival would redraw at
        /// times the world layer has no say over.
        /// </para>
        /// </summary>
        bool TryLastReading(string machineInstanceId, out TestResult reading, out SampleId sample);
    }
}
