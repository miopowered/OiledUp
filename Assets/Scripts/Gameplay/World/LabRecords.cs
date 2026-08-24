using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Everything the terminal draws in one rebuild: the open queue, the records already in doubt,
    /// the instruments, the books and the root causes on the dropdown.
    /// <para>
    /// <b>Why a snapshot rather than a view interface.</b> Single player and the host read their own
    /// <see cref="LabState"/> and must keep doing so — a screen that went through a replication seam
    /// on a machine that is simulating would draw a publish behind its own lab, and would put a wire
    /// format in the middle of a game that has no wire. So this is a plain bundle of the objects the
    /// screen already knew how to draw, gathered once per rebuild: <see cref="FromHost"/> points them
    /// straight at the live lab, and <c>Residue.Net</c> fills the same fields from replicated views on
    /// a client. One set of drawing code, one set of rows, no branch on session state below this line.
    /// </para>
    /// <para>
    /// The samples are real <see cref="SampleState"/> objects on both sides. That is the type's own
    /// contract — "everything about a sample that a client is allowed to know", with ground truth
    /// living in a separate class the wire cannot express — and <see cref="SampleLifecycle"/> already
    /// anticipates a client deriving the stage from one.
    /// </para>
    /// </summary>
    public sealed class LabRecords
    {
        /// <summary>1-based. Zero before the first day begins.</summary>
        public int Day;

        /// <summary>Contract finished, or the money ran out (§1.2).</summary>
        public bool IsRunOver;

        /// <summary>
        /// A shift is open. False in the gap between shifts, which is when the desk shows the day's
        /// reckoning instead of its queue — see <see cref="Reports"/>.
        /// </summary>
        public bool DayInProgress;

        /// <summary>The contract's name and its length in days, for the line the run closes on.</summary>
        public string ContractName = "";

        public int ContractLength;

        public float Money;
        public float Reputation;
        public float SolventUnits;
        public int ReferenceStandards;

        /// <summary>What the outpost opened with, and what has moved since. Closing screen only.</summary>
        public float StartingMoney;
        public float TotalEarned;
        public float TotalLost;

        /// <summary>Price of one solvent unit and of one certified ampoule. The ORDER buttons quote these.</summary>
        public float SolventUnitCost;
        public float ReferenceStandardUnitCost;

        /// <summary>Printed on the certificate, so the figures on it can be looked up in the manual.</summary>
        public string StandardId;

        /// <summary>Arrived, no verdict filed. Sorted by id, which is arrival order.</summary>
        public IReadOnlyList<SampleState> Open = new List<SampleState>();

        /// <summary>Filed records whose numbers came off an instrument later found to be drifting (§5.3).</summary>
        public IReadOnlyList<SampleState> InDoubt = new List<SampleState>();

        public IReadOnlyList<InstrumentRecord> Instruments = new List<InstrumentRecord>();

        /// <summary>
        /// The verdicts that came due at the last day end (§4.3, §5.4). Empty while a shift is open.
        /// <para>
        /// These reach a joined desk too, because everybody worked the shift and everybody should see
        /// the reckoning. What crosses is not the host's <see cref="ConsequenceReport"/> but a
        /// projection of it — <c>Residue.Net.Views.ReportView</c> — which carries the outcome, the
        /// money and the sentence, and withholds the diagnosis of any unit that is about to come round
        /// again. The reasoning for that lives on the view, next to the code that enforces it.
        /// </para>
        /// A report rebuilt from the wire has no <see cref="ConsequenceReport.FaultName"/> and no
        /// <see cref="ConsequenceReport.ActualRootCause"/> at all: whatever a client is allowed to
        /// know is already in the headline, and a field nothing draws is a field that leaks the day
        /// somebody draws it.
        /// </summary>
        public IReadOnlyList<ConsequenceReport> Reports = new List<ConsequenceReport>();

        /// <summary>Root causes for the verdict dropdown. Content, identical in every process.</summary>
        public IReadOnlyList<RootCauseDef> Causes = new List<RootCauseDef>();

        /// <summary>The sample under that id, or null. Looks in both lists — a re-opened record is in neither for long.</summary>
        public SampleState Sample(SampleId id)
        {
            foreach (var s in Open)
            {
                if (s != null && s.Id == id) return s;
            }
            foreach (var s in InDoubt)
            {
                if (s != null && s.Id == id) return s;
            }
            return null;
        }

        /// <summary>
        /// The least oil that could repeat one of this record's suspect tests, or
        /// <see cref="float.PositiveInfinity"/> if nothing on this bench can repeat any of them.
        /// <para>
        /// The same rule <see cref="LabState.SmallestSuspectDraw"/> applies at the gate, read here for
        /// the screen. It is worth the duplicate loop: the number is how the player finds out
        /// <i>before</i> pressing the button that a record can never be checked again, and a client
        /// has no <see cref="LabState"/> to ask. If the two ever disagreed the host's refusal is what
        /// happens, and the player reads it verbatim.
        /// </para>
        /// </summary>
        public float SmallestReTestDraw(SampleState sample)
        {
            float smallest = float.PositiveInfinity;
            if (sample == null) return smallest;

            foreach (var result in sample.Results)
            {
                if (!result.Suspect) continue;

                foreach (var instrument in Instruments)
                {
                    if (instrument?.Def == null || instrument.Def.Id != result.MachineId) continue;
                    if (instrument.Def.SampleVolumeMl < smallest) smallest = instrument.Def.SampleVolumeMl;
                }
            }

            return smallest;
        }

        /// <summary>Gather this process's own lab. Single player and host; no view layer involved.</summary>
        public static LabRecords FromHost(LabState lab)
        {
            if (lab == null) return null;

            var instruments = new List<InstrumentRecord>(lab.Machines.Count);
            foreach (var machine in lab.Machines) instruments.Add(InstrumentRecord.FromHost(machine));

            return new LabRecords
            {
                Day = lab.Day,
                IsRunOver = lab.IsRunOver,
                DayInProgress = lab.DayInProgress,
                ContractName = lab.Plan != null ? lab.Plan.DisplayName : "",
                ContractLength = lab.Plan != null ? lab.Plan.Length : 0,
                Money = lab.Economy.Money,
                Reputation = lab.Economy.Reputation,
                SolventUnits = lab.Economy.SolventUnits,
                ReferenceStandards = lab.Economy.ReferenceStandards,
                StartingMoney = lab.Tuning != null ? lab.Tuning.StartingMoney : 0f,
                TotalEarned = lab.Economy.TotalEarned,
                TotalLost = lab.Economy.TotalLost,
                SolventUnitCost = lab.Economy.SolventCost(1),
                ReferenceStandardUnitCost = lab.Economy.ReferenceStandardCost(1),
                StandardId = lab.Standard != null ? lab.Standard.Id : "",
                Open = lab.OpenSamples(),
                InDoubt = lab.Samples.SuspectArchive(),
                Instruments = instruments,

                // Straight off the lab. Single player and the host read their own reports and always
                // did; nothing here goes near a view (see the type doc).
                Reports = lab.LastReports,
                Causes = lab.Content != null ? lab.Content.Causes : new List<RootCauseDef>()
            };
        }
    }
}
