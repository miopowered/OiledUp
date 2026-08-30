using Residue.Data;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    public enum MachineAction
    {
        /// <summary>
        /// Flush the instrument. Zeroes residue, spends a charge out of the bottle in your hands
        /// and takes a real chunk of time.
        /// </summary>
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
    /// <para>
    /// <b>All four work from a joined client.</b> Three of them read nothing but
    /// <see cref="LabView.Current"/>. The flush additionally needs the <see cref="SolventBottle"/> in
    /// the player's own hands (#14) — which is a local prop, but one whose charge count the host
    /// publishes and <see cref="BottleReconciler"/> writes on every process, so a client's prompt
    /// quotes the same number the host will spend. A host and a client run identical code here, and
    /// the host decides either way.
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

        private IMachineView Machine => station != null ? station.Machine : null;

        private static bool ShiftOver
        {
            get
            {
                var lab = LabView.Current;
                return lab != null && lab.ShiftOver;
            }
        }

        public override float HoldSeconds
        {
            get
            {
                if (action != MachineAction.Clean) return 0f;
                return Mathf.Max(MinimumFlushSeconds, cleanSeconds * TimeScale);
            }
        }

        /// <summary>
        /// <c>LabRuntime</c>'s testing time scale, read back off the instrument rather than off the
        /// lab.
        /// <para>
        /// A client has no <see cref="LabState"/> to ask, but it does have the instrument's actual run
        /// time — the host sends it precisely so nobody has to guess — and the scale is the ratio
        /// between that and the published figure both sides ship. Deriving it costs one divide and
        /// cannot disagree with the host, which reading a local serialized field would.
        /// </para>
        /// </summary>
        private float TimeScale
        {
            get
            {
                var machine = Machine;
                if (machine?.Def == null || machine.Def.RunTimeSeconds <= 0f) return 1f;
                return machine.RunSeconds / machine.Def.RunTimeSeconds;
            }
        }

        // -- Sound ------------------------------------------------------------------------------------

        /// <summary>
        /// Solvent running through the instrument while the flush is held (#46).
        /// <para>
        /// The flush is the longest hold in the game at shipping balance — §5.2 puts it at 20-40 s —
        /// and it made no sound at all, which is the same complaint #46 raises about the wash
        /// station's four seconds. It is the same clip as the tap for the same reason it is the same
        /// liquid. Only the flush gets one: the other three actions are taps, and a tap has nothing
        /// to keep saying.
        /// </para>
        /// Local to whoever is holding, like every other hold sound — hold state is not replicated,
        /// and the person who needs to hear it is the one pressing the key.
        /// </summary>
        private void Update()
        {
            bool flushing = action == MachineAction.Clean && watcher != null &&
                            (Interactable)watcher.Target == this && watcher.HoldProgress > 0f;

            if (!flushing)
            {
                if (pour != null && pour.isPlaying) pour.Stop();
                return;
            }

            if (pour == null)
            {
                pour = gameObject.AddComponent<AudioSource>();
                pour.playOnAwake = false;
                pour.loop = true;
                pour.clip = LabSoundBank.SolventPour;

                // Pitched down against the wash station's tap: this is solvent going through a
                // sample path rather than into an open bottle, and the two holds should not be
                // confusable when both are happening in the same room.
                pour.pitch = 0.82f;
                pour.spatialBlend = 1f;
                pour.rolloffMode = AudioRolloffMode.Linear;
                pour.minDistance = 1.5f;
                pour.maxDistance = 14f;
                pour.dopplerLevel = 0f;
                AudioBus.Register(pour, AudioCategory.Effects, 0.26f);
            }

            if (!pour.isPlaying && pour.clip != null) pour.Play();
        }

        private void OnDestroy() => AudioBus.Unregister(pour);

        private AudioSource pour;
        private PlayerInteractor watcher;

        public override string Prompt(PlayerInteractor player)
        {
            watcher = player;

            var machine = Machine;
            var lab = LabView.Current;
            if (machine == null || machine.Def == null) return null;

            if (action == MachineAction.Clean)
            {
                if (machine.IsRunning) return PromptStrings.ActionFlushWhileRunning.Text;

                // Named separately from "out of solvent". An empty bottle sends you to the wash
                // station; no bottle at all sends you there for a different reason, and a player who
                // walked over with the wrong one is owed the distinction (§9).
                var bottle = player.Carried as SolventBottle;
                if (bottle == null)
                {
                    return player.Carried != null
                        ? PromptStrings.ActionNeedsBottleNotThat.Format(
                            ("machine", machine.DisplayName))
                        : PromptStrings.ActionFetchBottle.Format(("machine", machine.DisplayName));
                }

                if (bottle.IsEmpty) return PromptStrings.ActionBottleEmpty.Text;

                // One charge and several are separate lines, not one line plus an "s".
                string flushSeconds = HoldSeconds.ToString("F0");
                return bottle.Charges == 1
                    ? PromptStrings.ActionHoldToFlushOne.Format(
                        ("machine", machine.DisplayName), ("seconds", flushSeconds))
                    : PromptStrings.ActionHoldToFlush.Format(
                        ("machine", machine.DisplayName), ("seconds", flushSeconds),
                        ("charges", bottle.Charges));
            }

            if (machine.IsRunning) return PromptStrings.ActionBusy.Text;

            if (action == MachineAction.Calibrate)
            {
                if (!machine.IsEmpty) return PromptStrings.ActionRemoveVialCalibrate.Text;
                if (lab == null) return null;
                if (!machine.HasFreshCheck(lab.Day)) return PromptStrings.ActionNeedsFreshCheck.Text;
                if (lab.Money < lab.CalibrationCost) return PromptStrings.ActionCannotAfford.Text;

                return PromptStrings.ActionRecalibrate.Format(
                    ("machine", machine.DisplayName),
                    ("seconds", machine.CalibrationSeconds.ToString("F0")),
                    ("cost", lab.CalibrationCost.ToString("N0")));
            }

            if (action == MachineAction.Reference)
            {
                if (!machine.IsEmpty) return PromptStrings.ActionRemoveVialStandard.Text;
                if (ShiftOver) return PromptStrings.ActionShiftOver.Text;
                if (lab != null && lab.ReferenceStandards < 1)
                    return PromptStrings.ActionNoStandards.Text;

                return PromptStrings.ActionRunStandard.Format(
                    ("seconds", machine.RunSeconds.ToString("F0")));
            }

            if (!machine.IsEmpty) return PromptStrings.ActionRemoveVialBlank.Text;
            if (ShiftOver) return PromptStrings.ActionShiftOver.Text;
            return PromptStrings.ActionRunBlank.Format(("seconds", machine.RunSeconds.ToString("F0")));
        }

        public override bool CanInteract(PlayerInteractor player)
        {
            var machine = Machine;
            var lab = LabView.Current;
            if (machine == null || machine.IsRunning || lab == null) return false;

            if (action == MachineAction.Clean)
            {
                // Flushing is housekeeping, not analysis — still allowed after the shift ends. What
                // gates it now is what is in your hands, not what is in the books: the drum's balance
                // decides whether you could have filled a bottle, and this decides whether you did.
                return player.Carried is SolventBottle bottle && !bottle.IsEmpty;
            }

            // Calibration is housekeeping too: it produces no reading, so the shift clock does not
            // gate it. Being locked out of correcting an instrument you have just proved is wrong
            // would be a punishment for checking.
            if (action == MachineAction.Calibrate)
                return machine.IsEmpty && machine.HasFreshCheck(lab.Day) && lab.Money >= lab.CalibrationCost;

            if (action == MachineAction.Reference)
                return machine.IsEmpty && !ShiftOver && lab.ReferenceStandards >= 1;

            return machine.IsEmpty && !ShiftOver;
        }

        /// <summary>
        /// Four requests. The affordability and occupancy checks above are the same ones the host
        /// will make — <see cref="LabState.TryStartReferenceRun"/> and friends are the gateways at
        /// both ends — but they are made here only so the button can grey itself out and say why
        /// without asking anybody. Whatever this side concluded, the host decides.
        /// </summary>
        public override void Interact(PlayerInteractor player)
        {
            if (station == null) return;

            string id = station.InstanceId;
            string title = Title;

            switch (action)
            {
                case MachineAction.Clean:
                    LabCommands.Attempt(player, LabCommand.FlushMachine(id),
                        _ => player.Say(PromptStrings.ActionFlushed.Format(("machine", title))));
                    return;

                case MachineAction.Reference:
                    LabCommands.Attempt(player, LabCommand.RunReference(id),
                        _ => player.Say(
                            PromptStrings.ActionStandardRunning.Format(("machine", title))));
                    return;

                case MachineAction.Calibrate:
                    LabCommands.Attempt(player, LabCommand.Calibrate(id),
                        _ => player.Say(PromptStrings.ActionRecalibrating.Format(("machine", title))));
                    return;

                default:
                    LabCommands.Attempt(player, LabCommand.RunBlank(id),
                        _ => player.Say(PromptStrings.ActionBlankRunning.Format(("machine", title))));
                    return;
            }
        }

        /// <summary>The instrument's name, or a neutral stand-in where this process has no view of it.</summary>
        private string Title
        {
            get
            {
                var machine = Machine;
                return machine != null ? machine.DisplayName : "Instrument";
            }
        }
    }
}
