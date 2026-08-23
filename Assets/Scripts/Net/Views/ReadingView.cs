using System;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// One element's measured value from one run: the number a verdict is actually argued from.
    /// <para>
    /// <b>Its own list, and that is the budget decision.</b> The obvious shape is a fixed list of
    /// pairs inside <see cref="ResultView"/>, and it does not survive contact with the rule it has to
    /// hold. <c>FixedList512Bytes</c> takes about fourteen pairs; the elemental panel is five today
    /// and the tables are meant to grow. Every fixed cap therefore needs an overflow rule, and every
    /// overflow rule is a way for the terminal to show fewer numbers than the host used — which means
    /// a player filing CRITICAL on evidence they were never shown, exactly what hard rule 3 forbids.
    /// Truncating silently would be worse still. A flat keyed list has no cap to exceed: the wire
    /// carries what the run measured, however many that turns out to be, and the only cost is four
    /// bytes of key per reading.
    /// </para>
    /// <para>
    /// It is a leaf. Nothing here says what the value <i>means</i> — thresholds, categories and units
    /// are content both sides already ship, resolved through the <c>ContentCatalog</c> against
    /// <see cref="ElementId"/>. Sending a copy of a threshold would give the lab two sources of truth
    /// for a limit, which is the one number the host and a client must never disagree about.
    /// </para>
    /// </summary>
    public struct ReadingView : INetworkSerializable, IEquatable<ReadingView>
    {
        /// <summary><see cref="ResultView.Key"/> of the run this came off.</summary>
        public int ResultKey;

        /// <summary><see cref="Residue.Data.ElementDef.Id"/>.</summary>
        public FixedString32Bytes ElementId;

        /// <summary>
        /// What the instrument read: the true value plus whatever the machine was carrying, plus
        /// noise, scaled by drift. Never the underlying figure — that one never leaves the host.
        /// </summary>
        public float Value;

        public ReadingView(int resultKey, string elementId, float value)
        {
            ResultKey = resultKey;
            ElementId = ViewText.Fixed32(elementId);
            Value = value;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ResultKey);
            serializer.SerializeValue(ref ElementId);
            serializer.SerializeValue(ref Value);
        }

        public bool Equals(ReadingView other) =>
            ResultKey == other.ResultKey &&
            ElementId.Equals(other.ElementId) &&
            Value.Equals(other.Value);

        public override bool Equals(object obj) => obj is ReadingView o && Equals(o);

        public override int GetHashCode() => ResultKey * 397 ^ ElementId.GetHashCode();

        public override string ToString() => $"R{ResultKey} {ElementId} {Value:0.###}";
    }
}
