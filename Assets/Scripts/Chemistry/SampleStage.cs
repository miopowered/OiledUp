namespace Residue.Chemistry
{
    /// <summary>
    /// How far a sample has got along the §5.1 chain: crate → unload → log → prep → instrument →
    /// results → verdict → archive → consequence.
    /// <para>
    /// Declared in order so stages compare with <c>&lt;</c> and <c>&gt;</c>. Several rules are
    /// naturally "anything before a verdict is filed", and expressing those as a comparison keeps
    /// them from drifting apart when a stage is inserted later.
    /// </para>
    /// Note what is deliberately absent. <b>Storage is not a stage</b> — §5.1 branches to
    /// <c>[fridge | bench]</c> between logging and prep, and which one you chose is a
    /// <see cref="SampleLocation"/>, not a step. Nor is "in an instrument": a sample visits several
    /// machines and would have to travel backwards through any stage that encoded occupancy.
    /// </summary>
    public enum SampleStage
    {
        /// <summary>Arrived on this morning's delivery and still in the crate.</summary>
        InCrate,

        /// <summary>Out of the crate, with nothing on file against it yet.</summary>
        Unpacked,

        /// <summary>Registered at the terminal under a tank tag — the player's, which may be wrong (§5.1).</summary>
        Logged,

        /// <summary>Agitated back to homogeneous, so an instrument will accept it.</summary>
        Prepped,

        /// <summary>At least one printout has been walked to the terminal and filed.</summary>
        Measured,

        /// <summary>A verdict has been filed. The record is closed and waiting on reality.</summary>
        Archived,

        /// <summary>The consequence has landed and been reported back to the player (§5.4).</summary>
        Resolved
    }
}
