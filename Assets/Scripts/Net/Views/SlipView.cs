using System;
using Residue.Chemistry;
using Residue.Gameplay.Simulation;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// One results slip: which ticket it is, which run it reports, what is printed across the top,
    /// and where the paper is.
    /// <para>
    /// <b>It names its reading instead of carrying one.</b> <see cref="ResultKey"/> is the identity
    /// <see cref="ResultView.Key"/> already assigns a finished run, and the numbers travel once, as
    /// <see cref="ReadingView"/> rows under that key — the same rows the terminal and the instrument
    /// screens read. Putting the values on the slip as well would be a second wire path to the same
    /// figures at a cost that scales with both, and the day the two disagreed the paper in a player's
    /// hand and the panel at the desk would quote different results for one run.
    /// </para>
    /// <para>
    /// <b>Why the slip has to replicate at all.</b> A printout was spawned host-side from
    /// <c>LabState.RunCompleted</c> and nothing carried it, so a joined client's instrument trays were
    /// empty: they could read the numbers off a machine's screen and not walk them to the desk, which
    /// made filing a result host-only. Two players run instruments in parallel and only one could do
    /// the paperwork — a hole in the middle of the co-op loop rather than a missing polish item.
    /// </para>
    /// <para>
    /// A list the <i>world</i> reads — paper you pick up and look at — where <see cref="SampleView"/>
    /// is what displays draw. Same split as <see cref="VialView"/>, for the same reason.
    /// </para>
    /// Slips are not <c>NetworkObject</c>s, for the reason §3.2 gives about vials. Only the record
    /// travels; each client snaps its own local prop to the tray, rack hole or pair of hands this
    /// names.
    /// </summary>
    public struct SlipView : INetworkSerializable, IEquatable<SlipView>
    {
        /// <summary><c>ResultSlips</c> ticket. Identity for the paper, and what filing sends.</summary>
        public int Ticket;

        /// <summary>
        /// The run this reports, as <see cref="ResultView.Key"/>. Zero when the run is not on the wire
        /// — the slip is still takeable and still filable, it simply cannot be read at a glance yet.
        /// </summary>
        public int ResultKey;

        /// <summary><see cref="Chemistry.SampleId.Value"/>, or 0 for a blank or a certified standard.</summary>
        public int Sample;

        /// <summary>A solvent blank (§5.2), so a prompt can caption it without resolving the numbers.</summary>
        public bool IsBlank;

        /// <summary>The instrument's display name, as printed at the head of the slip.</summary>
        public FixedString64Bytes MachineName;

        /// <summary>
        /// What the lab calls the sample this reports on, so a player holding the paper can find the
        /// row it belongs to. "BLANK" or "CERT STANDARD" when the run belongs to the instrument
        /// rather than to any sample.
        /// </summary>
        public FixedString64Bytes RecordTag;

        // -- Location, flattened ---------------------------------------------------------------------
        //
        // SampleLocation holds a managed string, so it cannot ride in a NetworkList directly. Split
        // rather than wrapped, for the reason VialView gives: a wrapper that silently truncated the
        // container id would put the paper in the wrong tray.

        public SampleLocationKind Kind;
        public ulong HolderClientId;
        public FixedString64Bytes ContainerId;
        public int SlotIndex;

        public SampleId SampleId => new(Sample);

        /// <summary>Rebuild the location this record describes.</summary>
        public SampleLocation Location => new()
        {
            Kind = Kind,
            HolderClientId = HolderClientId,
            ContainerId = ContainerId.IsEmpty ? null : ContainerId.ToString(),
            SlotIndex = SlotIndex
        };

        /// <summary>
        /// Project host state for replication. The only place the slip projection is written, so there
        /// is one line to audit when asking what a client can see of a printout.
        /// <para>
        /// Note what is <b>not</b> here: the <c>TestResult</c> on the slip. It is deliberately not a
        /// parameter, so there is no signature through which the values could start travelling twice.
        /// </para>
        /// </summary>
        public static SlipView From(ResultSlips.Slip slip, int resultKey, string machineName,
                                   string recordTag)
        {
            var location = slip.Location;

            return new SlipView
            {
                Ticket = slip.Ticket,
                ResultKey = resultKey,
                Sample = slip.Sample.Value,
                IsBlank = slip.Result != null && slip.Result.IsBlank,
                MachineName = ViewText.Fixed64(machineName),
                RecordTag = ViewText.Fixed64(recordTag),
                Kind = location.Kind,
                HolderClientId = location.HolderClientId,
                ContainerId = ViewText.Fixed64(location.ContainerId),
                SlotIndex = location.SlotIndex
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Ticket);
            serializer.SerializeValue(ref ResultKey);
            serializer.SerializeValue(ref Sample);
            serializer.SerializeValue(ref IsBlank);
            serializer.SerializeValue(ref MachineName);
            serializer.SerializeValue(ref RecordTag);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref HolderClientId);
            serializer.SerializeValue(ref ContainerId);
            serializer.SerializeValue(ref SlotIndex);
        }

        public bool Equals(SlipView other) =>
            Ticket == other.Ticket &&
            ResultKey == other.ResultKey &&
            Sample == other.Sample &&
            IsBlank == other.IsBlank &&
            MachineName.Equals(other.MachineName) &&
            RecordTag.Equals(other.RecordTag) &&
            Kind == other.Kind &&
            HolderClientId == other.HolderClientId &&
            ContainerId.Equals(other.ContainerId) &&
            SlotIndex == other.SlotIndex;

        public override bool Equals(object obj) => obj is SlipView o && Equals(o);

        public override int GetHashCode() => Ticket;

        public override string ToString() =>
            $"slip #{Ticket} -> R{ResultKey} [{RecordTag}] {Kind}({ContainerId}#{SlotIndex})";
    }
}
