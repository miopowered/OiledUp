using System;
using UnityEngine;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// Money, reputation and consumables for one run. A run ends on contract completion or on
    /// financial failure (§1.2), so bankruptcy is a real losing condition rather than a soft cap.
    /// </summary>
    public sealed class Economy
    {
        private readonly EconomyTuning tuning;

        public float Money { get; private set; }
        public float Reputation { get; private set; }

        /// <summary>
        /// Solvent for machine cleaning (§5.2). Cleaning costs money, which is exactly why players
        /// will be tempted to skip it — that temptation is the mechanic.
        /// </summary>
        public float SolventUnits { get; private set; }

        public float TotalEarned { get; private set; }
        public float TotalLost { get; private set; }

        public event Action Changed;

        public Economy(EconomyTuning tuning, float startingSolvent = 12f)
        {
            this.tuning = tuning;
            Money = tuning.StartingMoney;
            Reputation = tuning.StartingReputation;
            SolventUnits = startingSolvent;
        }

        public bool IsBankrupt => Money < 0f;

        public void Apply(ConsequenceReport report)
        {
            Money += report.MoneyDelta;
            Reputation = Mathf.Clamp(Reputation + report.ReputationDelta, 0f, 100f);

            if (report.MoneyDelta >= 0f) TotalEarned += report.MoneyDelta;
            else TotalLost += -report.MoneyDelta;

            Changed?.Invoke();
        }

        /// <summary>Charge an unavoidable cost (a machine run's consumables). May go negative.</summary>
        public void Charge(float amount)
        {
            if (amount <= 0f) return;
            Money -= amount;
            TotalLost += amount;
            Changed?.Invoke();
        }

        /// <summary>Attempt a discretionary purchase. Returns false and charges nothing if unaffordable.</summary>
        public bool TrySpend(float amount)
        {
            if (amount > Money) return false;
            Money -= amount;
            TotalLost += amount;
            Changed?.Invoke();
            return true;
        }

        public bool TryConsumeSolvent(float units = 1f)
        {
            if (SolventUnits < units) return false;
            SolventUnits -= units;
            Changed?.Invoke();
            return true;
        }

        public void AddSolvent(float units)
        {
            SolventUnits += units;
            Changed?.Invoke();
        }
    }
}
