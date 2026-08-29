using System;
using System.Collections.Generic;
using Residue.Gameplay.Simulation;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// One line of a delivery note, exactly as the customer typed it: which tank they say they drew
    /// and what that tank runs (§5.1, #29).
    ///
    /// <para>
    /// <b>The claim travels; the answer does not.</b> <see cref="DeliveryNote.Line"/> also holds the
    /// <c>SampleId</c> that really answers the claim, and that field is deliberately absent here. It is
    /// host bookkeeping, it is never printed on the paper the player picks up, and it <i>is</i> #32 —
    /// a client holding it could tick off the answered rows and the reconciliation would be done for
    /// the player. A note that reconciled itself would leave nothing to find, which is the argument
    /// <see cref="DeliveryNote"/> makes for the model and which has to survive the wire or it only
    /// applies to whoever is hosting.
    /// </para>
    ///
    /// <para>
    /// A row per line rather than a blob of printed text on <see cref="CartonView"/>, for two reasons.
    /// The page can run to a dozen lines and a fixed-size string long enough for the worst one would be
    /// paid on every carton every publish. And the terminal's reconcile panel does not want a page — it
    /// wants the rows, one button each (see <c>TerminalScreen.ReconcilePanel</c>), so a client that
    /// received prose would have to parse its way back to them.
    /// </para>
    ///
    /// The client rebuilds a real <see cref="DeliveryNote"/> out of these, so the paper prop and the
    /// desk both draw the same object the host's own copies draw — one set of drawing code, and no
    /// second way to render a note that could quietly disagree with the first.
    /// </summary>
    public struct NoteLineView : INetworkSerializable, IEquatable<NoteLineView>
    {
        /// <summary>The box this line's note came in — <see cref="CartonView.Id"/>.</summary>
        public FixedString64Bytes CartonId;

        /// <summary>
        /// Where this row sits on the printed page, 0-based.
        /// <para>
        /// It travels rather than being inferred from arrival order because a discrepancy is
        /// <i>inserted</i> where it was booked rather than appended (see <c>DeliveryNote.Insert</c>),
        /// and because the terminal sends the row number back when a player registers a vial against
        /// it. A page numbered differently at the two ends would file the wrong claim.
        /// </para>
        /// </summary>
        public int Index;

        /// <summary>The tank the sender says this draw came from. May be absent on the page.</summary>
        public FixedString64Bytes TankTag;

        /// <summary>
        /// <see cref="Residue.Data.EquipmentProfileDef.Id"/> of the fluid that tank runs, resolved
        /// against the client's own tables — the same choice <see cref="SampleView.ProfileId"/> makes.
        /// </summary>
        public FixedString64Bytes ProfileId;

        /// <summary>
        /// Project a whole note for replication. The only place note lines are written, so there is
        /// one line to audit when asking what a client can read off a delivery note.
        /// </summary>
        public static void Gather(Carton carton, List<NoteLineView> into)
        {
            var note = carton != null ? carton.Note : null;
            if (note == null || into == null) return;

            for (int i = 0; i < note.Lines.Count; i++)
            {
                var line = note.Lines[i];

                into.Add(new NoteLineView
                {
                    CartonId = ViewText.Fixed64(carton.Id),
                    Index = i,
                    TankTag = ViewText.Fixed64(line.TankTag),
                    ProfileId = ViewText.Fixed64(line.Profile != null ? line.Profile.Id : null)

                    // line.Sample is not projected, and there is no field for it. See the type doc.
                });
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref CartonId);
            serializer.SerializeValue(ref Index);
            serializer.SerializeValue(ref TankTag);
            serializer.SerializeValue(ref ProfileId);
        }

        public bool Equals(NoteLineView other) =>
            CartonId.Equals(other.CartonId) &&
            Index == other.Index &&
            TankTag.Equals(other.TankTag) &&
            ProfileId.Equals(other.ProfileId);

        public override bool Equals(object obj) => obj is NoteLineView o && Equals(o);

        public override int GetHashCode() => CartonId.GetHashCode() * 397 ^ Index;

        public override string ToString() => $"{CartonId} #{Index + 1} {TankTag} [{ProfileId}]";
    }
}
