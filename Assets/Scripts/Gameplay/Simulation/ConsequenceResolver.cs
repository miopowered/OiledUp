using Residue.Chemistry;
using Residue.Data;

namespace Residue.Gameplay.Simulation
{
    /// <summary>How a filed verdict turned out once reality arrived.</summary>
    public enum ConsequenceOutcome
    {
        /// <summary>CRITICAL, and the fault was real. The tank came out before it cost anything.</summary>
        CorrectCritical,

        /// <summary>CRITICAL on a good tank. Thousands of litres dumped and a line stopped for nothing.</summary>
        FalsePositive,

        /// <summary>MONITOR on a developing fault. Reasonable call; the tank is resampled worse.</summary>
        MonitorDeveloping,

        /// <summary>MONITOR on an imminent fault. The batch was quenched in it anyway.</summary>
        MonitorOnImminent,

        /// <summary>MONITOR on a good tank. Wasted a re-draw, but nothing was lost.</summary>
        MonitorUnnecessary,

        /// <summary>NORMAL over a real fault. Cracked parts, a customer claim, or a tank fire.</summary>
        MissedFault,

        /// <summary>NORMAL on a good tank. Routine.</summary>
        CorrectNormal
    }

    /// <summary>The scored result of one verdict, safe to show the player.</summary>
    public sealed class ConsequenceReport
    {
        public SampleId Sample;

        /// <summary>
        /// The tank this was filed under, so an end-of-day headline names something the player can
        /// find again on the terminal and on a bottle. Copied at resolve time rather than looked up
        /// later, because the report outlives the sample's place in the day's roster.
        /// </summary>
        public string RecordTag;

        public Verdict Filed;
        public ConsequenceOutcome Outcome;

        public float MoneyDelta;
        public float ReputationDelta;

        public bool RootCauseCorrect;

        /// <summary>Revealed only now — §4.3 says the fault name is shown after resolution, never before.</summary>
        public string FaultName;
        public string ActualRootCause;

        /// <summary>MONITOR on a developing fault re-sends the sample next cycle with worse numbers (§5.4).</summary>
        public bool RequeueSample;

        public string Headline;

        public bool IsGood => Outcome is ConsequenceOutcome.CorrectCritical
            or ConsequenceOutcome.CorrectNormal
            or ConsequenceOutcome.MonitorDeveloping;
    }

    /// <summary>
    /// Implements the §5.4 outcome table. Server-side: it reads ground truth, so it is only ever
    /// called from inside <see cref="SampleRegistry"/>.
    /// </summary>
    public static class ConsequenceResolver
    {
        public static ConsequenceReport Resolve(SampleState state, SampleGroundTruth truth, EconomyTuning tuning)
        {
            var verdict = state.FiledVerdict ?? Verdict.Normal;
            var fault = truth.PrimaryFault;
            bool hasFault = !truth.IsHealthy;

            var report = new ConsequenceReport
            {
                Sample = state.Id,
                RecordTag = state.RecordTag,
                Filed = verdict,
                FaultName = fault != null ? fault.DisplayName : "No fault found",
                ActualRootCause = fault?.RootCause != null ? fault.RootCause.DisplayName : null
            };

            switch (verdict)
            {
                case Verdict.Critical when hasFault:
                    report.Outcome = ConsequenceOutcome.CorrectCritical;
                    report.MoneyDelta = tuning.BasePayout + tuning.AccuracyBonus;
                    report.ReputationDelta = tuning.CorrectCriticalReputation;

                    report.RootCauseCorrect = state.FiledRootCause != null &&
                                              fault.RootCause == state.FiledRootCause;
                    if (report.RootCauseCorrect)
                    {
                        report.MoneyDelta += tuning.RootCauseBonus;
                        report.Headline = $"{state.RecordTag}: taken out of service in time. Cause confirmed " +
                                          $"as {report.ActualRootCause}. Full payout plus diagnostic bonus.";
                    }
                    else
                    {
                        report.Headline = $"{state.RecordTag}: taken out of service in time — " +
                                          $"{fault.DisplayName}. " +
                                          (state.FiledRootCause != null
                                              ? $"Filed cause was wrong; it was {report.ActualRootCause}."
                                              : "No root cause filed, so no diagnostic bonus.");
                    }
                    break;

                case Verdict.Critical:
                    report.Outcome = ConsequenceOutcome.FalsePositive;
                    report.MoneyDelta = -tuning.UnnecessaryTeardownCost;
                    report.ReputationDelta = tuning.FalsePositiveReputation;
                    report.Headline = $"{state.RecordTag}: tank dumped and recharged on your call. " +
                                      "The oil tested serviceable. Line downtime and the fresh charge are ours.";
                    break;

                case Verdict.Monitor when hasFault && truth.WorstSeverity == FaultSeverity.Imminent:
                    report.Outcome = ConsequenceOutcome.MonitorOnImminent;
                    report.MoneyDelta = -fault.RepairCost * tuning.MonitorOnImminentMultiplier;
                    report.ReputationDelta = tuning.MonitorOnImminentReputation;
                    report.Headline = $"{state.RecordTag}: kept quenching on your advice and it should not " +
                                      $"have been. {fault.DisplayName}. {fault.MissedConsequence}";
                    break;

                case Verdict.Monitor when hasFault:
                    report.Outcome = ConsequenceOutcome.MonitorDeveloping;
                    report.MoneyDelta = tuning.BasePayout * tuning.MonitorPartialFraction;
                    report.RequeueSample = true;
                    report.Headline = $"{state.RecordTag}: kept in service and scheduled for another draw. " +
                                      "The numbers are worse this cycle.";
                    break;

                case Verdict.Monitor:
                    report.Outcome = ConsequenceOutcome.MonitorUnnecessary;
                    report.MoneyDelta = tuning.BasePayout * tuning.MonitorPartialFraction;
                    report.ReputationDelta = -0.5f;
                    report.Headline = $"{state.RecordTag}: redrawn at your request, still within spec.";
                    break;

                case Verdict.Normal when hasFault:
                    report.Outcome = ConsequenceOutcome.MissedFault;
                    report.MoneyDelta = -fault.RepairCost * tuning.MissedFaultMultiplier(truth.WorstSeverity);
                    report.ReputationDelta = tuning.MissedFaultReputation;

                    // The consequence text lives on the fault, so the worst outcome in the game is a
                    // data value someone can find rather than a branch someone has to remember.
                    report.Headline = $"{state.RecordTag}: PASSED AS FIT TO QUENCH. {fault.DisplayName}. " +
                                      $"{fault.MissedConsequence} Named in the incident file.";
                    break;

                default:
                    report.Outcome = ConsequenceOutcome.CorrectNormal;
                    report.MoneyDelta = tuning.BasePayout;
                    report.ReputationDelta = tuning.CorrectNormalReputation;
                    report.Headline = $"{state.RecordTag}: cleared as fit for service. Routine payout.";
                    break;
            }

            return report;
        }
    }
}
