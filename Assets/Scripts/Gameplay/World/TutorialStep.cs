namespace Residue.Gameplay.World
{
    /// <summary>
    /// One thing the tutorial points at, in the order <c>TutorialObjectives</c> lists them.
    ///
    /// <para>
    /// <b>Every entry names an action the player takes, never a state of the lab.</b> That is not a
    /// naming preference: an objective is ticked by the signal the action already raises — a
    /// <see cref="LabCommandKind"/> the host accepted, or <c>LabState.RunCompleted</c> — so a step
    /// phrased as a condition would have nothing to listen to and would have to be polled, which is
    /// how a tracker ends up disagreeing with the room it is describing.
    /// </para>
    ///
    /// <para>
    /// The order is the order they are drawn in and nothing else. No step gates any other, and the
    /// tracker will tick the last one first if that is what the player does — see
    /// <c>TutorialObjectives</c> for why an objective that had to be completed before the next thing
    /// worked would be #73's mistake wearing a tutorial's clothes.
    /// </para>
    /// </summary>
    public enum TutorialStep
    {
        /// <summary>Unset. Never appears in the script; it is what <c>Next</c> answers when there is
        /// nothing left to point at.</summary>
        None = 0,

        // -- Day one: the loop -------------------------------------------------------------------

        TakeACarton,
        OpenTheCarton,
        TakeAVial,
        LoadAnInstrument,
        StartTheRun,
        LetARunFinish,
        FileTheSlip,
        FileAVerdict,
        EndTheDay,

        // -- Day two: the two tells hard rule 3 rests on -----------------------------------------

        RunABlank,
        FillABottle,
        FlushAnInstrument,
        RunAStandard,
        Recalibrate
    }
}
