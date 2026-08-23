using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The physical instrument. Contextual single-key interaction: load, run, take back.
    /// <para>
    /// Note what this deliberately cannot do — compute a result. Measurement needs ground truth and
    /// therefore happens inside <see cref="SampleRegistry"/> on the host. At M4 this component runs
    /// on every client and must stay incapable of it (§3.1).
    /// </para>
    /// </summary>
    public sealed class MachineStation : Interactable
    {
        [Tooltip("Must match an id in LabRuntime.installedMachineIds.")]
        [SerializeField] private string machineInstanceId = "icp";

        [SerializeField] private Transform vialSocket;

        [Tooltip("Where the results slip appears when a run finishes.")]
        [SerializeField] private Transform printoutSocket;

        [SerializeField] private Renderer statusLight;
        [SerializeField] private MachineDisplay display;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock block;
        private MachineInstance machine;
        private bool ranSinceLoad;

        public MachineInstance Machine => machine;
        public string InstanceId => machineInstanceId;
        public Transform VialSocket => vialSocket;

        private void Start()
        {
            var lab = LabRuntime.Instance;
            machine = lab != null ? lab.Lab.FindMachine(machineInstanceId) : null;

            if (machine == null)
            {
                Debug.LogError(
                    $"[MachineStation] No installed machine with instance id '{machineInstanceId}'.", this);
                return;
            }

            name = $"Machine_{machine.Def.Id}";
            lab.Lab.RunCompleted += OnRunCompleted;
        }

        private void OnDestroy()
        {
            var lab = LabRuntime.Instance;
            if (lab?.Lab != null) lab.Lab.RunCompleted -= OnRunCompleted;
        }

        private float nextDisplayRefresh;

        private void Update()
        {
            UpdateStatusLight();

            // Redrawing the screen rasterises every pixel, so throttle it. A progress readout does
            // not need 60 Hz.
            if (display == null || machine == null || !machine.IsRunning) return;
            if (Time.time < nextDisplayRefresh) return;

            nextDisplayRefresh = Time.time + 0.2f;
            display.ShowRunning(machine);
        }

        // -- Interaction ----------------------------------------------------------------------------

        public override string Prompt(PlayerInteractor player)
        {
            if (machine == null) return null;
            string title = machine.Def.DisplayName;

            if (machine.IsRunning)
                return $"{title} — running, {machine.SecondsRemaining:F0}s left";

            if (player.CarriedVial != null && machine.IsEmpty)
            {
                var sample = LabRuntime.Instance?.SampleFor(player.CarriedVial.SampleId);
                return machine.CanAccept(sample) switch
                {
                    LoadRefusal.Accepted => $"Load into {title}",
                    LoadRefusal.NotEnoughVolume =>
                        $"{title} needs {machine.Def.SampleVolumeMl:F0} ml — {sample?.VolumeMl:F1} ml left",
                    LoadRefusal.NotSettled => $"{title}: sample has settled out — hold LMB to agitate first",
                    LoadRefusal.NeedsPreheat => $"{title}: sample is cold, needs preheating",
                    _ => $"{title} is occupied"
                };
            }

            if (!machine.IsEmpty && player.Carried == null)
            {
                if (ranSinceLoad) return $"Take vial from {title}";
                return ShiftOver
                    ? $"{title} — shift over, no new runs"
                    : $"Run {title} ({machine.RunSeconds:F0}s)";
            }

            if (player.Carried != null) return "Hands full";
            return $"{title} — empty";
        }

        /// <summary>
        /// The working day has run out. Instruments stop accepting work, but anything already
        /// loaded can still be retrieved — being locked out of your own vials would be a softlock,
        /// and the pressure is meant to come from unfinished analysis, not confiscated glassware.
        /// </summary>
        private bool ShiftOver
        {
            get
            {
                var lab = LabRuntime.Instance;
                return lab != null && lab.Lab != null && lab.Lab.ShiftOver;
            }
        }

        public override bool CanInteract(PlayerInteractor player)
        {
            if (machine == null || machine.IsRunning) return false;

            if (player.Carried != null)
            {
                if (player.CarriedVial == null) return false; // holding a slip or a manual
                if (ShiftOver || !machine.IsEmpty) return false;
                var sample = LabRuntime.Instance?.SampleFor(player.CarriedVial.SampleId);
                return machine.CanAccept(sample) == LoadRefusal.Accepted;
            }

            if (machine.IsEmpty) return false;
            return ranSinceLoad || !ShiftOver;
        }

        public override void Interact(PlayerInteractor player)
        {
            if (machine == null || machine.IsRunning) return;

            if (player.CarriedVial != null) { Load(player); return; }
            if (player.Carried != null) return;
            if (machine.IsEmpty) return;

            if (ranSinceLoad) TakeBack(player);
            else StartRun(player);
        }

        private void Load(PlayerInteractor player)
        {
            var lab = LabRuntime.Instance;
            var sample = lab?.SampleFor(player.CarriedVial.SampleId);
            if (sample == null) return;

            if (machine.TryLoad(sample) != LoadRefusal.Accepted) return;

            var vial = player.ReleaseCarried();
            vial.AttachTo(vialSocket != null ? vialSocket : transform, interactable: false);
            ranSinceLoad = false;
        }

        private void StartRun(PlayerInteractor player)
        {
            if (!machine.TryBeginRun()) return;
            ranSinceLoad = true;
            player.Say($"{machine.Def.DisplayName}: running. {machine.Def.RunTimeSeconds:F0}s.");
        }

        private void TakeBack(PlayerInteractor player)
        {
            var id = machine.Unload();
            if (!id.IsValid) return;

            var lab = LabRuntime.Instance;
            var vial = lab?.PropFor(id);
            var sample = lab?.SampleFor(id);

            if (vial != null)
            {
                player.TryCarry(vial);
                if (sample != null) vial.SetFillFraction(sample.VolumeMl / 100f);
            }
            ranSinceLoad = false;
        }

        private void OnRunCompleted(MachineInstance completed, TestResult result)
        {
            if (completed != machine) return;

            var lab = LabRuntime.Instance;
            var sample = lab?.SampleFor(machine.LoadedSample);
            var vial = lab?.PropFor(machine.LoadedSample);
            if (sample != null && vial != null) vial.SetFillFraction(sample.VolumeMl / 100f);

            if (display != null) display.Show(machine, result, sample);
            EmitPrintout(result, sample);
        }

        /// <summary>
        /// Drop a slip in the output tray. Only one fits: running again before collecting the last
        /// one loses it. The reading is still on the instrument's display, so nothing becomes
        /// unknowable — you just have to go and read it rather than carry it away.
        /// </summary>
        private void EmitPrintout(TestResult result, Chemistry.SampleState sample)
        {
            var lab = LabRuntime.Instance;
            if (lab == null || result == null) return;

            if (currentPrintout != null) Destroy(currentPrintout.gameObject);

            currentPrintout = lab.SpawnPrintout(
                machine.LoadedSample,
                result,
                machine.Def.DisplayName,
                sample != null ? sample.EquipmentTag : "BLANK",
                printoutSocket != null ? printoutSocket : transform);
        }

        private PrintoutProp currentPrintout;

        // -- Status light ---------------------------------------------------------------------------

        /// <summary>
        /// Deliberately avoids red/amber/green. Palette row 4 means verdict state and nothing else
        /// (§2.2) — if a machine glows amber for "busy", the player stops reading amber as "caution"
        /// on a result, which is the one thing that colour has to mean.
        /// </summary>
        private void UpdateStatusLight()
        {
            if (statusLight == null || machine == null) return;
            block ??= new MaterialPropertyBlock();

            Color colour;
            float emission;

            if (machine.IsRunning)
            {
                // Coolant family, pulsing while it works.
                float t = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
                colour = new Color(0.16f, 0.55f, 0.62f);
                emission = Mathf.Lerp(0.25f, 0.9f, t);
            }
            else if (!machine.IsEmpty && ranSinceLoad)
            {
                colour = new Color(0.86f, 0.84f, 0.76f); // neutral warm: result waiting
                emission = 0.7f;
            }
            else
            {
                colour = new Color(0.18f, 0.19f, 0.20f);
                emission = 0.05f;
            }

            statusLight.GetPropertyBlock(block);
            block.SetColor(BaseColor, colour);
            block.SetColor(EmissionColor, colour * emission);
            statusLight.SetPropertyBlock(block);
        }
    }
}
