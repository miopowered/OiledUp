using System;

namespace Residue.Chemistry
{
    /// <summary>Stable handle for a sample. Physical vial props carry this, not a reference to state.</summary>
    [Serializable]
    public readonly struct SampleId : IEquatable<SampleId>, IComparable<SampleId>
    {
        public readonly int Value;

        public SampleId(int value) => Value = value;

        public bool IsValid => Value > 0;
        public static readonly SampleId None = new(0);

        public bool Equals(SampleId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SampleId o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(SampleId other) => Value.CompareTo(other.Value);
        public override string ToString() => $"S{Value:D5}";

        public static bool operator ==(SampleId a, SampleId b) => a.Value == b.Value;
        public static bool operator !=(SampleId a, SampleId b) => a.Value != b.Value;
    }

    /// <summary>Where a vial physically is (§3.2). The server owns this; clients re-parent local props to match.</summary>
    public enum SampleLocationKind
    {
        InCrate,
        InFridge,
        OnSurface,
        Held,
        InMachine,
        Archived,
        Consumed
    }

    /// <summary>
    /// Server-side location record. Vials are NOT NetworkObjects — a busy shift has 200+ of them.
    /// Only the location changes replicate; each client snaps its pooled local prop to the slot.
    /// </summary>
    [Serializable]
    public struct SampleLocation
    {
        public SampleLocationKind Kind;

        /// <summary>Owning client when <see cref="Kind"/> is <see cref="SampleLocationKind.Held"/>.</summary>
        public ulong HolderClientId;

        /// <summary>Crate / surface / machine identifier for the container kinds.</summary>
        public string ContainerId;

        /// <summary>Slot within the container, or -1 when not slotted.</summary>
        public int SlotIndex;

        public static SampleLocation InCrate(string crateId, int slot) => new()
        { Kind = SampleLocationKind.InCrate, ContainerId = crateId, SlotIndex = slot };

        public static SampleLocation Held(ulong clientId) => new()
        { Kind = SampleLocationKind.Held, HolderClientId = clientId, SlotIndex = -1 };

        public static SampleLocation InMachine(string machineId, int slot) => new()
        { Kind = SampleLocationKind.InMachine, ContainerId = machineId, SlotIndex = slot };

        public static SampleLocation OnSurface(string surfaceId, int slot) => new()
        { Kind = SampleLocationKind.OnSurface, ContainerId = surfaceId, SlotIndex = slot };

        public static SampleLocation InFridge(int slot) => new()
        { Kind = SampleLocationKind.InFridge, ContainerId = "fridge", SlotIndex = slot };

        public static SampleLocation Archived() => new()
        { Kind = SampleLocationKind.Archived, SlotIndex = -1 };

        public static SampleLocation Consumed() => new()
        { Kind = SampleLocationKind.Consumed, SlotIndex = -1 };

        public override string ToString() => Kind switch
        {
            SampleLocationKind.Held => $"Held(client {HolderClientId})",
            SampleLocationKind.Archived or SampleLocationKind.Consumed => Kind.ToString(),
            _ => $"{Kind}({ContainerId}#{SlotIndex})"
        };
    }
}
