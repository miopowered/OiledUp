using System;
using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Gameplay.Simulation;
using Unity.Collections;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// One settled verdict, as everyone who worked the shift may read it. Everybody was on the
    /// bench; everybody sees the reckoning.
    /// <para>
    /// <b>Why this is allowed to name a fault at all.</b> Hard rule 2 keeps ground truth off the
    /// wire, and this is the one player-facing object that says what was actually wrong. It crosses
    /// because it is <i>past</i> truth: §4.3 names the fault only after the verdict has been scored
    /// and the money has moved, which is the same argument that lets the host's own screen print it.
    /// Content is not the risk. Timing is.
    /// </para>
    /// <para>
    /// <b>The timing rule.</b> Two things put a sample back in play, and a report naming the fault on
    /// one of them is the answer to a question the game has not finished asking — which does not look
    /// like a leak, because the report is "past" truth right up until the sample returns.
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>MONITOR on a developing fault requeues the unit</b> (§5.4). The next morning
    /// <see cref="SampleRegistry.BuildRequeue"/> re-sends it carrying the <i>same</i> fault further
    /// along — deliberately not a fresh roll, or MONITOR would be a coin flip — and the re-draw
    /// arrives captioned as a re-draw of the very record whose report is on the glass. So the rule
    /// is: <b>a report that puts its own unit back in play crosses without the diagnosis.</b>
    /// <see cref="From"/> withholds any headline that names the fault or its root cause and
    /// substitutes one that does not. §5.4's own copy for that outcome already names nothing; this
    /// is the same decision made a second time, structurally, so that rewriting the sentence cannot
    /// quietly turn a partial payout into an answer.
    /// </description></item>
    /// <item><description>
    /// <b>A record re-opened after a recalibration goes back to <c>Measured</c> and can be re-filed</b>
    /// (§5.3). This one needs no rule, and why is the interesting half. A report exists only for a
    /// sample <see cref="SampleRegistry.ResolveDue"/> got <see cref="SampleLifecycle.TryResolve"/>
    /// to accept, and <see cref="SampleStage.Resolved"/> is the one stage with no outgoing edge in
    /// the lifecycle table — <see cref="SampleRegistry.ReopenForRetest"/> can only reach an
    /// <see cref="SampleStage.Archived"/> record, and cancels its pending consequence so no report is
    /// ever written for it. A reported record therefore cannot come back, and its answer is spent.
    /// That is a load-bearing dependency on a file this one does not own, so it is pinned by
    /// <c>NetworkViewTests.AReportedRecord_CanNeverComeBackIntoPlay</c> rather than by memory.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>And reports are only on the wire between shifts.</b> <see cref="LabState.LastReports"/>
    /// outlives the day it describes; the host's screen retires it behind START NEXT DAY and a client
    /// has no such list of its own to drop. So <see cref="Gather"/> publishes nothing while a day is
    /// in progress — and <see cref="LabState.BeginDay"/> raises <c>DayInProgress</c> <i>before</i> it
    /// generates the re-draws, which means the rows are already gone by the time the sample they talk
    /// about is back through the door.
    /// </para>
    /// No paper label rides in here. The headline names the tank the player <i>filed the vial
    /// under</i>, so a mis-log (§5.1) still has exactly one tell and it is on the bottle — see
    /// <see cref="VialView"/> for why that boundary is a separate list rather than a comment.
    /// </summary>
    public struct ReportView : INetworkSerializable, IEquatable<ReportView>
    {
        /// <summary><see cref="Chemistry.SampleId.Value"/> of the record this settles. The matching
        /// <see cref="SampleView"/> row is what a screen looks the tag up in.</summary>
        public int Sample;

        /// <summary>
        /// The day this was published on. Carried so a client can drop rows belonging to a day its
        /// clock has already left: the report list and <see cref="DayView"/> are separate writes, and
        /// a summary drawn a frame apart from the day it is titled with is a screen that lies quietly.
        /// </summary>
        public int Day;

        /// <summary>
        /// Which row of the §5.4 outcome table this is. Safe for the same reason the headline is —
        /// it is a statement about a call that has already been paid out — and needed, because it is
        /// what colours the card.
        /// </summary>
        public ConsequenceOutcome Outcome;

        public float MoneyDelta;

        public float ReputationDelta;

        /// <summary>The filed root cause matched the real one, so the §5.4 diagnostic bonus was paid.</summary>
        public bool RootCauseCorrect;

        /// <summary>
        /// The sentence <see cref="ConsequenceResolver"/> wrote, verbatim — or, where this report
        /// puts its own unit back in play, one that names nothing. See the type doc.
        /// <para>
        /// Prose rather than the parts, so the host's screen and a joined one say the same thing in
        /// the same voice. A client re-composing its own sentence from the outcome would be a second
        /// author in front of the same rule, and §5.4's copy is written to be read once, at the
        /// moment it lands.
        /// </para>
        /// </summary>
        public FixedString512Bytes Headline;

        /// <summary>The handle back, for a caller matching a card to a record.</summary>
        public SampleId SampleId => new(Sample);

        /// <summary>
        /// Rebuild the rows a client may hold. The only place the report projection is written, and
        /// the only place the between-shifts rule is applied.
        /// <para>
        /// It lives beside the projection rather than in <c>LabNetwork</c>'s publish loop on purpose:
        /// the timing rule <i>is</i> the reason this type is allowed to exist, and a rule kept in the
        /// caller is a rule the next person to edit the caller can drop without noticing.
        /// </para>
        /// </summary>
        public static void Gather(LabState lab, List<ReportView> into)
        {
            if (into == null) return;
            into.Clear();

            // Nothing to say mid-shift. The day's reckoning belongs to the gap between shifts, and a
            // list that outlived that gap would still be on a client's desk on the morning the
            // re-drawn sample it names walks back in.
            if (lab == null || lab.DayInProgress) return;

            var reports = lab.LastReports;
            if (reports == null) return;

            for (int i = 0; i < reports.Count; i++) into.Add(From(reports[i], lab.Day));
        }

        /// <summary>Project one settled verdict. See the type doc for what is withheld and why.</summary>
        public static ReportView From(ConsequenceReport report, int day)
        {
            if (report == null) return default;

            return new ReportView
            {
                Sample = report.Sample.Value,
                Day = day,
                Outcome = report.Outcome,
                MoneyDelta = report.MoneyDelta,
                ReputationDelta = report.ReputationDelta,
                RootCauseCorrect = report.RootCauseCorrect,
                Headline = ViewText.Fixed512(Say(report))
            };
        }

        /// <summary>
        /// What this report is allowed to say out loud.
        /// <para>
        /// A unit kept in service comes back tomorrow with the same fault further along (§5.4), so
        /// until it has been called for the last time its diagnosis is still the answer to a live
        /// question. The substitute says only what the player already did and what happens next —
        /// deliberately not a rewrite of the resolver's sentence, so that the redacted path holds
        /// nothing anybody could later be tempted to enrich.
        /// </para>
        /// Never reached today: §5.4's copy for this outcome names nothing to begin with, which is
        /// the right call and is checked by
        /// <c>NetworkViewTests.NoReplicatedReport_NamesAFaultOnASampleStillInPlay</c>. This is the
        /// same decision made a second time, where a rewording cannot get past it.
        /// </summary>
        private static string Say(ConsequenceReport report)
        {
            if (!report.RequeueSample || !Diagnoses(report)) return report.Headline;

            return $"{report.RecordTag}: your MONITOR stands. The unit is still quenching and " +
                   "another draw is scheduled.";
        }

        /// <summary>
        /// True when the headline repeats something the resolver was told and a client was not. Both
        /// names are compared, because §5.4's best outcome prints the root cause instead of the fault.
        /// </summary>
        private static bool Diagnoses(ConsequenceReport report)
        {
            string headline = report.Headline;
            if (string.IsNullOrEmpty(headline)) return false;

            return Mentions(headline, report.FaultName) || Mentions(headline, report.ActualRootCause);
        }

        private static bool Mentions(string headline, string name) =>
            !string.IsNullOrEmpty(name) &&
            headline.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sample);
            serializer.SerializeValue(ref Day);
            serializer.SerializeValue(ref Outcome);
            serializer.SerializeValue(ref MoneyDelta);
            serializer.SerializeValue(ref ReputationDelta);
            serializer.SerializeValue(ref RootCauseCorrect);
            serializer.SerializeValue(ref Headline);
        }

        public bool Equals(ReportView other) =>
            Sample == other.Sample &&
            Day == other.Day &&
            Outcome == other.Outcome &&
            MoneyDelta.Equals(other.MoneyDelta) &&
            ReputationDelta.Equals(other.ReputationDelta) &&
            RootCauseCorrect == other.RootCauseCorrect &&
            Headline.Equals(other.Headline);

        public override bool Equals(object obj) => obj is ReportView o && Equals(o);

        public override int GetHashCode() => Sample;

        public override string ToString() => $"S{Sample:D5} day {Day} {Outcome} £{MoneyDelta:0}";
    }
}
