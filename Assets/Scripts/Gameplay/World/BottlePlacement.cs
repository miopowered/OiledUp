using Residue.Chemistry;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// One solvent bottle, as the room needs it: which bottle, how many flushes are in it, and where.
    /// <para>
    /// The vocabulary <see cref="BottleFeed"/> speaks, and deliberately not <c>Residue.Net</c>'s
    /// <c>SolventBottleView</c> — for the reason <see cref="VialPlacement"/> gives at length.
    /// <c>Residue.Gameplay</c> cannot see the netcode layer, so the replicated record is translated
    /// into this at the boundary and everything downstream is the same code a host runs.
    /// </para>
    /// It carries the charge count rather than a fill fraction because a charge is what the player
    /// spends. A prompt that said "63% full" would be asking them to do the division.
    /// </summary>
    public readonly struct BottlePlacement
    {
        public readonly string Id;

        public readonly int Charges;

        public readonly int Capacity;

        /// <summary>The host's own record of where this bottle is.</summary>
        public readonly SampleLocation Location;

        public BottlePlacement(string id, int charges, int capacity, SampleLocation location)
        {
            Id = id;
            Charges = charges;
            Capacity = capacity;
            Location = location;
        }

        public override string ToString() => $"{Id} [{Charges}/{Capacity}] {Location}";
    }
}
