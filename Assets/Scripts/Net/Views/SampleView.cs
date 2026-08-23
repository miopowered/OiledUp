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
    /// Note what is absent and why. <see cref="SampleState.EquipmentTag"/> — the paper label — is not
    /// here; <see cref="RecordTag"/> carries what the player typed instead, so a mis-logged vial
    /// travels under the tank they named (§5.1). <see cref="SampleState.IsMislogged"/> is not here
    /// either: both halves of that comparison are readable in the world, but only one of them is
    /// readable on a screen, and a client that could diff them would be handed the answer to a
    /// mistake the design wants found by walking back to the bottle.
    /// </para>
    /// A struct, and <c>IEquatable</c>, so it is legal inside a <c>NetworkList</c> — §3.2 rules out a
    /// NetworkObject per vial, which makes a list of these the shape the sample roster has to take.
    /// </summary>
    public struct SampleView : INetworkSerializable, IEquatable<SampleView>
    {
        /// <summary><see cref="Chemistry.SampleId.Value"/>. Local vial props are keyed on this (§3.2).</summary>
        public int Id;

        /// <summary>What the terminal calls this sample: the tag the player typed, or an unlogged placeholder.</summary>
        public FixedString64Bytes RecordTag;

        /// <summary><see cref="EquipmentProfileDef.Id"/>, so the client can look up thresholds it already ships.</summary>
        public FixedString64Bytes ProfileId;

        /// <summary>Millilitres left. §4.5's whole test-ordering decision is about this number.</summary>
        public float VolumeMl;

        public SampleStage Stage;

        public bool IsLogged;

        /// <summary>False when <see cref="FiledVerdict"/> is meaningless. <c>Verdict?</c> does not serialize.</summary>
        public bool HasVerdict;

        public Verdict FiledVerdict;

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
                Stage = state.Stage,
                IsLogged = state.IsLogged,
                HasVerdict = state.FiledVerdict.HasValue,
                FiledVerdict = state.FiledVerdict ?? Verdict.Normal,
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
            serializer.SerializeValue(ref Stage);
            serializer.SerializeValue(ref IsLogged);
            serializer.SerializeValue(ref HasVerdict);
            serializer.SerializeValue(ref FiledVerdict);
            serializer.SerializeValue(ref WorstReading);
            serializer.SerializeValue(ref HasSuspectResult);
        }

        public bool Equals(SampleView other) =>
            Id == other.Id &&
            RecordTag.Equals(other.RecordTag) &&
            ProfileId.Equals(other.ProfileId) &&
            VolumeMl.Equals(other.VolumeMl) &&
            Stage == other.Stage &&
            IsLogged == other.IsLogged &&
            HasVerdict == other.HasVerdict &&
            FiledVerdict == other.FiledVerdict &&
            WorstReading == other.WorstReading &&
            HasSuspectResult == other.HasSuspectResult;

        public override bool Equals(object obj) => obj is SampleView o && Equals(o);

        public override int GetHashCode() => Id;

        public override string ToString() => $"S{Id:D5} [{RecordTag}] {Stage}";
    }
}
