using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using UnityEngine;

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
    /// no line is simply not in <see cref="Lines"/>. Both shapes are now produced:
    /// <see cref="LabState"/> introduces them at day start from the sender's own propensities — see
    /// <see cref="DeliveryDiscrepancies"/>.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="Line.Sample"/> is host bookkeeping and is never printed.</b> It is how the lab
    /// knows which vial answers which claim; the paper carries a tank tag and a fluid, exactly as a
    /// real dispatch note does. See <c>DeliveryNoteProp.Printed</c> for what the player actually reads.
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
        /// Put a line somewhere other than the end.
        /// <para>
        /// Used only by <see cref="LabState"/> when it introduces a discrepancy (#32). A note is
        /// printed in the order the dispatcher booked the draws, so a claim the box does not answer —
        /// or a second draw from a tank already listed — belongs wherever it was booked, not tacked
        /// onto the bottom. Always-last would be a pattern the player learns in three deliveries, and
        /// then the reconciliation is a glance at one row instead of a read of the whole page.
        /// </para>
        /// <para>
        /// Callers that recorded a line index before inserting have to recompute it. Indices are a
        /// property of the printed page, not of a sample.
        /// </para>
        /// </summary>
        internal void Insert(int index, string tankTag, EquipmentProfileDef profile, SampleId sample) =>
            lines.Insert(Mathf.Clamp(index, 0, lines.Count), new Line(tankTag, profile, sample));

        /// <summary>Which line a vial answers, or -1 for one this note never mentioned.</summary>
        public int IndexOf(SampleId sample)
        {
            if (!sample.IsValid) return -1;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Sample == sample) return i;
            }
            return -1;
        }

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
