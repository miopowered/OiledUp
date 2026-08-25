using System;
using Residue.Chemistry;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// One physical bottle: where it is, how full, and what is printed on its label.
    /// <para>
    /// Deliberately a <b>separate list from <see cref="SampleView"/></b>. The two answer different
    /// questions: this one is read by things in the <i>world</i> — where a bottle is standing, which
    /// prop to snap to which rack hole — and <see cref="SampleView"/> is what screens draw. They
    /// change at different rates and for different reasons, so a client that only cares about the
    /// room does not resubscribe to the paperwork every time a verdict is filed.
    /// </para>
    /// <para>
    /// The separation used to carry a second, sharper job: keeping the paper label away from any
    /// screen that could diff it against a typed tag. #73 removed booking-in, so there is no typed
    /// tag and no diff to make. The split stands on its first argument alone now.
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
        /// The tag printed on the bottle, exactly as the courier wrote it. Since #73 this is also
        /// what the record is filed under, so it agrees with <see cref="SampleView.RecordTag"/> by
        /// construction rather than by anyone typing it correctly.
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
