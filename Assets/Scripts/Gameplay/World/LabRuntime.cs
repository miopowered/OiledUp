using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The scene's bridge to the simulation. Owns the <see cref="LabState"/>, ticks it, and keeps
    /// the map from sample ids to the physical props representing them.
    /// <para>
    /// Everything game-logical lives in <see cref="LabState"/>, which is a plain C# object; this
    /// MonoBehaviour exists only to give it a Unity lifecycle and a place to hang scene references.
    /// At M4 this becomes the host-only component and the simulation moves behind RPCs unchanged.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class LabRuntime : MonoBehaviour, ILabStations
    {
        public static LabRuntime Instance { get; private set; }

        [Header("Content")]
        [SerializeField] private ContentCatalog catalog;

        [Tooltip("Fixed seed reproduces a whole contract exactly. Set to 0 to vary per session.")]
        [SerializeField] private int seed = 20260823;

        [Header("Installed instruments")]
        [Tooltip("MachineDef ids, in the order the stations appear in the scene.")]
        [SerializeField]
        private string[] installedMachineIds =
            { "cooling_curve", "karl_fischer", "viscometer", "centrifuge", "elemental" };

        [Header("Testing")]
        [Tooltip("Multiplier on every instrument's run time and on the flush hold.\n\n" +
                 "1 = the real balance. Lower values make the loop testable without editing " +
                 "ContentTables, which would ship. The RATIOS between instruments are design " +
                 "(§10: a cooling curve costs 5x a centrifuge run), so scaling preserves them.\n\n" +
                 "Set back to 1 before judging whether the game is fun.")]
        [SerializeField, Range(0.01f, 1f)] private float machineTimeScale = 0.05f;

        [Header("Props")]
        [SerializeField] private VialProp vialPrefab;
        [SerializeField] private PrintoutProp printoutPrefab;

        [Tooltip("The carryable solvent bottle. One instance is built per bottle in SolventStore, " +
                 "into the wash station's cradles.")]
        [SerializeField] private SolventBottle bottlePrefab;

        [Tooltip("The delivery carton. One instance is built per carton the truck sets down in the " +
                 "bay (#30). Needs a CartonLid on its lid child.")]
        [SerializeField] private CartonProp cartonPrefab;

        [Tooltip("The delivery note. One instance is built per carton, in its note socket, the " +
                 "moment the box is opened (#31).")]
        [SerializeField] private DeliveryNoteProp notePrefab;

        public LabState Lab { get; private set; }

        /// <summary>
        /// Whether this process simulates the lab. True in single player and on the host; the
        /// netcode layer sets it false on a connected client before the lab scene loads.
        /// <para>
        /// This is the load-bearing half of hard rule 2 at M4. <see cref="LabState"/> owns a
        /// <see cref="SampleGenerator"/>, and generating a sample produces its
        /// <c>SampleGroundTruth</c> alongside it. A client that ran this constructor would hold a
        /// full truth-bearing simulation in its own process — the answers to a different lab than
        /// the host's, but a working engine for computing them all the same. §3.1 is explicit that
        /// only the host simulates, and the cheapest way to guarantee it is to never build the
        /// thing on a client rather than to remember not to read from it.
        /// </para>
        /// A static rather than a serialized field because it is a fact about the process, decided
        /// before any scene object exists. <c>Residue.Gameplay</c> cannot ask NGO directly — the
        /// dependency runs the other way, and that direction is what keeps ground truth off the
        /// wire (see the assembly diagram in CLAUDE.md).
        /// </summary>
        public static bool SimulatesLocally = true;

        /// <summary>
        /// True when this process has no lab of its own and is reading replicated views instead.
        /// World components use it to tell "not a host" from "host, but something went wrong".
        /// </summary>
        public bool IsReplicatedClient => Lab == null && !SimulatesLocally;

        /// <summary>Definitions, for anything that needs to look up a unit or a source hint.</summary>
        public ContentCatalog Catalog => catalog;

        private readonly Dictionary<SampleId, VialProp> props = new();

        public IReadOnlyDictionary<SampleId, VialProp> Props => props;

        /// <summary>
        /// Keeps this process's bottles in step with the host's, on a process that has no lab of its
        /// own. Null wherever <see cref="Lab"/> is not — a process that simulates spawns its own props
        /// as the samples arrive and has nothing to reconcile against.
        /// </summary>
        private VialReconciler vials;

        /// <summary>
        /// Keeps this process's results slips in step with the host's. Client-only for the reason
        /// <see cref="vials"/> is: a process that simulates prints its own paper as its own runs
        /// finish, through <see cref="SpawnPrintout"/>.
        /// </summary>
        private SlipReconciler slips;

        /// <summary>
        /// Keeps this process's delivery cartons in step with the host's (#80). Client-only for the
        /// reason <see cref="vials"/> is: a host sets its own boxes down as the truck unloads, through
        /// <see cref="DeliveryBayStation"/>.
        /// </summary>
        private CartonReconciler cartons;

        /// <summary>
        /// Keeps the solvent bottles where the host says they are. Unlike <see cref="vials"/> this
        /// runs on every process — a host has no separate bottle spawner to duplicate, so both sides
        /// share one. Built on first use rather than in <c>Awake</c>, so an edit-mode test that never
        /// runs a Unity lifecycle method still gets a working one.
        /// </summary>
        private BottleReconciler bottles;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildLabIfAuthoritative();
        }

        /// <summary>
        /// Build the lab, unless this process is a client and therefore has no business simulating.
        /// <para>
        /// Split out of <c>Awake</c> and made public so the §3.1 authority rule can be tested at all.
        /// Unity does not run MonoBehaviour lifecycle methods in edit mode, so an edit-mode test can
        /// create this component and activate it and <c>Awake</c> still never fires — which makes a
        /// test written that way pass while asserting nothing. <c>Awake</c> is now one delegating
        /// call, so a test of this method is a test of what actually runs.
        /// </para>
        /// </summary>
        public void BuildLabIfAuthoritative()
        {
            if (catalog == null || !catalog.IsComplete)
            {
                Debug.LogError(
                    "[LabRuntime] ContentCatalog is missing or empty. Run " +
                    "Residue > Content > Rebuild Definitions, then assign Assets/Data/ContentCatalog.asset.",
                    this);
                enabled = false;
                return;
            }

            // A client builds no lab. See SimulatesLocally: constructing one here would put a
            // truth-bearing simulation in every player's process.
            //
            // It does build the bottles, though. They are local props either way (§3.2) — the only
            // difference is where the instruction to place one comes from, and on this side it comes
            // off the wire rather than out of a LabState.
            if (!SimulatesLocally)
            {
                vials = new VialReconciler(this);
                slips = new SlipReconciler(this);
                cartons = new CartonReconciler(this);
                Debug.Log("[LabRuntime] Client process — the lab is replicated, not simulated.", this);
                return;
            }

            // Past the client return, and therefore host-only. This is the whole of the save layer's
            // authority story: a client never reaches these lines, so it neither loads a run nor
            // installs the hook that writes one. A save on a client would be a second set of books
            // for state the host owns, and the two would part company on the first command the host
            // refused (#49).
            if (RunSaveSlot.TakeContinueRequest()) ContinueSavedRun();

            // Read here, and always read, for the reason the CONTINUE latch is: the lab arrives
            // through a scene load and there is nothing on the other side to hand an argument to. The
            // Lab check is what makes the two latches order-independent — a save that rebuilt itself
            // wins, and a tutorial request left over from a menu the player backed out of is simply
            // consumed and dropped.
            bool tutorial = TutorialRun.TakeRequest() && Lab == null;

            Lab ??= tutorial
                ? new LabState(catalog, ContractPlan.Tutorial(), ContractPlan.TutorialSeed)
                : new LabState(catalog, ContractPlan.Default(),
                               seed == 0 ? Random.Range(1, int.MaxValue) : seed);
            Lab.MachineTimeScale = machineTimeScale;

            // End of day is the only moment the simulation is quiescent, so it is the only moment a
            // snapshot is a picture of anything. See RunSnapshotCapture.
            //
            // The tutorial is not subscribed at all, rather than guarded inside the handler. There is
            // one save slot (#49) and it belongs to the real contract: a two-day guided run that wrote
            // to it would destroy a twenty-day contract at day 14 for anyone who pressed TUTORIAL to
            // look at it, and OnDayEndedSaveRun deletes the file outright on a finished run — which
            // the tutorial reaches in ten minutes. Nothing is lost by it: the tutorial is short,
            // fixed-seed and replayable from the menu, so there is no run in it to lose.
            if (savingAllowed && !tutorial) Lab.DayEnded += OnDayEndedSaveRun;

            // Loud on purpose. A scaled lab tells you nothing about whether the queue pressure works,
            // and this is exactly the kind of testing knob that ends up in a build.
            if (!Mathf.Approximately(machineTimeScale, 1f))
            {
                Debug.LogWarning(
                    $"[LabRuntime] Instrument times scaled to {machineTimeScale:P0} of the real balance " +
                    "for testing. Machine occupancy and the volume economy will not behave realistically. " +
                    "Set machineTimeScale back to 1 on the LabRuntime object before judging the loop.", this);
            }

            // A continued run already has its bench: the save names each instrument by instance id and
            // brings its residue and its drift back with it. Installing over the top would give the
            // lab two of everything and orphan the runtime state that arrived with the save.
            if (Lab.Machines.Count == 0)
            {
                foreach (var id in installedMachineIds)
                {
                    var def = catalog.Machine(id);
                    if (def == null)
                    {
                        Debug.LogWarning($"[LabRuntime] No MachineDef with id '{id}'; skipping.", this);
                        continue;
                    }
                    Lab.Install(def, id);
                }
            }

            // This process simulates, so this process validates. On a client the executor stays null
            // and LabCommands refuses locally rather than mutating a lab that is not there.
            LabCommands.Executor = new LabCommandExecutor(Lab, this);

            // And this process reads its own lab rather than a snapshot of it. On a client this stays
            // null and Residue.Net installs the replicated view instead — see LabView.
            LabView.Host = new HostLabView(Lab);

            // Last, and only on a tutorial. The tracker subscribes to signals the lines above have
            // just finished installing, and it is an observer throughout — see TutorialObjectives for
            // why nothing in the simulation is allowed to know it is there.
            if (tutorial) TutorialObjectives.Begin(Lab);
        }

        /// <summary>True while this run is the guided two-day contract (<see cref="TutorialRun"/>).</summary>
        public bool IsTutorial => Lab != null && Lab.Plan != null &&
                                  Lab.Plan.Id == ContractPlan.TutorialId;

        // -- Saving and continuing (#49) ----------------------------------------------------------------

        /// <summary>
        /// False once a requested CONTINUE has been refused. A save this build could not read must
        /// survive the shift the player ends up playing instead: overwriting it at the next day end
        /// would destroy the only copy of a run that a build with the missing content could still
        /// load. Nothing else clears it — a plain NEW SHIFT is the player choosing to start over, and
        /// that legitimately writes over whatever was there.
        /// </summary>
        private bool savingAllowed = true;

        /// <summary>True when this lab was rebuilt from disk rather than generated.</summary>
        public bool Continued { get; private set; }

        /// <summary>
        /// Rebuild the saved run, or say loudly why not and leave <see cref="Lab"/> null so the caller
        /// starts a fresh one.
        /// </summary>
        private void ContinueSavedRun()
        {
            if (RunSaveSlot.TryLoad(catalog, out var restored, out string refusal))
            {
                Lab = restored;
                Continued = true;
                Debug.Log($"[LabRuntime] Continued the saved run at day {restored.Day}.", this);
                return;
            }

            savingAllowed = false;
            Debug.LogError(
                $"[LabRuntime] CONTINUE was asked for and refused: {refusal} Starting a new shift " +
                "instead. The save has been left alone so a build that can read it still can.", this);
        }

        /// <summary>
        /// Write the run out as the day closes. Subscribed only on a process that simulates, so this
        /// is the structural half of "host-only" — see <see cref="RunSaveSlot"/> for the rest.
        /// <para>
        /// A finished run clears the slot rather than saving over it. CONTINUE on a contract that has
        /// no day left to start is a button that loads a lab and then refuses to open it, which is a
        /// worse answer than not offering the button.
        /// </para>
        /// </summary>
        private void OnDayEndedSaveRun(IReadOnlyList<ConsequenceReport> reports)
        {
            if (Lab == null) return;

            if (Lab.IsRunOver)
            {
                RunSaveSlot.Store.Delete();
                return;
            }

            if (!RunSaveSlot.TrySave(Lab, out string refusal))
            {
                // §9: never fail quietly. A run that has silently stopped saving is a run the player
                // finds out about by losing it.
                Debug.LogError($"[LabRuntime] {refusal}", this);
            }
        }

        /// <summary>
        /// Put the room back together after a continued load: a vial for every bottle the save says
        /// exists, and the paper nobody filed before they quit.
        /// <para>
        /// A fresh run gets its props as the samples arrive — <see cref="CartonProp"/> when a box is
        /// cut open, <c>MachineStation</c> for a printout. A continued run has bottles on shelves, in
        /// the fridge and inside instruments that no arrival will ever announce, so without this the
        /// player walks into a lab whose terminal lists twenty records and whose benches are bare.
        /// </para>
        /// <para>
        /// A vial still in a carton is deliberately <i>not</i> rebuilt here: a restored box comes back
        /// sealed (see <c>DeliveryBay.RebuildFrom</c>), a sealed box advertises no slots, and
        /// <see cref="PropSockets"/> therefore answers null and this loop skips it. The bottle appears
        /// when somebody opens the box, which is the only place a vial has ever come out of one.
        /// </para>
        /// <para>
        /// Anything the save recorded as <i>held</i> is put back on a shelf first. A client id is a
        /// connection number and means nothing across a restart, so a bottle left marked held by one
        /// is a bottle nobody can ever pick up — the same failure
        /// <c>SolventStore.ReleaseAllHeldBy</c> exists to prevent on a disconnect.
        /// </para>
        /// </summary>
        private void RestoreProps()
        {
            if (Lab == null) return;

            foreach (var sample in Lab.Samples.All)
            {
                if (sample.Location.Kind != SampleLocationKind.Held) continue;
                SampleLifecycle.TryMove(sample, SampleLocation.OnSurface(SampleRack.DefaultRackId, -1), out _);
            }

            var slips = new List<ResultSlips.Slip>();
            Lab.Slips.CollectInto(slips);
            foreach (var slip in slips)
            {
                if (slip.Location.Kind != SampleLocationKind.Held) continue;
                Lab.Slips.ReleaseAllHeldBy(slip.Location.HolderClientId);
            }

            foreach (var bottle in Lab.Solvent.All)
            {
                if (bottle.Location.Kind != SampleLocationKind.Held) continue;
                Lab.Solvent.ReleaseAllHeldBy(bottle.Location.HolderClientId);
            }

            foreach (var sample in Lab.Samples.All)
            {
                if (props.ContainsKey(sample.Id)) continue;

                var socket = PropSockets.For(sample.Location, null, out bool reachable);
                if (socket == null) continue;   // archived, consumed, or a fixture this scene lacks

                SpawnVial(sample.Id, sample.EquipmentTag, sample.VolumeMl, socket, reachable);
            }

            slips.Clear();
            Lab.Slips.CollectInto(slips);
            foreach (var slip in slips)
            {
                if (slipProps.ContainsKey(slip.Ticket)) continue;

                var socket = PropSockets.ForSlip(slip.Location, null, out bool reachable);
                if (socket == null) continue;

                var machine = Lab.FindMachine(slip.MachineInstanceId);
                var sample = Lab.Samples.Get(slip.Sample);

                RestorePrintout(slip.Ticket, slip.Sample, slip.Result,
                                machine?.Def != null ? machine.Def.DisplayName : "Instrument",
                                RunCaption.For(slip.Result, sample != null ? sample.RecordTag : null),
                                socket, reachable);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Lab != null) Lab.DayEnded -= OnDayEndedSaveRun;
            if (LabCommands.Executor != null && LabCommands.Executor.Lab == Lab) LabCommands.Executor = null;

            // Checked against this runtime's own lab for the reason the executor is, and unhooked at
            // all because the tracker holds a subscription to a static event: a card left listening
            // after its lab has gone would tick on the next run's actions.
            TutorialObjectives.End(Lab);

            // Checked against this runtime's own lab, for the same reason the executor is: a second
            // LabRuntime destroying itself in Awake must not unhook the one that is actually running.
            if (LabView.Host is HostLabView host && host.Lab == Lab) LabView.Host = null;
        }

        // -- Fixtures ---------------------------------------------------------------------------------

        /// <summary>
        /// Where the placed things are, by id. Static because it is a fact about the scene rather than
        /// about any one component's lifetime, which keeps it free of the ordering question that
        /// self-registration otherwise raises — a fixture may register before or after the runtime
        /// wakes and it makes no difference.
        /// </summary>
        private static readonly Dictionary<string, Transform> fixtures = new();

        /// <summary>
        /// The subset of fixtures a bottle can actually be put <i>into</i>. Separate from
        /// <see cref="fixtures"/> because most of what registers — the terminal, a wall panel — has a
        /// position and no shelf, and a caller asking for slot 3 of the terminal should be told no
        /// rather than handed its root transform.
        /// </summary>
        private static readonly Dictionary<string, IVialSlots> slotted = new();

        /// <summary>
        /// Output trays, by the same fixture id. A third table rather than a second use of
        /// <see cref="slotted"/>, because an instrument is both — it holds a vial in its sample path
        /// and paper in its tray, and those are two different sockets under one id. Asking
        /// <see cref="IVialSlots"/> for "slot 0 of karl_fischer-0" has to keep meaning the sample
        /// path, or a client would park a printout inside the titrator.
        /// </summary>
        private static readonly Dictionary<string, Transform> trays = new();

        /// <summary>
        /// Announce a fixture so the host can tell whether a player is standing at it. Called from
        /// <c>OnEnable</c> by anything a command can be aimed at.
        /// </summary>
        public static void RegisterFixture(string fixtureId, Transform placed)
        {
            if (string.IsNullOrEmpty(fixtureId) || placed == null) return;
            fixtures[fixtureId] = placed;
        }

        /// <summary>
        /// Announce a fixture that also has places to put bottles in — see <see cref="IVialSlots"/>.
        /// <para>
        /// Registered rather than searched for because a <c>SampleLocation</c> names a container by
        /// id, and the id is the only thing that crosses the wire. A client resolving
        /// <c>rack#3</c> has nothing else to go on.
        /// </para>
        /// </summary>
        public static void RegisterFixture(string fixtureId, Transform placed, IVialSlots slots)
        {
            RegisterFixture(fixtureId, placed);
            if (string.IsNullOrEmpty(fixtureId) || placed == null || slots == null) return;
            slotted[fixtureId] = slots;
        }

        /// <summary>
        /// Announce where a fixture's printed paper lands. Only an instrument has one, and it is a
        /// bare transform rather than an <see cref="IVialSlots"/> because a tray holds exactly one
        /// slip — there is no slot to number and no occupancy to ask about.
        /// </summary>
        public static void RegisterTray(string fixtureId, Transform tray)
        {
            if (string.IsNullOrEmpty(fixtureId) || tray == null) return;
            trays[fixtureId] = tray;
        }

        /// <summary>
        /// The output tray placed under that id, or null if this scene has none. Null is a real
        /// answer and the reconciler treats it as "leave the prop alone".
        /// </summary>
        public static Transform TrayFor(string fixtureId)
        {
            if (string.IsNullOrEmpty(fixtureId)) return null;

            // Unity's ==, because a destroyed transform is a live C# reference and parenting to one
            // throws rather than doing nothing.
            return trays.TryGetValue(fixtureId, out var tray) && tray != null ? tray : null;
        }

        /// <summary>Withdraw a fixture. Ignores the call if something else has since claimed the id.</summary>
        public static void ForgetFixture(string fixtureId, Transform placed)
        {
            if (string.IsNullOrEmpty(fixtureId)) return;
            if (!fixtures.TryGetValue(fixtureId, out var current) || current != placed) return;

            fixtures.Remove(fixtureId);
            slotted.Remove(fixtureId);
            trays.Remove(fixtureId);
        }

        /// <summary>
        /// The container placed under that id, or null if this scene has none — or has one with no
        /// slots in it. Null is a real answer and the reconciler treats it as "leave the prop alone".
        /// </summary>
        public static IVialSlots SlotsFor(string containerId)
        {
            if (string.IsNullOrEmpty(containerId)) return null;
            if (!slotted.TryGetValue(containerId, out var slots)) return null;

            // Unity's ==, because a destroyed MonoBehaviour is a live C# reference and calling
            // Slot() on one throws rather than returning null.
            return slots is Object o && o == null ? null : slots;
        }

        bool ILabStations.TryLocate(string fixtureId, out Vector3 position)
        {
            position = default;
            if (string.IsNullOrEmpty(fixtureId)) return false;
            if (!fixtures.TryGetValue(fixtureId, out var placed) || placed == null) return false;

            position = placed.position;
            return true;
        }

        private void Start()
        {
            if (Lab == null) return;

            // A continued run opens between shifts, on the day it was saved at the end of. Beginning
            // the next day here would skip the summary the player quit on and generate a morning's
            // arrivals nobody asked for — the terminal's START NEXT DAY is where that decision lives.
            if (Lab.Day > 0)
            {
                RestoreProps();
                return;
            }

            Lab.BeginDay();
        }

        private void Update()
        {
            if (Lab != null)
            {
                Lab.Tick(Time.deltaTime);
            }
            else
            {
                // No lab means no simulation to advance, but there are still boxes, bottles and slips
                // in the room and the host has an opinion about where they are.
                //
                // Cartons first: a box is a container, and the bottles inside it can only be placed
                // once it has been built and has registered its slots.
                cartons?.Tick();
                vials?.Tick();
                slips?.Tick();
            }

            // Solvent bottles are reconciled either way — see BottleReconciler for why this one is
            // not host-exempt the way the vials are.
            (bottles ??= new BottleReconciler(this)).Tick();
        }

        // -- Props ----------------------------------------------------------------------------------

        /// <summary>
        /// Create the physical vial for a sample this process is simulating. Pooling comes later
        /// (§3.2); for one lab's worth of samples per day, instantiating is fine and keeps the MVP
        /// honest about what it is.
        /// </summary>
        public VialProp SpawnVial(SampleState sample, Transform socket) =>
            sample == null
                ? null
                : SpawnVial(sample.Id, sample.EquipmentTag, sample.VolumeMl, socket);

        /// <summary>
        /// Create the physical vial from the facts about a bottle, rather than from a
        /// <see cref="SampleState"/> nobody but the host has.
        /// <para>
        /// This is the one that actually builds the prop, and both sides go through it. §3.2 makes a
        /// vial a local prop on every machine in the session; the only thing that differs is where the
        /// instruction came from — the host's own registry, or the replicated record a
        /// <see cref="VialReconciler"/> is walking. Two spawn paths would be two things to keep
        /// looking alike, and the one nobody plays would be the one that drifted.
        /// </para>
        /// </summary>
        public VialProp SpawnVial(SampleId id, string label, float volumeMl, Transform socket,
                                  bool interactable = true)
        {
            if (!id.IsValid || vialPrefab == null || socket == null) return null;
            if (props.TryGetValue(id, out var existing) && existing != null) return existing;

            var vial = Instantiate(vialPrefab, socket);
            vial.Bind(id, label);
            vial.AttachTo(socket, interactable);
            vial.SetFillFraction(volumeMl / VialProp.FullMl);

            props[id] = vial;
            return vial;
        }

        /// <summary>
        /// Destroy the prop for a bottle that no longer exists, and forget it.
        /// <para>
        /// Called when a sample stops appearing in the replicated list, which is how "consumed" is
        /// expressed — the host drops the row rather than sending a tombstone (see
        /// <see cref="VialReconciler"/>). Anyone holding it is left holding nothing: Unity's null
        /// semantics make <c>PlayerInteractor.Carried</c> read as empty the moment the object goes,
        /// which is the right outcome and the reason no hand-back is wired here.
        /// </para>
        /// </summary>
        public void RetireVial(SampleId id)
        {
            if (!props.TryGetValue(id, out var prop)) return;
            props.Remove(id);
            if (prop == null) return;

            // Destroy is a play-mode call and logs an error in the Editor's edit mode, where the
            // reconciler is exercised by tests.
            if (Application.isPlaying) Destroy(prop.gameObject);
            else DestroyImmediate(prop.gameObject);
        }

        // -- Results slips ----------------------------------------------------------------------------
        //
        // Keyed by ticket rather than by SampleId: a blank and a certified standard belong to no
        // sample at all, and two runs of the same oil print two slips that have to be told apart.

        private readonly Dictionary<int, PrintoutProp> slipProps = new();

        public IReadOnlyDictionary<int, PrintoutProp> SlipProps => slipProps;

        public PrintoutProp SlipPropFor(int ticket) =>
            slipProps.TryGetValue(ticket, out var slip) ? slip : null;

        /// <summary>
        /// Drop a results slip into an instrument's output tray. Not pooled: a printout exists
        /// until someone files it or replaces it, and there are only ever a handful.
        /// <para>
        /// The slip is registered with <see cref="LabState.Slips"/> first and carries the ticket it
        /// was given. That ticket is how it is named later — a client filing a slip says which one,
        /// never what it says, so the numbers that reach a record are always the host's own.
        /// </para>
        /// Host-only: it needs a <see cref="LabState"/> to issue the ticket out of. The client's half
        /// is <see cref="SpawnSlip"/>, which is handed a ticket the host already minted.
        /// </summary>
        public PrintoutProp SpawnPrintout(SampleId sampleId, TestResult result, string machineInstanceId,
                                          string machineName, string recordTag, Transform socket)
        {
            if (printoutPrefab == null || result == null || socket == null) return null;
            if (Lab == null) return null;

            int ticket = Lab.Slips.Issue(sampleId, machineInstanceId, result);
            return RestorePrintout(ticket, sampleId, result, machineName, recordTag, socket);
        }

        /// <summary>
        /// Build the paper for a ticket the host <i>already</i> minted — a slip a save recorded and
        /// nobody had filed (#49).
        /// <para>
        /// Deliberately separate from <see cref="SpawnPrintout"/> rather than an optional argument on
        /// it: issuing a ticket is the host's answer to a printer running, and a caller that could
        /// name its own ticket could mint one over paper that already exists. Splitting the two keeps
        /// <c>ResultSlips.Issue</c> the only way a number is ever handed out during play.
        /// </para>
        /// </summary>
        private PrintoutProp RestorePrintout(int ticket, SampleId sampleId, TestResult result,
                                             string machineName, string recordTag, Transform socket,
                                             bool interactable = true)
        {
            if (ticket == ResultSlips.NoTicket || printoutPrefab == null || result == null ||
                socket == null) return null;

            if (slipProps.TryGetValue(ticket, out var existing) && existing != null) return existing;

            var printout = Instantiate(printoutPrefab, socket);
            printout.Bind(ticket, sampleId, result, machineName, recordTag);
            printout.AttachTo(socket, interactable);

            slipProps[ticket] = printout;
            return printout;
        }

        /// <summary>
        /// Build the paper from the facts about a slip, rather than from a <see cref="TestResult"/>
        /// nobody but the host has.
        /// <para>
        /// The client's counterpart to <see cref="SpawnPrintout"/>, and the pair are deliberately
        /// <i>not</i> merged the way the two vial spawners are: a host mints the ticket as a side
        /// effect of printing, and a client is told one. Sharing a signature would mean one of the two
        /// passing a ticket it does not have yet.
        /// </para>
        /// The numbers are not passed because the slip does not carry them — it names its run with
        /// <paramref name="resultKey"/> and looks the values up through <see cref="SlipFeed.Numbers"/>
        /// if anybody actually reads it. See <see cref="SlipPlacement"/>.
        /// </summary>
        public PrintoutProp SpawnSlip(int ticket, int resultKey, SampleId sample, bool isBlank,
                                      string machineName, string recordTag, Transform socket,
                                      bool interactable = true)
        {
            if (ticket == 0 || printoutPrefab == null || socket == null) return null;
            if (slipProps.TryGetValue(ticket, out var existing) && existing != null) return existing;

            var slip = Instantiate(printoutPrefab, socket);
            slip.Bind(ticket, resultKey, sample, isBlank, machineName, recordTag);
            slip.AttachTo(socket, interactable);

            slipProps[ticket] = slip;
            return slip;
        }

        /// <summary>
        /// Destroy the paper for a slip that no longer exists, and forget it.
        /// <para>
        /// A slip is <b>consumed by filing</b>, which a bottle never is — so unlike
        /// <see cref="RetireVial"/> this is called on a host as well as on a client, from the two
        /// places the paper stops existing: the desk when it is filed, and the tray when a second run
        /// prints over it. One retire path means the prop table cannot be left holding a destroyed
        /// object on the side that has a <c>LabState</c>.
        /// </para>
        /// Anyone holding it is left holding nothing: Unity's null semantics make
        /// <c>PlayerInteractor.Carried</c> read as empty the moment the object goes.
        /// </summary>
        public void RetireSlip(int ticket)
        {
            if (!slipProps.TryGetValue(ticket, out var prop)) return;
            slipProps.Remove(ticket);
            if (prop == null) return;

            // Destroy is a play-mode call and logs an error in the Editor's edit mode, where the
            // reconciler is exercised by tests.
            if (Application.isPlaying) Destroy(prop.gameObject);
            else DestroyImmediate(prop.gameObject);
        }

        // -- Solvent bottles --------------------------------------------------------------------------
        //
        // A separate table from the vials, keyed by bottle id rather than by SampleId. Two of them
        // exist for the whole run and neither is ever retired, so there is no pooling question here —
        // just "has this one been built yet".

        private readonly Dictionary<string, SolventBottle> bottleProps = new();

        public SolventBottle BottlePropFor(string bottleId) =>
            !string.IsNullOrEmpty(bottleId) && bottleProps.TryGetValue(bottleId, out var b) ? b : null;

        /// <summary>
        /// Create the physical solvent bottle. Called only by <see cref="BottleReconciler"/>, on a
        /// host and on a client alike — one spawn path, for the reason
        /// <see cref="SpawnVial(SampleId,string,float,Transform,bool)"/> gives.
        /// </summary>
        public SolventBottle SpawnBottle(string bottleId, int capacity, int charges, Transform socket,
                                         bool interactable = true)
        {
            if (string.IsNullOrEmpty(bottleId) || bottlePrefab == null || socket == null) return null;
            if (bottleProps.TryGetValue(bottleId, out var existing) && existing != null) return existing;

            var bottle = Instantiate(bottlePrefab, socket);
            bottle.Bind(bottleId, capacity);
            bottle.AttachTo(socket, interactable);
            bottle.SetCharges(charges);

            bottleProps[bottleId] = bottle;
            return bottle;
        }

        // -- Deliveries (#30, #31) ----------------------------------------------------------------------
        //
        // Keyed by carton id. A note has no key of its own — one carton, one note — so the two tables
        // share it, which is also what makes retiring a flattened box able to take its paper with it.

        private readonly Dictionary<string, CartonProp> cartonProps = new();
        private readonly Dictionary<string, DeliveryNoteProp> noteProps = new();

        public CartonProp CartonPropFor(string cartonId) =>
            !string.IsNullOrEmpty(cartonId) && cartonProps.TryGetValue(cartonId, out var c) ? c : null;

        public DeliveryNoteProp NotePropFor(string cartonId) =>
            !string.IsNullOrEmpty(cartonId) && noteProps.TryGetValue(cartonId, out var n) ? n : null;

        /// <summary>The boxes standing in this process's room, so a reconciler can retire the strays.</summary>
        public IReadOnlyDictionary<string, CartonProp> CartonProps => cartonProps;

        /// <summary>
        /// Build the physical box for a carton the truck has just set down. Not pooled: there are a
        /// handful a day and each one is flattened within the shift.
        /// </summary>
        public CartonProp SpawnCarton(Carton carton, Transform socket, bool interactable = true) =>
            carton == null
                ? null
                : SpawnCarton(carton.Id, carton.JobNumber, carton.SenderName, socket, interactable);

        /// <summary>
        /// Build the physical box from the facts about a delivery, rather than from a
        /// <see cref="Carton"/> nobody but the host has.
        /// <para>
        /// This is the one that actually builds the prop, and both sides go through it — the argument
        /// <see cref="SpawnVial(SampleId,string,float,Transform,bool)"/> makes at length. §3.2 keeps a
        /// carton a local prop on every machine in the session; the only thing that differs is where
        /// the instruction came from, the host's own bay or the replicated record a
        /// <see cref="CartonReconciler"/> is walking.
        /// </para>
        /// </summary>
        public CartonProp SpawnCarton(string cartonId, string jobNumber, string senderName,
                                      Transform socket, bool interactable = true)
        {
            if (string.IsNullOrEmpty(cartonId) || cartonPrefab == null || socket == null) return null;
            if (cartonProps.TryGetValue(cartonId, out var existing) && existing != null) return existing;

            var prop = Instantiate(cartonPrefab, socket);
            prop.Bind(cartonId, jobNumber, senderName);
            prop.AttachTo(socket, interactable);

            cartonProps[cartonId] = prop;
            return prop;
        }

        /// <summary>
        /// Build the paper that was in the box. Called by <see cref="CartonProp"/> the moment the seal
        /// goes, because until then the note is not a thing anybody can reach (#31).
        /// </summary>
        public DeliveryNoteProp SpawnNote(Carton carton, Transform socket) =>
            carton == null
                ? null
                : SpawnNote(carton.Id, carton.JobNumber, carton.SenderName,
                            DeliveryNoteProp.Printed(carton.Note), socket);

        /// <summary>
        /// Build the paper from what is printed on it, rather than from a <see cref="DeliveryNote"/>
        /// only the host holds. One builder, two callers, for the reason
        /// <see cref="SpawnCarton(string,string,string,Transform,bool)"/> gives.
        /// </summary>
        public DeliveryNoteProp SpawnNote(string cartonId, string jobNumber, string senderName,
                                          string printed, Transform socket)
        {
            if (string.IsNullOrEmpty(cartonId) || notePrefab == null || socket == null) return null;
            if (noteProps.TryGetValue(cartonId, out var existing) && existing != null) return existing;

            var prop = Instantiate(notePrefab, socket);
            prop.Bind(cartonId, jobNumber, senderName, printed);
            prop.AttachTo(socket, interactable: true);

            noteProps[cartonId] = prop;
            return prop;
        }

        /// <summary>
        /// Destroy a flattened carton, and the note with it.
        /// <para>
        /// The paper goes because the box it belonged to is gone: a note is a document for a shift
        /// rather than a record (see <c>DeliveryNote</c>), and what outlives the day is the job number
        /// on each <c>SampleState</c>. <c>DeliveryBay</c> already refuses to flatten a box whose note
        /// is still in it or still in somebody's hands, so nothing is ever taken out of a grip here.
        /// </para>
        /// </summary>
        public void RetireCarton(string cartonId)
        {
            if (string.IsNullOrEmpty(cartonId)) return;

            if (noteProps.TryGetValue(cartonId, out var note))
            {
                noteProps.Remove(cartonId);
                DestroyProp(note != null ? note.gameObject : null);
            }

            if (!cartonProps.TryGetValue(cartonId, out var carton)) return;
            cartonProps.Remove(cartonId);
            DestroyProp(carton != null ? carton.gameObject : null);
        }

        /// <summary>Destroy is a play-mode call and logs an error in edit mode, where tests run.</summary>
        private static void DestroyProp(GameObject prop)
        {
            if (prop == null) return;
            if (Application.isPlaying) Destroy(prop);
            else DestroyImmediate(prop);
        }

        public VialProp PropFor(SampleId id) => props.TryGetValue(id, out var v) ? v : null;

        public SampleState SampleFor(SampleId id) => Lab?.Samples.Get(id);
    }
}
