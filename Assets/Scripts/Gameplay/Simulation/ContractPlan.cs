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
        public string Id = "mvp_shakedown";
        public string DisplayName = "Shakedown";
        public List<DayPlan> Days = new();

        public int Length => Days.Count;

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
                Id = "mvp_shakedown",
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
    }
}
