namespace Residue.Gameplay.World
{
    /// <summary>
    /// The installed <see cref="IRecordFeed"/>, or null when this process has a lab of its own.
    /// <para>
    /// Static for the same reason <see cref="LabView.Replicated"/> is: there is one lab and one
    /// process-wide answer to where its finished readings come from. Installed by
    /// <c>Residue.Net.LabNetwork</c> on spawn and cleared on despawn, so a screen that outlives the
    /// session goes back to drawing nothing rather than drawing a dead list.
    /// </para>
    /// </summary>
    public static class RecordFeed
    {
        public static IRecordFeed Source;

        /// <summary>
        /// True when this process reads its results off the wire. Sugar for the two screens that ask,
        /// and the one place to change the day a client also holds slips.
        /// </summary>
        public static bool IsReplicated => Source != null;
    }
}
