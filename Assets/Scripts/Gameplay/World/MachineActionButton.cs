using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    public enum MachineAction
    {
        /// <summary>Flush the instrument. Zeroes residue, costs solvent and a real chunk of time.</summary>
        Clean,

        /// <summary>Push solvent through and read what is left behind. The tell, not the fix.</summary>
        Blank,

        /// <summary>Run a certified standard. Known values in, so whatever comes back out is the drift (§5.3).</summary>
        Reference,

        /// <summary>Zero the instrument against today's certificate, and find out what that costs (§5.3).</summary>
        Calibrate
    }

    /// <summary>
    /// A physical button on an instrument for the actions that are not "run this vial".
    /// <para>
    /// Cleaning is a held action on purpose. §5.2 makes skipping the flush tempting, and §9 requires
    /// prep to be hand-operated tasks with a real time cost rather than menu clicks — a one-tap
    /// clean would delete the entire mechanic.
    /// </para>
    /// The two calibration actions are taps rather than holds because their cost is already paid
    /// somewhere honest: a standard run and a recalibration both occupy the instrument for a share of
    /// its own cycle, and both are charged. Adding a hold on top would tax the player's hands for
    /// something the machine is doing.
    /// </summary>
    public sealed class MachineActionButton : Interactable
    {
        [SerializeField] private MachineStation station;
        [SerializeField] private MachineAction action = MachineAction.Clean;

        [Tooltip("§5.2 specifies 20-40 s for a clean. Tune after playtesting, but do not make it free.")]
        [SerializeField] private float cleanSeconds = 20f;

        /// <summary>
        /// Even fully scaled down for testing, a flush stays a held action. At a tap it stops being
        /// a cost, and the whole §5.2 temptation to skip it evaporates.
        /// </summary>
        private const float MinimumFlushSeconds = 2f;

        private void Awake()
        {
            if (station == null) station = GetComponentInParent<MachineStation>();
        }

        private static bool ShiftOver
        {
            get
            {
                var lab = LabRuntime.Instance;
                return lab != null && lab.Lab != null && lab.Lab.ShiftOver;
            }
        }

        public override float HoldSeconds
        {
            get
            {
                if (action != MachineAction.Clean) return 0f;

                var runtime = LabRuntime.Instance;
                float scale = runtime != null && runtime.Lab != null ? runtime.Lab.MachineTimeScale : 1f;
                return Mathf.Max(MinimumFlushSeconds, cleanSeconds * scale);
            }
        }

        public override string Prompt(PlayerInteractor player)
        {
            var machine = station != null ? station.Machine : null;
            var lab = LabRuntime.Instance?.Lab;
            if (machine == null) return null;

            if (action == MachineAction.Clean)
            {
                if (machine.IsRunning) return "Cannot flush while running";
                if (lab != null && lab.Economy.SolventUnits < 1f) return "Out of solvent";
                return $"Hold to flush {machine.Def.DisplayName} ({HoldSeconds:F0}s, 1 solvent)";
            }

            if (machine.IsRunning) return "Instrument busy";

            if (action == MachineAction.Calibrate)
            {
                if (!machine.IsEmpty) return "Remove the vial before calibrating";
                if (lab == null) return null;
                if (!machine.HasFreshCheck(lab.Day)) return "Run today's certified standard first";
                if (lab.Economy.Money < lab.Tuning.CalibrationCost) return "Cannot afford the calibration";

                return $"Recalibrate {machine.Def.DisplayName} " +
                       $"({machine.CalibrationSeconds:F0}s, £{lab.Tuning.CalibrationCost:N0})";
            }

            if (action == MachineAction.Reference)
            {
                if (!machine.IsEmpty) return "Remove the vial before running a standard";
                if (ShiftOver) return "Shift over — no new runs";
                if (lab != null && lab.Economy.ReferenceStandards < 1)
                    return "No certified standards — order them at the terminal";

                return $"Run certified standard ({machine.RunSeconds:F0}s, 1 ampoule) — flush afterwards";
            }

            if (!machine.IsEmpty) return "Remove the vial before running a blank";
            if (ShiftOver) return "Shift over — no new runs";
            return $"Run solvent blank ({machine.RunSeconds:F0}s)";
        }

        public override bool CanInteract(PlayerInteractor player)
        {
            var machine = station != null ? station.Machine : null;
            var lab = LabRuntime.Instance?.Lab;
            if (machine == null || machine.IsRunning || lab == null) return false;

            if (action == MachineAction.Clean)
            {
                // Flushing is housekeeping, not analysis — still allowed after the shift ends.
                return lab.Economy.SolventUnits >= 1f;
            }

            // Calibration is housekeeping too: it produces no reading, so the shift clock does not
            // gate it. Being locked out of correcting an instrument you have just proved is wrong
            // would be a punishment for checking.
            if (action == MachineAction.Calibrate)
                return machine.IsEmpty && machine.HasFreshCheck(lab.Day) &&
                       lab.Economy.Money >= lab.Tuning.CalibrationCost;

            if (action == MachineAction.Reference)
                return machine.IsEmpty && !ShiftOver && lab.Economy.ReferenceStandards >= 1;

            return machine.IsEmpty && !ShiftOver;
        }

        public override void Interact(PlayerInteractor player)
        {
            var machine = station != null ? station.Machine : null;
            var lab = LabRuntime.Instance?.Lab;
            if (machine == null || lab == null) return;

            switch (action)
            {
                case MachineAction.Clean:
                    if (!lab.Economy.TryConsumeSolvent()) return;
                    machine.Clean();
                    player.Say($"{machine.Def.DisplayName}: flushed. Residue cleared.");
                    return;

                case MachineAction.Reference:
                    if (!lab.TryStartReferenceRun(machine, out string standardRefusal))
                    {
                        player.Say(standardRefusal);
                        return;
                    }
                    player.Say($"{machine.Def.DisplayName}: certified standard running. " +
                               "Compare it against the certificate at the terminal.");
                    return;

                case MachineAction.Calibrate:
                    if (!lab.TryStartCalibration(machine, out string calibrationRefusal))
                    {
                        player.Say(calibrationRefusal);
                        return;
                    }
                    player.Say($"{machine.Def.DisplayName}: recalibrating.");
                    return;

                default:
                    if (machine.TryBeginBlank())
                        player.Say($"{machine.Def.DisplayName}: blank running. Check the terminal for what it finds.");
                    return;
            }
        }
    }
}
