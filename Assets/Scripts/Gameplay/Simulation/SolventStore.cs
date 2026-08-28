using System.Collections.Generic;
using Residue.Chemistry;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// The wash station's drum and the bottles that draw on it. The host's whole answer to "who is
    /// holding what solvent, and how much of it is left".
    /// <para>
    /// <b>The money did not move.</b> <see cref="Economy.SolventUnits"/> is still the stock, still
    /// bought at the terminal, still priced by <see cref="EconomyTuning.SolventUnitCost"/>, and one
    /// unit still buys exactly one flush — see <see cref="UnitsPerCharge"/>. What changed is where the
    /// unit is spent: it leaves the drum when a bottle is filled rather than when an instrument is
    /// flushed, and the bottle carries it across the room in between. Issue #14 called this a §5.5
    /// layout argument rather than a §5.2 one, and a layout change that quietly re-priced the flush
    /// would be neither.
    /// </para>
    /// <para>
    /// <b>Filling tops up rather than refills.</b> A part-full bottle draws only its
    /// <see cref="SolventBottleState.Headroom"/>, so no charge is ever paid for twice and topping up
    /// early costs nothing but the walk. That matters: the decision the station is here to create is
    /// "top up now, or start the round and risk running dry at the far end of the room", and a fill
    /// that wasted whatever was already in the bottle would answer it for the player.
    /// </para>
    /// <para>
    /// Plain C# on the host, like everything else under <c>Residue.Gameplay.Simulation</c>. Clients
    /// see bottles through <c>Residue.Net.Views.SolventBottleView</c> and can no more write to one
    /// than they can write to a <see cref="SampleState"/>.
    /// </para>
    /// </summary>
    public sealed class SolventStore
    {
        /// <summary>
        /// Flushes a full bottle holds.
        /// <para>
        /// Four, against the five instruments the MVP lab installs, and the gap is the whole point. A
        /// bottle that covered a full sweep would make the wash station a once-a-morning ritual and
        /// then decoration; one that covered a single flush would make the room a corridor, which is
        /// the failure #14 explicitly did not want. At four, the common case — flushing the two or
        /// three instruments one critical sample passed through — is a single trip, and a full sweep
        /// is not, so the walk lands inside the shift where it costs something.
        /// </para>
        /// It is also what keeps "top up now with one left?" a live question every round, rather than
        /// a thing you do at the start of the day and forget.
        /// </summary>
        public const int BottleCapacity = 4;

        /// <summary>
        /// Solvent drawn from the drum per charge. One, so a flush costs exactly what it always cost.
        /// <para>
        /// Aliased to <see cref="MachineRuntimeState.SolventPerClean"/> rather than written as 1
        /// again, because that constant is the one <c>§5.2</c> reasons about and two independent
        /// literals would be two things to retune.
        /// </para>
        /// </summary>
        public const float UnitsPerCharge = MachineRuntimeState.SolventPerClean;

        /// <summary>
        /// Bottles in the lab. Two: enough that a second player is not simply blocked, few enough
        /// that four of them cannot all be flushing at once. §5.5 names the single wash station as a
        /// deliberate bottleneck, and a bottle per pair of hands would quietly remove it.
        /// </summary>
        public const int BottleCount = 2;

        /// <summary>
        /// Fixture id of the wash station, and therefore the container a bottle lives in when nobody
        /// is carrying it.
        /// <para>
        /// Declared here rather than on the scene component for the same reason
        /// <c>SampleRack.DefaultRackId</c> is a constant: the host writes this id into a location
        /// record and a client has to resolve it back to a transform, so both ends need the literal
        /// and neither may own it. <c>Residue.Gameplay.World.WashStation</c> reads it from here —
        /// this assembly cannot see that one, and the dependency runs the right way round.
        /// </para>
        /// </summary>
        public const string StationId = "wash";

        private readonly Economy economy;
        private readonly List<SolventBottleState> bottles = new();

        /// <summary>
        /// Bottles start <b>empty</b>, in their cradles. A run's solvent is exactly what the drum
        /// holds and what the terminal sells; handing out pre-filled bottles would add free flushes
        /// that no economy test would ever notice.
        /// </summary>
        public SolventStore(Economy economy, int bottleCount = BottleCount)
        {
            this.economy = economy;

            for (int i = 0; i < bottleCount; i++)
            {
                bottles.Add(new SolventBottleState
                {
                    Id = $"bottle-{i + 1}",
                    Capacity = BottleCapacity,
                    Charges = 0,
                    Location = SampleLocation.OnSurface(StationId, i)
                });
            }
        }

        public IReadOnlyList<SolventBottleState> All => bottles;

        /// <summary>
        /// Put one bottle back the way a save found it (#49).
        /// <para>
        /// Matched by id onto the bottles the constructor already made, rather than rebuilding the
        /// list. The lab has a fixed number of them (<see cref="BottleCount"/>) and that count is
        /// balance, not save data: a run saved when there were three must not hand a fourth to a build
        /// that ships two, and a bottle the save does not mention stays where the constructor put it —
        /// empty, in its cradle — which is the only safe answer.
        /// </para>
        /// </summary>
        internal void Restore(string bottleId, int capacity, int charges, SampleLocation location)
        {
            var bottle = Find(bottleId);
            if (bottle == null) return;

            if (capacity > 0) bottle.Capacity = capacity;
            bottle.Charges = charges < 0 ? 0 : charges > bottle.Capacity ? bottle.Capacity : charges;
            bottle.Location = location;
        }

        public SolventBottleState Find(string bottleId)
        {
            if (string.IsNullOrEmpty(bottleId)) return null;

            foreach (var bottle in bottles)
            {
                if (bottle.Id == bottleId) return bottle;
            }
            return null;
        }

        /// <summary>The bottle in a player's hands, or null if they are not carrying one.</summary>
        public SolventBottleState HeldBy(ulong clientId)
        {
            foreach (var bottle in bottles)
            {
                if (bottle.IsHeldBy(clientId)) return bottle;
            }
            return null;
        }

        /// <summary>
        /// Pick a bottle up. Refuses one already in somebody else's hands — the same race
        /// <c>LabCommandExecutor.TakeVial</c> settles for a vial, and for the same reason: two players
        /// walking off with the same object is two records of one thing.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryTake(string bottleId, ulong clientId, out string refusal)
        {
            refusal = null;

            var bottle = Find(bottleId);
            if (bottle == null) { refusal = "No such solvent bottle."; return false; }

            if (bottle.Location.Kind == SampleLocationKind.Held)
            {
                if (bottle.IsHeldBy(clientId)) return true;

                refusal = "Someone else is carrying that solvent bottle.";
                return false;
            }

            bottle.Location = SampleLocation.Held(clientId);
            return true;
        }

        /// <summary>
        /// Set a carried bottle down on a shelf. Any slotted container will do — a cradle at the wash
        /// station, or a hole in a sample rack, where it takes up space a vial wanted.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryPutDown(string bottleId, ulong clientId, string surfaceId, int slot,
                               out string refusal)
        {
            refusal = null;

            var bottle = Find(bottleId);
            if (bottle == null) { refusal = "No such solvent bottle."; return false; }
            if (!bottle.IsHeldBy(clientId)) { refusal = "You are not carrying that bottle."; return false; }

            bottle.Location = SampleLocation.OnSurface(
                string.IsNullOrEmpty(surfaceId) ? StationId : surfaceId, slot);
            return true;
        }

        /// <summary>
        /// Top a carried bottle up from the drum, one unit per charge. Returns how many charges the
        /// drum could actually cover, which may be fewer than the bottle had room for — a partial
        /// fill is honest and a refusal would strand the last of the solvent in the drum.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryFill(string bottleId, ulong clientId, out int added, out string refusal)
        {
            added = 0;
            refusal = null;

            var bottle = Find(bottleId);
            if (bottle == null) { refusal = "No such solvent bottle."; return false; }

            if (!bottle.IsHeldBy(clientId))
            {
                refusal = "Pick the bottle up before filling it.";
                return false;
            }

            if (bottle.IsFull)
            {
                refusal = $"That bottle is already full ({bottle.Capacity} flushes).";
                return false;
            }

            int available = AvailableCharges;
            if (available <= 0)
            {
                refusal = "The solvent drum is empty. Order more at the terminal.";
                return false;
            }

            added = bottle.Headroom < available ? bottle.Headroom : available;

            // Charged before the bottle is credited, so a drum that refuses cannot leave a bottle
            // holding solvent nobody paid for.
            if (!economy.TryConsumeSolvent(added * UnitsPerCharge))
            {
                added = 0;
                refusal = "The solvent drum is empty. Order more at the terminal.";
                return false;
            }

            bottle.Charges += added;
            return true;
        }

        /// <summary>
        /// Spend one charge, at the instrument. The bottle must be in the hands of the player asking
        /// according to the <i>host's</i> record — a client's own belief about what it is carrying is
        /// exactly the thing §3.1 will not take on trust.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryConsumeCharge(string bottleId, ulong clientId, out string refusal)
        {
            refusal = null;

            var bottle = Find(bottleId);
            if (bottle == null) { refusal = "No such solvent bottle."; return false; }

            if (!bottle.IsHeldBy(clientId))
            {
                refusal = "You are not carrying that bottle.";
                return false;
            }

            if (bottle.IsEmpty)
            {
                refusal = "The solvent bottle is empty. Refill it at the wash station.";
                return false;
            }

            bottle.Charges--;
            return true;
        }

        /// <summary>
        /// Put back anything a dropped connection was carrying.
        /// <para>
        /// Not a courtesy, for the same reason <c>LabNetwork.OnItemReleased</c> is not: a bottle left
        /// marked held by a client id that no longer exists is a bottle nobody can ever pick up, and
        /// with two in the lab that is half the flushing capacity of the run gone for good.
        /// </para>
        /// </summary>
        public void ReleaseAllHeldBy(ulong clientId)
        {
            for (int i = 0; i < bottles.Count; i++)
            {
                var bottle = bottles[i];
                if (!bottle.IsHeldBy(clientId)) continue;

                // Back to its own cradle rather than to wherever it was last set down. The station is
                // the one place every player knows to look.
                bottle.Location = SampleLocation.OnSurface(StationId, i);
            }
        }

        /// <summary>Whole charges the drum can currently cover.</summary>
        public int AvailableCharges =>
            economy == null ? 0 : (int)(economy.SolventUnits / UnitsPerCharge);

        /// <summary>
        /// What one flush costs in money, end to end. The terminal prices solvent by the unit and the
        /// wash station spends units by the charge, so this is the figure the two agree on — and the
        /// one <c>EconomyTests</c> weighs against a base payout.
        /// </summary>
        public static float FlushCost(EconomyTuning tuning) =>
            tuning == null ? 0f : tuning.SolventUnitCost * UnitsPerCharge;
    }
}
