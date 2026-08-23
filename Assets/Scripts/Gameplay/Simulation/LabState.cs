using System;
using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// The whole run, host-side: the day clock, the sample vault, the installed instruments and the
    /// books. Everything a client is allowed to see hangs off here; everything it is not stays
    /// inside <see cref="SampleRegistry"/>.
    /// <para>
    /// Deliberately a plain C# class with no MonoBehaviour and no Unity lifecycle, so the entire
    /// game loop can be stepped in a unit test — and so it becomes the host's owned state at M4
    /// without a rewrite.
    /// </para>
    /// </summary>
    public sealed class LabState
    {
        public int Day { get; private set; }
        public float DaySecondsRemaining { get; private set; }
        public bool DayInProgress { get; private set; }

        public Economy Economy { get; }
        public SampleRegistry Samples { get; } = new();
        public List<MachineInstance> Machines { get; } = new();
        public EconomyTuning Tuning { get; }
        public ContractPlan Plan { get; }
        public ContentCatalog Content { get; }

        /// <summary>Reports from the most recent day end, for the summary screen.</summary>
        public IReadOnlyList<ConsequenceReport> LastReports => lastReports;

        public event Action<MachineInstance, TestResult> RunCompleted;
        public event Action<int> DayStarted;
        public event Action<IReadOnlyList<ConsequenceReport>> DayEnded;

        private Rng rng;
        private readonly SampleGenerator generator;
        private readonly Dictionary<string, EquipmentProfileDef> profilesById = new();
        private readonly List<ConsequenceReport> lastReports = new();

        public LabState(ContentCatalog content, ContractPlan plan, int seed, EconomyTuning tuning = null)
        {
            Content = content;
            Plan = plan ?? ContractPlan.Default();
            Tuning = tuning ?? new EconomyTuning();
            Economy = new Economy(Tuning);

            rng = new Rng(seed);
            generator = new SampleGenerator(content.Faults);

            foreach (var p in content.Profiles)
            {
                if (p != null) profilesById[p.Id] = p;
            }
        }

        /// <summary>Install one instrument. The MVP lab is fixed; §5.5 layout mode replaces this at M5.</summary>
        public MachineInstance Install(MachineDef def, string instanceId = null)
        {
            if (def == null) return null;
            var instance = new MachineInstance(instanceId ?? $"{def.Id}-{Machines.Count}", def);
            Machines.Add(instance);
            return instance;
        }

        public MachineInstance FindMachine(string instanceId)
        {
            foreach (var m in Machines)
            {
                if (m.InstanceId == instanceId) return m;
            }
            return null;
        }

        // -- Day cycle ------------------------------------------------------------------------------

        public void BeginDay()
        {
            Day++;
            var plan = Plan.ForDay(Day);
            DaySecondsRemaining = plan.DaySeconds;
            DayInProgress = true;

            // Drift walks a fresh direction each day, so a machine is never predictably biased (§5.3).
            foreach (var m in Machines) m.Runtime.BeginDay(ref rng);

            GenerateArrivals(plan);
            DayStarted?.Invoke(Day);
        }

        public void Tick(float deltaSeconds)
        {
            if (!DayInProgress) return;

            DaySecondsRemaining = Mathf.Max(0f, DaySecondsRemaining - deltaSeconds);

            foreach (var machine in Machines)
            {
                var finished = machine.Tick(deltaSeconds);
                if (finished == RunKind.None) continue;

                TestResult result = finished == RunKind.Blank
                    ? MeasurementPipeline.RunBlank(machine.Runtime, Day, ref rng)
                    : Samples.RunMachine(machine.LoadedSample, machine, Day, ref rng);

                if (result == null) continue;

                machine.LastResult = result;
                Economy.Charge(result.Cost);
                RunCompleted?.Invoke(machine, result);
            }
        }

        /// <summary>
        /// Close the day and settle every verdict that has come due. Returns the reports so the
        /// summary screen can show what the player got wrong days after they got it wrong.
        /// </summary>
        public IReadOnlyList<ConsequenceReport> EndDay()
        {
            DayInProgress = false;
            DaySecondsRemaining = 0f;

            lastReports.Clear();
            foreach (var report in Samples.ResolveDue(Day, Tuning))
            {
                Economy.Apply(report);
                lastReports.Add(report);
            }

            DayEnded?.Invoke(lastReports);
            return lastReports;
        }

        /// <summary>Run is over: the contract finished, or the money ran out (§1.2).</summary>
        public bool IsRunOver => Economy.IsBankrupt || Day > Plan.Length;

        // -- Arrivals -------------------------------------------------------------------------------

        private void GenerateArrivals(in DayPlan plan)
        {
            if (plan.ProfileIds == null || plan.ProfileIds.Length == 0) return;

            for (int i = 0; i < plan.SampleCount; i++)
            {
                string profileId = plan.ProfileIds[rng.Range(0, plan.ProfileIds.Length)];
                if (!profilesById.TryGetValue(profileId, out var profile)) continue;

                var request = GenerationRequest.Default(profile, EquipmentTags.For(profileId, ref rng), Day);
                request.HealthyChance = plan.HealthyChance;
                request.HoursSinceOilChange = rng.Range(0.15f, 1f) * profile.DefaultOilChangeHours;

                // Fill the ambiguity quota first. A borderline sample must have a fault to sit on the
                // edge of, so healthy is off the table for these.
                if (i < plan.BorderlineCount)
                {
                    request.ForceBorderline = true;
                    request.HealthyChance = 0f;
                }

                var generated = generator.Generate(request, ref rng);
                if (generated == null) continue;

                generated.State.FieldTechNote = EquipmentTags.Note(ref rng);
                generated.State.Location = SampleLocation.InCrate("intake", i);

                // Arrives unsettled and at ambient. Agitating and warming are player actions with a
                // real time cost (§9) rather than menu clicks.
                generated.State.IsSettled = false;
                generated.State.TemperatureC = rng.Range(4f, 22f);

                Samples.Add(generated);
            }
        }

        /// <summary>Samples that have arrived and not yet had a verdict filed.</summary>
        public List<SampleState> OpenSamples()
        {
            var open = new List<SampleState>();
            foreach (var s in Samples.All)
            {
                if (!s.FiledVerdict.HasValue) open.Add(s);
            }
            open.Sort((a, b) => a.Id.CompareTo(b.Id));
            return open;
        }
    }
}
