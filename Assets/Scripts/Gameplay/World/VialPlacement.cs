using Residue.Chemistry;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// One bottle, as the room needs it: which sample, what the paper says, how full, and where.
    /// <para>
    /// The vocabulary <see cref="VialFeed"/> speaks, and deliberately <i>not</i> <c>Residue.Net</c>'s
    /// <c>VialView</c>. <c>Residue.Gameplay</c> cannot see the netcode layer and must not — that
    /// direction is what keeps ground truth off the wire (CLAUDE.md's assembly diagram) — so the
    /// replicated record is translated into this at the boundary, and everything downstream of it is
    /// the same code on a host and on a client.
    /// </para>
    /// <para>
    /// <see cref="Label"/> is what is printed on the bottle, never what anybody typed. §5.1's mis-log
    /// is only a fair mechanic because walking back and reading the label is a tell the player can
    /// use, so the label has to reach a client — and nothing that reaches a <i>screen</i> may carry it,
    /// or a terminal could diff the two and hand the correction over. Hence a separate record from
    /// the one the screens read, all the way down.
    /// </para>
    /// </summary>
    public readonly struct VialPlacement
    {
        public readonly SampleId Sample;

        /// <summary>The tag the courier wrote on the bottle.</summary>
        public readonly string Label;

        public readonly float VolumeMl;

        /// <summary>The host's own record of where this bottle is.</summary>
        public readonly SampleLocation Location;

        public VialPlacement(SampleId sample, string label, float volumeMl, SampleLocation location)
        {
            Sample = sample;
            Label = label;
            VolumeMl = volumeMl;
            Location = location;
        }

        public override string ToString() => $"{Sample} [{Label}] {Location}";
    }
}
