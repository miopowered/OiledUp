using System;
using Residue.Chemistry;
using Residue.Gameplay.Simulation;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// One delivery carton: which box it is, how far through its short life it has got, where it is
    /// standing, how much is left in it, and where its paperwork has ended up.
    ///
    /// <para>
    /// <b>Why the box has to replicate at all.</b> #30 put the day's samples on a lorry and nothing
    /// carried the lorry's contents, so a joined client saw the bay and the truck and no boxes — it
    /// could not carry one in, open one, or reach a single vial. On a client the day simply never
    /// started. This is the row that ends that: the same list <see cref="VialView"/> and
    /// <see cref="SlipView"/> are, for the third kind of thing you pick up.
    /// </para>
    ///
    /// <para>
    /// <b>It carries a count, never a manifest.</b> A carton's contents are samples, and
    /// <see cref="Carton.Contents"/> is the answer to #32 — which vial arrived under which claim. So
    /// what crosses is <see cref="VialsRemaining"/>, an integer, and the bottles themselves travel
    /// where they always did, as <see cref="VialView"/> rows whose <c>ContainerId</c> names this box.
    /// A client can therefore draw the room and read the prompt ("3 vials still in it") without ever
    /// being told which three, and reconciling the note against the box stays the work the player does
    /// with the paper in their hands. Hard rule 2 is upheld here by there being no field that could
    /// express the answer.
    /// </para>
    ///
    /// <para>
    /// The note's location rides along rather than travelling as a list of its own, because there is
    /// exactly one note per carton — <see cref="Carton"/> makes that argument for the model and it
    /// holds on the wire for the same reason. What is <i>printed</i> on the paper is separate: see
    /// <see cref="NoteLineView"/>.
    /// </para>
    ///
    /// Cartons are not <c>NetworkObject</c>s, for the reason §3.2 gives about vials. Only the record
    /// travels; each client builds its own box out of it — see <c>CartonReconciler</c>.
    /// </summary>
    public struct CartonView : INetworkSerializable, IEquatable<CartonView>
    {
        /// <summary>
        /// <see cref="Carton.Id"/>, which is also the <c>ContainerId</c> a vial inside this box
        /// carries.
        /// <para>
        /// The same width as <see cref="VialView.ContainerId"/> deliberately. A narrower budget here
        /// would clip a long job number on this row and not on that one, and the two strings would
        /// stop matching — which presents as bottles that never find the box they are in, on a client
        /// only, for one customer only.
        /// </para>
        /// </summary>
        public FixedString64Bytes Id;

        /// <summary>What is printed on the outside of the box, e.g. "KH-04127".</summary>
        public FixedString64Bytes JobNumber;

        /// <summary>
        /// <see cref="Residue.Data.CustomerDef.Id"/>, not the display name — the same choice
        /// <see cref="SampleView.CustomerId"/> makes and for the same reason. The client resolves it
        /// against its own <c>ContentCatalog</c>, so a screen wanting anything other than the name has
        /// something to ask.
        /// </summary>
        public FixedString64Bytes CustomerId;

        /// <summary>The day the delivery was booked out, as printed at the head of its note.</summary>
        public int Day;

        /// <summary>On the truck, or in the lab. A flattened box is dropped from the list instead.</summary>
        public CartonStage Stage;

        /// <summary>Still taped. What <c>CartonLid</c> offers a hold to change (§5.1, #31).</summary>
        public bool IsSealed;

        /// <summary>
        /// How many bottles are still physically inside. Counted host-side off the vials' own
        /// locations (<see cref="DeliveryBay.RemainingIn"/>), so the box and the bottles cannot
        /// disagree — and it is a count rather than a list for the reason the type doc gives.
        /// </summary>
        public int VialsRemaining;

        // -- Location, flattened ---------------------------------------------------------------------
        //
        // SampleLocation holds a managed string, so it cannot ride in a NetworkList directly. Split
        // rather than wrapped, for the reason VialView gives.

        public SampleLocationKind Kind;
        public ulong HolderClientId;
        public FixedString64Bytes ContainerId;
        public int SlotIndex;

        // -- The note's location, flattened ------------------------------------------------------------
        //
        // The paper is a second carryable with a life of its own: it starts in the box, comes out, gets
        // walked to a bench and left there. Without this a client's note stayed glued inside the carton
        // however far another player had carried it.

        public SampleLocationKind NoteKind;
        public ulong NoteHolderClientId;
        public FixedString64Bytes NoteContainerId;
        public int NoteSlotIndex;

        /// <summary>Rebuild the location this record describes.</summary>
        public SampleLocation Location => new()
        {
            Kind = Kind,
            HolderClientId = HolderClientId,
            ContainerId = ContainerId.IsEmpty ? null : ContainerId.ToString(),
            SlotIndex = SlotIndex
        };

        /// <inheritdoc cref="Location"/>
        public SampleLocation NoteLocation => new()
        {
            Kind = NoteKind,
            HolderClientId = NoteHolderClientId,
            ContainerId = NoteContainerId.IsEmpty ? null : NoteContainerId.ToString(),
            SlotIndex = NoteSlotIndex
        };

        /// <summary>True while the paper has not been lifted out — <see cref="Carton.NoteIsInside"/>.</summary>
        public bool NoteIsInside => NoteKind == SampleLocationKind.InCrate;

        /// <summary>
        /// Project host state for replication. The only place the carton projection is written, so
        /// there is one line to audit when asking what a client can see of a delivery.
        /// <para>
        /// Note what is <b>not</b> a parameter: the <see cref="Carton"/>'s manifest. The count comes in
        /// as an integer the caller has already reduced, so there is no signature through which the
        /// list of sample ids could start travelling.
        /// </para>
        /// </summary>
        public static CartonView From(Carton carton, int vialsRemaining)
        {
            if (carton == null) return default;

            var location = carton.Location;
            var note = carton.NoteLocation;
            var customer = carton.Note != null ? carton.Note.Customer : null;

            return new CartonView
            {
                Id = ViewText.Fixed64(carton.Id),
                JobNumber = ViewText.Fixed64(carton.JobNumber),
                CustomerId = ViewText.Fixed64(customer != null ? customer.Id : null),
                Day = carton.Day,
                Stage = carton.Stage,
                IsSealed = carton.IsSealed,
                VialsRemaining = vialsRemaining,
                Kind = location.Kind,
                HolderClientId = location.HolderClientId,
                ContainerId = ViewText.Fixed64(location.ContainerId),
                SlotIndex = location.SlotIndex,
                NoteKind = note.Kind,
                NoteHolderClientId = note.HolderClientId,
                NoteContainerId = ViewText.Fixed64(note.ContainerId),
                NoteSlotIndex = note.SlotIndex
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref JobNumber);
            serializer.SerializeValue(ref CustomerId);
            serializer.SerializeValue(ref Day);
            serializer.SerializeValue(ref Stage);
            serializer.SerializeValue(ref IsSealed);
            serializer.SerializeValue(ref VialsRemaining);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref HolderClientId);
            serializer.SerializeValue(ref ContainerId);
            serializer.SerializeValue(ref SlotIndex);
            serializer.SerializeValue(ref NoteKind);
            serializer.SerializeValue(ref NoteHolderClientId);
            serializer.SerializeValue(ref NoteContainerId);
            serializer.SerializeValue(ref NoteSlotIndex);
        }

        public bool Equals(CartonView other) =>
            Id.Equals(other.Id) &&
            JobNumber.Equals(other.JobNumber) &&
            CustomerId.Equals(other.CustomerId) &&
            Day == other.Day &&
            Stage == other.Stage &&
            IsSealed == other.IsSealed &&
            VialsRemaining == other.VialsRemaining &&
            Kind == other.Kind &&
            HolderClientId == other.HolderClientId &&
            ContainerId.Equals(other.ContainerId) &&
            SlotIndex == other.SlotIndex &&
            NoteKind == other.NoteKind &&
            NoteHolderClientId == other.NoteHolderClientId &&
            NoteContainerId.Equals(other.NoteContainerId) &&
            NoteSlotIndex == other.NoteSlotIndex;

        public override bool Equals(object obj) => obj is CartonView o && Equals(o);

        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() =>
            $"{Id} [{Stage}{(IsSealed ? ", sealed" : ", open")}] {VialsRemaining} left, " +
            $"{Kind}({ContainerId}#{SlotIndex})";
    }
}
