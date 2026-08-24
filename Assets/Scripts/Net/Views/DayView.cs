using System;
using Residue.Gameplay.Simulation;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// The shift clock, replicated.
    /// <para>
    /// §3.1 puts the day timer on the host, which means a client cannot count down its own — and the
    /// clock is the pressure the whole game runs on (§6.1: the queue outpacing your hands). The two
    /// derived flags travel alongside the raw seconds rather than being recomputed client-side,
    /// because <see cref="LabState.ShiftOver"/> and <see cref="LabState.IsRunOver"/> are also fed by
    /// the contract length and the bank balance. A client inferring "shift over" from the clock alone
    /// would be right most of the time, which is the worst kind of wrong.
    /// </para>
    /// </summary>
    public struct DayView : INetworkSerializable, IEquatable<DayView>
    {
        /// <summary>1-based. Zero before the first day begins.</summary>
        public int Day;

        public float SecondsRemaining;

        public bool DayInProgress;

        /// <summary>Instruments refuse to start new runs. Anything already running still finishes.</summary>
        public bool ShiftOver;

        /// <summary>Contract finished, or the money ran out (§1.2).</summary>
        public bool IsRunOver;

        /// <summary>
        /// Which contract this is and how many days it runs, for the line the end-of-run screen
        /// closes on — "CONTRACT COMPLETE — Shakedown, 20 days."
        /// <para>
        /// They ride on the clock because the clock is the only thing that already knows which day of
        /// what this is, and because a run ends for everyone at once: a joined player who worked the
        /// whole contract and is told only their balance got half an ending. Two fields that never
        /// change during a run, on a variable that is written anyway.
        /// </para>
        /// </summary>
        public FixedString32Bytes ContractName;

        /// <summary>Contracted days. See <see cref="ContractName"/>.</summary>
        public int ContractLength;

        /// <summary>Project host state for replication. The only place the day projection is written.</summary>
        public static DayView From(LabState lab) => lab == null
            ? default
            : new DayView
            {
                Day = lab.Day,
                SecondsRemaining = lab.DaySecondsRemaining,
                DayInProgress = lab.DayInProgress,
                ShiftOver = lab.ShiftOver,
                IsRunOver = lab.IsRunOver,
                ContractName = ViewText.Fixed32(lab.Plan != null ? lab.Plan.DisplayName : null),
                ContractLength = lab.Plan != null ? lab.Plan.Length : 0
            };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Day);
            serializer.SerializeValue(ref SecondsRemaining);
            serializer.SerializeValue(ref DayInProgress);
            serializer.SerializeValue(ref ShiftOver);
            serializer.SerializeValue(ref IsRunOver);
            serializer.SerializeValue(ref ContractName);
            serializer.SerializeValue(ref ContractLength);
        }

        public bool Equals(DayView other) =>
            Day == other.Day &&
            SecondsRemaining.Equals(other.SecondsRemaining) &&
            DayInProgress == other.DayInProgress &&
            ShiftOver == other.ShiftOver &&
            IsRunOver == other.IsRunOver &&
            ContractName.Equals(other.ContractName) &&
            ContractLength == other.ContractLength;

        public override bool Equals(object obj) => obj is DayView o && Equals(o);

        public override int GetHashCode() => Day;

        public override string ToString() => $"Day {Day} · {SecondsRemaining:F0}s left";
    }
}
