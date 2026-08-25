using System;
using Residue.Chemistry;
using Residue.Data;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// Everything a client is allowed to know about one sample.
    /// <para>
    /// This is hard rule 2 made structural at the last hop. <see cref="SampleGroundTruth"/> never
    /// leaves <c>SampleRegistry</c>, but <see cref="SampleState"/> is a live server object full of
    /// managed lists that a client has no business holding either — a client that had
    /// <see cref="SampleState.Results"/> could re-score the panel itself, and §3.1 is explicit that a
    /// client must never compute a test result. So the host projects, once, in
    /// <see cref="From"/>, and the projection is the only thing that crosses.
    /// </para>
    /// <para>
    /// <see cref="RecordTag"/> is the name the lab files this sample under, which since #73 is simply
    /// the tag printed on its label. There is no longer a second, typed name it could disagree with,
    /// so no screen can hand the player a discrepancy — there is none to hand them.
    /// </para>
    /// A struct, and <c>IEquatable</c>, so it is legal inside a <c>NetworkList</c> — §3.2 rules out a
    /// NetworkObject per vial, which makes a list of these the shape the sample roster has to take.
    /// </summary>
    public struct SampleView : INetworkSerializable, IEquatable<SampleView>
    {
        /// <summary><see cref="Chemistry.SampleId.Value"/>. Local vial props are keyed on this (§3.2).</summary>
        public int Id;

        /// <summary>What the terminal calls this sample — the tag printed on its label.</summary>
        public FixedString64Bytes RecordTag;

        /// <summary><see cref="EquipmentProfileDef.Id"/>, so the client can look up thresholds it already ships.</summary>
        public FixedString64Bytes ProfileId;

        /// <summary>Millilitres left. §4.5's whole test-ordering decision is about this number.</summary>
        public float VolumeMl;

        /// <summary>
        /// Hours the oil has been in the tank. Context for every reading on the panel — the same acid
        /// number means one thing at 300 h and another at 5000 h — and safe to send because the
        /// generator copies it straight from the arrival plan: it is drawn per delivery, never from
        /// what is wrong with the oil.
        /// </summary>
        public float HoursSinceOilChange;

        /// <summary>
        /// The courier's note, verbatim. Vague, wrong or absent by design (§4.4), and drawn from a
        /// fixed pool independently of the fault — it is atmosphere and misdirection, not a hint, so
        /// it says nothing a client could not have been told at the desk.
        /// <para>
        /// It travels because the host's terminal prints it. A client filing a verdict without the
        /// note would be making the call on strictly less evidence than the player beside them, which
        /// is the co-op version of the thing hard rule 3 forbids.
        /// </para>
        /// </summary>
        public FixedString128Bytes FieldTechNote;

        /// <summary>
        /// The earlier sample this is a re-draw of, or 0 for a first draw (§5.4). A re-draw exists
        /// because the player filed MONITOR, which is their own decision reflected back.
        /// </summary>
        public int ResampleOf;

        public SampleStage Stage;

        /// <summary>False when <see cref="FiledVerdict"/> is meaningless. <c>Verdict?</c> does not serialize.</summary>
        public bool HasVerdict;

        public Verdict FiledVerdict;

        /// <summary>Day the call was filed, or -1. The §5.3 archive names it beside the verdict.</summary>
        public int FiledOnDay;

        /// <summary>
        /// Worst <i>reading</i> against the profile — measured numbers scored by published thresholds,
        /// which is what the terminal colours (§6.1). Deliberately not <see cref="FaultSeverity"/>:
        /// that one describes what is actually wrong with the oil, and only the host may know it.
        /// </summary>
        public ReadingSeverity WorstReading;

        /// <summary>
        /// At least one filed result was taken while its instrument was drifting (§5.3). This is a
        /// statement about the machine, not about the oil, which is why it is safe to send.
        /// </summary>
        public bool HasSuspectResult;

        /// <summary>The handle back, for callers matching a view to a pooled vial prop.</summary>
        public SampleId SampleId => new(Id);

        /// <summary>
        /// Project host state for replication. The only place the sample projection is written, so
        /// there is exactly one line to audit when asking what a client can see.
        /// </summary>
        public static SampleView From(SampleState state)
        {
            if (state == null) return default;

            bool suspect = false;
            foreach (var result in state.Results)
            {
                if (!result.Suspect) continue;
                suspect = true;
                break;
            }

            return new SampleView
            {
                Id = state.Id.Value,
                RecordTag = ViewText.Fixed64(state.RecordTag),
                ProfileId = ViewText.Fixed64(state.Profile != null ? state.Profile.Id : null),
                VolumeMl = state.VolumeMl,
                HoursSinceOilChange = state.HoursSinceOilChange,
                FieldTechNote = ViewText.Fixed128(state.FieldTechNote),
                ResampleOf = state.ResampleOf.Value,
                Stage = state.Stage,
                HasVerdict = state.FiledVerdict.HasValue,
                FiledVerdict = state.FiledVerdict ?? Verdict.Normal,
                FiledOnDay = state.FiledOnDay,
                WorstReading = state.WorstReading(),
                HasSuspectResult = suspect
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref RecordTag);
            serializer.SerializeValue(ref ProfileId);
            serializer.SerializeValue(ref VolumeMl);
            serializer.SerializeValue(ref HoursSinceOilChange);
            serializer.SerializeValue(ref FieldTechNote);
            serializer.SerializeValue(ref ResampleOf);
            serializer.SerializeValue(ref Stage);
            serializer.SerializeValue(ref HasVerdict);
            serializer.SerializeValue(ref FiledVerdict);
            serializer.SerializeValue(ref FiledOnDay);
            serializer.SerializeValue(ref WorstReading);
            serializer.SerializeValue(ref HasSuspectResult);
        }

        public bool Equals(SampleView other) =>
            Id == other.Id &&
            RecordTag.Equals(other.RecordTag) &&
            ProfileId.Equals(other.ProfileId) &&
            VolumeMl.Equals(other.VolumeMl) &&
            HoursSinceOilChange.Equals(other.HoursSinceOilChange) &&
            FieldTechNote.Equals(other.FieldTechNote) &&
            ResampleOf == other.ResampleOf &&
            Stage == other.Stage &&
            HasVerdict == other.HasVerdict &&
            FiledVerdict == other.FiledVerdict &&
            FiledOnDay == other.FiledOnDay &&
            WorstReading == other.WorstReading &&
            HasSuspectResult == other.HasSuspectResult;

        public override bool Equals(object obj) => obj is SampleView o && Equals(o);

        public override int GetHashCode() => Id;

        public override string ToString() => $"S{Id:D5} [{RecordTag}] {Stage}";
    }
}
