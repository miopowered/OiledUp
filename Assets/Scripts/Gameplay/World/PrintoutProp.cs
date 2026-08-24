using Residue.Chemistry;
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
        /// The name the run was printed under: the tag someone typed, not the label on the bottle.
        /// <para>
        /// An instrument cannot read a paper label, and this prop's text is compared against the
        /// terminal often enough that printing the label would resolve a mis-log for free (§5.1).
        /// A slip off an unlogged vial says so, via <see cref="SampleState.RecordTag"/>.
        /// </para>
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

        public override string DisplayName => $"{MachineName} printout — {RecordTag}";

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
        }

        public override string Prompt(PlayerInteractor player)
        {
            if (player.Carried != null) return "Hands full";
            return IsBlank
                ? $"Take blank slip — {MachineName}"
                : $"Take printout — {RecordTag}";
        }

        public override string UseHint => "read slip";

        /// <summary>Glance at the slip without walking to the desk. Reading is not filing.</summary>
        public override void UseInHand(PlayerInteractor player)
        {
            var reading = Result;
            if (reading == null)
            {
                // Two different states, said differently. "Blank" is a slip with nothing on it;
                // a key that has not resolved is a slip whose numbers are still in the post, and
                // telling the player it was blank would be a lie they would act on.
                player.Say(ResultKey == 0
                    ? "The slip is blank."
                    : "The numbers on this slip have not come through yet.");
                return;
            }

            var text = new System.Text.StringBuilder();
            text.Append(reading.IsBlank ? $"{MachineName} blank: " : $"{RecordTag} · {MachineName}: ");

            int shown = 0;
            foreach (var kv in reading.Values)
            {
                if (shown++ >= 6) { text.Append("…"); break; }
                text.Append($"{kv.Key} {kv.Value:0.###}   ");
            }

            if (shown == 0) text.Append("no values reported");
            player.Say(text.ToString(), 6f);
        }
    }
}
