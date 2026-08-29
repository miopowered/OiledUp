using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// The paperwork that arrives with a carton: who sent it, under what job number, and what they
    /// say is in the box (§5.1, #29).
    ///
    /// <para>
    /// <b>The note is a claim, not a fact.</b> Every line is what the customer says they sent, and
    /// nothing here checks it against what actually arrived — that comparison is the whole of #32 and
    /// it belongs to the player, not to the model. Keeping the two apart is what makes a discrepancy
    /// discoverable rather than announced: the note is the detectable tell, exactly as a blank run is
    /// for contamination (hard rule 3). A model that reconciled itself would leave nothing to find.
    /// </para>
    ///
    /// <para>
    /// <b>Lines carry the sample they refer to, and it may be absent.</b> <see cref="Line.Sample"/> is
    /// <see cref="SampleId.None"/> for a line whose vial never turned up, and a vial that arrived with
    /// no line is simply not in <see cref="Lines"/>. Both shapes are expressible today so that #32 can
    /// produce them without reshaping the note — but nothing generates either yet, and a note built by
    /// <see cref="For"/> always matches its carton exactly.
    /// </para>
    ///
    /// <para>
    /// Customer and profile are held as definitions rather than ids because a note is a runtime object
    /// that lives for a day, not a saved record. What persists is on <see cref="SampleState"/>, and
    /// that stores ids — a save must not pin a copy of the balance tables.
    /// </para>
    /// </summary>
    public sealed class DeliveryNote
    {
        /// <summary>One claimed vial: the tank it was drawn from and the fluid that tank runs.</summary>
        public readonly struct Line
        {
            public readonly string TankTag;
            public readonly EquipmentProfileDef Profile;

            /// <summary>The vial that answers this line, or <see cref="SampleId.None"/> if none did.</summary>
            public readonly SampleId Sample;

            public Line(string tankTag, EquipmentProfileDef profile, SampleId sample)
            {
                TankTag = tankTag;
                Profile = profile;
                Sample = sample;
            }

            /// <summary>False for a line the carton did not answer — #32's "missing sample".</summary>
            public bool Arrived => Sample.IsValid;

            public override string ToString() =>
                $"{TankTag} [{(Profile != null ? Profile.Id : "?")}] {(Arrived ? Sample.ToString() : "MISSING")}";
        }

        private readonly List<Line> lines = new();

        public CustomerDef Customer { get; }
        public string JobNumber { get; }

        /// <summary>The day the delivery was booked out by the customer, not the day it was opened.</summary>
        public int Day { get; }

        public IReadOnlyList<Line> Lines => lines;

        public int Count => lines.Count;

        public DeliveryNote(CustomerDef customer, string jobNumber, int day)
        {
            Customer = customer;
            JobNumber = jobNumber;
            Day = day;
        }

        public void Add(in Line line) => lines.Add(line);

        public void Add(string tankTag, EquipmentProfileDef profile, SampleId sample) =>
            lines.Add(new Line(tankTag, profile, sample));

        /// <summary>
        /// The line naming a tank, or a line with no tag if this note does not mention it. Used by the
        /// reconciliation UI to answer "is this vial on the paper at all".
        /// <para>
        /// Matched on the tag rather than the sample id on purpose: a vial whose label the player is
        /// reading is identified by what is printed on it, which is the same thing they are comparing
        /// against the note. Matching by id would answer a question the player cannot ask.
        /// </para>
        /// </summary>
        public bool TryFind(string tankTag, out Line found)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (!string.Equals(lines[i].TankTag, tankTag, System.StringComparison.Ordinal)) continue;
                found = lines[i];
                return true;
            }

            found = default;
            return false;
        }

        /// <summary>
        /// How many lines name the same tank. Two is §6.1's same-drum trap as it appears on paper;
        /// the readings are what confirm it, which is what keeps it fair.
        /// </summary>
        public int CountFor(string tankTag)
        {
            int count = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (string.Equals(lines[i].TankTag, tankTag, System.StringComparison.Ordinal)) count++;
            }
            return count;
        }

        public override string ToString() =>
            $"{JobNumber} {(Customer != null ? Customer.DisplayName : "unknown sender")} " +
            $"day {Day}, {lines.Count} line(s)";
    }
}
