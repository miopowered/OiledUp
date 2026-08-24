using Residue.Chemistry;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// One solvent bottle, host-side: how many flushes are left in it and where in the room it is.
    /// <para>
    /// §5.2 puts the flush at the instrument, because that is where the carryover is, and §5.5 puts
    /// the solvent at a wash station on the other side of the room. A bottle is what joins the two —
    /// and it has to be a physical thing rather than a counter, or the walk between them is a number
    /// going down instead of a trip somebody has to make.
    /// </para>
    /// <para>
    /// It reuses <see cref="SampleLocation"/> rather than inventing a location vocabulary of its own.
    /// The type is named for samples but says nothing about them: it is "a slot in a container, or a
    /// pair of hands", which is exactly what a bottle needs. Sharing it means a bottle resolves to a
    /// socket through the same <see cref="Residue.Gameplay.World.PropSockets"/> a vial does, sits in
    /// the same <c>IVialSlots</c> shelves, and competes with vials for them — which is §2.6's one pair
    /// of hands made literal on the shelf as well as in the hand.
    /// </para>
    /// Only two of the seven kinds are ever used: <see cref="SampleLocationKind.Held"/> and
    /// <see cref="SampleLocationKind.OnSurface"/>. A bottle is never consumed, never archived and
    /// never goes inside an instrument — you pour from it, you do not load it.
    /// </summary>
    public sealed class SolventBottleState
    {
        /// <summary>Stable handle. Named by the store, never by a client.</summary>
        public string Id;

        /// <summary>How many flushes a full bottle holds. See <see cref="SolventStore.BottleCapacity"/>.</summary>
        public int Capacity = SolventStore.BottleCapacity;

        /// <summary>
        /// Flushes left. Not litres and not solvent units — a charge is one instrument's sample path
        /// cleaned out, which is the only quantity the player ever spends.
        /// </summary>
        public int Charges;

        public SampleLocation Location;

        public bool IsEmpty => Charges <= 0;

        public bool IsFull => Charges >= Capacity;

        /// <summary>Charges this bottle has room for. What a top-up draws out of the drum.</summary>
        public int Headroom => Capacity - Charges;

        /// <summary>True when the host's record says this player has it in their hands.</summary>
        public bool IsHeldBy(ulong clientId) =>
            Location.Kind == SampleLocationKind.Held && Location.HolderClientId == clientId;

        public override string ToString() => $"{Id} [{Charges}/{Capacity}] {Location}";
    }
}
