using Residue.Chemistry;
using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A results slip produced by an instrument. Physical, carryable, and losable.
    /// <para>
    /// Instruments used to write straight into the sample's history, which meant the numbers
    /// teleported to the terminal and the room was decoration. A printout has to be walked to the
    /// desk, and it competes with vials for your one pair of hands. §9 lists "too much reading, not
    /// enough doing" as a live risk; this is the fix.
    /// </para>
    /// <para>
    /// A slip you drop and forget is data you paid for and did not get. That is deliberate — but it
    /// is only fair because the machine's own display shows the same values (see
    /// <see cref="MachineDisplay"/>), so nothing is hidden that could not be read.
    /// </para>
    /// <para>
    /// <b>On a client it holds no <see cref="TestResult"/> at all.</b> The paper is a local prop
    /// (§3.2) and only the record travels, so a replicated slip is bound to a
    /// <see cref="ResultKey"/> — the identity <c>ResultView.Key</c> already gives a finished run —
    /// and fetches the numbers through <see cref="SlipFeed.Numbers"/> the one time anybody reads
    /// them. The values therefore reach a client exactly once, in the list the terminal and the
    /// instrument screens already draw; a copy on the slip as well would be a second wire path to the
    /// same figures, and the day the two disagreed the paper in your hand and the panel at the desk
    /// would quote different results for one run.
    /// </para>
    /// </summary>
    public sealed class PrintoutProp : Carryable
    {
        [SerializeField] private MeshRenderer paper;

        /// <summary>
        /// The physical text baked onto <see cref="paper"/> (#82) — see <see cref="PrintedSheetSurface"/>
        /// for why it is its own overlay rather than a texture swap on that renderer's material. Built
        /// once, on the first bind that finds a real sheet, and redrawn on every bind after; several
        /// EditMode fixtures bind a bare prop with no sheet at all, which this and
        /// <see cref="RenderOntoPaper"/> treat as "nothing to write on yet" rather than an error.
        /// </summary>
        private PrintedSheetSurface surface;

        /// <summary>
        /// The host's handle for this slip (<see cref="Residue.Gameplay.Simulation.ResultSlips"/>).
        /// <para>
        /// Filing names the ticket and never the values. §3.1 forbids a client computing a test
        /// result, and a client that could post one instead would be the same hole with an extra
        /// step — so the numbers below are for reading at a glance, and the ticket is what the
        /// terminal actually sends.
        /// </para>
        /// </summary>
        public int Ticket { get; private set; }

        /// <summary>
        /// Which run this slip reports, as <c>ResultView.Key</c>. Zero on a host, which is holding the
        /// run itself and has no need to name it.
        /// </summary>
        public int ResultKey { get; private set; }

        public SampleId SampleId { get; private set; }

        /// <summary>
        /// A solvent blank — the instrument's own carryover rather than anyone's oil (§5.2).
        /// <para>
        /// A replicated flag rather than <c>Result.IsBlank</c>, because prompts ask this every frame a
        /// player is looking at the slip and <see cref="Result"/> is a lookup. More to the point: a
        /// client whose numbers have not arrived would read a blank slip as a sample one and offer to
        /// file it against a record.
        /// </para>
        /// </summary>
        public bool IsBlank { get; private set; }

        public string MachineName { get; private set; } = "instrument";

        /// <summary>
        /// The name the run was printed under — <see cref="SampleState.RecordTag"/>, so the paper
        /// and the terminal row agree. "BLANK" or "CERT STANDARD" for a run that belongs to the
        /// instrument rather than to a sample.
        /// </summary>
        public string RecordTag { get; private set; } = "UNKNOWN";

        private TestResult result;

        /// <summary>
        /// What the slip says, or null when this process has not been told — a client whose
        /// <see cref="ResultKey"/> has not arrived, or whose session has gone away.
        /// <para>
        /// Resolved on demand and then kept: a finished run's numbers never change, and the only
        /// caller is a player glancing at the paper. Re-attempted while it is still null, because the
        /// slip and the run it names are published in the same pass and a client can hold one before
        /// the other has landed.
        /// </para>
        /// </summary>
        public TestResult Result
        {
            get
            {
                if (result != null || ResultKey == 0) return result;

                var numbers = SlipFeed.Numbers;
                if (numbers != null && numbers(ResultKey, out var fetched)) result = fetched;
                return result;
            }
        }

        public override string DisplayName =>
            PromptStrings.PrintoutName.Format(("machine", MachineName), ("tag", RecordTag));

        /// <summary>
        /// Bind the paper to the run it came off. The host's half: it is holding the
        /// <see cref="TestResult"/> because it produced it.
        /// </summary>
        public void Bind(int ticket, SampleId sampleId, TestResult result, string machineName,
                         string recordTag)
        {
            this.result = result;
            ResultKey = 0;
            IsBlank = result != null && result.IsBlank;
            Describe(ticket, sampleId, machineName, recordTag);
        }

        /// <summary>
        /// Bind the paper to a run it can only name. The client's half — see the type doc for why the
        /// values do not travel on the slip.
        /// <para>
        /// Called every reconcile pass rather than only on spawn, so a key that arrives a publish
        /// late still reaches the prop. The cached reading is dropped only if the key actually
        /// changed, which for one piece of paper it never does; re-fetching it every frame would
        /// throw away the whole point of resolving on demand.
        /// </para>
        /// </summary>
        public void Bind(int ticket, int resultKey, SampleId sampleId, bool isBlank, string machineName,
                         string recordTag)
        {
            if (ResultKey != resultKey) result = null;

            ResultKey = resultKey;
            IsBlank = isBlank;
            Describe(ticket, sampleId, machineName, recordTag);
        }

        private void Describe(int ticket, SampleId sampleId, string machineName, string recordTag)
        {
            Ticket = ticket;
            SampleId = sampleId;
            MachineName = string.IsNullOrEmpty(machineName) ? "instrument" : machineName;
            RecordTag = string.IsNullOrEmpty(recordTag) ? "UNKNOWN" : recordTag;
            name = $"Printout_{sampleId}_{MachineName}";
            RenderOntoPaper();
        }

        /// <summary>
        /// Put what <see cref="BuildReadingText"/> already says onto the physical slip. Reusing that
        /// method rather than composing a second layout keeps the paper and the bottom-left HUD
        /// overlay (kept deliberately — see the type doc) reading the same values by construction,
        /// including the "not through yet" and "this paper is blank" placeholders for a client still
        /// waiting on <see cref="ResultKey"/> to resolve.
        /// <para>
        /// Called from <see cref="Describe"/>, i.e. on every bind — including the reconcile passes a
        /// client's slip gets re-bound on — so a reading that was not available yet gets drawn onto the
        /// paper the moment it is.
        /// </para>
        /// </summary>
        private void RenderOntoPaper()
        {
            if (surface == null)
            {
                if (paper == null) return;
                surface = new PrintedSheetSurface(paper, name);
            }

            surface.Draw(BuildReadingText());
        }

        private void OnDestroy() => surface?.Dispose();

        public override string Prompt(PlayerInteractor player)
        {
            if (!player.InventoryHasSpace) return PromptStrings.InventoryFull.Text;
            return IsBlank
                ? PromptStrings.PrintoutTakeBlank.Format(("machine", MachineName))
                : PromptStrings.PrintoutTake.Format(("tag", RecordTag));
        }

        public override string UseHint => PromptStrings.PrintoutUseHint.Text;

        public override string InspectionText => BuildReadingText();

        /// <summary>
        /// Tips the sheet up towards the camera in the inspection view, the same way
        /// <c>DeliveryNoteProp</c> and <c>ReferenceBook</c> do for the same reason: <see cref="paper"/>
        /// is a flat, thin box whose readable face is its local up, and the default identity rotation
        /// would present that face edge-on rather than facing the player. Added alongside #82's baked
        /// text — a face nobody is looking at is not a fixable typography problem.
        /// </summary>
        public override Quaternion InspectionRotation => Quaternion.Euler(-90f, 0f, 0f);

        public override Quaternion InventoryIconRotation => InspectionRotation;

        /// <summary>Glance at the slip without walking to the desk. Reading is not filing.</summary>
        public override void UseInHand(PlayerInteractor player)
        {
            player.Say(BuildReadingText(), 6f);
        }

        private string BuildReadingText()
        {
            var reading = Result;
            if (reading == null)
                return ResultKey == 0
                    ? PromptStrings.PrintoutPaperBlank.Text
                    : PromptStrings.PrintoutNumbersPending.Text;

            var text = new System.Text.StringBuilder();
            text.AppendLine(reading.IsBlank
                ? PromptStrings.PrintoutHeadingBlank.Format(("machine", MachineName))
                : PromptStrings.PrintoutHeading.Format(("tag", RecordTag), ("machine", MachineName)));
            text.AppendLine();

            // Element id and figure, in a fixed column. Data rather than a sentence — an element id
            // put through a translation table is a lookup that fails in one language only.
            foreach (var kv in reading.Values) text.AppendLine($"{kv.Key,-10} {kv.Value:0.###}");

            if (reading.Values.Count == 0) text.Append(PromptStrings.PrintoutNoValues.Text);
            return text.ToString();
        }
    }
}
