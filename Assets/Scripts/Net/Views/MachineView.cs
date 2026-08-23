using System;
using Residue.Chemistry;
using Residue.Gameplay.Simulation;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// One instrument as a client sees it: whether it is busy, and the two tells that say whether its
    /// numbers can be trusted.
    /// <para>
    /// Those tells are not decoration. Hard rule 3 says never punish something the player could not
    /// have checked, and residue (§5.2) and drift (§5.3) are only fair because a blank and a certified
    /// standard reveal them. A co-op client standing in front of the machine has to be able to read
    /// the same thing the host knows, or the rule holds for the host and quietly fails for everyone
    /// else in the lab.
    /// </para>
    /// <para>
    /// What does not cross: the residue map itself and <see cref="MachineRuntimeState.DriftPercent"/>.
    /// Both are the hidden state the tells exist to expose. Sending the drift figure directly would
    /// make the reference ampoule a formality, and sending the residue would delete the flush decision
    /// — which is exactly the reasoning <see cref="CalibrationCheck"/> gives for not folding one into
    /// the other. <see cref="CalibrationErrorFraction"/> is fine to send because it is the number the
    /// certificate and the readout put on the same screen anyway, and it deliberately cannot tell
    /// residue and drift apart.
    /// </para>
    /// </summary>
    public struct MachineView : INetworkSerializable, IEquatable<MachineView>
    {
        /// <summary>
        /// Below this a blank is reading instrument noise, not carryover. Matches the figure the
        /// terminal already prints "clean" against, so the host and the client cannot come apart on
        /// what a dirty machine is.
        /// </summary>
        public const float ResidueFloor = 0.0001f;

        /// <summary><see cref="MachineInstance.InstanceId"/>, not the <c>MachineDef</c> id — a lab may hold two of a kind.</summary>
        public FixedString64Bytes InstanceId;

        public bool IsRunning;

        /// <summary>Seconds left on whatever is running. Zero when idle.</summary>
        public float SecondsRemaining;

        /// <summary>Runs since the last solvent flush. The player's cue that carryover is building (§5.2).</summary>
        public int RunsSinceFlush;

        /// <summary>Day of the last solvent blank, or -1 if this instrument has never had one.</summary>
        public int LastBlankDay;

        /// <summary>True if that blank came back with carryover in it. The §5.2 tell, reduced to its verdict.</summary>
        public bool LastBlankFoundResidue;

        /// <summary>False when no certificate is on file — cleared by a recalibration, which consumes it.</summary>
        public bool HasCalibrationCheck;

        /// <summary>Mean signed error from the last certified standard. 0.18 means it read 18% high.</summary>
        public float CalibrationErrorFraction;

        /// <summary>Whether that error clears <see cref="CalibrationCheck.Tolerance"/>.</summary>
        public bool CalibrationOutOfTolerance;

        public bool IsIdle => !IsRunning;

        /// <summary>True once a blank has ever been run here. Below that, residue is simply unknown.</summary>
        public bool HasBlank => LastBlankDay >= 0;

        /// <summary>
        /// Project host state for replication. The only place the instrument projection is written.
        /// </summary>
        public static MachineView From(MachineInstance machine)
        {
            if (machine == null) return default;

            var check = machine.LastCheck;

            return new MachineView
            {
                InstanceId = ViewText.Fixed64(machine.InstanceId),
                IsRunning = machine.IsRunning,
                SecondsRemaining = machine.IsRunning ? machine.SecondsRemaining : 0f,
                RunsSinceFlush = machine.Runtime != null ? machine.Runtime.RunsSinceClean : 0,
                LastBlankDay = machine.LastBlankDay,
                LastBlankFoundResidue = FoundResidue(machine.LastBlank),
                HasCalibrationCheck = check != null,
                CalibrationErrorFraction = check?.ErrorFraction ?? 0f,
                CalibrationOutOfTolerance = check != null && check.IsOutOfTolerance
            };
        }

        /// <summary>
        /// Did this blank come back dirty? A blank measures carryover directly, so any element above
        /// the noise floor is residue the next sample will inherit.
        /// </summary>
        private static bool FoundResidue(TestResult blank)
        {
            if (blank == null) return false;

            foreach (var kv in blank.Values)
            {
                if (kv.Value > ResidueFloor) return true;
            }
            return false;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref InstanceId);
            serializer.SerializeValue(ref IsRunning);
            serializer.SerializeValue(ref SecondsRemaining);
            serializer.SerializeValue(ref RunsSinceFlush);
            serializer.SerializeValue(ref LastBlankDay);
            serializer.SerializeValue(ref LastBlankFoundResidue);
            serializer.SerializeValue(ref HasCalibrationCheck);
            serializer.SerializeValue(ref CalibrationErrorFraction);
            serializer.SerializeValue(ref CalibrationOutOfTolerance);
        }

        public bool Equals(MachineView other) =>
            InstanceId.Equals(other.InstanceId) &&
            IsRunning == other.IsRunning &&
            SecondsRemaining.Equals(other.SecondsRemaining) &&
            RunsSinceFlush == other.RunsSinceFlush &&
            LastBlankDay == other.LastBlankDay &&
            LastBlankFoundResidue == other.LastBlankFoundResidue &&
            HasCalibrationCheck == other.HasCalibrationCheck &&
            CalibrationErrorFraction.Equals(other.CalibrationErrorFraction) &&
            CalibrationOutOfTolerance == other.CalibrationOutOfTolerance;

        public override bool Equals(object obj) => obj is MachineView o && Equals(o);

        public override int GetHashCode() => InstanceId.GetHashCode();

        public override string ToString() =>
            $"{InstanceId} {(IsRunning ? $"running {SecondsRemaining:F0}s" : "idle")}";
    }
}
