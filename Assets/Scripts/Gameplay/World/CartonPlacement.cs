using Residue.Chemistry;
using Residue.Gameplay.Simulation;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// One delivery carton, as the room needs it: which box, how far through its life, where it is
    /// standing, how much is left in it and where its paperwork has got to.
    /// <para>
    /// The vocabulary <see cref="CartonFeed"/> speaks, and deliberately <i>not</i>
    /// <c>Residue.Net</c>'s <c>CartonView</c> — the same split <see cref="SlipPlacement"/> and
    /// <see cref="BottlePlacement"/> make, for the same reason. <c>Residue.Gameplay</c> cannot see the
    /// netcode layer and must not (CLAUDE.md's assembly diagram), so the replicated record is
    /// translated at the boundary and everything downstream is the code a host runs too.
    /// </para>
    /// <para>
    /// <b>A count, not a manifest.</b> <see cref="VialsRemaining"/> is how much is still in the box;
    /// <i>which</i> bottles those are is not here and does not cross the wire, because that is the
    /// answer to #32 and it belongs to the player. The bottles place themselves — each one's own
    /// record names this carton as its container, which is what <c>VialReconciler</c> already reads.
    /// </para>
    /// <para>
    /// <see cref="Note"/> is the one managed thing on this struct, and it is here rather than as a
    /// printed string because both readers want the object: <c>DeliveryNoteProp.Printed</c> types the
    /// page out of it, and the terminal's reconcile panel wants the rows one at a time. On a client it
    /// is rebuilt from the published lines with every <c>Line.Sample</c> left empty — see
    /// <c>NoteLineView</c>.
    /// </para>
    /// </summary>
    public readonly struct CartonPlacement
    {
        /// <summary>The host's handle for this box, which is also its fixture id in the room.</summary>
        public readonly string Id;

        public readonly string JobNumber;

        /// <summary>Who sent it, already resolved to a name a prompt can print.</summary>
        public readonly string SenderName;

        public readonly CartonStage Stage;

        /// <summary>Still taped. What <see cref="CartonLid"/> offers a hold to change (#31).</summary>
        public readonly bool IsSealed;

        /// <summary>Bottles still physically inside. See the type doc for why this is a number.</summary>
        public readonly int VialsRemaining;

        /// <summary>The host's own record of where the box is.</summary>
        public readonly SampleLocation Location;

        /// <summary>The host's own record of where the delivery note is.</summary>
        public readonly SampleLocation NoteLocation;

        /// <summary>What the customer wrote, or null if this process has not been told yet.</summary>
        public readonly DeliveryNote Note;

        public CartonPlacement(string id, string jobNumber, string senderName, CartonStage stage,
                               bool isSealed, int vialsRemaining, SampleLocation location,
                               SampleLocation noteLocation, DeliveryNote note)
        {
            Id = id;
            JobNumber = jobNumber;
            SenderName = senderName;
            Stage = stage;
            IsSealed = isSealed;
            VialsRemaining = vialsRemaining;
            Location = location;
            NoteLocation = noteLocation;
            Note = note;
        }

        /// <summary>False for the default value, which is what "no such box here" looks like.</summary>
        public bool Exists => !string.IsNullOrEmpty(Id);

        /// <summary>True while the paper has not been lifted out (<see cref="Carton.NoteIsInside"/>).</summary>
        public bool NoteIsInside => NoteLocation.Kind == SampleLocationKind.InCrate;

        /// <summary>Standing in one of the bay's marked places, which is what keeps the truck there.</summary>
        public bool IsStandingInBay =>
            Stage == CartonStage.Delivered &&
            Location.Kind == SampleLocationKind.OnSurface &&
            string.Equals(Location.ContainerId, DeliveryBay.BayId, System.StringComparison.Ordinal);

        /// <summary>
        /// Project this process's own box into the same record the wire carries, so a host and a
        /// client hand the props identical facts. The count is passed in rather than read off the
        /// carton for the reason the type doc gives — see <see cref="DeliveryBay.RemainingIn"/>.
        /// </summary>
        public static CartonPlacement From(Carton carton, int vialsRemaining) =>
            carton == null
                ? default
                : new CartonPlacement(carton.Id, carton.JobNumber, carton.SenderName, carton.Stage,
                                      carton.IsSealed, vialsRemaining, carton.Location,
                                      carton.NoteLocation, carton.Note);

        public override string ToString() =>
            $"{Id} [{Stage}{(IsSealed ? ", sealed" : ", open")}] {VialsRemaining} left, {Location}";
    }
}
