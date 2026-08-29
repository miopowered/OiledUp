using System.Collections.Generic;
using Residue.Chemistry;

namespace Residue.Gameplay.Simulation
{
    /// <summary>Where a carton is in its short life, from the host's point of view.</summary>
    public enum CartonStage
    {
        /// <summary>Generated, paid for, and still on the truck. Nothing in it can be reached (#30).</summary>
        OnTheRoad,

        /// <summary>Standing in the bay, or carried, or set down somewhere in the lab.</summary>
        Delivered,

        /// <summary>Flattened. It has no prop and nothing can be put back into it (#31).</summary>
        Discarded
    }

    /// <summary>
    /// One box off the truck: a delivery note, the vials that came under it, and a lid (#30, #31).
    ///
    /// <para>
    /// <b>One carton per note, and the note already decided the grouping.</b> <see cref="LabState"/>
    /// has issued one <see cref="DeliveryNote"/> per sender per day since #29, and a carton comes from
    /// one firm — so inventing a second way to group vials would give the lab two answers to "what
    /// arrived together" and let them disagree. The note is the grouping; this is its box.
    /// </para>
    ///
    /// <para>
    /// <b>It holds ids, not samples.</b> Where a vial actually <i>is</i> lives in
    /// <see cref="SampleState.Location"/>, exactly as it does for a rack — so a vial lifted out of a
    /// carton is out of it because its location says so, and no second list has to be kept in step.
    /// <see cref="Contents"/> is the manifest ("these arrived in this box"), which is a different and
    /// permanent fact: #32 reconciles against it after the box is empty.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing here touches the oil.</b> Opening a carton changes <see cref="IsSealed"/> and
    /// nothing else. Vials arrive cold and unsettled and stay that way — the agitation happens in the
    /// hold that loads an instrument (<c>MachineStation.HoldSeconds</c>), and a carton that quietly
    /// marked its contents ready would delete the step the whole §4.5 refusal exists for.
    /// </para>
    /// </summary>
    public sealed class Carton
    {
        /// <summary>
        /// Marks a container id as a carton rather than a rack or an instrument. Present so
        /// <c>LabCommandExecutor</c> can tell "this vial is in a box that may still be sealed" from
        /// "this vial is on a shelf", without asking the bay about every id it ever sees.
        /// </summary>
        public const string IdPrefix = "carton:";

        /// <summary>
        /// Where the paper sits while it is still in the box. Never a real slot index: the note has
        /// its own socket on the prop and does not compete with the vials for a hole.
        /// </summary>
        public const int InsideSlot = -1;

        private readonly List<SampleId> contents = new();

        public string Id { get; }
        public DeliveryNote Note { get; }

        /// <summary>The day it was booked out, which is the day it is due to arrive.</summary>
        public int Day { get; }

        public CartonStage Stage { get; internal set; } = CartonStage.OnTheRoad;

        /// <summary>Sealed until somebody holds Interact on it long enough (§5.1, #31).</summary>
        public bool IsSealed { get; internal set; } = true;

        /// <summary>
        /// Where the box is. Meaningless while <see cref="Stage"/> is
        /// <see cref="CartonStage.OnTheRoad"/>; a <see cref="SampleLocation"/> afterwards for the same
        /// reason a bottle uses one — the bay, a pair of hands and a patch of floor are the same three
        /// answers, and <c>PropSockets</c> already resolves all three.
        /// </summary>
        public SampleLocation Location;

        /// <summary>Where the delivery note is. Inside the box until somebody lifts it out (#31).</summary>
        public SampleLocation NoteLocation;

        public Carton(string id, DeliveryNote note, int day)
        {
            Id = id;
            Note = note;
            Day = day;
            NoteLocation = SampleLocation.InCrate(id, InsideSlot);
        }

        /// <summary>Everything that came in this box, whether or not it is still in it.</summary>
        public IReadOnlyList<SampleId> Contents => contents;

        public string JobNumber => Note != null ? Note.JobNumber : null;

        public string SenderName =>
            Note != null && Note.Customer != null ? Note.Customer.DisplayName : "an unnamed sender";

        /// <summary>True while the paper has not been taken out and can still be reached in the box.</summary>
        public bool NoteIsInside => NoteLocation.Kind == SampleLocationKind.InCrate;

        internal void Add(SampleId sample)
        {
            if (sample.IsValid && !contents.Contains(sample)) contents.Add(sample);
        }

        /// <summary>
        /// The container id for a delivery. Keyed on the day as well as the job number so a job
        /// number that repeats across a twenty-day contract cannot make two boxes the same box.
        /// </summary>
        public static string IdFor(int day, string jobNumber) =>
            $"{IdPrefix}{day}:{(string.IsNullOrEmpty(jobNumber) ? "unmarked" : jobNumber)}";

        public static bool IsCartonId(string containerId) =>
            !string.IsNullOrEmpty(containerId) &&
            containerId.StartsWith(IdPrefix, System.StringComparison.Ordinal);

        public override string ToString() =>
            $"{Id} [{Stage}{(IsSealed ? ", sealed" : ", open")}] {contents.Count} vial(s)";
    }
}
