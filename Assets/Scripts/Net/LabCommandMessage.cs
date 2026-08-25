using System;
using Residue.Chemistry;
using Residue.Gameplay.World;
using Residue.Net.Views;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net
{
    /// <summary>
    /// A <see cref="LabCommand"/> in the shape the wire takes.
    /// <para>
    /// One message for all twenty actions, mirroring the single flat command type it carries. The
    /// alternative — an RPC per action — puts twenty entry points on <see cref="LabNetwork"/>, and
    /// every one of them is a place where the authorisation could be forgotten. With one, there is
    /// one door and it is impossible to add an action that bypasses it.
    /// </para>
    /// <para>
    /// <b>It cannot express a measured value, and that is the point.</b> §3.1 forbids a client
    /// computing a test result; a client permitted to post one would be the same hole with an extra
    /// step. There is no field here a number could travel in — filing names a slip by its ticket, and
    /// the host looks the numbers up in its own <c>ResultSlips</c>. Hard rule 2 holds for the same
    /// structural reason it holds in <see cref="Views.SampleView"/>: nothing in this type can express
    /// a <c>SampleGroundTruth</c>, so no amount of plumbing can put one on the wire.
    /// </para>
    /// Text is <c>FixedString</c> because NGO's serializer will not take a managed string; anything
    /// longer than the budget is truncated rather than thrown, for the reason
    /// <see cref="ViewText"/> gives.
    /// </summary>
    public struct LabCommandMessage : INetworkSerializable
    {
        /// <summary>
        /// <see cref="LabCommandKind"/>. A byte rather than the enum so an unrecognised value from a
        /// modified client arrives as data to be checked rather than as a cast that has already
        /// happened — see <see cref="ToCommand"/>.
        /// </summary>
        public byte Kind;

        public FixedString64Bytes FixtureId;

        /// <summary><see cref="SampleId.Value"/>.</summary>
        public int Sample;

        public int Amount;

        public FixedString64Bytes Text;

        public static LabCommandMessage From(LabCommand command) => new()
        {
            Kind = (byte)command.Kind,
            FixtureId = ViewText.Fixed64(command.FixtureId),
            Sample = command.Sample.Value,
            Amount = command.Amount,
            Text = ViewText.Fixed64(command.Text)
        };

        /// <summary>
        /// Unpack, treating everything in here as hostile. An out-of-range <see cref="Kind"/> becomes
        /// <see cref="LabCommandKind.None"/>, which the executor refuses — rather than a cast to
        /// whichever action happens to sit at that number.
        /// </summary>
        public LabCommand ToCommand()
        {
            var kind = Enum.IsDefined(typeof(LabCommandKind), (int)Kind)
                ? (LabCommandKind)Kind
                : LabCommandKind.None;

            return new LabCommand(kind, FixtureId.ToString(), new SampleId(Sample), Amount,
                                  Text.ToString());
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref FixtureId);
            serializer.SerializeValue(ref Sample);
            serializer.SerializeValue(ref Amount);
            serializer.SerializeValue(ref Text);
        }

        public override string ToString() => $"cmd {Kind} [{FixtureId}] S{Sample} {Amount} \"{Text}\"";
    }
}
