using System;
using Residue.Chemistry;
using Residue.Gameplay.Simulation;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// One solvent bottle: which one, how many flushes are in it, and where it is.
    /// <para>
    /// A flush needs no vial, which is why it was one of the first things a joined client could do.
    /// Making it need a bottle would have taken that back unless the bottle travelled — so it
    /// travels. This is the record that carries it, and it is deliberately the same shape as
    /// <see cref="VialView"/>: a flat id, a couple of numbers, and a location split into unmanaged
    /// parts, because that is what a <c>NetworkList</c> element can be.
    /// </para>
    /// <para>
    /// <b>A separate list from <see cref="VialView"/> rather than a flag on it.</b> The two have
    /// nothing in common but a location, and every consumer of one wants none of the other's fields.
    /// Folding them together would put a null half in every row and a "which kind is this" branch in
    /// every reader.
    /// </para>
    /// Two rows, republished with everything else. §3.2's "do not spawn a NetworkObject per vial"
    /// argument is about 200 bottles a shift, not two — but the local-prop machinery already existed
    /// and a NetworkObject would have needed networked cradles to sit in.
    /// </summary>
    public struct SolventBottleView : INetworkSerializable, IEquatable<SolventBottleView>
    {
        /// <summary><see cref="SolventBottleState.Id"/>. The key a client's prop hangs off.</summary>
        public FixedString32Bytes Id;

        /// <summary>Flushes left. What the button on the instrument prints and the host spends.</summary>
        public int Charges;

        public int Capacity;

        // -- Location, flattened. Same split, and the same reason, as VialView's.

        public SampleLocationKind Kind;
        public ulong HolderClientId;
        public FixedString64Bytes ContainerId;
        public int SlotIndex;

        /// <summary>Rebuild the location this record describes.</summary>
        public SampleLocation Location => new()
        {
            Kind = Kind,
            HolderClientId = HolderClientId,
            ContainerId = ContainerId.IsEmpty ? null : ContainerId.ToString(),
            SlotIndex = SlotIndex
        };

        /// <summary>
        /// Project host state for replication. The only place the bottle projection is written, so
        /// there is one line to audit when asking what a client can see of one.
        /// </summary>
        public static SolventBottleView From(SolventBottleState state)
        {
            if (state == null) return default;

            var location = state.Location;

            return new SolventBottleView
            {
                Id = ViewText.Fixed32(state.Id),
                Charges = state.Charges,
                Capacity = state.Capacity,
                Kind = location.Kind,
                HolderClientId = location.HolderClientId,
                ContainerId = ViewText.Fixed64(location.ContainerId),
                SlotIndex = location.SlotIndex
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Charges);
            serializer.SerializeValue(ref Capacity);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref HolderClientId);
            serializer.SerializeValue(ref ContainerId);
            serializer.SerializeValue(ref SlotIndex);
        }

        public bool Equals(SolventBottleView other) =>
            Id.Equals(other.Id) &&
            Charges == other.Charges &&
            Capacity == other.Capacity &&
            Kind == other.Kind &&
            HolderClientId == other.HolderClientId &&
            ContainerId.Equals(other.ContainerId) &&
            SlotIndex == other.SlotIndex;

        public override bool Equals(object obj) => obj is SolventBottleView o && Equals(o);

        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() => $"{Id} [{Charges}/{Capacity}] {Kind}({ContainerId}#{SlotIndex})";
    }
}
