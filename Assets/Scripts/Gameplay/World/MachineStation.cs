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

        // Announced before anything else so the host can tell whether a player asking to run this
        // instrument is standing at it. Independent of whether this process has a lab: on a client
        // the station has no MachineInstance, and it still has a position.
        private void OnEnable() => LabRuntime.RegisterFixture(machineInstanceId, transform);

        private void OnDisable() => LabRuntime.ForgetFixture(machineInstanceId, transform);

        private void Start()
        {
            var lab = LabRuntime.Instance;

            // LabRuntime disables itself and logs when content is missing, and its Lab is then null.
            // Dereferencing it anyway gives one NullReferenceException per station, which buries the
            // single error that actually says what went wrong.
            if (lab == null || lab.Lab == null)
            {
                enabled = false;
                return;
            }

            machine = lab.Lab.FindMachine(machineInstanceId);

            if (machine == null)
            {
                Debug.LogError(
                    $"[MachineStation] No installed machine with instance id '{machineInstanceId}'.", this);
                return;
            }

            name = $"Machine_{machine.Def.Id}";
            lab.Lab.RunCompleted += OnRunCompleted;
            lab.Lab.Calibrated += OnCalibrated;
        }

        private void OnDestroy()
        {
            var lab = LabRuntime.Instance;
            if (lab?.Lab == null) return;

            lab.Lab.RunCompleted -= OnRunCompleted;
            lab.Lab.Calibrated -= OnCalibrated;
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

                // Named separately rather than left to fall out as "not settled". An unlogged vial
                // cannot be agitated either (§5.1), so without this the player is sent to shake a
                // bottle that will refuse for a completely different reason.
                if (sample != null && !sample.IsLogged)
                    return $"{title}: {sample.Id} is not booked in — register it at the terminal";

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
                if (sample == null || !sample.IsLogged) return false;
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

        /// <summary>
        /// Three requests, one shape. Nothing below writes lab state — the vial only leaves the hand,
        /// and the tray light only changes, once the host has said yes. The prompt above already
        /// decided the same thing locally so the player is not left guessing, but that decision is
        /// advisory: <see cref="LabCommandExecutor"/> re-runs <see cref="MachineInstance.CanAccept"/>
        /// on arrival, which is what makes a client asking to load a vial it is not carrying — or to
        /// operate this instrument from across the room — a refusal rather than a result.
        /// </summary>
        private void Load(PlayerInteractor player)
        {
            LabCommands.Attempt(player, LabCommand.LoadMachine(machineInstanceId), _ =>
            {
                var vial = player.ReleaseCarried();
                if (vial != null) vial.AttachTo(vialSocket != null ? vialSocket : transform, interactable: false);
                ranSinceLoad = false;
            });
        }

        private void StartRun(PlayerInteractor player)
        {
            LabCommands.Attempt(player, LabCommand.StartRun(machineInstanceId), _ =>
            {
                ranSinceLoad = true;
                player.Say($"{Title}: running. {(machine != null ? machine.RunSeconds : 0f):F0}s.");
            });
        }

        private void TakeBack(PlayerInteractor player)
        {
            LabCommands.Attempt(player, LabCommand.TakeFromMachine(machineInstanceId), result =>
            {
                ranSinceLoad = false;

                var lab = LabRuntime.Instance;
                var vial = lab != null ? lab.PropFor(result.Sample) : null;
                if (vial == null) return;

                player.TryCarry(vial);

                var sample = lab.SampleFor(result.Sample);
                if (sample != null) vial.SetFillFraction(sample.VolumeMl / 100f);
            });
        }

        /// <summary>The instrument's name, or a neutral stand-in on a process that has no lab yet.</summary>
        private string Title => machine != null && machine.Def != null
            ? machine.Def.DisplayName
            : "Instrument";

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
        /// Say at the machine what the recalibration cost in confidence (§5.3). The full list of
        /// records it put in doubt lives at the terminal, but the player is standing here when it
        /// happens, and a correction that produced no visible sign would look like nothing occurred.
        /// </summary>
        private void OnCalibrated(MachineInstance calibrated, CalibrationOutcome outcome)
        {
            if (calibrated != machine || display == null) return;

            display.ShowNotice(
                machine,
                $"CAL {(outcome.CorrectedDrift >= 0f ? "+" : "-")}{Mathf.Abs(outcome.CorrectedDrift) * 100f:F1}%",
                outcome.CastsDoubt ? $"{outcome.AffectedArchived} FILED SUSPECT" : "NOTHING IN DOUBT");
        }

        /// <summary>
        /// Drop a slip in the output tray. Only one fits: running again before collecting the last
        /// one loses it. The reading is still on the instrument's display, so nothing becomes
        /// unknowable — you just have to go and read it rather than carry it away.
        /// </summary>
        private void EmitPrintout(TestResult result, Chemistry.SampleState sample)
        {
            var lab = LabRuntime.Instance;
            if (lab == null || lab.Lab == null || result == null) return;

            var tray = printoutSocket != null ? printoutSocket : transform;

            // The tray holds one slip, and running again before collecting the last one loses it —
            // the reading is still on the display, so nothing becomes unknowable. Only the slip still
            // sitting in the tray, though: this used to destroy whatever prop the field pointed at,
            // which after somebody picked it up meant tearing the paper out of their hands.
            //
            // The ticket goes with the paper. Retiring the prop without retiring the ticket would
            // leave the old numbers filable by a stale request long after the slip was gone.
            if (currentPrintout != null && currentPrintout.transform.parent == tray)
            {
                lab.Lab.Slips.Discard(currentPrintout.Ticket);
                Destroy(currentPrintout.gameObject);
            }

            currentPrintout = lab.SpawnPrintout(
                machine.LoadedSample,
                result,
                machine.InstanceId,
                machine.Def.DisplayName,
                sample != null ? sample.EquipmentTag : result.IsReference ? "CERT STANDARD" : "BLANK",
                tray);
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
