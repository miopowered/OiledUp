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

        /// <summary>
        /// The <c>MachineDef</c> id, so a client can look the definition up in its own catalog.
        /// <para>
        /// Definitions are immutable content both sides already ship — run time, sample volume,
        /// display name, what it can and cannot detect. Sending the id rather than the values keeps
        /// the wire small and, more importantly, keeps one source of truth: a client reading a
        /// replicated <i>copy</i> of a threshold would drift from the host the moment the tables were
        /// retuned and only one side rebuilt.
        /// </para>
        /// </summary>
        public FixedString64Bytes DefId;

        public bool IsRunning;

        /// <summary>
        /// Whether a vial is sitting in the instrument. Not <i>which</i> vial: a client asks "is this
        /// free" to draw its prompt, and the sample it would load is the one in its own hands.
        /// </summary>
        public bool IsLoaded;

        /// <summary>
        /// A sample run has finished and its vial is still in the instrument. The difference between
        /// a station offering "run this" and offering "take your vial back", which every player in the
        /// room has to agree on or two of them will run the same sample twice (§4.5).
        /// </summary>
        public bool HasResultWaiting;

        /// <summary>Seconds left on whatever is running. Zero when idle.</summary>
        public float SecondsRemaining;

        /// <summary>
        /// How long a sample run takes on this instrument, after the testing time scale.
        /// <para>
        /// Sent rather than derived, even though <see cref="MachineDef.RunTimeSeconds"/> is content
        /// both sides ship, because <see cref="LabRuntime"/>'s time scale is applied host-side and a
        /// client that multiplied by its own copy would be quoting the wrong number the moment the two
        /// disagreed. Four bytes to make "Run (30s)" mean the same thing to everyone in the room.
        /// </para>
        /// </summary>
        public float RunSeconds;

        /// <summary>
        /// Fraction of the current run completed, for the progress bar on the instrument's own screen.
        /// Zero when idle. Derived host-side because the run's total duration is not otherwise
        /// knowable here — a recalibration is half a cycle, not a whole one (§5.3).
        /// </summary>
        public float Progress;

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

        /// <summary>
        /// Day the certificate on file was run, or -1 for none. §5.3 only lets a recalibration
        /// proceed on a check from <i>today</i>, so a client needs the day to grey the button rather
        /// than offer an action the host is about to refuse.
        /// </summary>
        public int CalibrationCheckDay;

        // -- The last recalibration -------------------------------------------------------------------
        //
        // Safe to send, and for the same reason the end-of-day report may name a fault: this is what a
        // correction turned out to have been, after the player bought the ampoule and ran it. It is
        // the answer to a question they asked, not the hidden state the question was about — which is
        // why DriftPercent stays host-side and this does not.

        /// <summary>False when this instrument has never been recalibrated.</summary>
        public bool HasRecalibration;

        public int RecalibratedDay;

        /// <summary>Signed error removed. 0.18 means it had been reading 18% high.</summary>
        public float RecalibrationCorrected;

        /// <summary>Individual runs the correction put in doubt.</summary>
        public int RecalibrationFlaggedRuns;

        /// <summary>Samples with at least one run inside the drift window.</summary>
        public int RecalibrationAffectedSamples;

        /// <summary>Of those, the filed records. This is the number that hurts (§5.3).</summary>
        public int RecalibrationAffectedRecords;

        public bool IsIdle => !IsRunning;
        public bool IsEmpty => !IsLoaded;

        /// <summary>True once a blank has ever been run here. Below that, residue is simply unknown.</summary>
        public bool HasBlank => LastBlankDay >= 0;

        /// <summary>
        /// Project host state for replication. The only place the instrument projection is written.
        /// </summary>
        public static MachineView From(MachineInstance machine)
        {
            if (machine == null) return default;

            var check = machine.LastCheck;
            var calibration = machine.LastCalibration;

            return new MachineView
            {
                InstanceId = ViewText.Fixed64(machine.InstanceId),
                DefId = ViewText.Fixed64(machine.Def != null ? machine.Def.Id : null),
                IsRunning = machine.IsRunning,
                IsLoaded = !machine.IsEmpty,
                HasResultWaiting = machine.HasResultWaiting,
                SecondsRemaining = machine.IsRunning ? machine.SecondsRemaining : 0f,
                RunSeconds = machine.RunSeconds,
                Progress = machine.Progress,
                RunsSinceFlush = machine.Runtime != null ? machine.Runtime.RunsSinceClean : 0,
                LastBlankDay = machine.LastBlankDay,
                LastBlankFoundResidue = FoundResidue(machine.LastBlank),
                HasCalibrationCheck = check != null,
                CalibrationErrorFraction = check?.ErrorFraction ?? 0f,
                CalibrationOutOfTolerance = check != null && check.IsOutOfTolerance,
                CalibrationCheckDay = check?.Day ?? -1,
                HasRecalibration = calibration.HasValue,
                RecalibratedDay = calibration?.Day ?? -1,
                RecalibrationCorrected = calibration?.CorrectedDrift ?? 0f,
                RecalibrationFlaggedRuns = calibration?.FlaggedResults ?? 0,
                RecalibrationAffectedSamples = calibration?.AffectedSamples ?? 0,
                RecalibrationAffectedRecords = calibration?.AffectedArchived ?? 0
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
            serializer.SerializeValue(ref DefId);
            serializer.SerializeValue(ref IsRunning);
            serializer.SerializeValue(ref IsLoaded);
            serializer.SerializeValue(ref HasResultWaiting);
            serializer.SerializeValue(ref SecondsRemaining);
            serializer.SerializeValue(ref RunSeconds);
            serializer.SerializeValue(ref Progress);
            serializer.SerializeValue(ref RunsSinceFlush);
            serializer.SerializeValue(ref LastBlankDay);
            serializer.SerializeValue(ref LastBlankFoundResidue);
            serializer.SerializeValue(ref HasCalibrationCheck);
            serializer.SerializeValue(ref CalibrationErrorFraction);
            serializer.SerializeValue(ref CalibrationOutOfTolerance);
            serializer.SerializeValue(ref CalibrationCheckDay);
            serializer.SerializeValue(ref HasRecalibration);
            serializer.SerializeValue(ref RecalibratedDay);
            serializer.SerializeValue(ref RecalibrationCorrected);
            serializer.SerializeValue(ref RecalibrationFlaggedRuns);
            serializer.SerializeValue(ref RecalibrationAffectedSamples);
            serializer.SerializeValue(ref RecalibrationAffectedRecords);
        }

        public bool Equals(MachineView other) =>
            InstanceId.Equals(other.InstanceId) &&
            DefId.Equals(other.DefId) &&
            IsRunning == other.IsRunning &&
            IsLoaded == other.IsLoaded &&
            HasResultWaiting == other.HasResultWaiting &&
            SecondsRemaining.Equals(other.SecondsRemaining) &&
            RunSeconds.Equals(other.RunSeconds) &&
            Progress.Equals(other.Progress) &&
            RunsSinceFlush == other.RunsSinceFlush &&
            LastBlankDay == other.LastBlankDay &&
            LastBlankFoundResidue == other.LastBlankFoundResidue &&
            HasCalibrationCheck == other.HasCalibrationCheck &&
            CalibrationErrorFraction.Equals(other.CalibrationErrorFraction) &&
            CalibrationOutOfTolerance == other.CalibrationOutOfTolerance &&
            CalibrationCheckDay == other.CalibrationCheckDay &&
            HasRecalibration == other.HasRecalibration &&
            RecalibratedDay == other.RecalibratedDay &&
            RecalibrationCorrected.Equals(other.RecalibrationCorrected) &&
            RecalibrationFlaggedRuns == other.RecalibrationFlaggedRuns &&
            RecalibrationAffectedSamples == other.RecalibrationAffectedSamples &&
            RecalibrationAffectedRecords == other.RecalibrationAffectedRecords;

        public override bool Equals(object obj) => obj is MachineView o && Equals(o);

        public override int GetHashCode() => InstanceId.GetHashCode();

        public override string ToString() =>
            $"{InstanceId} {(IsRunning ? $"running {SecondsRemaining:F0}s" : "idle")}";
    }
}
