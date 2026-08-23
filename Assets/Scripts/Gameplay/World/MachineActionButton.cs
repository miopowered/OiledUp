using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    public enum MachineAction
    {
        /// <summary>Flush the instrument. Zeroes residue, costs solvent and a real chunk of time.</summary>
        Clean,

        /// <summary>Push solvent through and read what is left behind. The tell, not the fix.</summary>
        Blank
    }

    /// <summary>
    /// A physical button on an instrument for the two actions that are not "run this vial".
    /// <para>
    /// Cleaning is a held action on purpose. §5.2 makes skipping the flush tempting, and §9 requires
    /// prep to be hand-operated tasks with a real time cost rather than menu clicks — a one-tap
    /// clean would delete the entire mechanic.
    /// </para>
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
            if (machine == null) return null;

            if (action == MachineAction.Clean)
            {
                var economy = LabRuntime.Instance?.Lab.Economy;
                if (machine.IsRunning) return "Cannot flush while running";
                if (economy != null && economy.SolventUnits < 1f) return "Out of solvent";
                return $"Hold to flush {machine.Def.DisplayName} ({HoldSeconds:F0}s, 1 solvent)";
            }

            if (machine.IsRunning) return "Instrument busy";
            if (!machine.IsEmpty) return "Remove the vial before running a blank";
            if (ShiftOver) return "Shift over — no new runs";
            return $"Run solvent blank ({machine.RunSeconds:F0}s)";
        }

        public override bool CanInteract(PlayerInteractor player)
        {
            var machine = station != null ? station.Machine : null;
            if (machine == null || machine.IsRunning) return false;

            if (action == MachineAction.Clean)
            {
                // Flushing is housekeeping, not analysis — still allowed after the shift ends.
                var economy = LabRuntime.Instance?.Lab.Economy;
                return economy != null && economy.SolventUnits >= 1f;
            }

            return machine.IsEmpty && !ShiftOver;
        }

        public override void Interact(PlayerInteractor player)
        {
            var machine = station != null ? station.Machine : null;
            var lab = LabRuntime.Instance?.Lab;
            if (machine == null || lab == null) return;

            if (action == MachineAction.Clean)
            {
                if (!lab.Economy.TryConsumeSolvent()) return;
                machine.Clean();
                player.Say($"{machine.Def.DisplayName}: flushed. Residue cleared.");
                return;
            }

            if (machine.TryBeginBlank())
                player.Say($"{machine.Def.DisplayName}: blank running. Check the terminal for what it finds.");
        }
    }
}
