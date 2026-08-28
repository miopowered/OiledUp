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
        Blank,

        /// <summary>A certified reference standard. The values going in are known, so the error coming out is the drift (§5.3).</summary>
        Reference,

        /// <summary>A recalibration. Occupies the instrument and produces no reading — only a corrected machine (§5.3).</summary>
        Calibration
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
        /// A sample run has finished and its vial is still sitting in the instrument.
        /// <para>
        /// The difference between "press E to run this" and "press E to take your vial back", and it
        /// belongs here rather than on the station that pressed the button. A station remembering it
        /// locally is only right for the player who started the run: in co-op, everyone else's station
        /// offers to run the sample a second time, which quietly spends millilitres nobody asked to
        /// spend (§4.5). Kept on the instrument, it is the same answer for the whole room and it
        /// replicates.
        /// </para>
        /// Only a <see cref="RunKind.Sample"/> sets it. A blank and a standard need no vial and leave
        /// nothing to collect.
        /// </summary>
        public bool HasResultWaiting;

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

        /// <summary>
        /// The certificate against the readout from the most recent standard, held for the same
        /// reason as <see cref="LastBlank"/>: it is the §5.3 tell, and a tell that cannot still be
        /// read at the terminal is no tell at all. Cleared by a recalibration, which consumes it.
        /// </summary>
        public CalibrationCheck LastCheck;

        /// <summary>What the last recalibration corrected, and how much filed work it put in doubt.</summary>
        public CalibrationOutcome? LastCalibration;

        /// <summary>
        /// The readout <see cref="LastCheck"/> was made from, kept only so a run save can rebuild the
        /// check rather than store a second copy of it (#49).
        /// <para>
        /// A <see cref="CalibrationCheck"/> is a <i>reading</i> of this result against the house
        /// certificate, and the certificate is derived from the profiles the catalog already holds.
        /// Saving the derived object would put a number on disk that has a source, and the two would
        /// be free to disagree the next time the baselines are retuned. Saving the run it came from
        /// cannot: <c>CalibrationCheck.From</c> re-runs the same arithmetic on load.
        /// </para>
        /// Cleared with <see cref="LastCheck"/>, because a consumed certificate has nothing left to read.
        /// </summary>
        internal TestResult LastCheckRun;

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
            HasResultWaiting = false;
            SampleLifecycle.TryMove(sample, SampleLocation.InMachine(InstanceId, 0), out _);
            return LoadRefusal.Accepted;
        }

        /// <summary>Take the vial back out. Returns <see cref="SampleId.None"/> if there was nothing in it.</summary>
        public SampleId Unload()
        {
            if (IsRunning) return SampleId.None;
            var id = LoadedSample;
            LoadedSample = SampleId.None;
            HasResultWaiting = false;
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
        /// Push a certified standard through. Needs no vial and consumes no sample volume; the
        /// ampoule is the consumable, and the caller is the one that spends it.
        /// </summary>
        public bool TryBeginReference()
        {
            if (IsRunning || !IsEmpty) return false;
            ActiveRun = RunKind.Reference;
            runDuration = RunSeconds;
            SecondsRemaining = runDuration;
            return true;
        }

        /// <summary>
        /// A zero-and-span adjustment, which is roughly half a measurement cycle on any of these
        /// instruments. Scaled off <see cref="RunSeconds"/> rather than given its own constant so the
        /// §10 ratios survive — calibrating the cooling curve tester has to hurt like the cooling
        /// curve tester, not like a titrator.
        /// </summary>
        public float CalibrationSeconds => Mathf.Max(0.1f, RunSeconds * 0.5f);

        /// <summary>
        /// True when a certificate from <paramref name="day"/> is on file.
        /// <para>
        /// An instrument cannot be calibrated against nothing, and a certificate from an earlier day
        /// has had a whole day of drift walk over it — <see cref="MachineRuntimeState.BeginDay"/>
        /// re-rolls the direction every morning. Letting one ampoule authorise every calibration for
        /// the rest of the contract would make the standard a formality instead of the thing that
        /// measures the error.
        /// </para>
        /// </summary>
        public bool HasFreshCheck(int day) => LastCheck != null && LastCheck.Day == day;

        /// <summary>Recalibrate against the standard already run today. Occupies the instrument.</summary>
        public bool TryBeginCalibration(int day)
        {
            if (IsRunning || !IsEmpty || !HasFreshCheck(day)) return false;
            ActiveRun = RunKind.Calibration;
            runDuration = CalibrationSeconds;
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

            if (finished == RunKind.Sample) HasResultWaiting = true;
            return finished;
        }

        public void Clean() => Runtime.Clean();

        /// <summary>
        /// How long the run in progress was going to take. Needed by a save (#49) because
        /// <see cref="Progress"/> is measured against it: restoring only
        /// <see cref="SecondsRemaining"/> would leave a half-finished run reading as barely started.
        /// </summary>
        internal float RunDuration => runDuration;

        /// <summary>Put a run in progress back the way a save found it. See <see cref="RunDuration"/>.</summary>
        internal void RestoreRun(RunKind kind, float secondsRemaining, float duration)
        {
            ActiveRun = kind;
            SecondsRemaining = secondsRemaining;
            runDuration = duration;
        }

        public override string ToString() =>
            $"{Def?.Id ?? "?"}[{InstanceId}] {(IsRunning ? $"running {Progress:P0}" : IsEmpty ? "idle" : "loaded")}";
    }
}
