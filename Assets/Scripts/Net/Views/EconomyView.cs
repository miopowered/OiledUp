using System;
using Residue.Gameplay.Simulation;
using Unity.Netcode;

namespace Residue.Net.Views
{
    /// <summary>
    /// The books, as every player in the lab sees them.
    /// <para>
    /// The economy is shared, so this is not a courtesy readout — §1.2 makes bankruptcy a losing
    /// condition, and a client deciding whether to order another ampoule or spend the oil on one more
    /// test is making that call against this number. A client working from a stale balance would be
    /// making the §4.5 decision blind.
    /// </para>
    /// The running totals used to stay host-side, on the grounds that they were end-of-run summary
    /// material. They still are — and the end-of-run summary now has to be drawable at a joined desk
    /// (#39), because a run ends for everybody at once and a player who worked the whole contract
    /// should not be handed a shorter ending than the person who happened to press Host.
    /// </summary>
    public struct EconomyView : INetworkSerializable, IEquatable<EconomyView>
    {
        public float Money;

        public float Reputation;

        /// <summary>
        /// Solvent left in the wash station's drum (§5.2). Purchasable, so running dry is a choice,
        /// not a wall. What is actually in a bottle travels separately, on
        /// <see cref="SolventBottleView"/> — this is the stock a bottle draws on.
        /// </summary>
        public float SolventUnits;

        /// <summary>Certified ampoules left (§5.3). The only way to find out an instrument is lying.</summary>
        public int ReferenceStandards;

        /// <summary>
        /// What a recalibration costs (§5.3).
        /// <para>
        /// Balance rather than state, and it never changes during a run — but the button on the
        /// instrument prints the price, and a client that read it off its own default
        /// <see cref="EconomyTuning"/> would quote a stale figure the first time the tables were tuned
        /// and only the host rebuilt. Cheaper to send four bytes than to own that class of bug.
        /// </para>
        /// </summary>
        public float CalibrationCost;

        /// <summary>
        /// Price of one solvent unit and of one certified ampoule (§5.2, §5.3). Same argument as
        /// <see cref="CalibrationCost"/>: the ORDER buttons at the terminal print a price and grey
        /// themselves out against the balance, and a client quoting its own default tuning would be
        /// wrong the first time either figure was retuned. Sent per unit rather than per pack so the
        /// pack size stays a decision the screen makes.
        /// </summary>
        public float SolventUnitCost;

        public float ReferenceStandardUnitCost;

        /// <summary>
        /// What the outpost opened with, and what has moved through the books since. Only the closing
        /// screen reads these — <see cref="Money"/> is the number every mid-shift decision is made
        /// against — but they are what turn "£4,120" into "£4,120 from £10,000", which is the
        /// difference between a balance and a verdict on the run.
        /// </summary>
        public float StartingMoney;

        public float TotalEarned;

        public float TotalLost;

        /// <summary>Project host state for replication. The only place the economy projection is written.</summary>
        public static EconomyView From(Economy economy, EconomyTuning tuning = null)
        {
            if (economy == null) return default;

            var balance = tuning ?? new EconomyTuning();
            return new EconomyView
            {
                Money = economy.Money,
                Reputation = economy.Reputation,
                SolventUnits = economy.SolventUnits,
                ReferenceStandards = economy.ReferenceStandards,
                CalibrationCost = balance.CalibrationCost,
                SolventUnitCost = balance.SolventUnitCost,
                ReferenceStandardUnitCost = balance.ReferenceStandardCost,
                StartingMoney = balance.StartingMoney,
                TotalEarned = economy.TotalEarned,
                TotalLost = economy.TotalLost
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Money);
            serializer.SerializeValue(ref Reputation);
            serializer.SerializeValue(ref SolventUnits);
            serializer.SerializeValue(ref ReferenceStandards);
            serializer.SerializeValue(ref CalibrationCost);
            serializer.SerializeValue(ref SolventUnitCost);
            serializer.SerializeValue(ref ReferenceStandardUnitCost);
            serializer.SerializeValue(ref StartingMoney);
            serializer.SerializeValue(ref TotalEarned);
            serializer.SerializeValue(ref TotalLost);
        }

        public bool Equals(EconomyView other) =>
            Money.Equals(other.Money) &&
            Reputation.Equals(other.Reputation) &&
            SolventUnits.Equals(other.SolventUnits) &&
            ReferenceStandards == other.ReferenceStandards &&
            CalibrationCost.Equals(other.CalibrationCost) &&
            SolventUnitCost.Equals(other.SolventUnitCost) &&
            ReferenceStandardUnitCost.Equals(other.ReferenceStandardUnitCost) &&
            StartingMoney.Equals(other.StartingMoney) &&
            TotalEarned.Equals(other.TotalEarned) &&
            TotalLost.Equals(other.TotalLost);

        public override bool Equals(object obj) => obj is EconomyView o && Equals(o);

        public override int GetHashCode() => Money.GetHashCode();

        public override string ToString() =>
            $"£{Money:N0} · rep {Reputation:F0} · {SolventUnits:F0} solvent · {ReferenceStandards} std";
    }
}
