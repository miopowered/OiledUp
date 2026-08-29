using Residue.Chemistry;
using Residue.Data;

namespace Residue.Gameplay.Simulation
{
    /// <summary>The one way a carton's paperwork can be wrong today. At most one per delivery.</summary>
    public enum PaperworkSlip
    {
        /// <summary>The note accounts for the box exactly. Most deliveries, from most firms.</summary>
        None,

        /// <summary>A line on the paper that no vial in the box answers.</summary>
        MissingSample,

        /// <summary>A vial in the box that the paper never mentions.</summary>
        UnlistedSample,

        /// <summary>A vial whose tank tag cannot be read. The note still lists the tank.</summary>
        UnreadableLabel
    }

    /// <summary>
    /// Decides what is wrong with one morning's delivery, from who sent it (#32, §6.1).
    ///
    /// <para>
    /// <b>Policy, separated from the doing.</b> Everything here is a roll against a customer's
    /// propensities and nothing here touches a sample — <see cref="LabState"/> carries the plan out.
    /// The split exists so the balance question ("is the careless firm actually worth checking, and
    /// is the meticulous one actually a control?") can be asked of a pure function over a few thousand
    /// seeds, rather than by generating a few thousand contracts.
    /// </para>
    ///
    /// <para>
    /// <b>Frequency comes from the sender and nowhere else.</b> <c>CustomerDef.PaperworkSlipChance</c>
    /// and <c>CustomerDef.SameDrumChance</c> have been modelled since #29 and read by nobody; this is
    /// what reads them. Vogel is 0.00 on both and is therefore a genuine control: a strange Vogel
    /// delivery is a strange delivery. Kessler is 0.30 and 0.25 and is therefore worth opening
    /// carefully. That contrast is the whole reason a sender has a name.
    /// </para>
    ///
    /// <para>
    /// <b>Every draw goes through the run's <see cref="Rng"/>.</b> A seed has to reproduce a whole
    /// contract — including which morning the note was wrong — or two players on one run are not on
    /// one run.
    /// </para>
    /// </summary>
    public static class DeliveryDiscrepancies
    {
        /// <summary>
        /// How often a plant legitimately books two draws from one tank on one delivery, whoever they
        /// are.
        ///
        /// <para>
        /// <b>This constant is load-bearing and must never be zero.</b> §6.1's trap is a customer
        /// quietly bottling one drum as several tanks, and the tell for it is that the vials measure
        /// the same. If a duplicated line on a note only ever meant "same drum", the note alone would
        /// give the answer away and the player would never need to run the second vial — the
        /// measurement, which is the fair part, would become optional. A real double draw has to be
        /// common enough that seeing two lines for one tank means "go and check", not "caught them".
        /// </para>
        ///
        /// <para>
        /// It is a world constant rather than a per-customer field because it is not a fault: a plant
        /// drawing its main bath twice in a morning is ordinary practice, and a meticulous firm does
        /// it as readily as a careless one. Balance that varies by sender lives in
        /// <c>ContentTables.cs</c>; this does not vary.
        /// </para>
        /// </summary>
        public const float DoubleDrawChance = 0.10f;

        /// <summary>
        /// Shift seconds spent on the phone to a customer's dispatcher, reading an unreadable label
        /// back to them (#32's "needs a call to the customer, which costs time").
        ///
        /// <para>
        /// Forty-five seconds against a 300 s default day: an eighth of a shift, for a bottle that is
        /// one of perhaps sixteen. It has to be dear enough that walking the note down the rack and
        /// working the vial out by elimination is usually the better trade, and cheap enough that a
        /// carton with two unanswered lines — where elimination genuinely cannot decide — is not a
        /// dead end. The call is the guaranteed answer you pay for; reading is the free answer that
        /// sometimes will not come.
        /// </para>
        /// </summary>
        public const float CallSeconds = 45f;

        /// <summary>What is wrong with one delivery. All false is the ordinary morning.</summary>
        public readonly struct Plan
        {
            /// <summary>The paperwork error, if any. At most one per carton — see <see cref="Roll"/>.</summary>
            public readonly PaperworkSlip Slip;

            /// <summary>Two vials arrive claiming one tank, against a note that books it twice.</summary>
            public readonly bool DuplicateClaim;

            /// <summary>
            /// ...and they came out of one drum. Meaningless unless <see cref="DuplicateClaim"/>.
            /// False means the plant really did draw twice, which is the case that keeps the paper
            /// tell honest.
            /// </summary>
            public readonly bool SameDrum;

            public Plan(PaperworkSlip slip, bool duplicateClaim, bool sameDrum)
            {
                Slip = slip;
                DuplicateClaim = duplicateClaim;
                SameDrum = duplicateClaim && sameDrum;
            }

            public bool IsClean => Slip == PaperworkSlip.None && !DuplicateClaim;

            public override string ToString() =>
                IsClean ? "clean" : $"{Slip}{(DuplicateClaim ? SameDrum ? " +same drum" : " +double draw" : "")}";
        }

        /// <summary>
        /// Roll one morning's delivery for one sender.
        ///
        /// <para>
        /// <b>Order matters and is fixed.</b> The drum question is asked before the paperwork question
        /// so that adding a paperwork case later cannot silently shift every existing seed's same-drum
        /// days. Both branches always consume the same number of draws for the same customer, for the
        /// same reason.
        /// </para>
        ///
        /// <para>
        /// <b>At most one paperwork error per carton.</b> A single roll at
        /// <c>PaperworkSlipChance</c> decides whether the note is wrong at all, and a second picks
        /// which way — so the field means exactly what its name says ("chance the note does not match
        /// the box") rather than three independent chances that compound into something else. It also
        /// keeps elimination usable: one unanswered line on a note is a puzzle with an answer, and
        /// three is a carton nobody can reconcile.
        /// </para>
        /// </summary>
        public static Plan Roll(CustomerDef customer, ref Rng rng)
        {
            float sameDrumChance = customer != null ? customer.SameDrumChance : 0f;
            float slipChance = customer != null ? customer.PaperworkSlipChance : 0f;

            bool sameDrum = rng.Chance(sameDrumChance);
            bool doubleDraw = rng.Chance(DoubleDrawChance);

            bool slipped = rng.Chance(slipChance);
            int pick = rng.Range(0, 3);

            bool duplicateClaim = sameDrum || doubleDraw;

            // An unreadable label and a duplicated tank cannot share a carton.
            //
            // Elimination is the free route to identifying an unlabelled bottle: every other tag on
            // the page is carried by a legible bottle, so the one line nobody claims is the answer. A
            // duplicate puts a second legible bottle on a line — and if that is the line the unlabelled
            // vial belonged to, nothing is left unclaimed and the free route silently disappears. The
            // player is then asked a question whose only answer costs 45 seconds, having been given no
            // sign that this carton is different. That is hard rule 3 broken by a coincidence, and it
            // happened about one carton in sixteen.
            //
            // The drum result wins because it was decided first and deliberately so (see above); the
            // paperwork case gives way instead, to one that stays solvable beside a duplicate. No extra
            // draw is taken, so seeds are unaffected for every carton that was already legal.
            var slip = pick switch
            {
                0 => PaperworkSlip.MissingSample,
                1 => PaperworkSlip.UnlistedSample,
                _ => duplicateClaim ? PaperworkSlip.MissingSample : PaperworkSlip.UnreadableLabel
            };

            return new Plan(slipped ? slip : PaperworkSlip.None, duplicateClaim, sameDrum);
        }
    }
}
