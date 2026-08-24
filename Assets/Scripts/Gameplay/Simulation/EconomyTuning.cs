using Residue.Data;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// Payout and penalty balance for §5.4.
    /// <para>
    /// The numbers here carry design pillar §1.1.3 — two-sided failure. They must make <b>both</b>
    /// blanket strategies lose money:
    /// </para>
    /// <list type="bullet">
    /// <item>Filing CRITICAL on everything must be unaffordable, not merely suboptimal — otherwise
    /// the safe play is to flag everything and the diagnosis never matters.</item>
    /// <item>Filing NORMAL on everything must be ruinous, because a missed Imminent fault is the
    /// thing the whole job exists to prevent.</item>
    /// </list>
    /// <c>EconomyTests</c> asserts exactly that, so tuning these values without running it is how
    /// you accidentally ship a game with a correct default answer.
    /// </summary>
    public sealed class EconomyTuning
    {
        public float StartingMoney = 6000f;
        public float StartingReputation = 60f;

        /// <summary>Paid for any correctly filed sample.</summary>
        public float BasePayout = 400f;

        /// <summary>Added on top when CRITICAL correctly catches a real fault.</summary>
        public float AccuracyBonus = 320f;

        /// <summary>
        /// Added when the filed root cause is also right. This is the payout that rewards
        /// understanding over table lookup (§5.4) — it should be large enough to be worth chasing.
        /// </summary>
        public float RootCauseBonus = 420f;

        /// <summary>Fraction of base paid for MONITOR on a genuinely developing fault.</summary>
        public float MonitorPartialFraction = 0.45f;

        /// <summary>
        /// Charged when CRITICAL is filed on a healthy unit. <see cref="FaultDef.TeardownCostIfWrong"/>
        /// covers the case where a specific fault was claimed; with no fault present there is no
        /// FaultDef to read, so this flat figure stands in for stripping a machine that was fine.
        /// </summary>
        public float UnnecessaryTeardownCost = 4200f;

        /// <summary>
        /// Multiplier on repair cost when MONITOR was filed against an Imminent fault. The equipment
        /// fails; you were close but the call was still wrong.
        /// </summary>
        public float MonitorOnImminentMultiplier = 1.6f;

        /// <summary>
        /// Price of one solvent unit, which is still exactly one flush (§5.2).
        /// <para>
        /// Has to be small enough that flushing is never the thing that bankrupts you, and large
        /// enough that flushing after every single run is visibly wasteful. The temptation to skip
        /// is the mechanic; pricing it out of reach would replace a decision with a rule.
        /// </para>
        /// <para>
        /// The wash station (#14) did not change this. A unit still buys a flush — it is now drawn
        /// into a bottle at the station instead of being spent at the instrument, and what the change
        /// bought is walking distance, not a higher price. <c>SolventStore.FlushCost</c> is the figure
        /// the two ends agree on; <c>EconomyTests</c> weighs it against <see cref="BasePayout"/>.
        /// </para>
        /// </summary>
        public float SolventUnitCost = 45f;

        /// <summary>
        /// Price of one certified reference ampoule (§5.3).
        /// <para>
        /// Deliberately several times a flush and well under a base payout. Checking a specific
        /// instrument you have started to distrust must be comfortably affordable; checking all five
        /// every morning must be a bill you notice. The decision §5.3 wants is <i>which</i>
        /// instrument you suspect, not whether you can afford to ask at all.
        /// </para>
        /// </summary>
        public float ReferenceStandardCost = 160f;

        /// <summary>
        /// Charged for a recalibration, on top of the instrument time it occupies.
        /// <para>
        /// §5.3 makes recalibration cost time and money so that "calibrate everything every morning"
        /// is a real bill rather than a free ritual. If it were free, drift would never reach the
        /// player as a discovery — which is the entire mechanic.
        /// </para>
        /// </summary>
        public float CalibrationCost = 140f;

        public float FalsePositiveReputation = -3f;
        public float MonitorOnImminentReputation = -8f;
        public float MissedFaultReputation = -12f;
        public float CorrectCriticalReputation = 2f;
        public float CorrectNormalReputation = 0.5f;

        /// <summary>
        /// How catastrophic a missed fault is, scaled by how far along it was. §5.4 calls filing
        /// NORMAL over a real fault "catastrophic", and the cost scales with severity.
        /// </summary>
        public float MissedFaultMultiplier(FaultSeverity severity) => severity switch
        {
            FaultSeverity.Benign => 0.8f,
            FaultSeverity.Developing => 1.8f,
            FaultSeverity.Imminent => 3.2f,
            _ => 1f
        };
    }
}
