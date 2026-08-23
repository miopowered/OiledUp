using System.IO;
using Residue.Data;
using Residue.Editor.Art;
using Residue.Editor.Content;
using Residue.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
    /// <b>Precondition:</b> a <i>saved</i> scene must be open, and the Editor must not be in play
    /// mode. Unity refuses <c>NewScene(Additive)</c> while an untitled unsaved scene is open, and
    /// additive is what keeps this from raising a save-changes modal that would hang MCP. If you
    /// need to clear the hierarchy first, open another saved scene rather than an empty one.
    /// </para>
    /// </summary>
    public static class LabSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Lab.unity";
        private const string MeshFolder = "Assets/Art/Generated";
        private const string PrefabFolder = "Assets/Prefabs";
        private const string UiFolder = "Assets/UI";
        private const string PaletteMaterial = "Assets/Art/Materials/M_Palette_Opaque.mat";
        private const string CatalogPath = "Assets/Data/ContentCatalog.asset";

        // Room is laid out on the §5.5 half-metre grid.
        private const float RoomWidth = 10f;
        private const float RoomDepth = 8f;
        private const float RoomHeight = 3f;
        private const float BenchHeight = 0.9f;

        private static readonly string[] MachineIds = { "icp", "ftir", "karl_fischer", "ferrography" };

        [MenuItem("Residue/Build/Rebuild Greybox Lab", priority = 40)]
        public static void Rebuild()
        {
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

            var palette = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterial);
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(CatalogPath);

            if (palette == null || catalog == null)
            {
                Debug.LogError("[LabSceneBuilder] Palette material or content catalog still missing. Aborting.");
                return;
            }

            var theme = EnsureRuntimeTheme();
            var panelSettings = EnsurePanelSettings(theme);
            var vialPrefab = BuildVialPrefab(palette);
            var inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(
                "Assets/InputSystem_Actions.inputactions");

            // Additive so the currently open scene is never closed — closing it would raise a
            // "save changes?" modal, which blocks the Editor and hangs every MCP call.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            BuildEnvironment(scene, palette);
            var runtime = BuildRuntime(scene, catalog, vialPrefab);
            var stations = BuildStations(scene, palette);
            var player = BuildPlayer(scene, palette, inputAsset, panelSettings);

            WireTerminal(stations.terminal, player.terminalScreen);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, removeScene: true);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LabSceneBuilder] Built {ScenePath}. Open it and press Play.");
        }

        // -- Environment -------------------------------------------------------------------------------

        private static void BuildEnvironment(Scene scene, Material palette)
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

            foreach (float x in lampX)
            {
                foreach (float z in lampZ)
                {
                    var lamp = new GameObject($"Lamp_{x:0}_{z:0}");
                    lamp.transform.SetParent(lightRoot.transform, false);
                    lamp.transform.position = new Vector3(x, RoomHeight - 0.35f, z);

                    var point = lamp.AddComponent<Light>();
                    point.type = LightType.Point;
                    point.range = 7.5f;
                    point.intensity = 2.6f;
                    point.color = new Color(0.98f, 0.97f, 0.92f);
                    point.shadows = LightShadows.None; // greybox: shadow cost is not worth it yet
                }
            }

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

        private static LabRuntime BuildRuntime(Scene scene, ContentCatalog catalog, VialProp vialPrefab)
        {
            var go = new GameObject("LabRuntime");
            SceneManager.MoveGameObjectToScene(go, scene);
            var runtime = go.AddComponent<LabRuntime>();

            var so = new SerializedObject(runtime);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("vialPrefab").objectReferenceValue = vialPrefab;

            var ids = so.FindProperty("installedMachineIds");
            ids.arraySize = MachineIds.Length;
            for (int i = 0; i < MachineIds.Length; i++)
                ids.GetArrayElementAtIndex(i).stringValue = MachineIds[i];

            so.ApplyModifiedPropertiesWithoutUndo();
            return runtime;
        }

        // -- Stations ----------------------------------------------------------------------------------

        private static (TerminalStation terminal, IntakeCrate crate) BuildStations(Scene scene, Material palette)
        {
            var root = NewRoot(scene, "Stations");

            var bodyMesh = SaveMesh(ProcMesh.Box("Machine_Body", new Vector3(0.62f, 0.5f, 0.5f),
                PaletteUv.Family.NeutralWarm, 6));
            var lightMesh = SaveMesh(ProcMesh.Box("Machine_Light", new Vector3(0.1f, 0.04f, 0.02f),
                PaletteUv.Family.Signal, 13));
            var buttonMesh = SaveMesh(ProcMesh.Box("Machine_Button", new Vector3(0.07f, 0.05f, 0.02f),
                PaletteUv.Family.Brass, 8));
            var socketMesh = SaveMesh(ProcMesh.Cylinder("Machine_Socket", 0.03f, 0.012f, 10,
                PaletteUv.Family.Sump, 3));

            float benchZ = -RoomDepth * 0.5f + 0.55f;
            float[] xs = { -2.7f, -0.9f, 0.9f, 2.7f };

            for (int i = 0; i < MachineIds.Length; i++)
            {
                var machineGo = new GameObject($"Machine_{MachineIds[i]}");
                SceneManager.MoveGameObjectToScene(machineGo, scene);
                machineGo.transform.SetParent(root.transform, false);
                machineGo.transform.position = new Vector3(xs[i], BenchHeight + 0.25f, benchZ);

                AddChild(machineGo, "Body", bodyMesh, palette, Vector3.zero, addCollider: true);
                var statusLight = AddChild(machineGo, "StatusLight", lightMesh, palette,
                    new Vector3(0f, 0.15f, 0.26f), addCollider: false);
                AddChild(machineGo, "Socket", socketMesh, palette, new Vector3(0f, 0.25f, 0.1f), addCollider: false);

                var socket = new GameObject("VialSocket");
                socket.transform.SetParent(machineGo.transform, false);
                socket.transform.localPosition = new Vector3(0f, 0.27f, 0.1f);

                var station = machineGo.AddComponent<MachineStation>();
                var so = new SerializedObject(station);
                so.FindProperty("machineInstanceId").stringValue = MachineIds[i];
                so.FindProperty("vialSocket").objectReferenceValue = socket.transform;
                so.FindProperty("statusLight").objectReferenceValue = statusLight.GetComponent<Renderer>();
                so.ApplyModifiedPropertiesWithoutUndo();

                AddActionButton(machineGo, "CleanButton", buttonMesh, palette,
                    new Vector3(-0.16f, -0.05f, 0.26f), station, MachineAction.Clean);
                AddActionButton(machineGo, "BlankButton", buttonMesh, palette,
                    new Vector3(0.16f, -0.05f, 0.26f), station, MachineAction.Blank);
            }

            // Staging racks on the island, plus one beside the instruments so a finished vial can be
            // parked without walking away from the bench.
            var rackMesh = SaveMesh(ProcMesh.Box("Rack_Base", new Vector3(0.56f, 0.03f, 0.28f),
                PaletteUv.Family.Sump, 6));

            AddRack(root, scene, "rack_island_a", new Vector3(-0.8f, BenchHeight, -1.4f), palette, rackMesh, 8);
            AddRack(root, scene, "rack_island_b", new Vector3(0.8f, BenchHeight, -1.4f), palette, rackMesh, 8);
            AddRack(root, scene, "rack_bench", new Vector3(-3.75f, BenchHeight, benchZ), palette, rackMesh, 4);

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

            return (terminal, crate);
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

            var cameraGo = new GameObject("EyeCamera");
            cameraGo.transform.SetParent(go.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 60f;
            camera.fieldOfView = 70f;
            cameraGo.AddComponent<AudioListener>();
            cameraGo.tag = "MainCamera";

            var carry = new GameObject("CarrySocket");
            carry.transform.SetParent(cameraGo.transform, false);
            carry.transform.localPosition = new Vector3(0.22f, -0.18f, 0.42f);

            var player = go.AddComponent<PlayerController>();
            var playerSo = new SerializedObject(player);
            playerSo.FindProperty("inputAsset").objectReferenceValue = inputAsset;
            playerSo.FindProperty("eyeCamera").objectReferenceValue = camera;
            playerSo.ApplyModifiedPropertiesWithoutUndo();

            var interactor = go.AddComponent<PlayerInteractor>();
            var interactorSo = new SerializedObject(interactor);
            interactorSo.FindProperty("player").objectReferenceValue = player;
            interactorSo.FindProperty("carrySocket").objectReferenceValue = carry.transform;
            interactorSo.ApplyModifiedPropertiesWithoutUndo();

            // HUD
            var hudGo = new GameObject("HUD");
            SceneManager.MoveGameObjectToScene(hudGo, scene);
            var hudDoc = hudGo.AddComponent<UIDocument>();
            SetPanelSettings(hudDoc, panelSettings, 0);
            var hud = hudGo.AddComponent<LabHud>();
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("interactor").objectReferenceValue = interactor;
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            // Terminal screen, on its own document so it can sit above the HUD.
            var screenGo = new GameObject("TerminalUI");
            SceneManager.MoveGameObjectToScene(screenGo, scene);
            var screenDoc = screenGo.AddComponent<UIDocument>();
            SetPanelSettings(screenDoc, panelSettings, 10);
            var screen = screenGo.AddComponent<TerminalScreen>();
            var screenSo = new SerializedObject(screen);
            screenSo.FindProperty("document").objectReferenceValue = screenDoc;
            screenSo.FindProperty("player").objectReferenceValue = player;
            screenSo.FindProperty("interactor").objectReferenceValue = interactor;
            screenSo.ApplyModifiedPropertiesWithoutUndo();

            return (player, screen);
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

            var glassMesh = SaveMesh(ProcMesh.Cylinder("Vial_Glass", 0.017f, 0.085f, 10,
                PaletteUv.Family.Solvent, 12));
            var fluidMesh = SaveMesh(ProcMesh.Cylinder("Vial_Fluid", 0.014f, 0.05f, 10,
                PaletteUv.Family.Oxide, 4));

            AddChild(go, "Glass", glassMesh, palette, Vector3.zero, addCollider: false);
            var fluid = AddChild(go, "Fluid", fluidMesh, palette, new Vector3(0f, 0.004f, 0f), addCollider: false);

            var collider = go.AddComponent<CapsuleCollider>();
            collider.radius = 0.025f;
            collider.height = 0.1f;
            collider.center = new Vector3(0f, 0.045f, 0f);

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var vial = go.AddComponent<VialProp>();
            var so = new SerializedObject(vial);
            so.FindProperty("fluidRenderer").objectReferenceValue = fluid.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<VialProp>();
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
