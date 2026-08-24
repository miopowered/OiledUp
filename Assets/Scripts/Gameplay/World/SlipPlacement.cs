using Residue.Chemistry;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// One results slip, as the room needs it: which ticket, which reading, what is printed across
    /// the top, and where the paper is.
    /// <para>
    /// The vocabulary <see cref="SlipFeed"/> speaks, and deliberately <i>not</i> <c>Residue.Net</c>'s
    /// <c>SlipView</c> — the same split <see cref="VialPlacement"/> makes, for the same reason.
    /// <c>Residue.Gameplay</c> cannot see the netcode layer and must not (CLAUDE.md's assembly
    /// diagram), so the replicated record is translated at the boundary and everything downstream is
    /// the code a host runs too.
    /// </para>
    /// <para>
    /// <b>It names its reading rather than carrying one.</b> <see cref="ResultKey"/> is the identity
    /// <c>ResultView.Key</c> already hands a finished run, and the numbers travel once, in the list
    /// the terminal and the instrument screens already read. Putting a copy of the values on the slip
    /// as well would be a second wire path to the same figures — and the day the two disagreed, the
    /// paper in your hand and the panel at the desk would be quoting different results for one run.
    /// </para>
    /// </summary>
    public readonly struct SlipPlacement
    {
        /// <summary>The host's handle for this slip (<c>ResultSlips</c>). Identity for the prop.</summary>
        public readonly int Ticket;

        /// <summary>The run this slip reports, as <c>ResultView.Key</c>. Zero if it has not travelled.</summary>
        public readonly int ResultKey;

        /// <summary>The sample it belongs to, or <see cref="SampleId.None"/> for a blank or a standard.</summary>
        public readonly SampleId Sample;

        /// <summary>A solvent blank, which reads the instrument's own carryover rather than any oil.</summary>
        public readonly bool IsBlank;

        /// <summary>The instrument's display name, as printed at the head of the slip.</summary>
        public readonly string MachineName;

        /// <summary>
        /// What the lab calls the sample this reports on — the tag someone typed, never the one on
        /// the label (§5.1). An instrument does not read bottles; it prints the run under the name
        /// the record gave it, and a mis-logged vial prints under the tank the player named.
        /// </summary>
        public readonly string RecordTag;

        /// <summary>The host's own record of where the paper is.</summary>
        public readonly SampleLocation Location;

        public SlipPlacement(int ticket, int resultKey, SampleId sample, bool isBlank,
                             string machineName, string recordTag, SampleLocation location)
        {
            Ticket = ticket;
            ResultKey = resultKey;
            Sample = sample;
            IsBlank = isBlank;
            MachineName = machineName;
            RecordTag = recordTag;
            Location = location;
        }

        public override string ToString() => $"slip #{Ticket} [{RecordTag}] {Location}";
    }
}
