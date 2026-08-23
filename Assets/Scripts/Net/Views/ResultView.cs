using System;
using Residue.Chemistry;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// One finished run, as everyone in the lab may read it: which instrument produced it, when, what
    /// kind of run it was, and whether it is trusted. The numbers themselves travel beside it as
    /// <see cref="ReadingView"/> rows carrying this row's <see cref="Key"/>.
    /// <para>
    /// <b>A measured value is not ground truth.</b> Hard rule 2 keeps <see cref="SampleGroundTruth"/>
    /// off the wire; a <see cref="TestResult"/> is the opposite thing — the reading an instrument
    /// produced, already carrying that instrument's residue (§5.2) and drift (§5.3), which is exactly
    /// what the player is meant to be looking at and arguing with. Sending it is what makes a verdict
    /// filed from a client a call the player can check, and hard rule 3 forbids asking for one they
    /// cannot.
    /// </para>
    /// <para>
    /// <b>Why the readings are not in here.</b> A <c>FixedList512Bytes</c> holds about fourteen
    /// (id, value) pairs, and the panel it has to hold is content the tables are free to grow. A cap
    /// that is generous today is a cap that silently drops the sixteenth metal the day somebody adds
    /// it — and a results table missing a row is the worst failure available here, because the player
    /// is then judged on a number that was never on their screen. Keyed rows in their own list have
    /// no cap to exceed and no truncation rule to get wrong; see <see cref="ReadingView"/>.
    /// </para>
    /// </summary>
    public struct ResultView : INetworkSerializable, IEquatable<ResultView>
    {
        /// <summary>
        /// Identifies this run for as long as it exists. Assigned once by the host and never reused,
        /// so a <see cref="ReadingView"/> naming it is self-describing: if the two lists were ever
        /// read a frame apart, the readings for a key that is not there simply do not draw. An offset
        /// into a neighbouring list would instead draw somebody else's numbers under this heading,
        /// which is the one way a results table can lie.
        /// </summary>
        public int Key;

        /// <summary><see cref="Chemistry.SampleId.Value"/>, or 0 for a run that belonged to the instrument.</summary>
        public int Sample;

        /// <summary>
        /// True once the player has walked the slip to the desk and it is on the record (§5.1).
        /// <para>
        /// The distinction is the mechanic, not bookkeeping. An instrument finishing a run puts
        /// nothing on a sample's record — see <see cref="SampleState.Results"/> — so a row that names
        /// a sample and is not filed is a reading sitting on a machine waiting to be carried. The
        /// terminal draws only filed ones; the instrument's own screen draws whatever is on it.
        /// </para>
        /// </summary>
        public bool Filed;

        /// <summary><see cref="TestResult.MachineId"/> — the definition, which is what the run log names.</summary>
        public FixedString32Bytes MachineDefId;

        /// <summary>
        /// Which placed instrument produced it, so a screen on the machine can find its own last
        /// reading. Empty for a filed result whose instrument has since moved on to something else.
        /// </summary>
        public FixedString32Bytes MachineInstanceId;

        public int DayRun;

        public float VolumeConsumedMl;

        public float Cost;

        /// <summary>A solvent blank: this reads the instrument's carryover directly (§5.2).</summary>
        public bool IsBlank;

        /// <summary>A certified standard: the gap against the certificate is the instrument's error (§5.3).</summary>
        public bool IsReference;

        /// <summary>The instrument was later found to have been drifting when this ran (§5.3).</summary>
        public bool Suspect;

        public SampleId SampleId => new(Sample);

        /// <summary>True when the run belonged to the instrument rather than to anyone's oil.</summary>
        public bool IsHousekeeping => IsBlank || IsReference;

        /// <summary>
        /// Project host state for replication. The only place the result projection is written.
        /// <para>
        /// The values are deliberately not touched here: they leave as <see cref="ReadingView"/> rows
        /// in <c>LabNetwork</c>, which is the one place that owns the key this row was given.
        /// </para>
        /// </summary>
        public static ResultView From(TestResult result, int key, SampleId sample, bool filed,
                                      string machineInstanceId)
        {
            if (result == null) return default;

            return new ResultView
            {
                Key = key,
                Sample = sample.Value,
                Filed = filed,
                MachineDefId = ViewText.Fixed32(result.MachineId),
                MachineInstanceId = ViewText.Fixed32(machineInstanceId),
                DayRun = result.DayRun,
                VolumeConsumedMl = result.VolumeConsumedMl,
                Cost = result.Cost,
                IsBlank = result.IsBlank,
                IsReference = result.IsReference,
                Suspect = result.Suspect
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Key);
            serializer.SerializeValue(ref Sample);
            serializer.SerializeValue(ref Filed);
            serializer.SerializeValue(ref MachineDefId);
            serializer.SerializeValue(ref MachineInstanceId);
            serializer.SerializeValue(ref DayRun);
            serializer.SerializeValue(ref VolumeConsumedMl);
            serializer.SerializeValue(ref Cost);
            serializer.SerializeValue(ref IsBlank);
            serializer.SerializeValue(ref IsReference);
            serializer.SerializeValue(ref Suspect);
        }

        public bool Equals(ResultView other) =>
            Key == other.Key &&
            Sample == other.Sample &&
            Filed == other.Filed &&
            MachineDefId.Equals(other.MachineDefId) &&
            MachineInstanceId.Equals(other.MachineInstanceId) &&
            DayRun == other.DayRun &&
            VolumeConsumedMl.Equals(other.VolumeConsumedMl) &&
            Cost.Equals(other.Cost) &&
            IsBlank == other.IsBlank &&
            IsReference == other.IsReference &&
            Suspect == other.Suspect;

        public override bool Equals(object obj) => obj is ResultView o && Equals(o);

        public override int GetHashCode() => Key;

        public override string ToString() =>
            $"R{Key} {MachineDefId} day {DayRun}{(IsBlank ? " BLANK" : IsReference ? " CERT" : "")}" +
            $"{(Suspect ? " SUSPECT" : "")}";
    }
}
