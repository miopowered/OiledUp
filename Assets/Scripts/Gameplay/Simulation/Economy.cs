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
        /// Solvent in the wash station's drum (§5.2). Cleaning costs money, which is exactly why
        /// players will be tempted to skip it — that temptation is the mechanic.
        /// <para>
        /// It is drawn down by filling a bottle rather than by flushing an instrument (see
        /// <see cref="SolventStore"/>): the accounting is unchanged, one unit to one flush, but the
        /// unit now leaves the drum at the station and crosses the room in somebody's hands.
        /// </para>
        /// </summary>
        public float SolventUnits { get; private set; }

        /// <summary>
        /// Certified reference ampoules (§5.3). These are the only way to find out whether an
        /// instrument has been quietly scaling every reading, so running out must be a purchasing
        /// decision the player makes rather than a wall they hit — hard rule 3 turns on the tell
        /// staying available.
        /// </summary>
        public int ReferenceStandards { get; private set; }

        public float TotalEarned { get; private set; }
        public float TotalLost { get; private set; }

        public event Action Changed;

        public Economy(EconomyTuning tuning, float startingSolvent = 12f, int startingStandards = 2)
        {
            this.tuning = tuning;
            Money = tuning.StartingMoney;
            Reputation = tuning.StartingReputation;
            SolventUnits = startingSolvent;
            ReferenceStandards = startingStandards;
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

        /// <summary>Price of restocking <paramref name="units"/> of solvent.</summary>
        public float SolventCost(int units) => tuning.SolventUnitCost * Mathf.Max(0, units);

        /// <summary>
        /// Buy solvent. Returns false and changes nothing if it is unaffordable.
        /// <para>
        /// Without this the run had no way to replenish, so the starting twelve units were the
        /// entire supply for the whole contract. Once dry, flushing became impossible and residue
        /// accumulated with no remedy — which is §9's "never punish something the player could not
        /// have checked" inverted into something they could not have <i>fixed</i>.
        /// </para>
        /// </summary>
        public bool TryBuySolvent(int units)
        {
            if (units <= 0) return false;
            if (!TrySpend(SolventCost(units))) return false;

            AddSolvent(units);
            return true;
        }

        public bool TryConsumeReferenceStandard()
        {
            if (ReferenceStandards < 1) return false;
            ReferenceStandards--;
            Changed?.Invoke();
            return true;
        }

        public void AddReferenceStandards(int count)
        {
            if (count <= 0) return;
            ReferenceStandards += count;
            Changed?.Invoke();
        }

        /// <summary>Price of ordering <paramref name="count"/> certified ampoules.</summary>
        public float ReferenceStandardCost(int count) =>
            tuning.ReferenceStandardCost * Mathf.Max(0, count);

        /// <summary>
        /// Order certified standards. Returns false and changes nothing if unaffordable.
        /// <para>
        /// Same argument as solvent: an instrument whose calibration cannot be checked punishes the
        /// player for hidden state they had no way to see, which hard rule 3 forbids. The cost is
        /// what makes checking every instrument every morning a choice rather than a routine.
        /// </para>
        /// </summary>
        public bool TryBuyReferenceStandards(int count)
        {
            if (count <= 0) return false;
            if (!TrySpend(ReferenceStandardCost(count))) return false;

            AddReferenceStandards(count);
            return true;
        }
    }
}
