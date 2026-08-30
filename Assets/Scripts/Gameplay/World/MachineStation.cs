using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The physical instrument. Contextual single-key interaction: load, run, take back.
    /// <para>
    /// Note what this deliberately cannot do — compute a result. Measurement needs ground truth and
    /// therefore happens inside <see cref="SampleRegistry"/> on the host. This component runs on
    /// every client and must stay incapable of it (§3.1).
    /// </para>
    /// <para>
    /// <b>It reads an <see cref="IMachineView"/>, not a <see cref="MachineInstance"/>.</b> A client
    /// has no <c>LabState</c> and never will (<see cref="LabRuntime.SimulatesLocally"/>), so a station
    /// that held the host's live object found nothing on a joined client and switched itself off —
    /// no prompt, no status light, no readout, nothing to press. Going through
    /// <see cref="LabView.Current"/> means the same code draws a host's own instrument and a
    /// replicated snapshot of one, and neither branch exists at this level.
    /// </para>
    /// </summary>
    public sealed class MachineStation : Interactable, IVialSlots
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
        private bool named;
        private bool wasRunning;

        /// <summary>
        /// This instrument as whatever this process can see of it, or null before either view is
        /// installed. Resolved per access rather than cached in <c>Start</c>: on a client the lab
        /// scene is up before <c>LabNetwork</c> spawns, so a station that latched null at startup
        /// would stay dead for the whole session.
        /// </summary>
        public IMachineView Machine => LabView.Current?.Machine(machineInstanceId);

        public string InstanceId => machineInstanceId;
        public Transform VialSocket => vialSocket;

        // Announced before anything else so the host can tell whether a player asking to run this
        // instrument is standing at it. Independent of whether this process has a lab: on a client
        // the station has no MachineInstance, and it still has a position.
        //
        // Registered as a container too, because MachineInstance.TryLoad records the vial at
        // InMachine(instanceId, 0) and a client has to be able to turn that back into a socket.
        //
        // And as a tray, separately, because a slip printed here is recorded at InMachine(instanceId)
        // as well — the same location naming two different sockets on one instrument. See
        // LabRuntime.RegisterTray for why they cannot share a table.
        private void OnEnable()
        {
            LabRuntime.RegisterFixture(machineInstanceId, transform, this);
            LabRuntime.RegisterTray(machineInstanceId, printoutSocket != null ? printoutSocket : transform);
        }

        private void OnDisable() => LabRuntime.ForgetFixture(machineInstanceId, transform);

        // -- IVialSlots -------------------------------------------------------------------------------
        //
        // An instrument takes exactly one vial, so the slot index is along for the ride and every
        // index resolves to the same socket.

        public Transform Slot(int index) => vialSocket != null ? vialSocket : transform;

        public int FreeSlot() => 0;

        public int SlotOf(Transform prop) =>
            prop != null && prop.parent == (vialSocket != null ? vialSocket : transform) ? 0 : -1;

        private void Start()
        {
            // Results and calibration outcomes are events on the host's own lab. A client gets
            // neither — there is nothing on the wire that carries a TestResult — so this subscribes
            // only where there is something to subscribe to, and the rest of the component works
            // regardless.
            var lab = HostLab;
            if (lab == null) return;

            if (lab.FindMachine(machineInstanceId) == null)
            {
                Debug.LogError(
                    $"[MachineStation] No installed machine with instance id '{machineInstanceId}'.", this);
                return;
            }

            lab.RunCompleted += OnRunCompleted;
            lab.Calibrated += OnCalibrated;
        }

        private void OnDestroy()
        {
            // Before the early return: the bus keeps a list slot per source and a station torn down
            // on a client has no lab to unsubscribe from but has the same two sources to give back.
            AudioBus.Unregister(loopSource);
            AudioBus.Unregister(shotSource);

            var lab = HostLab;
            if (lab == null) return;

            lab.RunCompleted -= OnRunCompleted;
            lab.Calibrated -= OnCalibrated;
        }

        /// <summary>
        /// This process's own lab, or null on a client. The only thing left in this component that
        /// cares which side it is on, and it cares because host-only events have no replicated twin.
        /// </summary>
        private static LabState HostLab
        {
            get
            {
                var runtime = LabRuntime.Instance;
                return runtime != null ? runtime.Lab : null;
            }
        }

        private float nextDisplayRefresh;

        private void Update()
        {
            var machine = Machine;

            UpdateStatusLight(machine);
            NameOnce(machine);
            UpdateDisplay(machine);
            UpdateSound(machine);
        }

        /// <summary>
        /// Take the instrument's name once it is knowable. On a client the definition resolves only
        /// after the first publish, which is several frames after this object exists.
        /// </summary>
        private void NameOnce(IMachineView machine)
        {
            if (named || machine?.Def == null) return;
            named = true;
            name = $"Machine_{machine.Def.Id}";

            // Built here rather than on the frame the run ends. Synthesising a chime is a couple of
            // hundred thousand samples of sin and exp, which is nothing once and a visible hitch if
            // it lands on the frame the player is being told something. Same for the two sources.
            LabSoundBank.RunFinished(InstrumentSoundId);
            EnsureSources();
        }

        private void UpdateDisplay(IMachineView machine)
        {
            if (display == null || machine == null) return;

            if (machine.IsRunning)
            {
                wasRunning = true;

                // Redrawing the screen rasterises every pixel, so throttle it. A progress readout does
                // not need 60 Hz.
                if (Time.time < nextDisplayRefresh) return;
                nextDisplayRefresh = Time.time + 0.2f;
                display.ShowRunning(machine);
                return;
            }

            if (!wasRunning) return;
            wasRunning = false;

            // A run just finished. On the host, OnRunCompleted has already drawn the numbers — it
            // fires from LabState.Tick, and LabRuntime's execution order puts that before this — so
            // clearing the screen here would wipe the result before anyone read it. A client has no
            // result to draw and would otherwise be left showing a frozen progress bar for ever.
            if (HostLab == null) display.ShowIdle(machine);
        }

        // -- Sound ------------------------------------------------------------------------------------

        /// <summary>
        /// What this instrument sounds like, which is a question about the box and not about the
        /// sample in it. The definition id rather than the instance id, so two centrifuges on one
        /// bench agree — and falling back to the instance id only because a client has no definition
        /// until the first publish arrives.
        /// </summary>
        private string InstrumentSoundId
        {
            get
            {
                var def = Machine?.Def;
                return def != null ? def.Id : machineInstanceId;
            }
        }

        /// <summary>
        /// How loud the instrument is while it works, before the effects slider. Constants rather
        /// than serialized fields: five stations in the scene would be five places to disagree, and
        /// CLAUDE.md keeps tuned numbers in source where a diff can see them.
        /// </summary>
        private const float RunningLoopVolume = 0.2f;

        /// <summary>Louder than the machine loop — it is happening in the player's own hands.</summary>
        private const float AgitateVolume = 0.35f;

        private AudioSource loopSource;
        private AudioSource shotSource;
        private AudioClip loopingClip;
        private float loopVolume;

        /// <summary>
        /// Has <i>this process</i> ever seen this instrument busy?
        /// <para>
        /// The guard against a late joiner hearing the whole lab finish at once. A client that
        /// connects mid-day receives its first snapshot with three instruments idle and results
        /// waiting in two of them; a chime driven by "not running now" would fire three times on that
        /// frame for runs that ended before the player arrived. This only ever becomes true from an
        /// observation, so the first frame a machine is visible can never produce an edge — there is
        /// nothing to compare against yet.
        /// </para>
        /// </summary>
        private bool observedRunning;

        /// <summary>
        /// The running loop and the run-finished chime, both on state every process can read.
        ///
        /// <para>
        /// <b>Why the edge and not the event.</b> <see cref="OnRunCompleted"/> is the obvious place
        /// and is wrong twice over. It is an event on the host's own <c>LabState</c>, so a joined
        /// client would hear nothing at all and co-op would be back to patrolling; and it is handed a
        /// <c>TestResult</c>, which is precisely the thing the chime must never be able to consult.
        /// <see cref="IMachineView.IsRunning"/> replicates, it says only that the box is busy, and
        /// reading the edge off it makes a host and a client run the same line.
        /// </para>
        ///
        /// <para>
        /// <b>What the chime is allowed to know.</b> Which instrument, and that a run ended. Not
        /// whether the reading was normal, caution or critical; not whether it was a sample, a blank,
        /// a certified standard or a recalibration; not whether the instrument has drifted. A chime
        /// that soured on a bad number would be a verdict arriving through the speakers on every
        /// client, ahead of the measurement and outside every check that guards the wire.
        /// </para>
        /// </summary>
        private void UpdateSound(IMachineView machine)
        {
            if (machine == null) return;

            if (machine.IsRunning)
            {
                observedRunning = true;
                PlayLoop(LabSoundBank.MachineLoop, LabSoundBank.RunningPitch(InstrumentSoundId),
                         RunningLoopVolume);
                return;
            }

            // The 2.5 s load hold is where §4.5's agitation is paid, and it used to be paid in
            // silence — which reads as an unresponsive key rather than as work.
            if (Shaking)
            {
                PlayLoop(LabSoundBank.Agitate, 1f, AgitateVolume);
                return;
            }

            StopLoop();

            if (!observedRunning) return;
            observedRunning = false;

            EnsureSources();
            AudioBus.PlayOneShot(shotSource, LabSoundBank.RunFinished(InstrumentSoundId),
                                 AudioCategory.Effects, 0.85f);
        }

        /// <summary>
        /// Is somebody holding Interact at this instrument right now?
        /// <para>
        /// Read off the player who is looking at it rather than pushed in by one, because
        /// <see cref="Prompt"/> already hands this component that player every frame it is targeted
        /// and <see cref="PlayerInteractor.HoldProgress"/> is already public for the HUD ring. Local
        /// to whoever is holding: hold state is not replicated and does not need to be, since the
        /// point of the sound is to tell the person pressing the key that something is happening.
        /// </para>
        /// </summary>
        private bool Shaking =>
            watcher != null && (Interactable)watcher.Target == this && watcher.HoldProgress > 0f;

        private PlayerInteractor watcher;

        /// <summary>
        /// Two sources: one for loops, one for one-shots. <see cref="AudioSource.Stop"/> ends
        /// one-shots as well as the loop, and the loop stops on exactly the frame the chime or the
        /// clunk starts — on one source the instrument would silence its own answer.
        /// <para>
        /// Both positional at full blend, which is #46's actual ask: a machine finishing across the
        /// room has to be a direction rather than an event. Built here rather than in the scene
        /// because a station is authored by <c>LabSceneBuilder</c> and a source wired by hand would
        /// exist on whichever fixtures happened to be authored after this change.
        /// </para>
        /// </summary>
        private void EnsureSources()
        {
            if (shotSource == null)
            {
                shotSource = Configure(gameObject.AddComponent<AudioSource>());

                // Authored at 1 because AudioBus.PlayOneShot carries the per-sound gain itself.
                AudioBus.Register(shotSource, AudioCategory.Effects, 1f);
            }

            if (loopSource != null) return;

            loopSource = Configure(gameObject.AddComponent<AudioSource>());
            loopSource.loop = true;

            // Registered at creation rather than at first play: a source the bus has never heard of
            // is a source the effects slider does not reach, and this one can exist for a whole day
            // before an instrument is first used.
            loopVolume = RunningLoopVolume;
            AudioBus.Register(loopSource, AudioCategory.Effects, RunningLoopVolume);
        }

        private void PlayLoop(AudioClip clip, float pitch, float volume)
        {
            if (clip == null) return;
            EnsureSources();

            // Re-registered only when the figure changes. The bus is a linear scan and this runs
            // every frame an instrument is working.
            if (!Mathf.Approximately(loopVolume, volume))
            {
                loopVolume = volume;

                // Through the bus rather than onto the source, so the effects slider reaches a loop
                // that started while it was already down.
                AudioBus.Register(loopSource, AudioCategory.Effects, volume);
            }

            loopSource.pitch = pitch;

            if (loopingClip == clip && loopSource.isPlaying) return;

            loopingClip = clip;
            loopSource.clip = clip;
            loopSource.Play();
        }

        private void StopLoop()
        {
            if (loopingClip == null) return;

            loopingClip = null;
            if (loopSource != null) loopSource.Stop();
        }

        private static AudioSource Configure(AudioSource source)
        {
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 26f;
            source.dopplerLevel = 0f;
            return source;
        }

        // -- Interaction ----------------------------------------------------------------------------

        [Tooltip("Seconds of holding Interact at the instrument before the vial goes in. This is " +
                 "where agitation happens, so it carries §4.5's time cost.")]
        [SerializeField] private float loadHoldSeconds = 2.5f;

        /// <summary>
        /// Loading is a hold; everything else here is a tap.
        /// <para>
        /// <b>Why loading costs seconds.</b> A sample that has stood in a crate has its heavy
        /// particulates on the bottom, and running it unshaken reads low on exactly the wear metals
        /// the player is looking for — so §4.5 requires it to be agitated first, and §9 requires that
        /// preparation to be a hand-operated task with a real cost rather than a menu click. That used
        /// to be a separate hold on a separate button, which meant the cost was paid somewhere the
        /// player was not looking and the instrument's refusal was the first they heard of it. Folding
        /// it into the load keeps the seconds and puts them where the action is.
        /// </para>
        /// Taking a finished vial back and starting a run stay taps: neither prepares anything, and a
        /// hold on them would be delay for its own sake.
        /// </summary>
        public override float HoldSeconds
        {
            get
            {
                var machine = Machine;
                bool loading = machine != null && !machine.IsRunning && machine.IsEmpty &&
                               !ShiftOver;

                return loading ? Mathf.Max(0f, loadHoldSeconds) : 0f;
            }
        }

        public override string Prompt(PlayerInteractor player)
        {
            // Asked every frame this instrument is targeted, which is the only regular signal it gets
            // about who is standing at it. See Shaking.
            watcher = player;

            var machine = Machine;
            if (machine == null) return null;
            string title = machine.DisplayName;

            if (machine.IsRunning)
                return $"{title} — running, {machine.SecondsRemaining:F0}s left";

            if (player.CarriedVial != null && machine.IsEmpty)
            {
                var sample = LabRuntime.Instance?.SampleFor(player.CarriedVial.SampleId);

                return machine.CanAccept(sample) switch
                {
                    LoadRefusal.Accepted => $"Hold to load into {title}",
                    LoadRefusal.NotEnoughVolume =>
                        $"{title} needs {machine.Def.SampleVolumeMl:F0} ml — {sample?.VolumeMl:F1} ml left",

                    // Not a refusal any more: the hold is where the shaking happens, so a settled
                    // sample is loadable and the prompt says what the seconds are buying.
                    LoadRefusal.NotSettled => $"Hold to shake and load into {title}",
                    LoadRefusal.NeedsPreheat => $"{title}: sample is cold, needs preheating",
                    _ => $"{title} is occupied"
                };
            }

            if (!machine.IsEmpty && player.Carried == null)
            {
                if (machine.HasResultWaiting) return $"Take vial from {title}";

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
        private static bool ShiftOver
        {
            get
            {
                var lab = LabView.Current;
                return lab != null && lab.ShiftOver;
            }
        }

        public override bool CanInteract(PlayerInteractor player)
        {
            var machine = Machine;
            if (machine == null || machine.IsRunning) return false;

            if (player.Carried != null)
            {
                if (player.CarriedVial == null) return false; // holding a slip or a manual
                if (ShiftOver || !machine.IsEmpty) return false;

                var sample = LabRuntime.Instance?.SampleFor(player.CarriedVial.SampleId);

                // A client is holding a bottle and none of the paperwork behind it: volume and
                // settling both live on a SampleState it does not have (§3.2). Offer the load and let
                // the host refuse in a sentence the player can read — the house pattern in
                // LabCommands, and the alternative is a second copy of §4.5 living out here to drift
                // from the enforced one.
                if (sample == null) return HostLab == null && Loadable(machine.CanAccept(null));

                return Loadable(machine.CanAccept(sample));
            }

            if (machine.IsEmpty) return false;
            if (machine.HasResultWaiting) return true;
            return !ShiftOver;
        }

        /// <summary>
        /// A settled sample is no longer a reason to refuse the load — the hold shakes it on the way
        /// in. Everything else <see cref="MachineInstance.CanAccept"/> says no to still means no:
        /// there is no amount of holding that produces the millilitres the run needs.
        /// </summary>
        private static bool Loadable(LoadRefusal refusal) =>
            refusal == LoadRefusal.Accepted || refusal == LoadRefusal.NotSettled;

        public override void Interact(PlayerInteractor player)
        {
            var machine = Machine;
            if (machine == null || machine.IsRunning) return;

            if (player.CarriedVial != null) { Load(player); return; }
            if (player.Carried != null) return;
            if (machine.IsEmpty) return;

            if (machine.HasResultWaiting) TakeBack(player);
            else StartRun(player, machine);
        }

        /// <summary>
        /// Three requests, one shape. Nothing below writes lab state — the vial only leaves the hand,
        /// and the tray light only changes, once the host has said yes. The prompt above already
        /// decided the same thing locally so the player is not left guessing, but that decision is
        /// advisory: <see cref="LabCommandExecutor"/> re-runs <see cref="MachineInstance.CanAccept"/>
        /// on arrival, which is what makes a client asking to load a vial it is not carrying — or to
        /// operate this instrument from across the room — a refusal rather than a result.
        /// </summary>
        /// <summary>
        /// Shake, then load. Two requests rather than one because they are two rules, and the host
        /// already validates each of them on its own terms — inventing a combined command would put a
        /// third copy of §4.5 somewhere it could drift from the enforced one.
        /// <para>
        /// The agitate is sent unconditionally rather than only when the sample looks settled.
        /// Prepped repeats in the lifecycle table, so shaking an already-homogeneous sample is a
        /// legal no-op; and a client cannot see <c>IsSettled</c> at all (§3.2), so the alternative is
        /// asking the host a question it would only have to answer again. Loading is chained inside
        /// the acceptance callback, so a refused shake never puts the vial in the instrument.
        /// </para>
        /// </summary>
        private void Load(PlayerInteractor player)
        {
            LabCommands.Attempt(player, LabCommand.Agitate(), _ =>
                LabCommands.Attempt(player, LabCommand.LoadMachine(machineInstanceId), _ =>
                {
                    // Quiet, because the instrument answers with its own clunk a line below. The
                    // generic "set down" belongs to a rack or a bench; a vial being seated in an
                    // instrument is the moment the hold paid off and should not sound like a shelf.
                    var vial = player.ReleaseCarried(quiet: true);
                    if (vial != null)
                        vial.AttachTo(vialSocket != null ? vialSocket : transform, interactable: false);

                    EnsureSources();
                    AudioBus.PlayOneShot(shotSource, LabSoundBank.Load, AudioCategory.Effects, 0.7f);
                }));
        }

        private void StartRun(PlayerInteractor player, IMachineView machine)
        {
            LabCommands.Attempt(player, LabCommand.StartRun(machineInstanceId),
                _ => player.Say($"{machine.DisplayName}: running. {machine.RunSeconds:F0}s."));
        }

        private void TakeBack(PlayerInteractor player)
        {
            LabCommands.Attempt(player, LabCommand.TakeFromMachine(machineInstanceId), result =>
            {
                var lab = LabRuntime.Instance;
                var vial = lab != null ? lab.PropFor(result.Sample) : null;
                if (vial == null) return;

                player.TryCarry(vial);

                // Host-side. On a client SampleFor is null and the fill arrives with the next publish
                // instead — VialReconciler refreshes it even for the bottle in your own hands, which
                // is the one place it touches a locally held prop and the reason it does.
                var sample = lab.SampleFor(result.Sample);
                if (sample != null) vial.SetFillFraction(sample.VolumeMl / VialProp.FullMl);
            });
        }

        private void OnRunCompleted(MachineInstance completed, TestResult result)
        {
            if (completed == null || completed.InstanceId != machineInstanceId) return;

            var lab = LabRuntime.Instance;
            var sample = lab?.SampleFor(completed.LoadedSample);
            var vial = lab?.PropFor(completed.LoadedSample);
            if (sample != null && vial != null) vial.SetFillFraction(sample.VolumeMl / VialProp.FullMl);

            // The id, not the SampleState in hand: MachineDisplay has one Show and derives the
            // caption itself, so the host cannot caption a run differently from a client watching
            // the same instrument (#56).
            if (display != null) display.Show(Machine, result, completed.LoadedSample);
            EmitPrintout(completed, result, sample);
        }

        /// <summary>
        /// Say at the machine what the recalibration cost in confidence (§5.3). The full list of
        /// records it put in doubt lives at the terminal, but the player is standing here when it
        /// happens, and a correction that produced no visible sign would look like nothing occurred.
        /// </summary>
        private void OnCalibrated(MachineInstance calibrated, CalibrationOutcome outcome)
        {
            if (calibrated == null || calibrated.InstanceId != machineInstanceId || display == null) return;

            display.ShowNotice(
                Machine,
                $"CAL {(outcome.CorrectedDrift >= 0f ? "+" : "-")}{Mathf.Abs(outcome.CorrectedDrift) * 100f:F1}%",
                outcome.CastsDoubt ? $"{outcome.AffectedArchived} FILED SUSPECT" : "NOTHING IN DOUBT");
        }

        /// <summary>
        /// Drop a slip in the output tray. Only one fits: running again before collecting the last
        /// one loses it. The reading is still on the instrument's display, so nothing becomes
        /// unknowable — you just have to go and read it rather than carry it away.
        /// <para>
        /// Host-only by construction. It is called from <see cref="OnRunCompleted"/>, which is an
        /// event on a lab a client does not have.
        /// </para>
        /// </summary>
        private void EmitPrintout(MachineInstance completed, TestResult result, SampleState sample)
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
            //
            // A slip this station never printed still counts. Continuing a saved run restores the
            // paper that was in the tray when the player quit (LabRuntime.RestorePrintout), and this
            // field knows nothing about it — so running the instrument again stacked two slips in one
            // socket, the restored one visible underneath for the rest of the session. Adopting
            // whatever is actually in the tray means "the tray holds one slip" stays true across a
            // save, which is where it would otherwise quietly stop being true.
            if (currentPrintout == null || currentPrintout.transform.parent != tray)
                currentPrintout = AdoptSlipInTray(lab, tray);

            if (currentPrintout != null && currentPrintout.transform.parent == tray)
            {
                // Through the runtime rather than Destroy, so the ticket, the prop table and the
                // object all stop existing together. A raw Destroy left the host's slip table holding
                // a reference to a torn-down prop, which is exactly the stale entry the client's
                // reconciler would trip over if the two sides ever shared this path.
                lab.Lab.Slips.Discard(currentPrintout.Ticket);
                lab.RetireSlip(currentPrintout.Ticket);
            }

            currentPrintout = lab.SpawnPrintout(
                completed.LoadedSample,
                result,
                completed.InstanceId,
                completed.Def != null ? completed.Def.DisplayName : "Instrument",
                // The name the record is filed under, so the player can match paper to a row on the
                // terminal — through the same seam the instrument's own screen uses, so the paper and
                // the display cannot caption one run two ways. That divergence is exactly what #56
                // was, and this line was a second instance of it: the screen said SOLVENT BLANK while
                // the paper it printed said BLANK.
                RunCaption.For(result, sample != null ? sample.RecordTag : null),
                tray);
        }

        private PrintoutProp currentPrintout;

        /// <summary>
        /// The slip physically sitting in this instrument's tray, whoever put it there.
        /// <para>
        /// Asked by ticket through the runtime rather than by walking the socket's children, because
        /// the ticket is what has to be retired alongside the prop — a slip discarded without its
        /// ticket leaves numbers that a stale request can still file long after the paper is gone.
        /// </para>
        /// </summary>
        private PrintoutProp AdoptSlipInTray(LabRuntime lab, Transform tray)
        {
            if (lab?.Lab?.Slips == null || tray == null) return null;

            // Reused rather than allocated per run: this is on the path a finished run takes, and
            // the list is only alive for the length of this method.
            outstandingSlips.Clear();
            lab.Lab.Slips.CollectInto(outstandingSlips);

            for (int i = 0; i < outstandingSlips.Count; i++)
            {
                var prop = lab.SlipPropFor(outstandingSlips[i].Ticket);
                if (prop != null && prop.transform.parent == tray) return prop;
            }

            return null;
        }

        private readonly List<ResultSlips.Slip> outstandingSlips = new();

        // -- Status light ---------------------------------------------------------------------------

        /// <summary>Coolant family. Pulses while the instrument works — see <see cref="UpdateStatusLight"/>.</summary>
        public static readonly Color RunningLight = new(0.16f, 0.55f, 0.62f);

        /// <summary>Neutral warm, steady and bright: there is a vial in here with numbers against it.</summary>
        public static readonly Color ResultLight = new(0.86f, 0.84f, 0.76f);

        /// <summary>Barely lit. Nothing is happening and nothing is waiting.</summary>
        public static readonly Color IdleLight = new(0.18f, 0.19f, 0.20f);

        /// <summary>
        /// Deliberately avoids red/amber/green. Palette row 4 means verdict state and nothing else
        /// (§2.2) — if a machine glows amber for "busy", the player stops reading amber as "caution"
        /// on a result, which is the one thing that colour has to mean. It follows that this light
        /// never carries a verdict either: it says what the <i>instrument</i> is doing.
        /// <para>
        /// <b>Readable with hue removed (#41).</b> Three channels, none of them hue. The three
        /// colours are separated in luminance — <c>SignalEncodingTests</c> computes that rather than
        /// trusting it — the emission multiplier widens the gap further (0.05 idle against 0.7
        /// waiting), and running is the only one that <i>moves</i>. A player who sees no colour at
        /// all still gets dark / bright-and-still / pulsing, and the screen beside it spells the same
        /// state out in words: READY, RUNNING with the clock, or the reading itself.
        /// </para>
        /// </summary>
        private void UpdateStatusLight(IMachineView machine)
        {
            if (statusLight == null || machine == null) return;
            block ??= new MaterialPropertyBlock();

            Color colour;
            float emission;

            if (machine.IsRunning)
            {
                float t = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
                colour = RunningLight;
                emission = Mathf.Lerp(0.25f, 0.9f, t);
            }
            else if (machine.HasResultWaiting)
            {
                colour = ResultLight;
                emission = 0.7f;
            }
            else
            {
                colour = IdleLight;
                emission = 0.05f;
            }

            statusLight.GetPropertyBlock(block);
            block.SetColor(BaseColor, colour);
            block.SetColor(EmissionColor, colour * emission);
            statusLight.SetPropertyBlock(block);
        }
    }
}
