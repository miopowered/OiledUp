using Residue.Chemistry;
using Residue.Data;

namespace Residue.Gameplay.Simulation
{
    /// <summary>How a filed verdict turned out once reality arrived.</summary>
    public enum ConsequenceOutcome
    {
        /// <summary>CRITICAL, and the fault was real. The job done right.</summary>
        CorrectCritical,

        /// <summary>CRITICAL on a healthy unit. Somebody tore down a working machine.</summary>
        FalsePositive,

        /// <summary>MONITOR on a developing fault. Reasonable call; sample comes back worse.</summary>
        MonitorDeveloping,

        /// <summary>MONITOR on an imminent fault. The equipment failed anyway.</summary>
        MonitorOnImminent,

        /// <summary>MONITOR on a healthy unit. Wasted a re-draw, but nothing broke.</summary>
        MonitorUnnecessary,

        /// <summary>NORMAL over a real fault. The catastrophic one.</summary>
        MissedFault,

        /// <summary>NORMAL on a healthy unit. Routine.</summary>
        CorrectNormal
    }

    /// <summary>The scored result of one verdict, safe to show the player.</summary>
    public sealed class ConsequenceReport
    {
        public SampleId Sample;
        public string EquipmentTag;
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
                EquipmentTag = state.EquipmentTag,
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
                        report.Headline = $"{state.EquipmentTag}: pulled in time. Root cause confirmed as " +
                                          $"{report.ActualRootCause}. Full payout plus diagnostic bonus.";
                    }
                    else
                    {
                        report.Headline = $"{state.EquipmentTag}: pulled in time — {fault.DisplayName}. " +
                                          (state.FiledRootCause != null
                                              ? $"Filed cause was wrong; it was {report.ActualRootCause}."
                                              : "No root cause filed.");
                    }
                    break;

                case Verdict.Critical:
                    report.Outcome = ConsequenceOutcome.FalsePositive;
                    report.MoneyDelta = -tuning.UnnecessaryTeardownCost;
                    report.ReputationDelta = tuning.FalsePositiveReputation;
                    report.Headline = $"{state.EquipmentTag}: stripped on your call and found serviceable. " +
                                      "The teardown is billed to us.";
                    break;

                case Verdict.Monitor when hasFault && truth.WorstSeverity == FaultSeverity.Imminent:
                    report.Outcome = ConsequenceOutcome.MonitorOnImminent;
                    report.MoneyDelta = -fault.RepairCost * tuning.MonitorOnImminentMultiplier;
                    report.ReputationDelta = tuning.MonitorOnImminentReputation;
                    report.Headline = $"{state.EquipmentTag}: FAILED IN SERVICE. You flagged it to watch; " +
                                      $"it needed pulling. {fault.DisplayName}.";
                    break;

                case Verdict.Monitor when hasFault:
                    report.Outcome = ConsequenceOutcome.MonitorDeveloping;
                    report.MoneyDelta = tuning.BasePayout * tuning.MonitorPartialFraction;
                    report.RequeueSample = true;
                    report.Headline = $"{state.EquipmentTag}: kept in service and resampled. " +
                                      "Numbers are worse this cycle.";
                    break;

                case Verdict.Monitor:
                    report.Outcome = ConsequenceOutcome.MonitorUnnecessary;
                    report.MoneyDelta = tuning.BasePayout * tuning.MonitorPartialFraction;
                    report.ReputationDelta = -0.5f;
                    report.Headline = $"{state.EquipmentTag}: resampled at your request, still clean.";
                    break;

                case Verdict.Normal when hasFault:
                    report.Outcome = ConsequenceOutcome.MissedFault;
                    report.MoneyDelta = -fault.RepairCost * tuning.MissedFaultMultiplier(truth.WorstSeverity);
                    report.ReputationDelta = tuning.MissedFaultReputation;
                    report.Headline = $"{state.EquipmentTag}: CATASTROPHIC FAILURE. Passed as normal on your " +
                                      $"report. {fault.DisplayName}. Named in the incident file.";
                    break;

                default:
                    report.Outcome = ConsequenceOutcome.CorrectNormal;
                    report.MoneyDelta = tuning.BasePayout;
                    report.ReputationDelta = tuning.CorrectNormalReputation;
                    report.Headline = $"{state.EquipmentTag}: cleared, ran clean. Routine payout.";
                    break;
            }

            return report;
        }
    }
}
