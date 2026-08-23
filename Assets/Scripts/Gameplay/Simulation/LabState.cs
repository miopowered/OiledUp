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

        /// <summary>
        /// The house certified reference material (§5.3). One blend serves every instrument, and its
        /// values are derived from the same published baselines the manual prints — the player can
        /// look up every figure on the certificate.
        /// </summary>
        public ReferenceStandard Standard { get; }

        /// <summary>Reports from the most recent day end, for the summary screen.</summary>
        public IReadOnlyList<ConsequenceReport> LastReports => lastReports;

        public event Action<MachineInstance, TestResult> RunCompleted;
        public event Action<int> DayStarted;
        public event Action<IReadOnlyList<ConsequenceReport>> DayEnded;

        /// <summary>An instrument has been recalibrated and the archive behind it re-scored (§5.3).</summary>
        public event Action<MachineInstance, CalibrationOutcome> Calibrated;

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
            Standard = ReferenceStandard.FromProfiles(content.Profiles);

            foreach (var p in content.Profiles)
            {
                if (p != null) profilesById[p.Id] = p;
            }
        }

        /// <summary>
        /// Multiplier applied to every instrument's run time. 1 is the shipping balance.
        /// Anything else is a testing convenience — see <see cref="MachineInstance.TimeScale"/>.
        /// </summary>
        public float MachineTimeScale
        {
            get => machineTimeScale;
            set
            {
                machineTimeScale = Mathf.Max(0.001f, value);
                foreach (var m in Machines) m.TimeScale = machineTimeScale;
            }
        }

        private float machineTimeScale = 1f;

        /// <summary>Install one instrument. The MVP lab is fixed; §5.5 layout mode replaces this at M5.</summary>
        public MachineInstance Install(MachineDef def, string instanceId = null)
        {
            if (def == null) return null;
            var instance = new MachineInstance(instanceId ?? $"{def.Id}-{Machines.Count}", def)
            {
                TimeScale = machineTimeScale
            };
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

        /// <summary>
        /// The working day has run out. Instruments refuse to <i>start</i> new runs; anything
        /// already running finishes. Samples you did not get to are still your problem — that is
        /// the §6.1 pressure of the queue outpacing your hands.
        /// </summary>
        public bool ShiftOver => DayInProgress && DaySecondsRemaining <= 0f;

        /// <summary>All contracted days are done. Checked after the day ends, not after the next begins.</summary>
        public bool ContractComplete => !DayInProgress && Day >= Plan.Length;

        /// <summary>Run is over: the contract finished, or the money ran out (§1.2).</summary>
        public bool IsRunOver => Economy.IsBankrupt || ContractComplete;

        /// <summary>Starts the next day. Returns false when the run is over, so callers cannot run past the contract.</summary>
        public bool BeginDay()
        {
            if (IsRunOver) return false;

            Day++;
            var plan = Plan.ForDay(Day);
            DaySecondsRemaining = plan.DaySeconds;
            DayInProgress = true;

            // Drift walks a fresh direction each day, so a machine is never predictably biased (§5.3).
            foreach (var m in Machines) m.Runtime.BeginDay(ref rng);

            GenerateArrivals(plan);
            GenerateRequeues();
            DayStarted?.Invoke(Day);
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            if (!DayInProgress) return;

            DaySecondsRemaining = Mathf.Max(0f, DaySecondsRemaining - deltaSeconds);

            foreach (var machine in Machines)
            {
                var finished = machine.Tick(deltaSeconds);
                if (finished == RunKind.None) continue;

                if (finished == RunKind.Calibration) { CompleteCalibration(machine); continue; }

                TestResult result = finished switch
                {
                    RunKind.Blank => MeasurementPipeline.RunBlank(machine.Runtime, Day, ref rng),
                    RunKind.Reference => MeasurementPipeline.RunReference(Standard, machine.Runtime, Day, ref rng),
                    _ => Samples.RunMachine(machine.LoadedSample, machine, Day, ref rng)
                };

                if (result == null) continue;

                machine.LastResult = result;
                if (finished == RunKind.Blank)
                {
                    machine.LastBlank = result;
                    machine.LastBlankDay = Day;
                }
                else if (finished == RunKind.Reference)
                {
                    machine.LastCheck = CalibrationCheck.From(Standard, result, machine.Def, Day);
                }

                Economy.Charge(result.Cost);
                RunCompleted?.Invoke(machine, result);
            }
        }

        // -- Calibration (§5.3) -----------------------------------------------------------------------

        /// <summary>
        /// Push a certified standard through an instrument. Spends an ampoule up front, then behaves
        /// like any other run — including leaving residue, which is why a check is followed by a flush.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryStartReferenceRun(MachineInstance machine, out string refusal)
        {
            refusal = null;
            if (machine == null) { refusal = "No such instrument."; return false; }

            if (machine.IsRunning) { refusal = $"{machine.Def.DisplayName} is busy."; return false; }
            if (!machine.IsEmpty) { refusal = "Take the vial out before running a standard."; return false; }
            if (ShiftOver) { refusal = "Shift over — no new runs."; return false; }

            if (Economy.ReferenceStandards < 1)
            {
                refusal = "No certified standards left. Order more at the terminal.";
                return false;
            }

            if (!machine.TryBeginReference())
            {
                refusal = $"{machine.Def.DisplayName} will not take a standard right now.";
                return false;
            }

            Economy.TryConsumeReferenceStandard();
            return true;
        }

        /// <summary>
        /// Recalibrate against today's certificate.
        /// <para>
        /// Housekeeping rather than analysis, so it is still allowed after the shift ends — same rule
        /// as the flush. It occupies the instrument either way, which is the "costs time" half of
        /// §5.3; <see cref="EconomyTuning.CalibrationCost"/> is the other half.
        /// </para>
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryStartCalibration(MachineInstance machine, out string refusal)
        {
            refusal = null;
            if (machine == null) { refusal = "No such instrument."; return false; }

            if (machine.IsRunning) { refusal = $"{machine.Def.DisplayName} is busy."; return false; }
            if (!machine.IsEmpty) { refusal = "Take the vial out before calibrating."; return false; }

            if (!machine.HasFreshCheck(Day))
            {
                refusal = "Run today's certified standard first — there is nothing to calibrate against.";
                return false;
            }

            if (Economy.Money < Tuning.CalibrationCost)
            {
                refusal = $"A calibration costs £{Tuning.CalibrationCost:N0}, and the account will not cover it.";
                return false;
            }

            if (!machine.TryBeginCalibration(Day))
            {
                refusal = $"{machine.Def.DisplayName} will not start a calibration right now.";
                return false;
            }

            Economy.Charge(Tuning.CalibrationCost);
            return true;
        }

        /// <summary>
        /// Finish a recalibration. The order is load-bearing: the suspicion window is read off the
        /// machine <i>before</i> <see cref="MachineRuntimeState.Calibrate"/> moves its start to now,
        /// which would otherwise leave the retroactive list permanently empty.
        /// </summary>
        private void CompleteCalibration(MachineInstance machine)
        {
            var outcome = Samples.FlagDriftSuspects(machine.Runtime, machine.Runtime.DriftPercent, Day);

            machine.Runtime.Calibrate(Day);
            machine.LastCheck = null;   // consumed — the next calibration needs a fresh standard
            machine.LastCalibration = outcome;

            Calibrated?.Invoke(machine, outcome);
        }

        /// <summary>
        /// Re-open a record filed on suspect numbers, if there is enough oil left to repeat one of the
        /// tests now in doubt (§5.3).
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryReopenSuspect(SampleId id, out string refusal)
        {
            refusal = null;
            if (!Samples.TryGet(id, out var sample)) { refusal = "No such sample."; return false; }

            return Samples.ReopenForRetest(id, SmallestSuspectDraw(sample), out refusal);
        }

        /// <summary>
        /// The least oil that could repeat one of this record's suspect tests, or
        /// <see cref="float.PositiveInfinity"/> if nothing on the bench can repeat any of them.
        /// <para>
        /// The terminal reads this to tell the player how short they are <i>before</i> they press the
        /// button, because "you cannot check this any more" is information they are owed, not a
        /// punchline.
        /// </para>
        /// </summary>
        public float SmallestSuspectDraw(SampleState sample)
        {
            float smallest = float.PositiveInfinity;
            if (sample == null) return smallest;

            foreach (var result in sample.Results)
            {
                if (!result.Suspect) continue;

                foreach (var machine in Machines)
                {
                    if (machine.Def == null || machine.Def.Id != result.MachineId) continue;
                    if (machine.Def.SampleVolumeMl < smallest) smallest = machine.Def.SampleVolumeMl;
                }
            }

            return smallest;
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
            Settle(Samples.ResolveDue(Day, Tuning));

            // If that closed the run — the contract ran out, or the money did — everything still
            // pending is about to be stranded, because no further day will ever resolve it. §5.4
            // delays the cost; it does not cancel it. A verdict the player never hears back on is
            // diagnostic work that silently did nothing, and with DaysToFailure running to 14 days
            // that was most of them.
            //
            // Settled after the due pass rather than instead of it, so a player who tips into
            // bankruptcy on this day's reports still gets the rest of their reckoning.
            if (IsRunOver) Settle(Samples.ResolveDue(Day, Tuning, settleEverything: true));

            DayEnded?.Invoke(lastReports);
            return lastReports;
        }

        private void Settle(List<ConsequenceReport> reports)
        {
            foreach (var report in reports)
            {
                Economy.Apply(report);
                lastReports.Add(report);

                // MONITOR on a developing fault keeps the unit in service and it gets resampled
                // next cycle with worse numbers (§5.4). Queue it now; the chemistry progresses when
                // the next day generates it.
                if (report.RequeueSample) Samples.QueueRequeue(report.Sample);
            }
        }

        /// <summary>Re-send the units the player chose to keep watching, with the fault further along.</summary>
        private void GenerateRequeues()
        {
            foreach (var id in Samples.TakePendingRequeues())
            {
                var generated = Samples.BuildRequeue(id, generator, Day, ref rng);
                if (generated == null) continue;

                generated.State.Location = SampleLocation.InCrate("intake", -1);
                generated.State.IsSettled = false;
                generated.State.TemperatureC = rng.Range(4f, 22f);
                Samples.Add(generated);
            }
        }

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
