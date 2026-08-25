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
    /// <see cref="Label"/> is what is printed on the bottle, which since #73 is also what the record
    /// is filed under. It travels in this record rather than in the one the screens read because the
    /// two answer different questions and change at different rates — see <c>VialView</c>.
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
