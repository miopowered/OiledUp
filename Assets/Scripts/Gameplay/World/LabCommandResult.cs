using Residue.Chemistry;
using Residue.Data;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// What the host made of a request: yes, or no and why.
    /// <para>
    /// The refusal is always a finished, player-facing sentence, because it is written by the
    /// gateway that did the refusing — <see cref="SampleLifecycle"/>,
    /// <see cref="Residue.Gameplay.Simulation.LabState.TryStartCalibration"/> and the rest already
    /// phrase these for the player, and §9 turns on the player being told what is wrong rather than
    /// being ignored. Nothing between here and the toast should reword one.
    /// </para>
    /// A result is delivered to the one player who asked, never broadcast. A refusal is a fact about
    /// somebody's hands and where they are standing; showing it to the rest of the lab would be noise
    /// at best and misleading at worst.
    /// </summary>
    public readonly struct LabCommandResult
    {
        public readonly bool Accepted;

        /// <summary>Player-facing reason. Never null when <see cref="Accepted"/> is false.</summary>
        public readonly string Refusal;

        /// <summary>
        /// The sample the command turned out to be about, for the actions that discover one rather
        /// than name one — taking a vial back out of an instrument, mainly. <see cref="SampleId.None"/>
        /// otherwise.
        /// </summary>
        public readonly SampleId Sample;

        private LabCommandResult(bool accepted, string refusal, SampleId sample)
        {
            Accepted = accepted;
            Refusal = refusal;
            Sample = sample;
        }

        public static readonly LabCommandResult Ok = new(true, null, SampleId.None);

        public static LabCommandResult Yes(SampleId sample) => new(true, null, sample);

        /// <summary>
        /// Refuse, with the reason as the player should read it. Falls back to a generic sentence
        /// rather than a null, so a gateway that forgets to fill one in still says something.
        /// </summary>
        public static LabCommandResult No(string refusal) =>
            new(false,
                string.IsNullOrEmpty(refusal) ? LabStrings.RefusedWithoutAReason.Text : refusal,
                SampleId.None);

        public override string ToString() => Accepted ? "accepted" : $"refused: {Refusal}";
    }
}
