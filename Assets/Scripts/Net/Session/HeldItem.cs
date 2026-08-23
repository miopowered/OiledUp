using System;
using Residue.Chemistry;
using Residue.Gameplay.World;

namespace Residue.Net.Session
{
    /// <summary>What kind of thing is in a player's hands. One pair of hands, so exactly one of these.</summary>
    public enum HeldItemKind
    {
        None,
        Vial,
        Printout,
        ReferenceBook
    }

    /// <summary>
    /// A held item reduced to what it takes to find or rebuild it — never a reference to the
    /// <see cref="Carryable"/> itself.
    /// <para>
    /// This is server state and the props are not. §3.2 makes vials and slips local-only pooled
    /// objects, so the host may be holding a record of an object that, on a client that has just
    /// reconnected, does not exist yet and will be a different instance when it does. A descriptor
    /// of primitives survives that; a <c>MonoBehaviour</c> reference would be a dangling pointer
    /// into a scene the reconnecting client has not finished loading.
    /// </para>
    /// Everything here is a value or a short string, so it round-trips over the wire and into a save
    /// file unchanged — which matters because §M5 needs the same descriptor and there is no reason
    /// to invent a second one.
    /// </summary>
    [Serializable]
    public readonly struct HeldItem : IEquatable<HeldItem>
    {
        public readonly HeldItemKind Kind;

        /// <summary>The sample a vial contains, or the sample a slip reports on. Unset otherwise.</summary>
        public readonly SampleId Sample;

        /// <summary>
        /// For a printout, the <see cref="Residue.Gameplay.Simulation.MachineInstance.InstanceId"/>
        /// that produced it — the instance rather than the definition, because a lab with two
        /// spectrometers has two trays and the slip came out of one of them.
        /// <para>
        /// For a manual, the <c>MachineDef</c> id it documents, or null for the general references.
        /// </para>
        /// </summary>
        public readonly string SourceId;

        /// <summary>Which manual. Meaningless unless <see cref="Kind"/> is a reference book.</summary>
        public readonly BookKind Book;

        private HeldItem(HeldItemKind kind, SampleId sample, string sourceId, BookKind book)
        {
            Kind = kind;
            Sample = sample;
            SourceId = string.IsNullOrEmpty(sourceId) ? null : sourceId;
            Book = book;
        }

        public static readonly HeldItem None = default;

        public static HeldItem Vial(SampleId sample) =>
            new(HeldItemKind.Vial, sample, null, default);

        public static HeldItem Printout(SampleId sample, string machineInstanceId) =>
            new(HeldItemKind.Printout, sample, machineInstanceId, default);

        public static HeldItem ReferenceBook(BookKind book, string machineId = null) =>
            new(HeldItemKind.ReferenceBook, SampleId.None, machineId, book);

        public bool IsEmpty => Kind == HeldItemKind.None;

        /// <summary>
        /// True when this describes oil rather than paper. The distinction is the whole reason
        /// <see cref="PlayerSession.ReleasedOnDisconnect"/> exists: a slip left in a tray costs the
        /// lab nothing, a vial nobody can reach costs it a sample.
        /// </summary>
        public bool IsSample => Kind == HeldItemKind.Vial;

        public bool Equals(HeldItem other) =>
            Kind == other.Kind &&
            Sample == other.Sample &&
            Book == other.Book &&
            string.Equals(SourceId, other.SourceId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is HeldItem o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ Sample.Value;
                hash = (hash * 397) ^ (int)Book;
                hash = (hash * 397) ^ (SourceId != null ? SourceId.GetHashCode() : 0);
                return hash;
            }
        }

        public static bool operator ==(HeldItem a, HeldItem b) => a.Equals(b);
        public static bool operator !=(HeldItem a, HeldItem b) => !a.Equals(b);

        public override string ToString() => Kind switch
        {
            HeldItemKind.Vial => $"Vial({Sample})",
            HeldItemKind.Printout => $"Printout({Sample} from {SourceId ?? "?"})",
            HeldItemKind.ReferenceBook => $"Book({Book}{(SourceId == null ? "" : ":" + SourceId)})",
            _ => "empty-handed"
        };
    }
}
