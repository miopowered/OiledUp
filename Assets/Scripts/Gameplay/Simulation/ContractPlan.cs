using System;
using System.Collections.Generic;

namespace Residue.Gameplay.Simulation
{
    /// <summary>What arrives on one in-game day.</summary>
    [Serializable]
    public struct DayPlan
    {
        public int SampleCount;

        /// <summary>Equipment profile ids that can arrive today. §6.1 opens with diesel only.</summary>
        public string[] ProfileIds;

        /// <summary>
        /// Firms whose vials can arrive today, or null for anyone in the catalog who runs the fluid.
        /// <para>
        /// §6.1's day one is "one customer, clean panel", and until now the only knob was the fluid —
        /// which is not the same thing, because six firms run the tutorial oil and they differ in how
        /// reliable their paperwork is (<c>CustomerReliability</c>). A day that wants a clean delivery
        /// has to be able to say so, or #32's discrepancies arrive on a morning that was supposed to
        /// be teaching the panel.
        /// </para>
        /// <para>
        /// Null on every day of <see cref="ContractPlan.Default"/>, so the shipping contract draws
        /// exactly the senders it always did.
        /// </para>
        /// </summary>
        public string[] CustomerIds;

        /// <summary>
        /// §6.3 ambiguity budget: how many of today's samples are forced into the band where a
        /// single test cannot resolve them. This is the pressure that survives the community
        /// writing the table down, so it is a per-day quota rather than a random chance.
        /// </summary>
        public int BorderlineCount;

        /// <summary>Probability a non-borderline sample is genuinely healthy.</summary>
        public float HealthyChance;

        /// <summary>Length of the working day, in seconds of real time.</summary>
        public float DaySeconds;
    }

    /// <summary>
    /// A fixed-length contract (§1.2) — days of arrivals, not an endless drift.
    /// Days beyond the authored list repeat the last entry so a run cannot fall off the end.
    /// </summary>
    [Serializable]
    public sealed class ContractPlan
    {
        /// <summary>Id of the shipping contract. See <see cref="ById"/>.</summary>
        public const string DefaultId = "mvp_shakedown";

        /// <summary>Id of the guided two-day contract. See <see cref="Tutorial"/>.</summary>
        public const string TutorialId = "tutorial_induction";

        public string Id = DefaultId;
        public string DisplayName = "Shakedown";
        public List<DayPlan> Days = new();

        public int Length => Days.Count;

        /// <summary>
        /// The authored contract with this id, or null when nothing answers to it.
        /// <para>
        /// A run save stores this id rather than a copy of the twenty <see cref="DayPlan"/> rows, for
        /// the same reason it stores content by id: the plan is balance, it is regenerated from
        /// <see cref="Default"/> whenever the ramp is retuned, and a save carrying a stale copy would
        /// quietly fork the arrival curve for anyone mid-contract. Null is the honest answer for an id
        /// this build no longer has, and the loader refuses on it rather than substituting a
        /// different contract under the player.
        /// </para>
        /// <para>
        /// <b>It answers for every contract the build ships, not for the ones that happen to be
        /// saved.</b> <see cref="RunSnapshotRestore"/> turns a null into "this build no longer offers
        /// that contract" and stops, so a build that shipped the tutorial and answered null for it
        /// would be refusing a run on a lie the player cannot act on. Which contracts get written to
        /// the slot is a separate question, decided in <c>LabRuntime</c> — the tutorial deliberately
        /// writes none — and this method must not encode that answer.
        /// </para>
        /// </summary>
        public static ContractPlan ById(string id)
        {
            if (string.Equals(id, DefaultId, StringComparison.Ordinal)) return Default();
            if (string.Equals(id, TutorialId, StringComparison.Ordinal)) return Tutorial();
            return null;
        }

        public DayPlan ForDay(int day)
        {
            if (Days.Count == 0) return Default().Days[0];
            int index = Math.Clamp(day - 1, 0, Days.Count - 1);
            return Days[index];
        }

        /// <summary>Length of the working day. See <see cref="Default"/> for why it is 5 minutes.</summary>
        public const float DefaultDaySeconds = 300f;

        /// <summary>
        /// The §6.1 twenty-day arc.
        /// <para>
        /// Length is load-bearing, not flavour. Consequences settle <c>DaysToFailure</c> days after
        /// a verdict is filed and those run from 4 to 14 days, so a short contract ends before the
        /// player learns whether a single diagnosis was right. The three-day version this replaces
        /// resolved 4 verdicts out of 24 — and only the healthy ones, never a fault anybody had to
        /// work for. <see cref="LabState.EndDay"/> covers the tail; this covers the rhythm.
        /// </para>
        /// The day is 5 minutes rather than 15 so a full run is roughly 100 minutes. A 20-day
        /// contract at the old 900s day was five hours, which is not a thing anyone will sit
        /// through to find out whether the loop works.
        /// <para>
        /// Authored as a curve rather than twenty literals so the shape of the ramp is the thing you
        /// read in a diff, not the arithmetic.
        /// </para>
        /// </summary>
        public static ContractPlan Default()
        {
            var plan = new ContractPlan
            {
                Id = DefaultId,
                DisplayName = "Shakedown",
                Days = new List<DayPlan>()
            };

            for (int day = 1; day <= 20; day++)
            {
                plan.Days.Add(new DayPlan
                {
                    // 7 rising to 16. Enough queue pressure by the end that instrument occupancy
                    // decides the day, which is the §5.5 skill ceiling.
                    SampleCount = 7 + (day - 1) * 9 / 19,

                    ProfileIds = ProfilesForDay(day),

                    // §6.3 ambiguity budget climbs with volume: roughly a third of the day, so the
                    // pressure scales instead of thinning out as the queue grows.
                    BorderlineCount = 2 + (day - 1) * 4 / 19,

                    // Healthy units get rarer, so "it is probably fine" stops being a free strategy.
                    HealthyChance = 0.35f - (day - 1) * 0.15f / 19f,

                    DaySeconds = DefaultDaySeconds
                });
            }

            return plan;
        }

        /// <summary>
        /// §6.1 widens the fluid families as the contract runs. One family first, so the panel can
        /// be learned before water limits start differing by an order of magnitude between samples.
        /// </summary>
        private static string[] ProfilesForDay(int day)
        {
            if (day <= 3)
                return new[] { "hardening_oil_general", "quench_oil_cold" };

            if (day <= 8)
                return new[] { "hardening_oil_general", "quench_oil_cold", "quench_oil_martempering" };

            if (day <= 14)
                return new[]
                {
                    "hardening_oil_general", "quench_oil_cold", "quench_oil_martempering",
                    "quench_oil_vacuum"
                };

            return new[]
            {
                "hardening_oil_general", "quench_oil_cold", "quench_oil_martempering",
                "quench_oil_vacuum", "quench_oil_accelerated", "corrosion_protection_oil"
            };
        }

        // -- The tutorial contract --------------------------------------------------------------------

        /// <summary>
        /// The seed the tutorial always runs on, so the two days are the same two days every time.
        /// <para>
        /// A guided run whose arrivals moved between attempts would make the objective card a
        /// generality — "load an instrument" against a delivery that might this time contain nothing
        /// loadable. Fixed here rather than on <c>LabRuntime</c>'s inspector field, because that field
        /// is a testing convenience and the tutorial's determinism is a property of the contract.
        /// </para>
        /// </summary>
        public const int TutorialSeed = 20260901;

        /// <summary>
        /// Two guided days, and the only contract in the game that is allowed to be easy.
        ///
        /// <para>
        /// <b>One fluid, and it is the one the tables already nominate.</b>
        /// <c>hardening_oil_general</c> is documented in <c>ContentTables</c> as the tutorial fluid —
        /// "wide bands everywhere, so a first-day player can be wrong by a margin and still read the
        /// panel correctly" — so nothing here is a second opinion about which oil teaches best.
        /// </para>
        ///
        /// <para>
        /// <b>One sender, and it is the reference customer.</b> Vogel's paperwork is never wrong
        /// (<c>CustomerReliability.Meticulous</c>, both discrepancy chances zero), so no smudged label
        /// or duplicated claim lands on a morning whose job is teaching where the instruments are.
        /// #32 is a mechanic the real contract introduces; the tutorial does not get to introduce it
        /// badly.
        /// </para>
        ///
        /// <para>
        /// <b>No ambiguity budget.</b> §6.3's borderline quota is the difficulty pressure that
        /// survives the community writing the table down. Spending it on someone who has not yet
        /// carried a vial across the room teaches nothing and reads as the chemistry being unreliable,
        /// which hard rule 1 does not allow even as an impression. <see cref="DayPlan.HealthyChance"/>
        /// is correspondingly generous — but not 1, because a tutorial in which everything is fine
        /// would be teaching the one wrong lesson it is possible to teach here.
        /// </para>
        ///
        /// <para>
        /// <b>Longer days than the shipping contract.</b> The truck arrives a quarter of the way in
        /// (<c>DeliveryBay.DefaultArrivalShiftFraction</c>), so a 300 s day leaves 225 s to unbox, run
        /// something that takes 180–300 s of its own, walk a slip to the desk and file it. That is not
        /// generous, it is a stopwatch. Ten minutes is.
        /// </para>
        ///
        /// <para>
        /// Two days rather than one because day two is where the blank and the certified standard
        /// live, and both only make sense on an instrument that has already had something through it.
        /// See <c>TutorialObjectives</c>, which is where those two are pointed at — hard rule 3 rests
        /// on the player knowing they exist.
        /// </para>
        /// </summary>
        public static ContractPlan Tutorial()
        {
            var oil = new[] { "hardening_oil_general" };
            var sender = new[] { "vogel_getriebe" };

            return new ContractPlan
            {
                Id = TutorialId,
                DisplayName = "Induction",
                Days = new List<DayPlan>
                {
                    // Enough vials that a mistake on one is not the end of the day, few enough that
                    // the bench never looks like work.
                    new()
                    {
                        SampleCount = 3,
                        ProfileIds = oil,
                        CustomerIds = sender,
                        BorderlineCount = 0,
                        HealthyChance = 0.65f,
                        DaySeconds = 600f
                    },

                    // One more than yesterday, so the second day is recognisably the same job rather
                    // than a separate lesson bolted on.
                    new()
                    {
                        SampleCount = 4,
                        ProfileIds = oil,
                        CustomerIds = sender,
                        BorderlineCount = 0,
                        HealthyChance = 0.60f,
                        DaySeconds = 600f
                    }
                }
            };
        }
    }
}
