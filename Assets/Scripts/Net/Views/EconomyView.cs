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
    /// Running totals (<c>TotalEarned</c>, <c>TotalLost</c>) stay host-side. They are end-of-run
    /// summary material, not something a player acts on mid-shift.
    /// </summary>
    public struct EconomyView : INetworkSerializable, IEquatable<EconomyView>
    {
        public float Money;

        public float Reputation;

        /// <summary>Solvent left for flushing (§5.2). Purchasable, so running dry is a choice, not a wall.</summary>
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

        /// <summary>Project host state for replication. The only place the economy projection is written.</summary>
        public static EconomyView From(Economy economy, EconomyTuning tuning = null) => economy == null
            ? default
            : new EconomyView
            {
                Money = economy.Money,
                Reputation = economy.Reputation,
                SolventUnits = economy.SolventUnits,
                ReferenceStandards = economy.ReferenceStandards,
                CalibrationCost = (tuning ?? new EconomyTuning()).CalibrationCost
            };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Money);
            serializer.SerializeValue(ref Reputation);
            serializer.SerializeValue(ref SolventUnits);
            serializer.SerializeValue(ref ReferenceStandards);
            serializer.SerializeValue(ref CalibrationCost);
        }

        public bool Equals(EconomyView other) =>
            Money.Equals(other.Money) &&
            Reputation.Equals(other.Reputation) &&
            SolventUnits.Equals(other.SolventUnits) &&
            ReferenceStandards == other.ReferenceStandards &&
            CalibrationCost.Equals(other.CalibrationCost);

        public override bool Equals(object obj) => obj is EconomyView o && Equals(o);

        public override int GetHashCode() => Money.GetHashCode();

        public override string ToString() =>
            $"£{Money:N0} · rep {Reputation:F0} · {SolventUnits:F0} solvent · {ReferenceStandards} std";
    }
}
