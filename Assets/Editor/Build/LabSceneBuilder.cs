using System.Collections.Generic;
using System.IO;
using Residue.Data;
using Residue.Editor.Art;
using Residue.Editor.Content;
using Residue.Gameplay.World;
using Residue.Net;
using Residue.Net.Connect;
using Residue.Net.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Residue.Editor.Build
{
    /// <summary>
    /// Generates the greybox lab scene from code.
    /// <para>
    /// §2.5 suggests ProBuilder for architecture, and that is right for a human. This project is
    /// built largely by agents that cannot see the Editor, so the lab is authored as a script
    /// instead: it is reviewable in a diff, reproducible on any machine, and an agent can move a
    /// bench by changing a number rather than by describing what it wants to a human.
    /// </para>
    /// Regenerating replaces the scene wholesale. Anything hand-placed in it will be lost, which is
    /// the trade for having it be reproducible.
    /// <para>
    /// <b>Precondition:</b> the open scene must have no unsaved changes, and the Editor must not be
    /// in play mode. Rebuilding closes whatever is open, and closing a dirty scene raises a
    /// save-changes modal that hangs every MCP call until a human clicks it. The scene is left open
    /// afterwards, so this can be run repeatedly without any hierarchy juggling in between.
    /// </para>
    /// </summary>
    public static class LabSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Lab.unity";
        private const string BootScenePath = "Assets/Scenes/Boot.unity";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string MeshFolder = "Assets/Art/Generated";
        private const string PrefabFolder = "Assets/Prefabs";
        private const string UiFolder = "Assets/UI";
        private const string PaletteMaterial = "Assets/Art/Materials/M_Palette_Opaque.mat";
        private const string EmissivePaletteMaterial = "Assets/Art/Materials/M_Palette_Emissive.mat";
        private const string CatalogPath = "Assets/Data/ContentCatalog.asset";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string VolumeProfilePath = "Assets/Settings/LabVolumeProfile.asset";

        // Room is laid out on the §5.5 half-metre grid.
        private const float RoomWidth = 10f;
        private const float RoomDepth = 8f;
        private const float RoomHeight = 3f;
        private const float BenchHeight = 0.9f;

        private static readonly string[] MachineIds =
            { "cooling_curve", "karl_fischer", "viscometer", "centrifuge", "elemental" };

        [MenuItem("Residue/Build/Rebuild Greybox Lab", priority = 40)]
        public static void Rebuild()
        {
            // Building replaces whatever is open, so refuse if that would lose work. This is also
            // what makes the NewScene call below safe: closing a clean scene is silent, closing a
            // dirty one raises a "save changes?" modal that blocks the Editor and hangs every MCP
            // call an agent makes.
            var open = SceneManager.GetActiveScene();
            if (open.isDirty)
            {
                Debug.LogError("[LabSceneBuilder] The open scene has unsaved changes. Save or " +
                               "discard them before rebuilding.");
                return;
            }

            EnsureFolders();

            if (AssetDatabase.LoadAssetAtPath<ContentCatalog>(CatalogPath) == null)
            {
                Debug.Log("[LabSceneBuilder] No ContentCatalog yet; running content rebuild first.");
                ContentBootstrap.Rebuild();
            }

            if (AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterial) == null)
            {
                Debug.Log("[LabSceneBuilder] No palette material yet; running palette rebuild first.");
                PaletteBootstrap.Rebuild();
            }

            EnsureLayer(ThirdPersonView.PlayerBodyLayer, "PlayerBody");

            // Single, not additive. Additive cannot run while an untitled scene is active, and it
            // cannot save over the Lab scene if the Lab scene is the thing already open — which is
            // the normal state of an Editor someone has been playing in. The dirty check above is
            // what makes closing the current scene safe.
            //
            // This has to happen BEFORE anything below loads an asset. Opening a scene in Single
            // mode runs UnloadUnusedAssets, which destroys any asset a local variable is the only
            // thing holding. Those variables do not become C# null — they become Unity's fake-null,
            // so wiring them into a component silently serialises a null reference and the failure
            // only shows up as an empty catalog at runtime, three steps from the cause.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var palette = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterial);
            var emissivePalette = AssetDatabase.LoadAssetAtPath<Material>(EmissivePaletteMaterial);
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(CatalogPath);

            if (palette == null || emissivePalette == null || catalog == null)
            {
                Debug.LogError("[LabSceneBuilder] Palette materials or content catalog still missing. Aborting.");
                return;
            }

            var theme = EnsureRuntimeTheme();
            var panelSettings = EnsurePanelSettings(theme);
            var vialPrefab = BuildVialPrefab(palette);
            var bottlePrefab = BuildSolventBottlePrefab(palette);
            var inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(
                InputActionsPath);

            var screenMaterial = EnsureScreenMaterial();
            var volumeProfile = EnsureLabVolumeProfile();
            var printoutPrefab = BuildPrintoutPrefab(palette);

            var books = new List<ReferenceBook>();

            BuildEnvironment(scene, palette, emissivePalette, volumeProfile);
            BuildRuntime(scene, catalog, vialPrefab, printoutPrefab, bottlePrefab);
            var stations = BuildStations(scene, palette, screenMaterial, books);
            var player = BuildPlayer(scene, palette, inputAsset, panelSettings);

            WireTerminal(stations.terminal, player.terminalScreen);

            // Scene-placed rather than spawned: the host loads this scene over the network and NGO
            // brings scene NetworkObjects up on every client as part of that load, so the lab's
            // window onto itself exists before anybody asks it anything.
            var netGo = NewRoot(scene, "LabNetwork");
            netGo.AddComponent<NetworkObject>();
            netGo.AddComponent<LabNetwork>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            BuildBootScene(panelSettings);
            RegisterScenesInBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LabSceneBuilder] Built {ScenePath}. It is open now — press Play.");
        }

        // -- Environment -------------------------------------------------------------------------------

        private static void BuildEnvironment(Scene scene, Material palette, Material emissivePalette,
                                             VolumeProfile volumeProfile)
        {
            var root = NewRoot(scene, "Environment");

            var room = new ProcMesh.Builder()
                .Room(new Vector3(RoomWidth, RoomHeight, RoomDepth), 0.2f, PaletteUv.Family.NeutralCold, 4)
                .ToMesh("Lab_Room");
            AddStatic(root, "Room", SaveMesh(room), palette, Vector3.zero, addCollider: true);

            // Bench along the far wall, machines sit on it. 0.9 m per §2.1.
            var bench = new ProcMesh.Builder()
                .Box(Vector3.zero, new Vector3(8f, BenchHeight, 0.7f), PaletteUv.Family.Steel, 5)
                .ToMesh("Lab_Bench");
            AddStatic(root, "Bench_Machines", SaveMesh(bench), palette,
                new Vector3(0f, BenchHeight * 0.5f, -RoomDepth * 0.5f + 0.55f), addCollider: true);

            var intakeBench = new ProcMesh.Builder()
                .Box(Vector3.zero, new Vector3(1.6f, BenchHeight, 0.8f), PaletteUv.Family.Steel, 5)
                .ToMesh("Lab_IntakeBench");
            AddStatic(root, "Bench_Intake", SaveMesh(intakeBench), palette,
                new Vector3(-RoomWidth * 0.5f + 1.1f, BenchHeight * 0.5f, 1.6f), addCollider: true);

            var desk = new ProcMesh.Builder()
                .Box(Vector3.zero, new Vector3(1.6f, BenchHeight, 0.8f), PaletteUv.Family.Steel, 5)
                .ToMesh("Lab_Desk");
            AddStatic(root, "Bench_Terminal", SaveMesh(desk), palette,
                new Vector3(RoomWidth * 0.5f - 1.1f, BenchHeight * 0.5f, 1.6f), addCollider: true);

            // Island between the door and the instruments. Staging space is not decoration: with one
            // pair of hands and four machines, somewhere to put a vial down is what stops the loop
            // deadlocking the moment every instrument is busy.
            var island = new ProcMesh.Builder()
                .Box(Vector3.zero, new Vector3(3.2f, BenchHeight, 0.8f), PaletteUv.Family.Steel, 6)
                .ToMesh("Lab_Island");
            AddStatic(root, "Bench_Island", SaveMesh(island), palette,
                new Vector3(0f, BenchHeight * 0.5f, -1.4f), addCollider: true);

            BuildWashStation(root, palette);

            var lightGo = new GameObject("Sun");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.35f;
            light.color = new Color(0.95f, 0.96f, 1f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48f, 35f, 0f);

            // Interior strip lighting. A sealed room with one exterior directional light is pitch
            // dark, and until the ceiling was wound correctly it only looked lit because light was
            // leaking through inverted faces. §2.4 wants baked lightmaps here eventually; realtime
            // points are the greybox stand-in so the lab is playable in the meantime.
            var lightRoot = NewRoot(scene, "Lighting");
            float[] lampX = { -3f, 0f, 3f };
            float[] lampZ = { -2.4f, 1.6f };
            var luminaireMesh = SaveMesh(ProcMesh.Box("Lab_FluorescentLuminaire",
                new Vector3(1.45f, 0.035f, 0.24f), PaletteUv.Family.NeutralCold, 15));
            var ballastMesh = SaveMesh(ProcMesh.Box("Lab_FluorescentBallast",
                new Vector3(1.55f, 0.055f, 0.30f), PaletteUv.Family.Steel, 5));

            foreach (float x in lampX)
            {
                foreach (float z in lampZ)
                {
                    var lamp = new GameObject($"Lamp_{x:0}_{z:0}");
                    lamp.transform.SetParent(lightRoot.transform, false);
                    lamp.transform.position = new Vector3(x, RoomHeight - 0.35f, z);

                    AddChild(lamp, "DIN_Leuchtenwanne", ballastMesh, palette,
                        new Vector3(0f, 0.035f, 0f), addCollider: false);
                    AddChild(lamp, "Leuchtstoffroehre_4000K", luminaireMesh, emissivePalette,
                        Vector3.zero, addCollider: false);

                    var point = lamp.AddComponent<Light>();
                    point.type = LightType.Point;
                    point.range = 7.5f;
                    point.intensity = 2.35f;
                    point.color = new Color(0.90f, 1f, 0.94f);
                    point.shadows = LightShadows.None; // greybox: shadow cost is not worth it yet
                }
            }

            var post = NewRoot(scene, "PostProcessing_Labor");
            var volume = post.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = volumeProfile;

            var ambience = NewRoot(scene, "Raumton_Oellabor");
            ambience.AddComponent<LabAmbience>();

            // Cold, dim ambient. §2.4's look is flat geometry plus soft fill, not dramatic lighting.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.45f, 0.50f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.35f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.20f, 0.21f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.16f, 0.18f, 0.21f);
            RenderSettings.fogStartDistance = 9f;
            RenderSettings.fogEndDistance = 26f;
        }

        // -- Simulation host ---------------------------------------------------------------------------

        private static LabRuntime BuildRuntime(Scene scene, ContentCatalog catalog,
                                               VialProp vialPrefab, PrintoutProp printoutPrefab,
                                               SolventBottle bottlePrefab)
        {
            var go = new GameObject("LabRuntime");
            SceneManager.MoveGameObjectToScene(go, scene);
            var runtime = go.AddComponent<LabRuntime>();

            var so = new SerializedObject(runtime);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("vialPrefab").objectReferenceValue = vialPrefab;
            so.FindProperty("printoutPrefab").objectReferenceValue = printoutPrefab;
            so.FindProperty("bottlePrefab").objectReferenceValue = bottlePrefab;

            var ids = so.FindProperty("installedMachineIds");
            ids.arraySize = MachineIds.Length;
            for (int i = 0; i < MachineIds.Length; i++)
                ids.GetArrayElementAtIndex(i).stringValue = MachineIds[i];

            so.ApplyModifiedPropertiesWithoutUndo();
            return runtime;
        }

        // -- Stations ----------------------------------------------------------------------------------

        private static (TerminalStation terminal, IntakeCrate crate) BuildStations(
            Scene scene, Material palette, Material screenMaterial, List<ReferenceBook> books)
        {
            var root = NewRoot(scene, "Stations");

            var lightMesh = SaveMesh(ProcMesh.Box("Machine_Light", new Vector3(0.1f, 0.025f, 0.015f),
                PaletteUv.Family.Signal, 13));
            var buttonMesh = SaveMesh(ProcMesh.Box("Machine_Button", new Vector3(0.055f, 0.04f, 0.018f),
                PaletteUv.Family.Brass, 8));

            float benchZ = -RoomDepth * 0.5f + 0.55f;
            float[] xs = { -3.0f, -1.5f, 0f, 1.5f, 3.0f };

            for (int i = 0; i < MachineIds.Length; i++)
            {
                var visual = VisualFor(MachineIds[i]);

                var machineGo = new GameObject($"Machine_{MachineIds[i]}");
                SceneManager.MoveGameObjectToScene(machineGo, scene);
                machineGo.transform.SetParent(root.transform, false);
                machineGo.transform.position = new Vector3(xs[i], BenchHeight, benchZ);

                var bodyMesh = SaveMesh(BuildMachineBody($"Machine_{MachineIds[i]}_Body", visual));
                AddChild(machineGo, "Body", bodyMesh, palette, Vector3.zero, addCollider: true);

                float front = visual.Size.z * 0.5f;

                // Screen: its own quad with real UVs and the emissive material, because
                // MachineDisplay writes a generated texture onto it every run.
                var screenMesh = SaveMesh(ProcMesh.ScreenQuad(
                    $"Machine_{MachineIds[i]}_Screen", visual.ScreenSize.x, visual.ScreenSize.y));
                var screenGo = AddChild(machineGo, "Screen", screenMesh, screenMaterial,
                    new Vector3(0f, visual.ScreenY, front + 0.004f), addCollider: false);

                var display = screenGo.AddComponent<MachineDisplay>();
                var dso = new SerializedObject(display);
                dso.FindProperty("screen").objectReferenceValue = screenGo.GetComponent<Renderer>();
                dso.FindProperty("style").enumValueIndex = visual.Panel ? 1 : 0;
                dso.FindProperty("pixelWidth").intValue = visual.Panel ? 192 : 128;
                dso.FindProperty("pixelHeight").intValue = visual.Panel ? 128 : 64;
                dso.FindProperty("scale").intValue = 2;
                dso.ApplyModifiedPropertiesWithoutUndo();

                var statusLight = AddChild(machineGo, "StatusLight", lightMesh, palette,
                    new Vector3(visual.Size.x * 0.5f - 0.08f, visual.Size.y - 0.04f, front + 0.008f),
                    addCollider: false);

                var vialSocket = new GameObject("VialSocket");
                vialSocket.transform.SetParent(machineGo.transform, false);
                vialSocket.transform.localPosition = new Vector3(0f, visual.Size.y + 0.005f, -0.06f);

                var traySocket = new GameObject("PrintoutSocket");
                traySocket.transform.SetParent(machineGo.transform, false);
                traySocket.transform.localPosition = new Vector3(0f, 0.075f, front + 0.055f);
                traySocket.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                var station = machineGo.AddComponent<MachineStation>();
                var so = new SerializedObject(station);
                so.FindProperty("machineInstanceId").stringValue = MachineIds[i];
                so.FindProperty("vialSocket").objectReferenceValue = vialSocket.transform;
                so.FindProperty("printoutSocket").objectReferenceValue = traySocket.transform;
                so.FindProperty("statusLight").objectReferenceValue = statusLight.GetComponent<Renderer>();
                so.FindProperty("display").objectReferenceValue = display;
                so.ApplyModifiedPropertiesWithoutUndo();

                // Four housekeeping buttons in a row, ordered the way §5.2 and §5.3 are actually
                // used: flush, then read what the flush left, then check the instrument against a
                // certificate, then zero it. 0.08 m apart, which is wider than the 0.055 m button so
                // the interaction ray cannot pick a neighbour by a millimetre.
                AddActionButton(machineGo, "CleanButton", buttonMesh, palette,
                    new Vector3(-0.14f, 0.16f, front + 0.01f), station, MachineAction.Clean);
                AddActionButton(machineGo, "BlankButton", buttonMesh, palette,
                    new Vector3(-0.06f, 0.16f, front + 0.01f), station, MachineAction.Blank);
                AddActionButton(machineGo, "ReferenceButton", buttonMesh, palette,
                    new Vector3(0.02f, 0.16f, front + 0.01f), station, MachineAction.Reference);
                AddActionButton(machineGo, "CalibrateButton", buttonMesh, palette,
                    new Vector3(0.10f, 0.16f, front + 0.01f), station, MachineAction.Calibrate);

                // Its own manual, sitting beside it. §5.5: where you keep a reference matters.
                //
                // Parented to the Stations root, NOT to the machine. A child book is inside the
                // machine's GetComponentsInChildren, so the machine's collider bounds swallow it —
                // which makes targeting diagnostics lie and would make any bounds-based selection
                // pick the wrong object.
                var bookGo = AddBook(root, $"Manual_{MachineIds[i]}",
                    Vector3.zero, BookKind.MachineManual, MachineIds[i]);
                bookGo.transform.position = machineGo.transform.position +
                    new Vector3(visual.Size.x * 0.5f + 0.13f, 0.015f, 0f);
                books.Add(bookGo);
            }

            // Staging racks on the island, plus one beside the instruments so a finished vial can be
            // parked without walking away from the bench.
            var rackMesh = SaveMesh(ProcMesh.Box("Rack_Base", new Vector3(0.56f, 0.03f, 0.28f),
                PaletteUv.Family.Sump, 6));

            AddRack(root, scene, "rack_island_a", new Vector3(-0.8f, BenchHeight, -1.4f), palette, rackMesh, 8);
            AddRack(root, scene, "rack_island_b", new Vector3(0.8f, BenchHeight, -1.4f), palette, rackMesh, 8);
            AddRack(root, scene, "rack_bench", new Vector3(-3.85f, BenchHeight, benchZ), palette, rackMesh, 4);

            // Intake crate
            var crateGo = new GameObject("IntakeCrate");
            SceneManager.MoveGameObjectToScene(crateGo, scene);
            crateGo.transform.SetParent(root.transform, false);
            crateGo.transform.position = new Vector3(-RoomWidth * 0.5f + 1.1f, BenchHeight, 1.6f);

            var crateMesh = SaveMesh(ProcMesh.Box("Crate_Body", new Vector3(0.75f, 0.22f, 0.55f),
                PaletteUv.Family.Oxide, 6));
            AddChild(crateGo, "Body", crateMesh, palette, new Vector3(0f, 0.11f, 0f), addCollider: true);

            var slotRoot = new GameObject("Slots");
            slotRoot.transform.SetParent(crateGo.transform, false);
            slotRoot.transform.localPosition = new Vector3(0f, 0.24f, 0f);

            var crate = crateGo.AddComponent<IntakeCrate>();
            var crateSo = new SerializedObject(crate);
            crateSo.FindProperty("slotRoot").objectReferenceValue = slotRoot.transform;
            crateSo.ApplyModifiedPropertiesWithoutUndo();

            // Terminal
            var terminalGo = new GameObject("Terminal");
            SceneManager.MoveGameObjectToScene(terminalGo, scene);
            terminalGo.transform.SetParent(root.transform, false);
            terminalGo.transform.position = new Vector3(RoomWidth * 0.5f - 1.1f, BenchHeight, 1.6f);

            var monitorMesh = SaveMesh(ProcMesh.Box("Terminal_Monitor", new Vector3(0.5f, 0.34f, 0.05f),
                PaletteUv.Family.DeepBlue, 5));
            AddChild(terminalGo, "Monitor", monitorMesh, palette, new Vector3(0f, 0.3f, 0f), addCollider: true);

            var terminal = terminalGo.AddComponent<TerminalStation>();

            BuildBookRack(root, scene, palette, books);

            return (terminal, crate);
        }

        // -- Machine visuals ---------------------------------------------------------------------------

        /// <summary>Per-instrument proportions, so they are distinguishable by silhouette alone.</summary>
        private readonly struct MachineVisual
        {
            public readonly Vector3 Size;
            public readonly Vector2 ScreenSize;
            public readonly float ScreenY;
            public readonly bool Panel;
            public readonly int Dials;
            public readonly int Vents;

            public MachineVisual(Vector3 size, Vector2 screenSize, float screenY, bool panel, int dials, int vents)
            {
                Size = size; ScreenSize = screenSize; ScreenY = screenY;
                Panel = panel; Dials = dials; Vents = vents;
            }
        }

        private static MachineVisual VisualFor(string id) => id switch
        {
            // Tall, wide and busy. The cooling curve tester is the centrepiece and the expensive
            // option, and should read that way from across the room.
            "cooling_curve" => new MachineVisual(new Vector3(0.80f, 0.64f, 0.52f), new Vector2(0.42f, 0.26f), 0.44f, true, 3, 8),

            // A titrator is a small box with a two-line readout and a lot of knobs.
            "karl_fischer" => new MachineVisual(new Vector3(0.46f, 0.38f, 0.42f), new Vector2(0.22f, 0.10f), 0.28f, false, 3, 2),

            "viscometer" => new MachineVisual(new Vector3(0.44f, 0.52f, 0.40f), new Vector2(0.20f, 0.10f), 0.40f, false, 2, 5),

            "centrifuge" => new MachineVisual(new Vector3(0.50f, 0.40f, 0.50f), new Vector2(0.20f, 0.09f), 0.28f, false, 2, 3),

            "elemental" => new MachineVisual(new Vector3(0.64f, 0.44f, 0.48f), new Vector2(0.32f, 0.20f), 0.29f, true, 2, 4),

            _ => new MachineVisual(new Vector3(0.55f, 0.44f, 0.46f), new Vector2(0.26f, 0.14f), 0.30f, false, 2, 4)
        };

        /// <summary>
        /// One mesh for an instrument's chassis: body, screen bezel, dials, vent slats, output slot
        /// and feet. Pivot at base centre per §2.1, so the machine sits on whatever it is placed on.
        /// </summary>
        private static Mesh BuildMachineBody(string name, MachineVisual v)
        {
            var b = new ProcMesh.Builder();
            float front = v.Size.z * 0.5f;
            const float footHeight = 0.018f;

            float bodyHeight = v.Size.y - footHeight;
            b.Box(new Vector3(0f, footHeight + bodyHeight * 0.5f, 0f),
                new Vector3(v.Size.x, bodyHeight, v.Size.z), PaletteUv.Family.NeutralWarm, 7);

            // Feet lift it off the bench so it reads as equipment rather than a painted block.
            float fx = v.Size.x * 0.5f - 0.045f, fz = v.Size.z * 0.5f - 0.045f;
            foreach (var corner in new[]
                     {
                         new Vector2(-fx, -fz), new Vector2(fx, -fz),
                         new Vector2(-fx, fz), new Vector2(fx, fz)
                     })
            {
                b.Box(new Vector3(corner.x, footHeight * 0.5f, corner.y),
                    new Vector3(0.05f, footHeight, 0.05f), PaletteUv.Family.Sump, 4);
            }

            // Recessed bezel around the screen.
            float bw = v.ScreenSize.x + 0.035f, bh = v.ScreenSize.y + 0.035f;
            b.Box(new Vector3(0f, v.ScreenY, front - 0.004f), new Vector3(bw, bh, 0.012f),
                PaletteUv.Family.Sump, 2);

            // Dials along the lower front. Cylinders build along +Y, so these read as knobs on the
            // top lip rather than dials on the face — good enough for greybox, and a dial that
            // protrudes upward is still unmistakably a dial.
            for (int i = 0; i < v.Dials; i++)
            {
                float x = v.Size.x * 0.5f - 0.075f - i * 0.075f;
                b.Cylinder(new Vector3(x, v.Size.y - 0.002f, front - 0.06f), 0.024f, 0.016f, 12,
                    PaletteUv.Family.Brass, 9);
            }

            // Vent slats down one side.
            for (int i = 0; i < v.Vents; i++)
            {
                float y = footHeight + 0.05f + i * 0.028f;
                if (y > v.Size.y - 0.05f) break;
                b.Box(new Vector3(-v.Size.x * 0.5f + 0.0035f, y, 0f),
                    new Vector3(0.007f, 0.012f, v.Size.z * 0.55f), PaletteUv.Family.Sump, 3);
            }

            // Output slot the printout emerges from.
            b.Box(new Vector3(0f, 0.075f, front - 0.006f), new Vector3(0.20f, 0.016f, 0.014f),
                PaletteUv.Family.Sump, 1);

            // Top-loading port for the vial.
            b.Cylinder(new Vector3(0f, v.Size.y - 0.006f, -0.06f), 0.032f, 0.008f, 12,
                PaletteUv.Family.Sump, 2);

            return b.ToMesh(name);
        }

        // -- Reference case ----------------------------------------------------------------------------

        /// <summary>
        /// The case the general manuals live in, against the wall beside the terminal desk.
        /// <para>
        /// Keeping references physical rather than making them a terminal tab means looking something
        /// up costs the walk and the shift time — §6.1 assumes reading is expensive. A case rather
        /// than the flat plank this replaces, because a manual now has to be <i>returnable</i>: an
        /// empty pigeonhole in a row of full ones is what tells a player holding a book where it
        /// goes, and a plank with three books lying on it says nothing at all.
        /// </para>
        /// Instrument manuals are deliberately left beside their instruments (§5.5 — where you keep a
        /// reference matters); this is where the ones that belong to no machine start, and where any
        /// of them can be put back.
        /// </summary>
        private static void BuildBookRack(GameObject root, Scene scene, Material palette,
                                          List<ReferenceBook> books)
        {
            // Cell geometry. These four numbers and BookRack.SlotOffset describe the same holes from
            // two sides, so they are commented together: cells are ColumnSpacing apart across and
            // RowSpacing apart up, and slotRoot sits at the middle of the bottom cell.
            const float width = 0.86f;
            const float depth = 0.28f;
            const float panel = 0.02f;
            const float plinthHeight = 0.55f;
            const float rowPitch = 0.20f;   // BookRack.RowSpacing
            const int rows = 4;

            float openTop = plinthHeight + rows * rowPitch;              // 1.35
            float openMid = (plinthHeight + openTop) * 0.5f;             // 0.95
            float openHeight = openTop - plinthHeight + panel * 2f;      // 0.84

            var caseBuilder = new ProcMesh.Builder()
                // A closed base, so the whole thing reads as a piece of lab furniture standing on the
                // floor rather than a plank hanging in the air.
                .Box(new Vector3(0f, plinthHeight * 0.5f, 0f), new Vector3(width, plinthHeight, depth),
                    PaletteUv.Family.Steel, 5)
                .Box(new Vector3(0f, plinthHeight + 0.02f, depth * 0.5f + 0.003f),
                    new Vector3(0.42f, 0.09f, 0.006f), PaletteUv.Family.NeutralWarm, 13)
                // Sides, centre divider and back. Two columns of cells.
                .Box(new Vector3(-(width - panel) * 0.5f, openMid, 0f),
                    new Vector3(panel, openHeight, depth), PaletteUv.Family.Steel, 6)
                .Box(new Vector3((width - panel) * 0.5f, openMid, 0f),
                    new Vector3(panel, openHeight, depth), PaletteUv.Family.Steel, 6)
                .Box(new Vector3(0f, openMid, 0f), new Vector3(panel, openHeight, depth),
                    PaletteUv.Family.Steel, 6)
                .Box(new Vector3(0f, openMid, -(depth - panel) * 0.5f),
                    new Vector3(width, openHeight, panel), PaletteUv.Family.Sump, 4);

            for (int row = 0; row <= rows; row++)
            {
                caseBuilder.Box(new Vector3(0f, plinthHeight + row * rowPitch, 0f),
                    new Vector3(width, panel, depth), PaletteUv.Family.Steel, 8);
            }

            var rackGo = new GameObject("BookRack");
            SceneManager.MoveGameObjectToScene(rackGo, scene);
            rackGo.transform.SetParent(root.transform, false);

            // Backed onto the far wall a step from the terminal desk, open face into the room. Close
            // enough that checking a threshold and typing it in are one walk, far enough that it is
            // still a walk.
            rackGo.transform.position = new Vector3(RoomWidth * 0.5f - 0.16f, 0f, 0.5f);
            rackGo.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

            AddChild(rackGo, "Case", SaveMesh(caseBuilder.ToMesh("Lab_BookRack")), palette,
                Vector3.zero, addCollider: true);

            var slotRoot = new GameObject("Slots");
            slotRoot.transform.SetParent(rackGo.transform, false);
            slotRoot.transform.localPosition =
                new Vector3(0f, plinthHeight + rowPitch * 0.5f, panel * 0.25f);

            // The cells are authored rather than built on the first frame of play, so the saved scene
            // shows the manuals in the holes they live in. BookRack adopts these by name.
            var cells = new Transform[BookRack.SlotCount];
            for (int i = 0; i < BookRack.SlotCount; i++)
            {
                var cell = new GameObject($"Slot_{i:D2}");
                cell.transform.SetParent(slotRoot.transform, false);
                cell.transform.localPosition = BookRack.SlotOffset(i);
                cell.transform.localRotation = Quaternion.Euler(BookRack.SlotTilt);
                cells[i] = cell.transform;
            }

            var rack = rackGo.AddComponent<BookRack>();
            var so = new SerializedObject(rack);
            so.FindProperty("rackId").stringValue = BookRack.FixtureId;
            so.FindProperty("slotRoot").objectReferenceValue = slotRoot.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            // At eye level, and not filling the case: the empty cells are what say a manual is out.
            books.Add(AddBook(cells[4].gameObject, "Elements", Vector3.zero, BookKind.ElementIndex, null));
            books.Add(AddBook(cells[5].gameObject, "Diagnostics", Vector3.zero, BookKind.DiagnosticGuide, null));
            books.Add(AddBook(cells[6].gameObject, "Thresholds", Vector3.zero, BookKind.ThresholdTables, null));
        }

        private static ReferenceBook AddBook(GameObject parent, string name,
                                             Vector3 localPosition, BookKind kind, string machineId)
        {
            var go = new GameObject($"Book_{name}");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPosition;
            var collider = go.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.014f, 0f);
            collider.size = new Vector3(0.36f, 0.028f, 0.24f);
            var book = go.AddComponent<ReferenceBook>();

            var so = new SerializedObject(book);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("machineId").stringValue = machineId ?? string.Empty;
            so.ApplyModifiedPropertiesWithoutUndo();
            return book;
        }

        private static void AddRack(GameObject root, Scene scene, string rackId, Vector3 position,
                                    Material palette, Mesh rackMesh, int slotCount)
        {
            var go = new GameObject($"Rack_{rackId}");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(root.transform, false);
            go.transform.position = position;

            AddChild(go, "Base", rackMesh, palette, new Vector3(0f, 0.015f, 0f), addCollider: true);

            var slotRoot = new GameObject("Slots");
            slotRoot.transform.SetParent(go.transform, false);
            slotRoot.transform.localPosition = new Vector3(0f, 0.03f, 0f);

            var rack = go.AddComponent<SampleRack>();
            var so = new SerializedObject(rack);
            so.FindProperty("rackId").stringValue = rackId;
            so.FindProperty("slotRoot").objectReferenceValue = slotRoot.transform;
            so.FindProperty("slotCount").intValue = slotCount;
            so.FindProperty("columns").intValue = Mathf.Min(4, slotCount);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddActionButton(GameObject parent, string name, Mesh mesh, Material palette,
                                            Vector3 localPosition, MachineStation station, MachineAction action)
        {
            var go = AddChild(parent, name, mesh, palette, localPosition, addCollider: true);
            var button = go.AddComponent<MachineActionButton>();

            var so = new SerializedObject(button);
            so.FindProperty("station").objectReferenceValue = station;
            so.FindProperty("action").enumValueIndex = (int)action;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // -- Player ------------------------------------------------------------------------------------

        private static (PlayerController controller, TerminalScreen terminalScreen) BuildPlayer(
            Scene scene, Material palette,
            UnityEngine.InputSystem.InputActionAsset inputAsset,
            PanelSettings panelSettings)
        {
            var go = new GameObject("Player");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(0f, 0.05f, 2.6f);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            var controllerComponent = go.AddComponent<CharacterController>();
            controllerComponent.height = 1.8f;
            controllerComponent.radius = 0.3f;
            controllerComponent.center = new Vector3(0f, 0.9f, 0f);

            // Three transforms, three owners, no contention: PlayerController writes eye height and
            // pitch to Head, PlayerHeadMotion writes bob and landing dip to CameraRig, and the
            // cameras just hang there. Collapsing any two of these into one transform means two
            // systems assigning localPosition every frame and the loser looking like jitter.
            var headGo = new GameObject("Head");
            headGo.transform.SetParent(go.transform, false);
            headGo.transform.localPosition = new Vector3(0f, 1.7f, 0f);

            var rigGo = new GameObject("CameraRig");
            rigGo.transform.SetParent(headGo.transform, false);

            var cameraGo = new GameObject("EyeCamera");
            cameraGo.transform.SetParent(rigGo.transform, false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 60f;
            camera.fieldOfView = 70f;
            cameraGo.AddComponent<AudioListener>();
            cameraGo.tag = "MainCamera";

            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;

            var carry = new GameObject("CarrySocket");
            carry.transform.SetParent(rigGo.transform, false);
            carry.transform.localPosition = new Vector3(0.22f, -0.18f, 0.42f);

            var player = go.AddComponent<PlayerController>();
            var playerSo = new SerializedObject(player);
            playerSo.FindProperty("inputAsset").objectReferenceValue = inputAsset;
            playerSo.FindProperty("eyeCamera").objectReferenceValue = camera;
            playerSo.FindProperty("head").objectReferenceValue = headGo.transform;
            playerSo.ApplyModifiedPropertiesWithoutUndo();

            var headMotion = go.AddComponent<PlayerHeadMotion>();
            var motionSo = new SerializedObject(headMotion);
            motionSo.FindProperty("player").objectReferenceValue = player;
            motionSo.FindProperty("rig").objectReferenceValue = rigGo.transform;
            motionSo.FindProperty("eyeCamera").objectReferenceValue = camera;
            motionSo.ApplyModifiedPropertiesWithoutUndo();

            var interactor = go.AddComponent<PlayerInteractor>();
            var interactorSo = new SerializedObject(interactor);
            interactorSo.FindProperty("player").objectReferenceValue = player;
            interactorSo.FindProperty("carrySocket").objectReferenceValue = carry.transform;
            interactorSo.FindProperty("mask").intValue = ~(1 << PlayerInteractor.IgnoreRaycastLayer);
            interactorSo.ApplyModifiedPropertiesWithoutUndo();

            // The eye camera sits inside the CharacterController capsule, so without this the very
            // first thing the interaction ray hits is the player's own body about 0.12 m out, and
            // every real target beyond it is discarded. Nothing in the lab was ever selectable
            // except by accident.
            SetLayerRecursively(go, PlayerInteractor.IgnoreRaycastLayer);

            // Both of these are built after the layer sweep above, so each sets its own layer:
            // hands join the player on Ignore Raycast, the body goes on its own layer so the owner's
            // camera can cull it while everyone else's still sees it.
            var hands = BuildHands(rigGo, palette, player, interactor);
            SetLayerRecursively(hands, PlayerInteractor.IgnoreRaycastLayer);
            BuildCharacterBody(go, palette, player, interactor);

            var thirdPerson = go.AddComponent<ThirdPersonView>();
            var thirdSo = new SerializedObject(thirdPerson);
            thirdSo.FindProperty("player").objectReferenceValue = player;
            thirdSo.FindProperty("eyeCamera").objectReferenceValue = camera;
            thirdSo.FindProperty("hands").objectReferenceValue = hands;
            thirdSo.ApplyModifiedPropertiesWithoutUndo();

            var interactionDebug = go.AddComponent<InteractionDebug>();
            var debugSo = new SerializedObject(interactionDebug);
            debugSo.FindProperty("interactor").objectReferenceValue = interactor;
            debugSo.ApplyModifiedPropertiesWithoutUndo();

            // Player-local HUD and terminal. Reference books no longer create a separate screen:
            // their text is drawn on the physical pages during item inspection.
            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(go.transform, false);
            var hudDoc = hudGo.AddComponent<UIDocument>();
            SetPanelSettings(hudDoc, panelSettings, 0);
            var hud = hudGo.AddComponent<LabHud>();
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("interactor").objectReferenceValue = interactor;
            hudSo.FindProperty("interactionDebug").objectReferenceValue = interactionDebug;
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            // Terminal screen, on its own document so it can sit above the HUD.
            var screenGo = new GameObject("TerminalUI");
            screenGo.transform.SetParent(go.transform, false);
            var screenDoc = screenGo.AddComponent<UIDocument>();
            SetPanelSettings(screenDoc, panelSettings, 10);
            var screen = screenGo.AddComponent<TerminalScreen>();
            var screenSo = new SerializedObject(screen);
            screenSo.FindProperty("document").objectReferenceValue = screenDoc;
            screenSo.FindProperty("player").objectReferenceValue = player;
            screenSo.FindProperty("interactor").objectReferenceValue = interactor;
            screenSo.ApplyModifiedPropertiesWithoutUndo();

            AddNetworking(go, player, interactor, headMotion, hands, thirdPerson, interactionDebug,
                camera, controllerComponent);

            // One character, saved once and used twice: netcode spawns this prefab per client, and
            // the scene keeps an instance so single player is still "open Lab.unity and press Play".
            // Built from the same object rather than authored twice, so the two cannot drift.
            SavePlayerPrefab(go);
            StripNetworkingFromSceneCopy(go);
            go.AddComponent<SceneOnlyPlayer>();

            return (player, screen);
        }

        /// <summary>
        /// Attach the netcode half of a player: identity on the wire, an owner-driven transform, and
        /// the switch that decides which components belong on this machine.
        /// </summary>
        private static void AddNetworking(GameObject go, PlayerController player,
                                          PlayerInteractor interactor, PlayerHeadMotion headMotion,
                                          GameObject hands, ThirdPersonView thirdPerson,
                                          InteractionDebug interactionDebug, Camera camera,
                                          CharacterController motor)
        {
            go.AddComponent<NetworkObject>();
            go.AddComponent<OwnerNetworkTransform>();

            var avatar = go.AddComponent<PlayerAvatar>();
            var so = new SerializedObject(avatar);
            so.FindProperty("controller").objectReferenceValue = player;
            so.FindProperty("interactor").objectReferenceValue = interactor;
            so.FindProperty("headMotion").objectReferenceValue = headMotion;
            so.FindProperty("hands").objectReferenceValue = hands.GetComponent<PlayerHands>();
            so.FindProperty("thirdPerson").objectReferenceValue = thirdPerson;
            so.FindProperty("interactionDebug").objectReferenceValue = interactionDebug;
            so.FindProperty("eyeCamera").objectReferenceValue = camera;
            so.FindProperty("earsOfTheOwner").objectReferenceValue = camera.GetComponent<AudioListener>();
            so.FindProperty("motor").objectReferenceValue = motor;
            so.FindProperty("body").objectReferenceValue = go.GetComponentInChildren<CharacterBody>(true);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // -- Boot scene ---------------------------------------------------------------------------------

        /// <summary>
        /// The scene the game starts in: a NetworkManager, and a menu offering single player, host,
        /// or a join code.
        /// <para>
        /// Separate from the lab because the transport has to be configured <i>before</i> the lab
        /// loads — a client must know it is a client in time for <see cref="LabRuntime"/> not to
        /// build a lab of its own. Deciding that inside the scene it applies to is too late.
        /// </para>
        /// </summary>
        private static void BuildBootScene(PanelSettings panelSettings)
        {
            var boot = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            PopulateBootScene(boot, panelSettings);

            EditorSceneManager.MarkSceneDirty(boot);
            EditorSceneManager.SaveScene(boot, BootScenePath);
            EditorSceneManager.CloseScene(boot, removeScene: true);
        }

        /// <summary>
        /// Rebuild only the front door, leaving the lab alone.
        /// <para>
        /// The menu changes far more often than the room does, and regenerating the whole lab to move
        /// a button means re-saving every mesh, prefab and material the room is made of — a large
        /// diff, a long wait, and a chance to break something unrelated to the change being made.
        /// This is the same <see cref="PopulateBootScene"/> the full rebuild runs, so the two cannot
        /// drift.
        /// </para>
        /// Single rather than additive: the Boot scene is usually the one already open, and saving
        /// over a scene that is loaded is not something the scene manager will do.
        /// </summary>
        [MenuItem("Residue/Build/Rebuild Boot Scene", priority = 41)]
        public static void RebuildBootScene()
        {
            var open = SceneManager.GetActiveScene();
            if (open.isDirty)
            {
                Debug.LogError("[LabSceneBuilder] The open scene has unsaved changes. Save or " +
                               "discard them before rebuilding.");
                return;
            }

            EnsureFolders();

            var panelSettings = EnsurePanelSettings(EnsureRuntimeTheme());
            var boot = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            PopulateBootScene(boot, panelSettings);

            EditorSceneManager.MarkSceneDirty(boot);
            EditorSceneManager.SaveScene(boot, BootScenePath);
            RegisterScenesInBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LabSceneBuilder] Built {BootScenePath}. It is open now — press Play.");
        }

        private static void PopulateBootScene(Scene boot, PanelSettings panelSettings)
        {
            var managerGo = NewRoot(boot, "NetworkManager");
            var manager = managerGo.AddComponent<NetworkManager>();
            var transport = managerGo.AddComponent<UnityTransport>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var so = new SerializedObject(manager);

            // Approval is what carries the stable id: LabNetwork reads it out of the connection
            // payload, and rejoin is keyed on it. Off, every reconnection looks like a stranger.
            SetBool(so, "NetworkConfig.ConnectionApproval", true);
            SetBool(so, "NetworkConfig.EnableSceneManagement", true);
            SetRef(so, "NetworkConfig.NetworkTransport", transport);
            SetRef(so, "NetworkConfig.PlayerPrefab", prefab);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (prefab == null)
                Debug.LogError($"[LabSceneBuilder] {PlayerPrefabPath} is missing, so NetworkManager " +
                               "has no player to spawn and a client would join a lab with no body.");

            var connectGo = NewRoot(boot, "Connect");
            var connection = connectGo.AddComponent<LabConnection>();

            var doc = connectGo.AddComponent<UIDocument>();

            // Above the book screen (20), which is the highest thing the lab draws. The menu is the
            // only screen allowed to cover everything — including, now, the pause menu it also owns.
            SetPanelSettings(doc, panelSettings, 30);

            // This object is DontDestroyOnLoad (see LabConnection.Awake), so the menu it carries
            // survives into the lab. That is deliberate and it is why the lab scene needs no menu of
            // its own: the title screen, the lobby, the settings screen and the pause menu are all
            // pages of this one document, and the settings screen a player opens mid-shift is the
            // same instance they opened from the title.
            var screen = connectGo.AddComponent<MenuScreen>();
            var screenSo = new SerializedObject(screen);
            screenSo.FindProperty("document").objectReferenceValue = doc;
            screenSo.FindProperty("connection").objectReferenceValue = connection;

            // The rebind screen needs the asset the bindings actually live on — the same instance
            // PlayerController binds against, so an override applied here is live in the lab without
            // a reload. There is no player in the Boot scene to borrow it from.
            var inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(
                InputActionsPath);

            if (inputAsset == null)
                Debug.LogError($"[LabSceneBuilder] {InputActionsPath} is missing, so the settings " +
                               "screen has no bindings to offer and rebinding will be unavailable.");

            screenSo.FindProperty("inputAsset").objectReferenceValue = inputAsset;
            screenSo.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Serialized-property paths into a third-party component are resolved by string, so a rename
        /// in a package upgrade turns into a silent no-op — a NetworkManager quietly missing its
        /// approval flag looks exactly like a rejoin bug. Say so instead.
        /// </summary>
        private static SerializedProperty Required(SerializedObject so, string path)
        {
            var property = so.FindProperty(path);
            if (property == null)
                Debug.LogError($"[LabSceneBuilder] '{path}' no longer exists on " +
                               $"{so.targetObject.GetType().Name}. The netcode setup is incomplete.");
            return property;
        }

        private static void SetBool(SerializedObject so, string path, bool value)
        {
            var p = Required(so, path);
            if (p != null) p.boolValue = value;
        }

        private static void SetRef(SerializedObject so, string path, Object value)
        {
            var p = Required(so, path);
            if (p != null) p.objectReferenceValue = value;
        }

        /// <summary>
        /// Boot first, then the lab. Netcode loads the lab by name over the network, and a scene that
        /// is not in this list simply does not load in a build — which surfaces as a client that
        /// connects and then sits on a black screen.
        /// </summary>
        private static void RegisterScenesInBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }

        /// <summary>
        /// Take the netcode off the copy that stays in the scene. The prefab keeps it; this one is
        /// the single-player body and must be invisible to NGO.
        /// <para>
        /// <b>This is not tidiness.</b> A <c>NetworkObject</c> sitting in a scene is a <i>scene</i>
        /// NetworkObject: when a client synchronises, NGO enumerates every one the scene declares and
        /// expects to match it. <see cref="SceneOnlyPlayer"/> destroys this object in <c>Awake</c>,
        /// which runs while that synchronisation is still in flight — so the client is left
        /// reconciling against an object that has just evaporated. Synchronisation does not complete,
        /// <c>LabNetwork</c> never spawns, <c>LabView.Replicated</c> is never installed, and every
        /// station, screen and crate in the room finds nothing to read.
        /// </para>
        /// The symptom is total and gives no clue where to look: a client walks into a fully built
        /// lab where nothing responds, while the host is perfectly fine.
        /// </summary>
        private static void StripNetworkingFromSceneCopy(GameObject go)
        {
            // PlayerAvatar and OwnerNetworkTransform both require the NetworkObject, so it goes last.
            DestroyIfPresent(go.GetComponent<PlayerAvatar>());
            DestroyIfPresent(go.GetComponent<OwnerNetworkTransform>());
            DestroyIfPresent(go.GetComponent<NetworkObject>());
        }

        private static void DestroyIfPresent(Component component)
        {
            if (component != null) Object.DestroyImmediate(component, allowDestroyingAssets: false);
        }

        private static void SavePlayerPrefab(GameObject go)
        {
            PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefabPath, out bool saved);

            if (!saved)
                Debug.LogError($"[LabSceneBuilder] Could not save {PlayerPrefabPath}. Netcode has no " +
                               "player to spawn, so a client would connect to a lab with no body.");
        }

        // -- Character ---------------------------------------------------------------------------------

        /// <summary>
        /// Forearm and palm per side, hung off the camera rig so head bob carries into them for free.
        /// Deliberately not on their own overlay camera: that needs URP camera stacking, which means
        /// a URP reference in three asmdefs. Tracked separately; a 0.05 m near clip is enough until
        /// someone presses their face into a wall.
        /// </summary>
        private static GameObject BuildHands(GameObject rig, Material palette,
                                             PlayerController player, PlayerInteractor interactor)
        {
            var root = new GameObject("Hands");
            root.transform.SetParent(rig.transform, false);

            var handMesh = SaveMesh(new ProcMesh.Builder()
                .Box(Vector3.zero, new Vector3(0.085f, 0.055f, 0.11f), PaletteUv.Family.NeutralWarm, 9)
                .Box(new Vector3(0f, 0f, -0.16f), new Vector3(0.072f, 0.072f, 0.20f),
                    PaletteUv.Family.NeutralCold, 6)
                .ToMesh("Player_Hand"));

            var left = AddChild(root, "HandL", handMesh, palette, Vector3.zero, addCollider: false);
            var right = AddChild(root, "HandR", handMesh, palette, Vector3.zero, addCollider: false);

            var hands = root.AddComponent<PlayerHands>();
            var so = new SerializedObject(hands);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("interactor").objectReferenceValue = interactor;
            so.FindProperty("leftHand").objectReferenceValue = left.transform;
            so.FindProperty("rightHand").objectReferenceValue = right.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        /// <summary>
        /// The segmented figure other players see. Every segment is a pivot GameObject with the box
        /// offset inside it, because <see cref="CharacterBody"/> rotates the pivot — a box centred on
        /// its own pivot would spin in place instead of swinging from the joint.
        /// </summary>
        private static void BuildCharacterBody(GameObject playerGo, Material palette,
                                               PlayerController player, PlayerInteractor interactor)
        {
            var root = new GameObject("Body");
            root.transform.SetParent(playerGo.transform, false);

            var pelvis = Joint(root, "Pelvis", new Vector3(0f, 0.92f, 0f));
            Segment(pelvis, "Pelvis_Mesh", palette, Vector3.zero, new Vector3(0.30f, 0.17f, 0.20f),
                PaletteUv.Family.DeepBlue, 5);

            var torso = Joint(pelvis, "Torso", new Vector3(0f, 0.09f, 0f));
            Segment(torso, "Torso_Mesh", palette, new Vector3(0f, 0.23f, 0f),
                new Vector3(0.36f, 0.46f, 0.22f), PaletteUv.Family.NeutralWarm, 8);

            var neck = Joint(torso, "Neck", new Vector3(0f, 0.50f, 0f));
            Segment(neck, "Head_Mesh", palette, new Vector3(0f, 0.11f, 0f),
                new Vector3(0.20f, 0.22f, 0.21f), PaletteUv.Family.NeutralWarm, 10);

            var (upperArmL, lowerArmL) = Limb(torso, "L", new Vector3(-0.23f, 0.42f, 0f),
                palette, 0.28f, 0.26f, new Vector3(0.10f, 0.28f, 0.10f), new Vector3(0.085f, 0.26f, 0.085f),
                PaletteUv.Family.NeutralWarm, PaletteUv.Family.NeutralWarm);
            var (upperArmR, lowerArmR) = Limb(torso, "R", new Vector3(0.23f, 0.42f, 0f),
                palette, 0.28f, 0.26f, new Vector3(0.10f, 0.28f, 0.10f), new Vector3(0.085f, 0.26f, 0.085f),
                PaletteUv.Family.NeutralWarm, PaletteUv.Family.NeutralWarm);

            var (upperLegL, lowerLegL) = Limb(pelvis, "LegL", new Vector3(-0.10f, -0.08f, 0f),
                palette, 0.42f, 0.40f, new Vector3(0.13f, 0.42f, 0.13f), new Vector3(0.11f, 0.40f, 0.11f),
                PaletteUv.Family.Sump, PaletteUv.Family.Sump);
            var (upperLegR, lowerLegR) = Limb(pelvis, "LegR", new Vector3(0.10f, -0.08f, 0f),
                palette, 0.42f, 0.40f, new Vector3(0.13f, 0.42f, 0.13f), new Vector3(0.11f, 0.40f, 0.11f),
                PaletteUv.Family.Sump, PaletteUv.Family.Sump);

            Segment(lowerLegL, "FootL", palette, new Vector3(0f, -0.365f, 0.05f),
                new Vector3(0.12f, 0.07f, 0.24f), PaletteUv.Family.Sump, 2);
            Segment(lowerLegR, "FootR", palette, new Vector3(0f, -0.365f, 0.05f),
                new Vector3(0.12f, 0.07f, 0.24f), PaletteUv.Family.Sump, 2);

            var body = root.AddComponent<CharacterBody>();
            var so = new SerializedObject(body);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("interactor").objectReferenceValue = interactor;
            so.FindProperty("pelvis").objectReferenceValue = pelvis.transform;
            so.FindProperty("torso").objectReferenceValue = torso.transform;
            so.FindProperty("neck").objectReferenceValue = neck.transform;
            so.FindProperty("upperArmL").objectReferenceValue = upperArmL.transform;
            so.FindProperty("lowerArmL").objectReferenceValue = lowerArmL.transform;
            so.FindProperty("upperArmR").objectReferenceValue = upperArmR.transform;
            so.FindProperty("lowerArmR").objectReferenceValue = lowerArmR.transform;
            so.FindProperty("upperLegL").objectReferenceValue = upperLegL.transform;
            so.FindProperty("lowerLegL").objectReferenceValue = lowerLegL.transform;
            so.FindProperty("upperLegR").objectReferenceValue = upperLegR.transform;
            so.FindProperty("lowerLegR").objectReferenceValue = lowerLegR.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Culled from the owner's own eye camera by ThirdPersonView. No colliders anywhere in
            // here, so the layer is purely a rendering concern and the interaction ray is unaffected.
            SetLayerRecursively(root, ThirdPersonView.PlayerBodyLayer);
        }

        private static GameObject Joint(GameObject parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPosition;
            return go;
        }

        private static void Segment(GameObject parent, string name, Material palette, Vector3 offset,
                                    Vector3 size, PaletteUv.Family family, int step)
        {
            var mesh = SaveMesh(new ProcMesh.Builder()
                .Box(Vector3.zero, size, family, step)
                .ToMesh($"Body_{name}"));
            AddChild(parent, name, mesh, palette, offset, addCollider: false);
        }

        private static (GameObject upper, GameObject lower) Limb(
            GameObject parent, string suffix, Vector3 socket, Material palette,
            float upperLength, float lowerLength, Vector3 upperSize, Vector3 lowerSize,
            PaletteUv.Family upperFamily, PaletteUv.Family lowerFamily)
        {
            var upper = Joint(parent, $"Upper{suffix}", socket);
            Segment(upper, $"Upper{suffix}_Mesh", palette, new Vector3(0f, -upperLength * 0.5f, 0f),
                upperSize, upperFamily, 6);

            var lower = Joint(upper, $"Lower{suffix}", new Vector3(0f, -upperLength, 0f));
            Segment(lower, $"Lower{suffix}_Mesh", palette, new Vector3(0f, -lowerLength * 0.5f, 0f),
                lowerSize, lowerFamily, 4);

            return (upper, lower);
        }

        private static void WireTerminal(TerminalStation station, TerminalScreen screen)
        {
            var so = new SerializedObject(station);
            so.FindProperty("screen").objectReferenceValue = screen;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetPanelSettings(UIDocument document, PanelSettings settings, float sortOrder)
        {
            var so = new SerializedObject(document);
            so.FindProperty("m_PanelSettings").objectReferenceValue = settings;
            var order = so.FindProperty("m_SortingOrder");
            if (order != null) order.floatValue = sortOrder;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // -- Assets ------------------------------------------------------------------------------------

        private static VialProp BuildVialPrefab(Material palette)
        {
            string path = $"{PrefabFolder}/Vial.prefab";

            var go = new GameObject("Vial");

            var glassMesh = SaveMesh(ProcMesh.Cylinder("Vial_Glass", 0.017f, 0.082f, 12,
                PaletteUv.Family.Solvent, 12));

            // Full-height fluid column: VialProp scales this on Y, so an almost-spent sample reads
            // as spent from across the room without opening a screen.
            var fluidMesh = SaveMesh(ProcMesh.Cylinder("Vial_Fluid", 0.0138f, 0.070f, 12,
                PaletteUv.Family.Oxide, 4));

            var capMesh = SaveMesh(ProcMesh.Cylinder("Vial_Cap", 0.0195f, 0.014f, 12,
                PaletteUv.Family.DeepBlue, 5));
            var labelMesh = SaveMesh(ProcMesh.Box("Vial_Label", new Vector3(0.024f, 0.030f, 0.002f),
                PaletteUv.Family.NeutralWarm, 13));

            AddChild(go, "Glass", glassMesh, palette, Vector3.zero, addCollider: false);
            var fluid = AddChild(go, "Fluid", fluidMesh, palette, new Vector3(0f, 0.005f, 0f), addCollider: false);
            AddChild(go, "Cap", capMesh, palette, new Vector3(0f, 0.080f, 0f), addCollider: false);
            AddChild(go, "Label", labelMesh, palette, new Vector3(0f, 0.036f, 0.0172f), addCollider: false);

            var collider = go.AddComponent<CapsuleCollider>();
            collider.radius = 0.025f;
            collider.height = 0.1f;
            collider.center = new Vector3(0f, 0.045f, 0f);

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var vial = go.AddComponent<VialProp>();
            var so = new SerializedObject(vial);
            so.FindProperty("fluidRenderer").objectReferenceValue = fluid.GetComponent<Renderer>();
            so.FindProperty("fluidTransform").objectReferenceValue = fluid.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<VialProp>();
        }

        /// <summary>
        /// The solvent bottle you fill at the wash station and carry to an instrument (§5.2, #14).
        /// <para>
        /// Deliberately much larger than a vial. It has to read as "the thing in your hands" from
        /// across the room, and it has to be obvious at a glance that carrying it means you are not
        /// carrying a sample — that trade is the mechanic §2.6 is protecting.
        /// </para>
        /// The fluid column is full height and scaled on Y per charge, the same way a vial shows how
        /// much sample is left, so how many flushes you have is legible without opening anything.
        /// </summary>
        private static SolventBottle BuildSolventBottlePrefab(Material palette)
        {
            string path = $"{PrefabFolder}/SolventBottle.prefab";

            var go = new GameObject("SolventBottle");

            var bodyMesh = SaveMesh(ProcMesh.Cylinder("Solvent_Body", 0.035f, 0.20f, 14,
                PaletteUv.Family.Solvent, 12));
            var fluidMesh = SaveMesh(ProcMesh.Cylinder("Solvent_Fluid", 0.030f, 0.175f, 14,
                PaletteUv.Family.Solvent, 9));
            var capMesh = SaveMesh(ProcMesh.Cylinder("Solvent_Cap", 0.020f, 0.030f, 12,
                PaletteUv.Family.Steel, 5));
            var handleMesh = SaveMesh(ProcMesh.Box("Solvent_Handle", new Vector3(0.012f, 0.055f, 0.012f),
                PaletteUv.Family.Steel, 6));

            AddChild(go, "Body", bodyMesh, palette, Vector3.zero, addCollider: false);
            var fluid = AddChild(go, "Fluid", fluidMesh, palette, new Vector3(0f, 0.008f, 0f), addCollider: false);
            AddChild(go, "Cap", capMesh, palette, new Vector3(0f, 0.200f, 0f), addCollider: false);
            AddChild(go, "Handle", handleMesh, palette, new Vector3(0f, 0.170f, 0.040f), addCollider: false);

            var collider = go.AddComponent<CapsuleCollider>();
            collider.radius = 0.045f;
            collider.height = 0.23f;
            collider.center = new Vector3(0f, 0.11f, 0f);

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var bottle = go.AddComponent<SolventBottle>();
            var so = new SerializedObject(bottle);
            so.FindProperty("fluidRenderer").objectReferenceValue = fluid.GetComponent<Renderer>();
            so.FindProperty("fluidTransform").objectReferenceValue = fluid.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<SolventBottle>();
        }

        /// <summary>
        /// The wash station: a drum, a valve, and cradles for the bottles.
        /// <para>
        /// Placed deliberately far from the instrument bench — on the opposite wall, past the island.
        /// The distance <i>is</i> the mechanic (#14). §5.5 makes lab layout the skill ceiling, and a
        /// cost you pay standing still is not a layout cost at all; putting the drum next to the
        /// machines would give the fixture without the decision.
        /// </para>
        /// Two colliders on purpose: the drum is a tap (stow a bottle), the valve is a hold (fill
        /// one). <see cref="Interactable.HoldSeconds"/> belongs to whichever you are looking at.
        /// </summary>
        private static void BuildWashStation(GameObject root, Material palette)
        {
            var stationGo = new GameObject("WashStation");
            stationGo.transform.SetParent(root.transform, false);
            stationGo.transform.position = new Vector3(-RoomWidth * 0.5f + 0.9f, 0f, -1.2f);
            stationGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var basinMesh = SaveMesh(new ProcMesh.Builder()
                .Box(new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.6f), PaletteUv.Family.Steel, 5)
                .Box(new Vector3(0f, 0.92f, 0f), new Vector3(0.82f, 0.06f, 0.54f), PaletteUv.Family.Sump, 3)
                .Box(new Vector3(0.30f, 1.25f, -0.12f), new Vector3(0.26f, 0.60f, 0.26f),
                    PaletteUv.Family.Solvent, 11)
                .ToMesh("Lab_WashStation"));

            AddStatic(stationGo, "Basin", basinMesh, palette, Vector3.zero, addCollider: true);

            // Bottles stand along the lip. WashStation builds one cradle per SolventStore.BottleCount
            // from this root, so the scene never hard-codes a count that could disagree with balance.
            var cradleRoot = new GameObject("Cradles");
            cradleRoot.transform.SetParent(stationGo.transform, false);
            cradleRoot.transform.localPosition = new Vector3(-0.18f, 0.95f, 0f);

            var station = stationGo.AddComponent<WashStation>();
            var so = new SerializedObject(station);
            so.FindProperty("cradleRoot").objectReferenceValue = cradleRoot.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            var valveMesh = SaveMesh(new ProcMesh.Builder()
                .Box(Vector3.zero, new Vector3(0.07f, 0.05f, 0.05f), PaletteUv.Family.Brass, 8)
                .Box(new Vector3(0f, -0.06f, 0f), new Vector3(0.02f, 0.08f, 0.02f), PaletteUv.Family.Steel, 4)
                .ToMesh("Lab_SolventValve"));

            var valveGo = AddChild(stationGo, "SolventValve", valveMesh, palette,
                new Vector3(0.30f, 1.05f, 0.02f), addCollider: true);
            valveGo.AddComponent<SolventValve>();
        }

        /// <summary>A results slip: a sheet with a printed header band, thin enough to read as paper.</summary>
        private static PrintoutProp BuildPrintoutPrefab(Material palette)
        {
            string path = $"{PrefabFolder}/Printout.prefab";
            var go = new GameObject("Printout");

            var sheetMesh = SaveMesh(ProcMesh.Box("Printout_Sheet", new Vector3(0.105f, 0.0015f, 0.145f),
                PaletteUv.Family.NeutralWarm, 14));
            var bandMesh = SaveMesh(ProcMesh.Box("Printout_Band", new Vector3(0.105f, 0.0018f, 0.020f),
                PaletteUv.Family.Sump, 5));

            var sheet = AddChild(go, "Sheet", sheetMesh, palette, Vector3.zero, addCollider: false);
            AddChild(go, "Band", bandMesh, palette, new Vector3(0f, 0.0004f, 0.058f), addCollider: false);

            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.11f, 0.02f, 0.15f);

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var printout = go.AddComponent<PrintoutProp>();
            var so = new SerializedObject(printout);
            so.FindProperty("paper").objectReferenceValue = sheet.GetComponent<MeshRenderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<PrintoutProp>();
        }

        /// <summary>
        /// UI Toolkit runtime panels need a theme, and the Editor only generates one when you use
        /// the Create menu. The generated file is a single import directive, so writing it directly
        /// is equivalent and keeps this reproducible.
        /// </summary>
        private static ThemeStyleSheet EnsureRuntimeTheme()
        {
            const string path = UiFolder + "/UnityDefaultRuntimeTheme.tss";
            var existing = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
            if (existing != null) return existing;

            File.WriteAllText(Path.GetFullPath(path), "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
        }

        private static PanelSettings EnsurePanelSettings(ThemeStyleSheet theme)
        {
            const string path = UiFolder + "/LabPanelSettings.asset";

            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                settings.name = "LabPanelSettings";
                AssetDatabase.CreateAsset(settings, path);
            }

            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        /// <summary>
        /// Instrument screens get their own material rather than a palette one.
        /// <para>
        /// A screen is the single place the §2.1 no-textures rule does not apply, because the
        /// texture IS the readout. Sharing the palette material also meant an unlit editor showed
        /// the whole 16x16 atlas on every instrument, which looks like a bug.
        /// </para>
        /// </summary>
        private static Material EnsureScreenMaterial()
        {
            const string path = "Assets/Art/Materials/M_Screen.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterial);

            if (material == null)
            {
                material = new Material(shader) { name = "M_Screen" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", new Color(0.04f, 0.06f, 0.07f));
            material.SetTexture("_BaseMap", null);
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_Metallic", 0f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.10f, 0.18f, 0.18f));
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// A restrained grade: clinical rather than cinematic. Only instrument emissives bloom;
        /// colour is cool and slightly drained; the vignette is just enough to remove the template
        /// render's weightless edges. The profile deliberately omits blur, depth of field, grain and
        /// chromatic aberration so labels and oil readings remain crisp (§2.4).
        /// </summary>
        private static VolumeProfile EnsureLabVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "LabVolumeProfile";
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            foreach (var component in new List<VolumeComponent>(profile.components))
                Object.DestroyImmediate(component, true);
            profile.components.Clear();

            LabPresentation.ConfigureProfile(profile);
            foreach (var component in profile.components)
                AssetDatabase.AddObjectToAsset(component, profile);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static Mesh SaveMesh(Mesh mesh)
        {
            string path = $"{MeshFolder}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                // Update in place so the GUID survives and existing references keep working.
                existing.Clear();
                existing.SetVertices(mesh.vertices);
                existing.SetNormals(mesh.normals);
                existing.SetUVs(0, mesh.uv);
                existing.SetTriangles(mesh.triangles, 0);
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // -- Helpers -----------------------------------------------------------------------------------

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        private static GameObject NewRoot(Scene scene, string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static GameObject AddStatic(GameObject parent, string name, Mesh mesh, Material palette,
                                            Vector3 localPosition, bool addCollider)
        {
            var go = AddChild(parent, name, mesh, palette, localPosition, addCollider);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            return go;
        }

        private static GameObject AddChild(GameObject parent, string name, Mesh mesh, Material palette,
                                           Vector3 localPosition, bool addCollider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPosition;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = palette;

            if (addCollider) go.AddComponent<MeshCollider>();
            return go;
        }

        /// <summary>
        /// Names a user layer in TagManager if it is free. Refuses to overwrite an existing name —
        /// silently stealing a layer someone else is using would move their objects out from under
        /// whatever culling mask or physics query depends on them.
        /// </summary>
        private static void EnsureLayer(int index, string name)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            if (layers == null || index < 0 || index >= layers.arraySize) return;

            var entry = layers.GetArrayElementAtIndex(index);
            if (entry.stringValue == name) return;

            if (!string.IsNullOrEmpty(entry.stringValue))
            {
                Debug.LogWarning($"[LabSceneBuilder] Layer {index} is already named " +
                                 $"'{entry.stringValue}'; wanted '{name}'. Leaving it alone.");
                return;
            }

            entry.stringValue = name;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolders()
        {
            foreach (string folder in new[] { "Assets/Scenes", MeshFolder, PrefabFolder, UiFolder, "Assets/Art/Generated" })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;

                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                string leaf = Path.GetFileName(folder);
                if (!AssetDatabase.IsValidFolder(parent))
                {
                    string grandparent = Path.GetDirectoryName(parent).Replace('\\', '/');
                    AssetDatabase.CreateFolder(grandparent, Path.GetFileName(parent));
                }
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }
    }
}
