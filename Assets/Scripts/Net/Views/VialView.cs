using System;
using Residue.Chemistry;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// One physical bottle: where it is, how full, and what is printed on its label.
    /// <para>
    /// Deliberately a <b>separate list from <see cref="SampleView"/></b>, and the separation is the
    /// whole design. §5.1 makes mis-logging a real failure mode, and it is only fair because the
    /// paper label is still on the bottle — walking back and reading it is the one tell. So the label
    /// has to reach a client, or a client can never check their own booking-in.
    /// </para>
    /// <para>
    /// But <see cref="SampleView"/> feeds <i>screens</i>, and it carries <c>RecordTag</c> — what the
    /// player typed. Put the label in that same struct and any screen could diff the two and print
    /// "you meant WERK-1 BATH A", which hands over the correction for free and deletes the mechanic.
    /// Two lists, one rule each: this one is read by things in the world, that one by things on a
    /// display. The boundary is structural, not a comment asking people to be careful.
    /// </para>
    /// Vials are not <c>NetworkObject</c>s — §3.2 is explicit that a busy shift has 200+ of them and
    /// one per bottle would drown the connection. Only the record travels; each client snaps its own
    /// local prop to the slot this names.
    /// </summary>
    public struct VialView : INetworkSerializable, IEquatable<VialView>
    {
        /// <summary><see cref="Chemistry.SampleId.Value"/>. The key a client's pooled prop hangs off.</summary>
        public int Id;

        /// <summary>
        /// The tag printed on the bottle, exactly as the courier wrote it. Not what anyone typed.
        /// <para>
        /// This is the only place it crosses the wire, and it must stay out of anything a screen
        /// draws — see the type doc.
        /// </para>
        /// </summary>
        public FixedString64Bytes Label;

        public float VolumeMl;

        // -- Location, flattened ---------------------------------------------------------------------
        //
        // SampleLocation holds a managed string, so it cannot ride in a NetworkList directly. Split
        // rather than wrapped, because a wrapper that silently truncated the container id would put a
        // vial in the wrong slot, and that is a bug you would chase in the world before the wire.

        public SampleLocationKind Kind;
        public ulong HolderClientId;
        public FixedString64Bytes ContainerId;
        public int SlotIndex;

        /// <summary>The handle back, for matching a record to a pooled prop.</summary>
        public SampleId SampleId => new(Id);

        /// <summary>True when this bottle no longer exists to be picked up.</summary>
        public bool IsGone => Kind == SampleLocationKind.Consumed;

        /// <summary>Rebuild the location this record describes.</summary>
        public SampleLocation Location => new()
        {
            Kind = Kind,
            HolderClientId = HolderClientId,
            ContainerId = ContainerId.IsEmpty ? null : ContainerId.ToString(),
            SlotIndex = SlotIndex
        };

        /// <summary>
        /// Project host state for replication. The only place the vial projection is written, so
        /// there is one line to audit when asking what a client can see of a bottle.
        /// </summary>
        public static VialView From(SampleState state)
        {
            if (state == null) return default;

            var location = state.Location;

            return new VialView
            {
                Id = state.Id.Value,
                Label = ViewText.Fixed64(state.EquipmentTag),
                VolumeMl = state.VolumeMl,
                Kind = location.Kind,
                HolderClientId = location.HolderClientId,
                ContainerId = ViewText.Fixed64(location.ContainerId),
                SlotIndex = location.SlotIndex
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Label);
            serializer.SerializeValue(ref VolumeMl);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref HolderClientId);
            serializer.SerializeValue(ref ContainerId);
            serializer.SerializeValue(ref SlotIndex);
        }

        public bool Equals(VialView other) =>
            Id == other.Id &&
            Label.Equals(other.Label) &&
            VolumeMl.Equals(other.VolumeMl) &&
            Kind == other.Kind &&
            HolderClientId == other.HolderClientId &&
            ContainerId.Equals(other.ContainerId) &&
            SlotIndex == other.SlotIndex;

        public override bool Equals(object obj) => obj is VialView o && Equals(o);

        public override int GetHashCode() => Id;

        public override string ToString() => $"S{Id:D5} [{Label}] {Kind}({ContainerId}#{SlotIndex})";
    }
}
