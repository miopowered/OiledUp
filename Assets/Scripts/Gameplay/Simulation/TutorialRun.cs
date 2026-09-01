namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// The one fact the menu has to get across the scene load: this shift is the tutorial.
    ///
    /// <para>
    /// A latch rather than an argument for exactly the reason <see cref="RunSaveSlot.RequestContinue"/>
    /// is one — the lab is reached through a scene load and the component that builds the run wakes on
    /// the other side of it, with nothing to hand it a parameter. Same shape, same reason, and
    /// deliberately the same idiom so the two read as one mechanism rather than two.
    /// </para>
    ///
    /// <para>
    /// <b>Mutually exclusive with CONTINUE, and the menu is what enforces it.</b> Every entry point in
    /// <c>LabConnection</c> clears whichever latch it is not setting, so a player who backs out of the
    /// tutorial and presses CONTINUE does not get a saved run rebuilt against a two-day contract.
    /// <c>LabRuntime</c> takes them in order and only builds a tutorial when the save layer did not
    /// already produce a lab.
    /// </para>
    ///
    /// <para>
    /// Nothing here is per-player and nothing here goes on the wire. The tutorial is single player: it
    /// is a fixed seed and a fixed contract, and a lobby whose host quietly started one would hand
    /// three other people a scripted two-day shift they did not ask for.
    /// </para>
    /// </summary>
    public static class TutorialRun
    {
        private static bool requested;

        /// <summary>The player picked TUTORIAL and the lab scene has not loaded yet.</summary>
        public static void Request() => requested = true;

        /// <summary>Read the latch and clear it, so a later NEW SHIFT cannot inherit it.</summary>
        public static bool TakeRequest()
        {
            bool taken = requested;
            requested = false;
            return taken;
        }

        /// <summary>Drop a pending request without acting on it — for a path back to the menu.</summary>
        public static void Forget() => requested = false;
    }
}
