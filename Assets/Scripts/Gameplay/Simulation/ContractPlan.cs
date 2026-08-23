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

        /// <summary>
        /// The MVP shakedown day. Diesel and gearbox together so both keystone traps are reachable:
        /// dirt ingress (root cause is the air filter, not the worn part) and gear spalling (which
        /// the ICP physically cannot see). Seven samples is enough queue pressure that machine
        /// occupancy starts to bite — §10's single-sample version cannot create the "four samples
        /// queued behind this one" tension that makes the decision hard.
        /// </summary>
        public static ContractPlan Default() => new()
        {
            Id = "mvp_shakedown",
            DisplayName = "Shakedown",
            Days =
            {
                new DayPlan
                {
                    SampleCount = 7,
                    ProfileIds = new[] { "diesel_engine_heavy", "gearbox_industrial" },
                    BorderlineCount = 2,
                    HealthyChance = 0.35f,
                    DaySeconds = 900f
                },
                new DayPlan
                {
                    SampleCount = 8,
                    ProfileIds = new[] { "diesel_engine_heavy", "gearbox_industrial" },
                    BorderlineCount = 3,
                    HealthyChance = 0.30f,
                    DaySeconds = 900f
                },
                new DayPlan
                {
                    SampleCount = 9,
                    ProfileIds = new[] { "diesel_engine_heavy", "gearbox_industrial", "hydraulic_system" },
                    BorderlineCount = 3,
                    HealthyChance = 0.30f,
                    DaySeconds = 900f
                }
            }
        };
    }
}
