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
                Debug.Log("[LabRuntime] Client process — the lab is replicated, not simulated.", this);
                return;
            }

            Lab = new LabState(catalog, ContractPlan.Default(), seed == 0 ? Random.Range(1, int.MaxValue) : seed)
            {
                MachineTimeScale = machineTimeScale
            };

            // Loud on purpose. A scaled lab tells you nothing about whether the queue pressure works,
            // and this is exactly the kind of testing knob that ends up in a build.
            if (!Mathf.Approximately(machineTimeScale, 1f))
            {
                Debug.LogWarning(
                    $"[LabRuntime] Instrument times scaled to {machineTimeScale:P0} of the real balance " +
                    "for testing. Machine occupancy and the volume economy will not behave realistically. " +
                    "Set machineTimeScale back to 1 on the LabRuntime object before judging the loop.", this);
            }

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

            // This process simulates, so this process validates. On a client the executor stays null
            // and LabCommands refuses locally rather than mutating a lab that is not there.
            LabCommands.Executor = new LabCommandExecutor(Lab, this);

            // And this process reads its own lab rather than a snapshot of it. On a client this stays
            // null and Residue.Net installs the replicated view instead — see LabView.
            LabView.Host = new HostLabView(Lab);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (LabCommands.Executor != null && LabCommands.Executor.Lab == Lab) LabCommands.Executor = null;

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

        /// <summary>Withdraw a fixture. Ignores the call if something else has since claimed the id.</summary>
        public static void ForgetFixture(string fixtureId, Transform placed)
        {
            if (string.IsNullOrEmpty(fixtureId)) return;
            if (!fixtures.TryGetValue(fixtureId, out var current) || current != placed) return;

            fixtures.Remove(fixtureId);
            slotted.Remove(fixtureId);
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
            Lab.BeginDay();
        }

        private void Update()
        {
            if (Lab != null)
            {
                Lab.Tick(Time.deltaTime);
                return;
            }

            // No lab means no simulation to advance, but there are still bottles in the room and the
            // host has an opinion about where they are.
            vials?.Tick();
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

        /// <summary>
        /// Drop a results slip into an instrument's output tray. Not pooled: a printout exists
        /// until someone files it or replaces it, and there are only ever a handful.
        /// <para>
        /// The slip is registered with <see cref="LabState.Slips"/> first and carries the ticket it
        /// was given. That ticket is how it is named later — a client filing a slip says which one,
        /// never what it says, so the numbers that reach a record are always the host's own.
        /// </para>
        /// </summary>
        public PrintoutProp SpawnPrintout(SampleId sampleId, TestResult result, string machineInstanceId,
                                          string machineName, string equipmentTag, Transform socket)
        {
            if (printoutPrefab == null || result == null || socket == null) return null;
            if (Lab == null) return null;

            int ticket = Lab.Slips.Issue(sampleId, machineInstanceId, result);

            var printout = Instantiate(printoutPrefab, socket);
            printout.Bind(ticket, sampleId, result, machineName, equipmentTag);
            printout.AttachTo(socket);
            return printout;
        }

        public VialProp PropFor(SampleId id) => props.TryGetValue(id, out var v) ? v : null;

        public SampleState SampleFor(SampleId id) => Lab?.Samples.Get(id);
    }
}
