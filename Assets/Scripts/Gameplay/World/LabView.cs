namespace Residue.Gameplay.World
{
    /// <summary>
    /// Where the world layer looks up what it is drawing.
    /// <para>
    /// The read-side twin of <see cref="LabCommands"/>, and deliberately the same shape. Actions go
    /// out through one static seam that knows whether "ask the server" is a round trip or a method
    /// call; reads come in through one static seam that knows whether "the lab" is a live
    /// <c>LabState</c> or a set of replicated snapshots. Every station, button, screen and HUD in the
    /// game reads <see cref="Current"/> and none of them contains a branch on session state.
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Single player.</b> <see cref="LabRuntime"/> installs <see cref="Host"/>
    /// when it builds the lab, and <see cref="Replicated"/> is never set. Nothing changes: the
    /// adapters forward straight to the objects the world layer used to hold directly.</description></item>
    /// <item><description><b>Host.</b> Identical. A host simulates, so it reads its own lab — the
    /// replicated views it publishes are for other people.</description></item>
    /// <item><description><b>Client.</b> No lab is built, so <see cref="Host"/> stays null and
    /// <c>Residue.Net</c> installs <see cref="Replicated"/> on spawn. The instruments in the room
    /// become readable, and everything that was previously switched off comes back.</description></item>
    /// </list>
    /// <para>
    /// Static for the same reason <see cref="LabCommands.Router"/> is: there is one lab and one
    /// process-wide answer to "is this process simulating". <c>Residue.Gameplay</c> cannot see
    /// <c>Residue.Net</c> and must not (CLAUDE.md's assembly diagram), so the dependency is inverted
    /// through <see cref="Replicated"/>.
    /// </para>
    /// </summary>
    public static class LabView
    {
        /// <summary>
        /// This process's own lab, or null if it does not simulate. Installed by
        /// <see cref="LabRuntime"/> when it builds a <c>LabState</c>, and cleared when it tears one down.
        /// </summary>
        public static ILabView Host;

        /// <summary>
        /// The replicated lab, or null when there is no session. Installed by
        /// <c>Residue.Net.LabNetwork</c> on spawn and cleared on despawn.
        /// </summary>
        public static ILabView Replicated;

        /// <summary>
        /// Whichever view this process has, or null before either is installed.
        /// <para>
        /// The host wins when both are present. A host does have replicated views — it writes them —
        /// but reading its own snapshot back would put its screens a publish behind its own lab for no
        /// reason at all.
        /// </para>
        /// Null is a real answer and every caller handles it: there is a window during scene load
        /// where a client has stations in the room and no <c>LabNetwork</c> spawned yet.
        /// </summary>
        public static ILabView Current => Host ?? Replicated;

        // VialsAreHostOnly and VialsMissingHere used to live here: the one sentence a crate, a rack or
        // an instrument said to a player reaching for a bottle that had not travelled. They are gone
        // because the bottles travel — see VialFeed and VialReconciler — and there is nothing left to
        // apologise for. Deleting the sentence was always the plan; this is the change that earned it.
    }
}
