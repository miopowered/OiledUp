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
    public sealed class LabRuntime : MonoBehaviour
    {
        public static LabRuntime Instance { get; private set; }

        [Header("Content")]
        [SerializeField] private ContentCatalog catalog;

        [Tooltip("Fixed seed reproduces a whole contract exactly. Set to 0 to vary per session.")]
        [SerializeField] private int seed = 20260823;

        [Header("Installed instruments")]
        [Tooltip("MachineDef ids, in the order the stations appear in the scene.")]
        [SerializeField]
        private string[] installedMachineIds = { "icp", "ftir", "karl_fischer", "ferrography" };

        [Header("Testing")]
        [Tooltip("Multiplier on every instrument's run time and on the flush hold.\n\n" +
                 "1 = the real balance. Lower values make the loop testable without editing " +
                 "ContentTables, which would ship. The RATIOS between instruments are design " +
                 "(§10: ferrography costs 15x an FTIR screen), so scaling preserves them.\n\n" +
                 "Set back to 1 before judging whether the game is fun.")]
        [SerializeField, Range(0.01f, 1f)] private float machineTimeScale = 0.05f;

        [Header("Props")]
        [SerializeField] private VialProp vialPrefab;
        [SerializeField] private PrintoutProp printoutPrefab;

        public LabState Lab { get; private set; }

        /// <summary>Definitions, for anything that needs to look up a unit or a source hint.</summary>
        public ContentCatalog Catalog => catalog;

        private readonly Dictionary<SampleId, VialProp> props = new();

        public IReadOnlyDictionary<SampleId, VialProp> Props => props;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (catalog == null || !catalog.IsComplete)
            {
                Debug.LogError(
                    "[LabRuntime] ContentCatalog is missing or empty. Run " +
                    "Residue > Content > Rebuild Definitions, then assign Assets/Data/ContentCatalog.asset.",
                    this);
                enabled = false;
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
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (Lab == null) return;
            Lab.BeginDay();
        }

        private void Update()
        {
            if (Lab == null) return;
            Lab.Tick(Time.deltaTime);
        }

        // -- Props ----------------------------------------------------------------------------------

        /// <summary>
        /// Create the physical vial for a sample. Pooling comes with M4 (§3.2); for one lab's worth
        /// of samples per day, instantiating is fine and keeps the MVP honest about what it is.
        /// </summary>
        public VialProp SpawnVial(SampleState sample, Transform socket)
        {
            if (sample == null || vialPrefab == null) return null;
            if (props.TryGetValue(sample.Id, out var existing) && existing != null) return existing;

            var vial = Instantiate(vialPrefab, socket);
            vial.Bind(sample.Id, sample.EquipmentTag);
            vial.AttachTo(socket);
            vial.SetFillFraction(sample.VolumeMl / 100f);

            props[sample.Id] = vial;
            return vial;
        }

        /// <summary>
        /// Drop a results slip into an instrument's output tray. Not pooled: a printout exists
        /// until someone files it or replaces it, and there are only ever a handful.
        /// </summary>
        public PrintoutProp SpawnPrintout(SampleId sampleId, TestResult result, string machineName,
                                          string equipmentTag, Transform socket)
        {
            if (printoutPrefab == null || result == null || socket == null) return null;

            var printout = Instantiate(printoutPrefab, socket);
            printout.Bind(sampleId, result, machineName, equipmentTag);
            printout.AttachTo(socket);
            return printout;
        }

        public VialProp PropFor(SampleId id) => props.TryGetValue(id, out var v) ? v : null;

        public SampleState SampleFor(SampleId id) => Lab?.Samples.Get(id);
    }
}
