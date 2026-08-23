using System.Collections.Generic;
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

        private static readonly string[] MachineIds =
            { "cooling_curve", "karl_fischer", "viscometer", "centrifuge", "elemental" };

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
            var screenMaterial = EnsureScreenMaterial();
            var printoutPrefab = BuildPrintoutPrefab(palette);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var books = new List<ReferenceBook>();

            BuildEnvironment(scene, palette);
            BuildRuntime(scene, catalog, vialPrefab, printoutPrefab);
            var stations = BuildStations(scene, palette, screenMaterial, books);
            var player = BuildPlayer(scene, palette, inputAsset, panelSettings);

            WireTerminal(stations.terminal, player.terminalScreen);
            foreach (var book in books) WireBook(book, player.bookScreen);

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

        private static LabRuntime BuildRuntime(Scene scene, ContentCatalog catalog,
                                               VialProp vialPrefab, PrintoutProp printoutPrefab)
        {
            var go = new GameObject("LabRuntime");
            SceneManager.MoveGameObjectToScene(go, scene);
            var runtime = go.AddComponent<LabRuntime>();

            var so = new SerializedObject(runtime);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("vialPrefab").objectReferenceValue = vialPrefab;
            so.FindProperty("printoutPrefab").objectReferenceValue = printoutPrefab;

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
            var bookMesh = SaveMesh(BuildBookMesh("Book_Volume"));

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

                AddActionButton(machineGo, "CleanButton", buttonMesh, palette,
                    new Vector3(-0.14f, 0.16f, front + 0.01f), station, MachineAction.Clean);
                AddActionButton(machineGo, "BlankButton", buttonMesh, palette,
                    new Vector3(-0.06f, 0.16f, front + 0.01f), station, MachineAction.Blank);

                // Its own manual, sitting beside it. §5.5: where you keep a reference matters.
                //
                // Parented to the Stations root, NOT to the machine. A child book is inside the
                // machine's GetComponentsInChildren, so the machine's collider bounds swallow it —
                // which makes targeting diagnostics lie and would make any bounds-based selection
                // pick the wrong object.
                var bookGo = AddBook(root, $"Manual_{MachineIds[i]}", bookMesh, palette,
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

            // Shelf of general references beside the desk. Keeping them here rather than as a
            // terminal tab means looking something up costs the walk and the shift time — §6.1
            // assumes reading is expensive.
            var shelfMesh = SaveMesh(ProcMesh.Box("Lab_Shelf", new Vector3(0.9f, 0.04f, 0.24f),
                PaletteUv.Family.Steel, 7));
            var shelfGo = new GameObject("BookShelf");
            SceneManager.MoveGameObjectToScene(shelfGo, scene);
            shelfGo.transform.SetParent(root.transform, false);
            shelfGo.transform.position = new Vector3(RoomWidth * 0.5f - 1.1f, BenchHeight + 0.5f, 1.98f);
            AddChild(shelfGo, "Plank", shelfMesh, palette, Vector3.zero, addCollider: true);

            // Shelf books are parented to the shelf, which is fine: the shelf has no Interactable,
            // so nothing's bounds are polluted by them.
            books.Add(AddBook(shelfGo, "Elements", bookMesh, palette,
                new Vector3(-0.28f, 0.04f, 0f), BookKind.ElementIndex, null));
            books.Add(AddBook(shelfGo, "Diagnostics", bookMesh, palette,
                new Vector3(0f, 0.04f, 0f), BookKind.DiagnosticGuide, null));
            books.Add(AddBook(shelfGo, "Thresholds", bookMesh, palette,
                new Vector3(0.28f, 0.04f, 0f), BookKind.ThresholdTables, null));

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

        private static Mesh BuildBookMesh(string name)
        {
            return new ProcMesh.Builder()
                .Box(new Vector3(0f, 0.014f, 0f), new Vector3(0.16f, 0.028f, 0.22f), PaletteUv.Family.NeutralCold, 9)
                .Box(new Vector3(-0.083f, 0.014f, 0f), new Vector3(0.008f, 0.030f, 0.225f), PaletteUv.Family.DeepBlue, 6)
                .ToMesh(name);
        }

        private static ReferenceBook AddBook(GameObject parent, string name, Mesh mesh, Material palette,
                                             Vector3 localPosition, BookKind kind, string machineId)
        {
            var go = AddChild(parent, $"Book_{name}", mesh, palette, localPosition, addCollider: true);
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

        private static (PlayerController controller, TerminalScreen terminalScreen, BookScreen bookScreen) BuildPlayer(
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
            interactorSo.FindProperty("mask").intValue = ~(1 << PlayerInteractor.IgnoreRaycastLayer);
            interactorSo.ApplyModifiedPropertiesWithoutUndo();

            // The eye camera sits inside the CharacterController capsule, so without this the very
            // first thing the interaction ray hits is the player's own body about 0.12 m out, and
            // every real target beyond it is discarded. Nothing in the lab was ever selectable
            // except by accident.
            SetLayerRecursively(go, PlayerInteractor.IgnoreRaycastLayer);

            var interactionDebug = go.AddComponent<InteractionDebug>();
            var debugSo = new SerializedObject(interactionDebug);
            debugSo.FindProperty("interactor").objectReferenceValue = interactor;
            debugSo.ApplyModifiedPropertiesWithoutUndo();

            // HUD
            var hudGo = new GameObject("HUD");
            SceneManager.MoveGameObjectToScene(hudGo, scene);
            var hudDoc = hudGo.AddComponent<UIDocument>();
            SetPanelSettings(hudDoc, panelSettings, 0);
            var hud = hudGo.AddComponent<LabHud>();
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("interactor").objectReferenceValue = interactor;
            hudSo.FindProperty("interactionDebug").objectReferenceValue = interactionDebug;
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

            // Reading view, above the terminal so an open manual covers it.
            var bookGo = new GameObject("BookUI");
            SceneManager.MoveGameObjectToScene(bookGo, scene);
            var bookDoc = bookGo.AddComponent<UIDocument>();
            SetPanelSettings(bookDoc, panelSettings, 20);
            var bookScreen = bookGo.AddComponent<BookScreen>();
            var bookSo = new SerializedObject(bookScreen);
            bookSo.FindProperty("document").objectReferenceValue = bookDoc;
            bookSo.FindProperty("player").objectReferenceValue = player;
            bookSo.FindProperty("interactor").objectReferenceValue = interactor;
            bookSo.ApplyModifiedPropertiesWithoutUndo();

            return (player, screen, bookScreen);
        }

        private static void WireTerminal(TerminalStation station, TerminalScreen screen)
        {
            var so = new SerializedObject(station);
            so.FindProperty("screen").objectReferenceValue = screen;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireBook(ReferenceBook book, BookScreen screen)
        {
            var so = new SerializedObject(book);
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
