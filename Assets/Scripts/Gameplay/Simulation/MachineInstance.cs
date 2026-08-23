using Residue.Chemistry;
using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.Simulation
{
    public enum RunKind
    {
        None,

        /// <summary>A real sample. Consumes volume and leaves residue behind.</summary>
        Sample,

        /// <summary>A solvent blank. Reads residue directly and consumes no sample (§5.2).</summary>
        Blank
    }

    /// <summary>Why a machine refused a sample. Surfaced to the player rather than failing silently.</summary>
    public enum LoadRefusal
    {
        Accepted,
        MachineBusy,
        MachineOccupied,
        NotEnoughVolume,
        NeedsPreheat,
        NotSettled
    }

    /// <summary>
    /// One physically placed instrument: its definition, its accumulated residue and drift, and
    /// whatever is currently sitting in it.
    /// <para>
    /// This does not compute results. Measuring requires ground truth, so it happens inside
    /// <see cref="SampleRegistry"/>; this type only decides whether a run is legal and counts down
    /// the clock.
    /// </para>
    /// </summary>
    public sealed class MachineInstance
    {
        public string InstanceId;
        public MachineDef Def;
        public readonly MachineRuntimeState Runtime;

        public SampleId LoadedSample = SampleId.None;
        public RunKind ActiveRun = RunKind.None;
        public float SecondsRemaining;

        /// <summary>
        /// Multiplier on <see cref="MachineDef.RunTimeSeconds"/>. 1 is the real balance.
        /// <para>
        /// This exists so testing does not require editing <see cref="ContentTables"/>. The absolute
        /// times are balance; the <i>ratios</i> between them are design — a ferrography run costing
        /// 15 times an FTIR screen is the §10 decision. Scaling preserves those ratios; editing the
        /// table would not, and would ship.
        /// </para>
        /// </summary>
        public float TimeScale = 1f;

        private float runDuration;

        /// <summary>Most recent output, kept so the player can walk away and come back to read it.</summary>
        public TestResult LastResult;

        /// <summary>
        /// Most recent solvent blank, held separately so a later sample run cannot overwrite it.
        /// <para>
        /// This is the §5.2 tell. Hard rule: never punish something the player could not have
        /// checked — contamination is only fair because a blank reveals it, so the blank result has
        /// to survive long enough to be read at the terminal.
        /// </para>
        /// </summary>
        public TestResult LastBlank;

        /// <summary>Day the last blank was run, so the terminal can say how stale it is.</summary>
        public int LastBlankDay = -1;

        public MachineInstance(string instanceId, MachineDef def)
        {
            InstanceId = instanceId;
            Def = def;
            Runtime = new MachineRuntimeState { InstanceId = instanceId, Def = def };
        }

        public bool IsRunning => ActiveRun != RunKind.None;
        public bool IsEmpty => !LoadedSample.IsValid;
        public bool IsIdle => !IsRunning;

        /// <summary>How long a run actually takes, after <see cref="TimeScale"/>.</summary>
        public float RunSeconds => Def == null
            ? 0f
            : Mathf.Max(0.1f, Def.RunTimeSeconds * Mathf.Max(0.001f, TimeScale));

        /// <summary>Fraction complete, for a progress readout.</summary>
        public float Progress => !IsRunning || runDuration <= 0f
            ? 0f
            : 1f - (SecondsRemaining / runDuration);

        public LoadRefusal CanAccept(SampleState sample)
        {
            if (IsRunning) return LoadRefusal.MachineBusy;
            if (!IsEmpty) return LoadRefusal.MachineOccupied;
            if (sample == null) return LoadRefusal.MachineOccupied;
            if (!sample.HasVolumeFor(Def)) return LoadRefusal.NotEnoughVolume;

            // Arctic samples arrive cold; running viscosity on them gives a false high (§6.1).
            if (Def.RequiresPreheat && sample.TemperatureC < Def.PreheatTargetC - 5f)
                return LoadRefusal.NeedsPreheat;

            if (!sample.IsSettled) return LoadRefusal.NotSettled;

            return LoadRefusal.Accepted;
        }

        public LoadRefusal TryLoad(SampleState sample)
        {
            var verdict = CanAccept(sample);
            if (verdict != LoadRefusal.Accepted) return verdict;

            LoadedSample = sample.Id;
            sample.Location = SampleLocation.InMachine(InstanceId, 0);
            return LoadRefusal.Accepted;
        }

        /// <summary>Take the vial back out. Returns <see cref="SampleId.None"/> if there was nothing in it.</summary>
        public SampleId Unload()
        {
            if (IsRunning) return SampleId.None;
            var id = LoadedSample;
            LoadedSample = SampleId.None;
            return id;
        }

        public bool TryBeginRun()
        {
            if (IsRunning || IsEmpty) return false;
            ActiveRun = RunKind.Sample;
            runDuration = RunSeconds;
            SecondsRemaining = runDuration;
            return true;
        }

        /// <summary>
        /// Push solvent through and read what the previous sample left behind. Needs no vial, and
        /// deliberately does not clean the machine — it is the tell, not the fix.
        /// </summary>
        public bool TryBeginBlank()
        {
            if (IsRunning || !IsEmpty) return false;
            ActiveRun = RunKind.Blank;
            runDuration = RunSeconds;
            SecondsRemaining = runDuration;
            return true;
        }

        /// <summary>
        /// Advance the clock. Returns the kind of run that just finished, or
        /// <see cref="RunKind.None"/> if nothing completed on this tick.
        /// </summary>
        public RunKind Tick(float deltaSeconds)
        {
            if (!IsRunning) return RunKind.None;

            SecondsRemaining -= deltaSeconds;
            if (SecondsRemaining > 0f) return RunKind.None;

            var finished = ActiveRun;
            SecondsRemaining = 0f;
            ActiveRun = RunKind.None;
            return finished;
        }

        public void Clean() => Runtime.Clean();

        public override string ToString() =>
            $"{Def?.Id ?? "?"}[{InstanceId}] {(IsRunning ? $"running {Progress:P0}" : IsEmpty ? "idle" : "loaded")}";
    }
}
