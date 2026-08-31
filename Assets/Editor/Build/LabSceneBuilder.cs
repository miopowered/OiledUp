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
        private const float WallThickness = 0.2f;

        // Delivery bay (#33): an opening in the west wall, aligned with the intake bench so a
        // delivery lands where it is carried from. Width and height are a small roller-door
        // opening, not full room height — the header above it is where the shutter housing sits.
        private const float BayWidth = 2.4f;
        private const float BayHeight = 2.3f;
        private const float BayCenterZ = 1.6f;

        // -- The building the lab sits in (#84) ---------------------------------------------------
        //
        // Aesthetic only. Not one station moves: every wall below was placed around the benches,
        // island, desk, wash station, book case, bay and truck where they already stand, and where a
        // wall wanted to be somewhere a bench already was, the wall gave way. See BuildBuilding.
        //
        // Doorways are sized from the CharacterController the player actually drives (PlayerController:
        // radius 0.3, skinWidth 0.02, standHeight 1.8), so the capsule is 0.64 m across and 1.8 m
        // tall. A structural hole of 1.4 x 2.25 lined with a 0.1 m reveal leaves 1.2 x 2.15 clear —
        // nearly twice the capsule's width so two technicians pass, and 0.35 m over a standing head.
        private const float DoorStructWidth = 1.4f;
        private const float DoorStructHeight = 2.25f;
        private const float DoorFrameDepth = 0.10f;

        // Interior windows. The sill is above the 0.85 m jump apex (PlayerController.jumpHeight), so
        // a window is a view and never a shortcut — and every one of them opens onto another sealed
        // room anyway, so the outer envelope has no glazing in it at all.
        private const float WindowStructWidth = 1.6f;
        private const float WindowSill = 1.10f;
        private const float WindowHead = 2.10f;

        // Openings cut into the lab itself. The north wall is clear from the island (z = -1.0) to
        // the wall; the east wall has gaps either side of the terminal desk (z 1.2..2.0) and north
        // of the book case (z 0.07..0.93).
        private const float LabNorthDoorX = 1.5f;
        private const float LabEastDoorZ = 3.0f;
        private const float LabEastWindowZ = -1.2f;

        // Corridor: an L wrapping the lab's north-east corner, so the two lab doors make a loop
        // rather than a cul-de-sac. Its inner corner is cut at 45 degrees — see BuildCorridorShell,
        // where the splay is a solid pier the two walls stop against rather than a panel standing
        // inside the corner they still fill.
        private const float CorridorCeiling = 2.55f;
        private const float CorrWest = -5.0f;
        private const float CorrEast = 7.4f;
        private const float CorrSouth = 4.2f;
        private const float CorrNorth = 6.4f;
        private const float TailWest = 5.2f;
        private const float TailSouth = -2.4f;
        private const float SplayX = 6.2f;   // where the 45 degree cut leaves the north wall
        private const float SplayZ = 5.2f;   // and where it meets the east wall

        private const float StoreCeiling = 2.80f;
        private const float StoreWest = -5.0f;
        private const float StoreEast = 1.2f;
        private const float StoreNorth = 9.4f;

        private const float OfficeCeiling = 3.00f;
        private const float OfficeWest = 1.4f;
        private const float OfficeEast = 7.4f;
        private const float OfficeNorth = 10.4f;

        // The office's north wall steps back into a recess — a desk nook with a lower ceiling, which
        // is what stops the room being one more rectangle.
        private const float NookWest = 3.0f;
        private const float NookEast = 5.8f;
        private const float NookNorth = 11.4f;
        private const float NookCeiling = 2.60f;

        // A covered loading dock west of the bay. Without it the bay opening is a hole in the outer
        // envelope that the player can simply walk out of, and the truck is parked in a void.
        private const float DockHeight = 3.2f;
        private const float DockWest = -10.4f;
        private const float DockSouth = -1.2f;
        private const float DockNorth = 4.2f;

        /// <summary>Where the north wall of the corridor sits, shared with the store and office.</summary>
        private const float PartyZ = 6.6f;

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
            EnsureLayer(HeldItemCamera.HeldItemLayer, "HeldItem");

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
            var cartonPrefab = BuildCartonPrefab(palette);
            var notePrefab = BuildDeliveryNotePrefab(palette);

            var books = new List<ReferenceBook>();

            BuildEnvironment(scene, palette, emissivePalette, volumeProfile);
            var truck = BuildDeliveryTruck(scene, palette);
            BuildDeliveryBay(scene, truck);
            BuildRuntime(scene, catalog, vialPrefab, printoutPrefab, bottlePrefab,
                         cartonPrefab, notePrefab);
            var stations = BuildStations(scene, palette, screenMaterial, books);
            var player = BuildPlayer(scene, palette, inputAsset, panelSettings);

            WireTerminal(stations, player.terminalScreen);

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

            var room = BuildRoomShellWithBay();
            AddStatic(root, "Room", SaveMesh(room), palette, Vector3.zero, addCollider: true);
            BuildBayDressing(root, palette);

            // Bench along the far wall, machines sit on it. 0.9 m per §2.1. Back to the wall, so it
            // gets the upstand.
            AddStatic(root, "Bench_Machines", SaveMesh(BuildBenchMesh("Lab_Bench", 8f, 0.7f, -1)),
                palette, new Vector3(0f, BenchPivot, -RoomDepth * 0.5f + 0.55f), addCollider: true);

            // No shelf under the intake bench: two of DeliveryBay's four standings fall inside its
            // footprint at floor height, so whatever is under this bench has to stay clear floor.
            AddStatic(root, "Bench_Intake",
                SaveMesh(BuildBenchMesh("Lab_IntakeBench", 1.6f, 0.8f, 0, underShelf: false)),
                palette, new Vector3(-RoomWidth * 0.5f + 1.1f, BenchPivot, 1.6f), addCollider: true);

            AddStatic(root, "Bench_Terminal", SaveMesh(BuildTerminalDeskMesh()), palette,
                new Vector3(RoomWidth * 0.5f - 1.1f, BenchPivot, 1.6f), addCollider: true);
            BuildTerminalDeskDressing(root, palette);

            // Island between the door and the instruments. Staging space is not decoration: with one
            // pair of hands and four machines, somewhere to put a vial down is what stops the loop
            // deadlocking the moment every instrument is busy. Worked from both sides, so no upstand.
            AddStatic(root, "Bench_Island", SaveMesh(BuildBenchMesh("Lab_Island", 3.2f, 0.8f, 0)),
                palette, new Vector3(0f, BenchPivot, -1.4f), addCollider: true);

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
            var labLights = new GameObject("Labor");
            labLights.transform.SetParent(lightRoot.transform, false);

            float[] lampX = { -3f, 0f, 3f };
            float[] lampZ = { -2.4f, 1.6f };
            var luminaireMesh = SaveMesh(ProcMesh.Box("Lab_FluorescentLuminaire",
                new Vector3(1.45f, 0.035f, 0.24f), PaletteUv.Family.NeutralCold, 15));
            var ballastMesh = SaveMesh(ProcMesh.Box("Lab_FluorescentBallast",
                new Vector3(1.55f, 0.055f, 0.30f), PaletteUv.Family.Steel, 5));
            var lamp = new Luminaire(ballastMesh, luminaireMesh, palette, emissivePalette);

            foreach (float x in lampX)
            {
                foreach (float z in lampZ)
                {
                    lamp.Place(labLights, $"Lamp_{x:0}_{z:0}",
                        new Vector3(x, RoomHeight - 0.35f, z), 0f,
                        7.5f, 2.35f, new Color(0.90f, 1f, 0.94f));
                }
            }

            BuildBuilding(root, lightRoot, palette, emissivePalette, lamp);

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

        /// <summary>
        /// The lab's own shell: floor, ceiling and four walls, with the bay opening in the west wall
        /// (#33) and, since #84, a door north into the corridor, a door east into it, and one
        /// interior window beside that door.
        /// <para>
        /// <see cref="ProcMesh.Builder.Room"/> builds a fully sealed box and cannot express a hole,
        /// so the walls go through <see cref="AddWall"/> instead — same per-wall thickness box, same
        /// family and steps, so the seam with <c>Room()</c>-built geometry is invisible.
        /// </para>
        /// No opening moved a station. The south wall has none because the instrument bench runs its
        /// entire length; the north and east ones sit in the gaps the desk and book case leave.
        /// </summary>
        private static Mesh BuildRoomShellWithBay()
        {
            const float w = RoomWidth, h = RoomHeight, d = RoomDepth, t = WallThickness;
            const PaletteUv.Family family = PaletteUv.Family.NeutralCold;
            const int step = 4;

            var b = new ProcMesh.Builder()
                // Floor and ceiling, identical to Room().
                .Box(new Vector3(0f, -t * 0.5f, 0f), new Vector3(w + t * 2f, t, d + t * 2f), family, step)
                .Box(new Vector3(0f, h + t * 0.5f, 0f), new Vector3(w + t * 2f, t, d + t * 2f), family, step + 2);

            AddWall(b, WallAxis.AlongX, -d * 0.5f - t * 0.5f, -(w * 0.5f + t), w * 0.5f + t,
                h, family, step + 1);

            AddWall(b, WallAxis.AlongX, d * 0.5f + t * 0.5f, -(w * 0.5f + t), w * 0.5f + t,
                h, family, step + 1, Opening.Door(LabNorthDoorX));

            AddWall(b, WallAxis.AlongZ, w * 0.5f + t * 0.5f, -d * 0.5f, d * 0.5f,
                h, family, step + 1,
                Opening.Window(LabEastWindowZ), Opening.Door(LabEastDoorZ));

            AddWall(b, WallAxis.AlongZ, -w * 0.5f - t * 0.5f, -d * 0.5f, d * 0.5f,
                h, family, step + 1, new Opening(BayCenterZ, BayWidth, 0f, BayHeight));

            return b.ToMesh("Lab_Room");
        }

        /// <summary>
        /// Static dressing around the bay opening: a housing for a rolled-up curtain under the
        /// header, and a track down each jamb. Purely cosmetic — nothing here moves, and no
        /// collider blocks the opening. The delivery behaviour owns when goods move through it.
        /// </summary>
        private static void BuildBayDressing(GameObject root, Material palette)
        {
            var housingMesh = SaveMesh(ProcMesh.Box("Bay_ShutterHousing",
                new Vector3(0.28f, 0.30f, BayWidth + 0.1f), PaletteUv.Family.Steel, 5));
            AddStatic(root, "Bay_ShutterHousing", housingMesh, palette,
                new Vector3(-RoomWidth * 0.5f + 0.20f, BayHeight + 0.05f, BayCenterZ), addCollider: false);

            var trackMesh = SaveMesh(ProcMesh.Box("Bay_Track",
                new Vector3(0.05f, BayHeight, 0.08f), PaletteUv.Family.Steel, 6));
            AddStatic(root, "Bay_TrackSouth", trackMesh, palette,
                new Vector3(-RoomWidth * 0.5f + 0.05f, BayHeight * 0.5f, BayCenterZ - BayWidth * 0.5f + 0.04f),
                addCollider: false);
            AddStatic(root, "Bay_TrackNorth", trackMesh, palette,
                new Vector3(-RoomWidth * 0.5f + 0.05f, BayHeight * 0.5f, BayCenterZ + BayWidth * 0.5f - 0.04f),
                addCollider: false);
        }

        // -- Benches -------------------------------------------------------------------------------

        // Every bench is placed with its transform 0.45 m off the floor and a mesh centred on that,
        // which is how they were authored when each was a single box. Heights below are measured from
        // the floor and shifted by this, so the geometry can change completely while the transform,
        // the footprint and the 0.9 m working height stay exactly where the machines and racks expect
        // them.
        private const float BenchPivot = BenchHeight * 0.5f;

        private const float BenchTop = 0.045f;
        private const float BenchRail = 0.09f;
        private const float BenchRailThickness = 0.03f;
        private const float BenchLeg = 0.06f;
        private const float BenchInset = 0.05f;

        /// <summary>Floor height to bench-mesh local Y.</summary>
        private static float OnBench(float fromFloor) => fromFloor - BenchPivot;

        /// <summary>
        /// A bench from the lab's own furniture range: a worktop with a nosing under each long edge,
        /// a perimeter apron rail carried on legs, an under-bench shelf, cable trunking along the
        /// back, and — where the bench stands against something — a splash upstand.
        /// <para>
        /// An extruded box reads as a placeholder because a real bench is mostly the space underneath
        /// it. The toe space, the shelf and the shadow under the nosing are what say "furniture" from
        /// across the room, and none of them cost a texture. Every part is inside the original
        /// footprint and the top is still at <see cref="BenchHeight"/>, so nothing standing on a bench
        /// moves by a millimetre.
        /// </para>
        /// Rails along Z own the corners and the ones along X stop against their inner faces — the
        /// same rule the building's walls follow, for the same reason.
        /// </summary>
        /// <param name="upstandSide">-1, 0 or +1: which Z edge carries the upstand and the trunking.
        /// 0 is an island, worked from both sides, which gets neither an upstand nor a back.</param>
        private static Mesh BuildBenchMesh(string name, float width, float depth, int upstandSide,
                                           bool underShelf = true)
        {
            float hw = width * 0.5f, hd = depth * 0.5f;
            float topY = BenchHeight - BenchTop * 0.5f;
            float underside = BenchHeight - BenchTop;
            float railY = underside - BenchRail * 0.5f;
            float legTop = underside - BenchRail;

            var b = new ProcMesh.Builder();

            b.Box(new Vector3(0f, OnBench(topY), 0f), new Vector3(width, BenchTop, depth),
                PaletteUv.Family.NeutralCold, 12);

            foreach (int side in new[] { -1, 1 })
            {
                // Nosing: flush with the worktop's edge and tucked under it, so the two coplanar
                // faces never overlap and the join is a shadow line rather than a fight.
                b.Box(new Vector3(0f, OnBench(underside - 0.01f), side * (hd - 0.0075f)),
                    new Vector3(width, 0.02f, 0.015f), PaletteUv.Family.Sump, 5);

                b.Box(new Vector3(side * (hw - BenchInset), OnBench(railY), 0f),
                    new Vector3(BenchRailThickness, BenchRail, depth - 2f * BenchInset + BenchRailThickness),
                    PaletteUv.Family.Steel, 5);
                b.Box(new Vector3(0f, OnBench(railY), side * (hd - BenchInset)),
                    new Vector3(width - 2f * BenchInset - BenchRailThickness, BenchRail, BenchRailThickness),
                    PaletteUv.Family.Steel, 5);
            }

            // A leg pair every 1.7 m or so. Four legs under an 8 m bench reads as a plank on stilts.
            int bays = Mathf.Max(1, Mathf.RoundToInt(width / 1.7f));
            for (int i = 0; i <= bays; i++)
            {
                float x = Mathf.Lerp(-(hw - BenchInset), hw - BenchInset, i / (float)bays);
                foreach (int side in new[] { -1, 1 })
                {
                    b.Box(new Vector3(x, OnBench(legTop * 0.5f), side * (hd - BenchInset)),
                        new Vector3(BenchLeg, legTop, BenchLeg), PaletteUv.Family.Steel, 4);
                }
            }

            if (underShelf)
            {
                // Sized to the legs' inner faces exactly, so it spans between them without entering
                // one of them.
                float shelfInset = BenchInset + BenchLeg * 0.5f;
                b.Box(new Vector3(0f, OnBench(0.28f), 0f),
                    new Vector3(width - 2f * shelfInset, 0.03f, depth - 2f * shelfInset),
                    PaletteUv.Family.Steel, 8);
            }

            int back = upstandSide != 0 ? upstandSide : 1;
            b.Box(new Vector3(0f, OnBench(0.79f), back * (hd - 0.16f)),
                new Vector3(width - 0.26f, 0.06f, 0.07f), PaletteUv.Family.Sump, 4);

            if (upstandSide != 0)
            {
                b.Box(new Vector3(0f, OnBench(BenchHeight + 0.02f), upstandSide * (hd - 0.01f)),
                    new Vector3(width, 0.04f, 0.02f), PaletteUv.Family.NeutralCold, 12);
            }

            return b.ToMesh(name);
        }

        // -- The terminal desk -----------------------------------------------------------------------

        /// <summary>
        /// Where every verdict is signed off, so it should look like somewhere work is signed off
        /// rather than a cube with a screen on it.
        /// <para>
        /// A worktop over a knee well, an end gable one side and a three-drawer pedestal the other,
        /// a pulled-out keyboard shelf, cable trunking gathered into a riser at the back corner, and
        /// a foot and neck under the terminal so the monitor stands on the desk instead of floating
        /// 0.13 m above it.
        /// </para>
        /// The transform, the footprint and the 0.9 m top are unchanged: the terminal, its screen and
        /// its interaction points are anchored to this desk and none of them may move. The operator's
        /// side is -Z, away from the east wall and clear of the east doorway, which is why the modesty
        /// panel, the riser and the trunking are all on +Z.
        /// </summary>
        private static Mesh BuildTerminalDeskMesh()
        {
            var b = new ProcMesh.Builder();

            b.Box(new Vector3(0f, OnBench(0.880f), 0f), new Vector3(1.68f, 0.04f, 0.86f),
                PaletteUv.Family.NeutralCold, 12);
            b.Box(new Vector3(0f, OnBench(0.850f), -0.4225f), new Vector3(1.68f, 0.02f, 0.015f),
                PaletteUv.Family.Sump, 5);
            b.Box(new Vector3(0f, OnBench(0.850f), 0.4225f), new Vector3(1.68f, 0.02f, 0.015f),
                PaletteUv.Family.Sump, 5);

            // End gable west, pedestal east, knee well between them.
            b.Box(new Vector3(-0.80f, OnBench(0.430f), 0f), new Vector3(0.04f, 0.86f, 0.78f),
                PaletteUv.Family.Steel, 5);

            // Modesty panel stops dead against the gable at -0.78 and the riser at +0.70.
            b.Box(new Vector3(-0.04f, OnBench(0.560f), 0.360f), new Vector3(1.48f, 0.34f, 0.025f),
                PaletteUv.Family.Steel, 6);

            b.Box(new Vector3(0.59f, OnBench(0.445f), 0f), new Vector3(0.42f, 0.83f, 0.60f),
                PaletteUv.Family.Steel, 7);
            b.Box(new Vector3(0.59f, OnBench(0.015f), 0f), new Vector3(0.38f, 0.03f, 0.56f),
                PaletteUv.Family.Sump, 4);

            foreach (float y in new[] { 0.135f, 0.395f, 0.655f })
            {
                b.Box(new Vector3(0.59f, OnBench(y), -0.310f), new Vector3(0.38f, 0.185f, 0.02f),
                    PaletteUv.Family.NeutralCold, 9);
                b.Box(new Vector3(0.59f, OnBench(y + 0.060f), -0.329f),
                    new Vector3(0.18f, 0.018f, 0.018f), PaletteUv.Family.Brass, 8);
            }

            // Keyboard shelf, pulled out and loaded, on runners beneath it.
            b.Box(new Vector3(-0.22f, OnBench(0.750f), -0.270f), new Vector3(0.64f, 0.025f, 0.30f),
                PaletteUv.Family.Steel, 8);
            foreach (float x in new[] { -0.5275f, 0.0875f })
            {
                b.Box(new Vector3(x, OnBench(0.7225f), -0.270f), new Vector3(0.025f, 0.03f, 0.30f),
                    PaletteUv.Family.Sump, 3);
            }
            b.Box(new Vector3(-0.22f, OnBench(0.7735f), -0.275f), new Vector3(0.42f, 0.022f, 0.14f),
                PaletteUv.Family.Sump, 5);
            b.Box(new Vector3(0.22f, OnBench(0.913f), -0.28f), new Vector3(0.055f, 0.026f, 0.09f),
                PaletteUv.Family.Sump, 5);

            // Trunking gathered along the back and dropped to the floor in a riser at the corner.
            b.Box(new Vector3(-0.10f, OnBench(0.790f), 0.310f), new Vector3(0.90f, 0.06f, 0.08f),
                PaletteUv.Family.Sump, 4);
            b.Box(new Vector3(0.75f, OnBench(0.430f), 0.350f), new Vector3(0.10f, 0.86f, 0.10f),
                PaletteUv.Family.Steel, 5);

            // Foot and neck for the monitor. The neck stops exactly on the terminal's underside at
            // 1.03 m, which is where BuildStations has anchored the screen since before this desk had
            // anything to stand it on.
            b.Box(new Vector3(0f, OnBench(0.907f), 0f), new Vector3(0.26f, 0.014f, 0.18f),
                PaletteUv.Family.Sump, 6);
            b.Box(new Vector3(0f, OnBench(0.972f), 0f), new Vector3(0.07f, 0.116f, 0.07f),
                PaletteUv.Family.Steel, 6);

            return b.ToMesh("Lab_Desk");
        }

        /// <summary>
        /// The loose things around the terminal desk: a chair, a letter tray and a stack of paper.
        /// Separate objects rather than part of the desk mesh, because each is skewed a few degrees —
        /// a chair squared exactly to a desk is the one arrangement no lab has ever had.
        /// </summary>
        private static void BuildTerminalDeskDressing(GameObject root, Material palette)
        {
            const float deskX = RoomWidth * 0.5f - 1.1f;
            const float deskZ = 1.6f;

            AddProp(root, "Buerostuhl_Terminal", SaveMesh(BuildTaskChairMesh()), palette,
                new Vector3(deskX - 0.18f, 0f, deskZ - 0.58f), 8f, addCollider: true);
            AddProp(root, "Ablagekorb_Terminal", SaveMesh(BuildLetterTrayMesh()), palette,
                new Vector3(deskX + 0.54f, BenchHeight, deskZ + 0.24f), -9f, addCollider: false);
            AddProp(root, "Papiere_Terminal", SaveMesh(BuildPaperStackMesh()), palette,
                new Vector3(deskX - 0.60f, BenchHeight, deskZ - 0.16f), 14f, addCollider: false);
        }

        /// <summary>
        /// A task chair, facing +Z at zero yaw. Cylinders build along +Y, so the base is a pentagonal
        /// foot on five castors rather than a five-arm star — same read at this scale, and every piece
        /// stacks on the one below it instead of intersecting it.
        /// </summary>
        private static Mesh BuildTaskChairMesh()
        {
            var b = new ProcMesh.Builder();

            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f;
                b.Cylinder(new Vector3(Mathf.Cos(a) * 0.235f, 0f, Mathf.Sin(a) * 0.235f),
                    0.028f, 0.04f, 6, PaletteUv.Family.Sump, 3);
            }

            b.Cylinder(new Vector3(0f, 0.04f, 0f), 0.265f, 0.04f, 5, PaletteUv.Family.Sump, 5);
            b.Cylinder(new Vector3(0f, 0.08f, 0f), 0.045f, 0.12f, 10, PaletteUv.Family.Steel, 6);
            b.Cylinder(new Vector3(0f, 0.20f, 0f), 0.030f, 0.20f, 10, PaletteUv.Family.Steel, 9);

            b.Box(new Vector3(0f, 0.43f, 0f), new Vector3(0.46f, 0.06f, 0.44f), PaletteUv.Family.Steel, 4);
            b.Box(new Vector3(0f, 0.475f, 0f), new Vector3(0.42f, 0.03f, 0.40f),
                PaletteUv.Family.DeepBlue, 6);
            b.Box(new Vector3(0f, 0.595f, -0.17f), new Vector3(0.06f, 0.21f, 0.06f),
                PaletteUv.Family.Steel, 5);
            b.Box(new Vector3(0f, 0.900f, -0.19f), new Vector3(0.44f, 0.40f, 0.05f),
                PaletteUv.Family.DeepBlue, 6);

            return b.ToMesh("Lab_TaskChair");
        }

        private static Mesh BuildLetterTrayMesh()
        {
            var b = new ProcMesh.Builder()
                .Box(new Vector3(0f, 0.006f, 0f), new Vector3(0.24f, 0.012f, 0.32f),
                    PaletteUv.Family.Steel, 6)
                .Box(new Vector3(-0.114f, 0.037f, 0f), new Vector3(0.012f, 0.05f, 0.32f),
                    PaletteUv.Family.Steel, 6)
                .Box(new Vector3(0.114f, 0.037f, 0f), new Vector3(0.012f, 0.05f, 0.32f),
                    PaletteUv.Family.Steel, 6)
                .Box(new Vector3(0f, 0.037f, 0.154f), new Vector3(0.216f, 0.05f, 0.012f),
                    PaletteUv.Family.Steel, 6)
                .Box(new Vector3(0.004f, 0.0145f, -0.006f), new Vector3(0.20f, 0.005f, 0.27f),
                    PaletteUv.Family.NeutralWarm, 14)
                .Box(new Vector3(-0.006f, 0.0195f, 0.004f), new Vector3(0.20f, 0.005f, 0.27f),
                    PaletteUv.Family.NeutralWarm, 13);

            return b.ToMesh("Lab_LetterTray");
        }

        private static Mesh BuildPaperStackMesh()
        {
            var b = new ProcMesh.Builder();

            for (int i = 0; i < 4; i++)
            {
                b.Box(new Vector3(i % 2 * 0.006f - 0.003f, 0.002f + i * 0.005f, i * 0.004f - 0.006f),
                    new Vector3(0.21f, 0.004f, 0.29f), PaletteUv.Family.NeutralWarm, 14 - i % 2);
            }

            b.Box(new Vector3(0.02f, 0.024f, 0.02f), new Vector3(0.13f, 0.008f, 0.008f),
                PaletteUv.Family.DeepBlue, 6);

            return b.ToMesh("Lab_PaperStack");
        }

        // -- The building around the lab (#84) ----------------------------------------------------

        /// <summary>Which way a wall runs. Its two faces look along the other horizontal axis.</summary>
        private enum WallAxis
        {
            AlongX,
            AlongZ
        }

        /// <summary>
        /// A hole in a wall, measured along that wall's own run.
        /// <para>
        /// A door and a window differ only in where the hole starts and stops vertically, so one type
        /// describes both and <see cref="AddWall"/> never has to know which it is cutting.
        /// </para>
        /// </summary>
        private readonly struct Opening
        {
            public readonly float Centre;
            public readonly float Width;
            public readonly float Sill;
            public readonly float Head;

            public Opening(float centre, float width, float sill, float head)
            {
                Centre = centre;
                Width = width;
                Sill = sill;
                Head = head;
            }

            public float Min => Centre - Width * 0.5f;
            public float Max => Centre + Width * 0.5f;

            /// <summary>True for a hole you can walk through, which is also a hole skirting stops at.</summary>
            public bool IsDoor => Sill <= 0.001f;

            public static Opening Door(float centre) =>
                new(centre, DoorStructWidth, 0f, DoorStructHeight);

            public static Opening Window(float centre) =>
                new(centre, WindowStructWidth, WindowSill, WindowHead);
        }

        /// <summary>
        /// A ceiling light fitting: housing, emissive tube, and the point light that does the work.
        /// <para>
        /// Bundled because the three must never be separated. §2.4 wants baked lightmaps eventually,
        /// but until then a room with a luminaire mesh and no <see cref="Light"/> in it is a black
        /// room with a white stripe on the ceiling — and a room that looks lit with neither is light
        /// leaking through inverted faces, which is the bug this file already carries a note about.
        /// </para>
        /// </summary>
        private readonly struct Luminaire
        {
            private readonly Mesh housing;
            private readonly Mesh tube;
            private readonly Material palette;
            private readonly Material emissive;

            public Luminaire(Mesh housing, Mesh tube, Material palette, Material emissive)
            {
                this.housing = housing;
                this.tube = tube;
                this.palette = palette;
                this.emissive = emissive;
            }

            public void Place(GameObject parent, string name, Vector3 position, float yaw,
                              float range, float intensity, Color colour)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent.transform, false);
                go.transform.localPosition = position;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

                AddChild(go, "DIN_Leuchtenwanne", housing, palette, new Vector3(0f, 0.035f, 0f), false);
                AddChild(go, "Leuchtstoffroehre_4000K", tube, emissive, Vector3.zero, false);

                var point = go.AddComponent<Light>();
                point.type = LightType.Point;
                point.range = range;
                point.intensity = intensity;
                point.color = colour;
                point.shadows = LightShadows.None; // greybox: shadow cost is not worth it yet
            }
        }

        // -- Wall and slab primitives -------------------------------------------------------------

        private const float SkirtingHeight = 0.12f;
        private const float SkirtingProud = 0.02f;
        private const float BeamDepth = 0.20f;
        private const float BeamWidth = 0.24f;

        /// <summary>One rectangle of wall. Degenerate spans are skipped, so callers never special-case
        /// an opening that reaches a corner.</summary>
        private static void AddWallSegment(ProcMesh.Builder b, WallAxis axis, float v,
                                           float u0, float u1, float y0, float y1,
                                           PaletteUv.Family family, int step)
        {
            float length = u1 - u0;
            float height = y1 - y0;
            if (length <= 0.001f || height <= 0.001f) return;

            float uc = (u0 + u1) * 0.5f;
            float yc = (y0 + y1) * 0.5f;

            var centre = axis == WallAxis.AlongX ? new Vector3(uc, yc, v) : new Vector3(v, yc, uc);
            var size = axis == WallAxis.AlongX
                ? new Vector3(length, height, WallThickness)
                : new Vector3(WallThickness, height, length);

            b.Box(centre, size, family, step);
        }

        /// <summary>
        /// A wall running from <paramref name="u0"/> to <paramref name="u1"/> with holes cut in it:
        /// a pier between each pair of openings, a band under each sill and a header over each head.
        /// <para>
        /// Every opening in the building goes through here rather than being hand-assembled, because
        /// hand-assembling one is exactly how a wall ends up with a hole nobody meant to leave — and
        /// a hole in an outer wall is the player walking out of the building.
        /// </para>
        /// </summary>
        private static void AddWall(ProcMesh.Builder b, WallAxis axis, float v, float u0, float u1,
                                    float height, PaletteUv.Family family, int step,
                                    params Opening[] openings)
        {
            if (openings == null || openings.Length == 0)
            {
                AddWallSegment(b, axis, v, u0, u1, 0f, height, family, step);
                return;
            }

            var sorted = (Opening[])openings.Clone();
            System.Array.Sort(sorted, (a, c) => a.Min.CompareTo(c.Min));

            float cursor = u0;
            foreach (var opening in sorted)
            {
                AddWallSegment(b, axis, v, cursor, opening.Min, 0f, height, family, step);
                AddWallSegment(b, axis, v, opening.Min, opening.Max, 0f, opening.Sill, family, step);
                AddWallSegment(b, axis, v, opening.Min, opening.Max, opening.Head, height, family, step);
                cursor = Mathf.Max(cursor, opening.Max);
            }

            AddWallSegment(b, axis, v, cursor, u1, 0f, height, family, step);
        }

        /// <summary>A floor or ceiling plate, given as its bounds rather than a centre and a size.</summary>
        private static void AddSlab(ProcMesh.Builder b, float xMin, float xMax, float zMin, float zMax,
                                    float yBottom, float yTop, PaletteUv.Family family, int step)
        {
            if (xMax - xMin <= 0.001f || zMax - zMin <= 0.001f) return;

            b.Box(new Vector3((xMin + xMax) * 0.5f, (yBottom + yTop) * 0.5f, (zMin + zMax) * 0.5f),
                new Vector3(xMax - xMin, yTop - yBottom, zMax - zMin), family, step);
        }

        /// <summary>
        /// Skirting along the inside face of a wall. <paramref name="inward"/> is +1 when the room is
        /// on the positive side of <paramref name="face"/>. Runs are broken at doorways: skirting
        /// across a threshold is the sort of detail that reads as a modelling mistake.
        /// </summary>
        private static void AddSkirting(ProcMesh.Builder b, WallAxis axis, float face, float inward,
                                        float u0, float u1, params Opening[] openings)
        {
            var doors = new List<Opening>();
            if (openings != null)
            {
                foreach (var opening in openings)
                    if (opening.IsDoor) doors.Add(opening);
            }
            doors.Sort((a, c) => a.Min.CompareTo(c.Min));

            float cursor = u0;
            foreach (var door in doors)
            {
                AddSkirtingRun(b, axis, face, inward, cursor, Mathf.Min(door.Min, u1));
                cursor = Mathf.Max(cursor, door.Max);
            }
            AddSkirtingRun(b, axis, face, inward, cursor, u1);
        }

        private static void AddSkirtingRun(ProcMesh.Builder b, WallAxis axis, float face, float inward,
                                           float u0, float u1)
        {
            float length = u1 - u0;
            if (length <= 0.001f) return;

            float uc = (u0 + u1) * 0.5f;
            float v = face + inward * SkirtingProud * 0.5f;

            var centre = axis == WallAxis.AlongX
                ? new Vector3(uc, SkirtingHeight * 0.5f, v)
                : new Vector3(v, SkirtingHeight * 0.5f, uc);
            var size = axis == WallAxis.AlongX
                ? new Vector3(length, SkirtingHeight, SkirtingProud)
                : new Vector3(SkirtingProud, SkirtingHeight, length);

            b.Box(centre, size, PaletteUv.Family.Sump, 5);
        }

        /// <summary>
        /// Skirting along a wall that is not axis-aligned. Order the endpoints so the room lies on
        /// the <c>(d.z, -d.x)</c> side of the run — the same handedness
        /// <see cref="ProcMesh.Builder.Prism"/> uses for its outward faces, so a splayed wall and the
        /// skirting under it are described the same way round.
        /// </summary>
        private static void AddDiagonalSkirting(ProcMesh.Builder b, Vector2 from, Vector2 to)
        {
            Vector2 run = to - from;
            if (run.sqrMagnitude <= 1e-6f) return;

            Vector2 inward = new Vector2(run.y, -run.x).normalized * SkirtingProud;
            b.Prism(0f, SkirtingHeight, PaletteUv.Family.Sump, 5,
                from, to, to + inward, from + inward);
        }

        /// <summary>A downstand beam, so a ceiling has structure in it rather than being a flat lid.</summary>
        private static void AddBeam(ProcMesh.Builder b, WallAxis axis, float v, float u0, float u1,
                                    float ceiling)
        {
            float length = u1 - u0;
            if (length <= 0.001f) return;

            float uc = (u0 + u1) * 0.5f;
            float y = ceiling - BeamDepth * 0.5f;

            var centre = axis == WallAxis.AlongX ? new Vector3(uc, y, v) : new Vector3(v, y, uc);
            var size = axis == WallAxis.AlongX
                ? new Vector3(length, BeamDepth, BeamWidth)
                : new Vector3(BeamWidth, BeamDepth, length);

            b.Box(centre, size, PaletteUv.Family.NeutralCold, 6);
        }

        /// <summary>A pilaster: a shallow pier standing proud of a wall, where a beam lands on it.</summary>
        private static void AddPilaster(ProcMesh.Builder b, WallAxis axis, float face, float inward,
                                        float u, float height)
        {
            const float width = 0.40f, proud = 0.14f;
            float v = face + inward * proud * 0.5f;

            var centre = axis == WallAxis.AlongX
                ? new Vector3(u, height * 0.5f, v)
                : new Vector3(v, height * 0.5f, u);
            var size = axis == WallAxis.AlongX
                ? new Vector3(width, height, proud)
                : new Vector3(proud, height, width);

            b.Box(centre, size, PaletteUv.Family.NeutralCold, 7);
        }

        // -- The rooms ----------------------------------------------------------------------------

        // Where the corridor's north wall is broken. Two doors and two interior windows: a window
        // between two rooms costs four boxes and is most of what makes a corridor worth walking
        // down, because a lit room seen from a dim one is somewhere to go.
        private const float StoreWindowX = -4.0f;
        private const float StoreDoorX = -2.2f;
        private const float OfficeDoorX = 3.2f;
        // 5.1 rather than 5.2: this wall now stops dead at SplayX (6.2), where the splayed corner
        // pier takes over, so the window has to clear that end with a pier of its own.
        private const float OfficeWindowX = 5.1f;

        /// <summary>
        /// The building the lab is a room in (#84): a corridor that turns, a store, an office with a
        /// recessed nook, and a covered loading dock behind the delivery bay.
        /// <para>
        /// <b>Aesthetic only, and that is the whole point.</b> §5.5 makes lab layout a player-facing
        /// mechanic and re-pacing the loop belongs to that work, so not one bench, machine, rack,
        /// desk, book case or truck moved by a millimetre for any of this. Every wall below was
        /// routed around them: the lab's south wall gets no door because the instrument bench runs
        /// its whole length, and the east wall's door and window sit in the gaps the terminal desk
        /// and the book case happen to leave.
        /// </para>
        /// The footprint is an L with a cut corner rather than a box with more boxes bolted on. The
        /// corridor leaves the lab's north wall, runs east, splays through 45 degrees at its outer
        /// corner and comes back down the lab's east flank to a second door, so circulation is a
        /// loop with the lab in the middle of it; the office's north wall steps back into a nook with
        /// a lower ceiling; and the dock is what turns the bay opening from a hole in the outer
        /// envelope into a room the truck is parked in.
        /// </summary>
        private static void BuildBuilding(GameObject root, GameObject lightRoot, Material palette,
                                          Material emissivePalette, Luminaire bigLamp)
        {
            var shell = new GameObject("Gebaeude");
            shell.transform.SetParent(root.transform, false);

            AddStatic(shell, "Flur", SaveMesh(BuildCorridorShell()), palette, Vector3.zero, true);
            AddStatic(shell, "Lager", SaveMesh(BuildStoreShell()), palette, Vector3.zero, true);
            AddStatic(shell, "Buero", SaveMesh(BuildOfficeShell()), palette, Vector3.zero, true);
            AddStatic(shell, "Verladehalle", SaveMesh(BuildDockShell()), palette, Vector3.zero, true);

            // Flur_Eckschraege is deliberately gone. The 45 degree corner used to be a rotated box
            // parented here, which is precisely how it came to sit inside the corner it was meant to
            // replace: a square-ended panel can only close a mitre by burying its ends in the walls
            // either side. It is now a mitred pier inside Lab_CorridorShell, so the corner is one
            // solid rather than three overlapping ones. See BuildCorridorShell.

            BuildOpeningTrim(shell, palette);
            BuildBuildingFixtures(shell, palette);
            BuildBuildingLighting(lightRoot, palette, emissivePalette, bigLamp);
        }

        /// <summary>
        /// The corridor: an L that leaves the lab's north wall, runs east and turns south down its
        /// east flank to a second door. Lower ceiling than the lab, warmer and darker floor — the
        /// style contract has no textures to change, so ceiling height and palette step are the only
        /// two levers there are for saying "you have left the big room".
        /// <para>
        /// <b>The splayed corner is a pier, not a panel.</b> It was a rotated box laid across the
        /// corner while both walls still ran through to meet behind it, so three solids occupied the
        /// same 45 degree wedge and their coincident faces shimmered. The party wall now stops at
        /// <see cref="SplayX"/>, the east wall stops at <see cref="SplayZ"/>, and the pentagonal pier
        /// between them fills everything the two walls gave up — including the corner of the party
        /// wall the office needs, which is why the chamfer could not simply be cut out of both.
        /// </para>
        /// </summary>
        private static Mesh BuildCorridorShell()
        {
            const float t = WallThickness, h = RoomHeight;
            const PaletteUv.Family wall = PaletteUv.Family.NeutralCold;
            const int step = 6;

            var north = new[]
            {
                Opening.Window(StoreWindowX), Opening.Door(StoreDoorX),
                Opening.Door(OfficeDoorX), Opening.Window(OfficeWindowX)
            };

            var b = new ProcMesh.Builder();

            AddSlab(b, CorrWest - t, CorrEast + t, CorrSouth, PartyZ, -t, 0f,
                PaletteUv.Family.NeutralWarm, 6);
            AddSlab(b, TailWest, CorrEast + t, TailSouth - t, CorrSouth, -t, 0f,
                PaletteUv.Family.NeutralWarm, 6);
            AddSlab(b, CorrWest - t, CorrEast + t, CorrSouth, PartyZ, CorridorCeiling, CorridorCeiling + t,
                PaletteUv.Family.NeutralCold, 9);
            AddSlab(b, TailWest, CorrEast + t, TailSouth - t, CorrSouth, CorridorCeiling, CorridorCeiling + t,
                PaletteUv.Family.NeutralCold, 9);

            // The roof, above the services void the dropped ceiling leaves. Not optional: the walls
            // run to RoomHeight, so without it the void is a hole in the top of the building.
            AddSlab(b, CorrWest - t, CorrEast + t, CorrSouth, PartyZ, RoomHeight, RoomHeight + t,
                PaletteUv.Family.NeutralCold, 4);
            AddSlab(b, TailWest, CorrEast + t, TailSouth - t, CorrSouth, RoomHeight, RoomHeight + t,
                PaletteUv.Family.NeutralCold, 4);

            // Walls along X own the corners and run to the outer face; walls along Z stop against
            // their inner faces. That is the lab shell's own convention, and keeping to it is what
            // stops two walls claiming the same 0.2 x 0.2 post where they meet.
            AddWall(b, WallAxis.AlongZ, CorrWest - t * 0.5f, CorrSouth, CorrNorth, h, wall, step);
            AddWall(b, WallAxis.AlongX, PartyZ - t * 0.5f, CorrWest - t, SplayX, h, wall, step, north);
            AddWall(b, WallAxis.AlongZ, CorrEast + t * 0.5f, TailSouth, SplayZ, h, wall, step);
            AddWall(b, WallAxis.AlongX, TailSouth - t * 0.5f, TailWest, CorrEast + t, h, wall, step);

            // The splayed corner. A pentagon, not a triangle: north of it is the office, which needs
            // the party wall carried through to x = CorrEast + t, so the chamfer can only be taken
            // out of the corridor's side of the block. The two walls above stop exactly on its E-A
            // and C-D faces, so the join is a butt and no volume is shared.
            b.Prism(0f, h, wall, step,
                new Vector2(SplayX, PartyZ),          // A, where the party wall stops
                new Vector2(CorrEast + t, PartyZ),    // B, the outer corner
                new Vector2(CorrEast + t, SplayZ),    // C
                new Vector2(CorrEast, SplayZ),        // D, where the east wall stops
                new Vector2(SplayX, CorrNorth));      // E, back up the 45 degree face

            // Trim against the lab's own walls is built here rather than into Lab_Room: those two
            // faces belong to the corridor from the corridor's side, and the lab must not grow
            // geometry that only exists because something outside it was added.
            AddSkirting(b, WallAxis.AlongX, CorrSouth, 1f, CorrWest, TailWest, Opening.Door(LabNorthDoorX));
            AddSkirting(b, WallAxis.AlongZ, TailWest, 1f, TailSouth, CorrSouth, Opening.Door(LabEastDoorZ));
            // The two orthogonal runs stop a mitre's width short of the splay and the diagonal run
            // picks up between them, so the skirting turns the corner with the wall instead of
            // stopping 0.2 m either side of it.
            AddSkirting(b, WallAxis.AlongX, CorrNorth, -1f, CorrWest, SplayX - 0.02f, north);
            AddSkirting(b, WallAxis.AlongZ, CorrWest, 1f, CorrSouth, CorrNorth);
            AddSkirting(b, WallAxis.AlongZ, CorrEast, -1f, TailSouth, SplayZ - 0.02f);
            AddSkirting(b, WallAxis.AlongX, TailSouth, 1f, TailWest, CorrEast);
            AddDiagonalSkirting(b, new Vector2(SplayX, CorrNorth), new Vector2(CorrEast, SplayZ));

            // Beams land on pilasters. Both are placed clear of the lab's north doorway (x 0.8..2.2)
            // and clear of the luminaires, which sit in the bays between them.
            foreach (float x in new[] { -4.2f, -1.8f, 0.4f, 3.0f })
            {
                AddPilaster(b, WallAxis.AlongX, CorrSouth, 1f, x, h);
                AddBeam(b, WallAxis.AlongZ, x, CorrSouth, CorrNorth, CorridorCeiling);
            }

            foreach (float z in new[] { -2.2f, -0.4f, 1.4f, 3.4f })
                AddBeam(b, WallAxis.AlongX, z, TailWest, CorrEast, CorridorCeiling);

            return b.ToMesh("Lab_CorridorShell");
        }

        /// <summary>
        /// The store, north-west of the corridor. Its south wall belongs to the corridor (which
        /// carries the door and window) and its east wall to the office, which needs that wall to
        /// run further north than the store does.
        /// </summary>
        private static Mesh BuildStoreShell()
        {
            const float t = WallThickness, h = RoomHeight;
            const PaletteUv.Family wall = PaletteUv.Family.NeutralCold;
            const int step = 4;

            var b = new ProcMesh.Builder();

            AddSlab(b, StoreWest - t, StoreEast + t, PartyZ, StoreNorth + t, -t, 0f,
                PaletteUv.Family.NeutralCold, 3);
            AddSlab(b, StoreWest - t, StoreEast + t, PartyZ, StoreNorth + t, StoreCeiling, StoreCeiling + t,
                PaletteUv.Family.NeutralCold, 9);
            AddSlab(b, StoreWest - t, StoreEast + t, PartyZ, StoreNorth + t, RoomHeight, RoomHeight + t,
                PaletteUv.Family.NeutralCold, 4);

            // The X wall owns the north-west corner and stops at StoreEast, where the office's own
            // west wall carries on north — both corners belong to exactly one solid.
            AddWall(b, WallAxis.AlongZ, StoreWest - t * 0.5f, PartyZ, StoreNorth, h, wall, step);
            AddWall(b, WallAxis.AlongX, StoreNorth + t * 0.5f, StoreWest - t, StoreEast, h, wall, step);

            AddSkirting(b, WallAxis.AlongZ, StoreWest, 1f, PartyZ, StoreNorth);
            AddSkirting(b, WallAxis.AlongZ, StoreEast, -1f, PartyZ, StoreNorth);
            AddSkirting(b, WallAxis.AlongX, StoreNorth, -1f, StoreWest, StoreEast);
            AddSkirting(b, WallAxis.AlongX, PartyZ, 1f, StoreWest, StoreEast, Opening.Door(StoreDoorX));

            AddBeam(b, WallAxis.AlongX, 7.2f, StoreWest, StoreEast, StoreCeiling);
            AddBeam(b, WallAxis.AlongX, 8.8f, StoreWest, StoreEast, StoreCeiling);

            return b.ToMesh("Lab_StoreShell");
        }

        /// <summary>
        /// The office, north-east of the corridor, with the recess that stops it being one more
        /// rectangle: the north wall steps back over 2.8 m into a nook with its own lower ceiling.
        /// The 0.4 m void that leaves above the nook is closed by the roof slab and by a header
        /// across the mouth — an unclosed one would be a hole looking straight out of the building.
        /// </summary>
        private static Mesh BuildOfficeShell()
        {
            const float t = WallThickness, h = RoomHeight;
            const PaletteUv.Family wall = PaletteUv.Family.NeutralWarm;
            const int step = 8;

            var b = new ProcMesh.Builder();

            // The nook's slabs start at the far side of the north wall rather than at its inner face,
            // so they butt against the office's own slabs instead of lying across them. Two floor
            // plates sharing a strip is two coplanar top faces, which is a shimmering band across the
            // threshold of the nook and nothing else.
            AddSlab(b, StoreEast, OfficeEast + t, PartyZ, OfficeNorth + t, -t, 0f,
                PaletteUv.Family.NeutralWarm, 9);
            AddSlab(b, NookWest - t, NookEast + t, OfficeNorth + t, NookNorth + t, -t, 0f,
                PaletteUv.Family.NeutralWarm, 9);
            AddSlab(b, StoreEast, OfficeEast + t, PartyZ, OfficeNorth + t, OfficeCeiling, OfficeCeiling + t,
                PaletteUv.Family.NeutralCold, 9);
            AddSlab(b, NookWest - t, NookEast + t, OfficeNorth + t, NookNorth + t, OfficeCeiling, OfficeCeiling + t,
                PaletteUv.Family.NeutralCold, 9);
            AddSlab(b, NookWest, NookEast, OfficeNorth + t, NookNorth, NookCeiling, NookCeiling + t,
                PaletteUv.Family.NeutralCold, 9);
            AddSlab(b, NookWest, NookEast, OfficeNorth, OfficeNorth + t, NookCeiling, OfficeCeiling,
                PaletteUv.Family.NeutralWarm, 8);

            // Same corner convention as the corridor and the store: the walls along X carry through
            // to the outer faces, the walls along Z stop against them.
            AddWall(b, WallAxis.AlongZ, StoreEast + t * 0.5f, PartyZ, OfficeNorth, h, wall, step);
            AddWall(b, WallAxis.AlongZ, OfficeEast + t * 0.5f, PartyZ, OfficeNorth, h, wall, step);
            AddWall(b, WallAxis.AlongX, OfficeNorth + t * 0.5f, StoreEast, NookWest, h, wall, step);
            AddWall(b, WallAxis.AlongX, OfficeNorth + t * 0.5f, NookEast, OfficeEast + t, h, wall, step);
            AddWall(b, WallAxis.AlongZ, NookWest - t * 0.5f, OfficeNorth + t, NookNorth, h, wall, step);
            AddWall(b, WallAxis.AlongZ, NookEast + t * 0.5f, OfficeNorth + t, NookNorth, h, wall, step);
            AddWall(b, WallAxis.AlongX, NookNorth + t * 0.5f, NookWest - t, NookEast + t, h, wall, step);

            AddSkirting(b, WallAxis.AlongZ, OfficeWest, 1f, PartyZ, OfficeNorth);
            AddSkirting(b, WallAxis.AlongZ, OfficeEast, -1f, PartyZ, OfficeNorth);
            AddSkirting(b, WallAxis.AlongX, OfficeNorth, -1f, OfficeWest, NookWest);
            AddSkirting(b, WallAxis.AlongX, OfficeNorth, -1f, NookEast, OfficeEast);
            AddSkirting(b, WallAxis.AlongX, PartyZ, 1f, OfficeWest, OfficeEast, Opening.Door(OfficeDoorX));
            AddSkirting(b, WallAxis.AlongZ, NookWest, 1f, OfficeNorth, NookNorth);
            AddSkirting(b, WallAxis.AlongZ, NookEast, -1f, OfficeNorth, NookNorth);
            AddSkirting(b, WallAxis.AlongX, NookNorth, -1f, NookWest, NookEast);

            AddBeam(b, WallAxis.AlongZ, NookWest, PartyZ, OfficeNorth, OfficeCeiling);
            AddBeam(b, WallAxis.AlongZ, NookEast, PartyZ, OfficeNorth, OfficeCeiling);

            return b.ToMesh("Lab_OfficeShell");
        }

        /// <summary>
        /// The covered loading dock behind the delivery bay (#33).
        /// <para>
        /// Without it the bay is a hole in the outer envelope — the player walks out of it into
        /// nothing — and the truck is parked in a void. Its east wall is the lab's own west wall, and
        /// it is 3.2 m to the underside so it closes exactly against the edge of the lab's roof slab
        /// with the truck's 2.85 m roof clearing under the beams.
        /// </para>
        /// The shutter at the far end is closed and does not open: deliveries arrive through the bay,
        /// and a door out of the building is not something this issue is adding.
        /// </summary>
        private static Mesh BuildDockShell()
        {
            const float t = WallThickness;
            const PaletteUv.Family wall = PaletteUv.Family.NeutralCold;
            const int step = 3;

            // The lab's west wall is this room's east wall, so the dock never builds one of its own.
            const float east = -RoomWidth * 0.5f - t;

            var b = new ProcMesh.Builder();

            AddSlab(b, DockWest - t, east, DockSouth - t, DockNorth + t, -t, 0f, PaletteUv.Family.Sump, 6);
            AddSlab(b, DockWest - t, east, DockSouth - t, DockNorth + t, DockHeight, DockHeight + t,
                PaletteUv.Family.NeutralCold, 5);

            // The two walls along X own both corners; the west wall stops against their inner faces.
            AddWall(b, WallAxis.AlongZ, DockWest - t * 0.5f, DockSouth, DockNorth, DockHeight, wall, step);
            AddWall(b, WallAxis.AlongX, DockSouth - t * 0.5f, DockWest - t, east, DockHeight, wall, step);
            AddWall(b, WallAxis.AlongX, DockNorth + t * 0.5f, DockWest - t, east, DockHeight, wall, step);

            AddSkirting(b, WallAxis.AlongZ, DockWest, 1f, DockSouth, DockNorth);
            AddSkirting(b, WallAxis.AlongX, DockSouth, 1f, DockWest, east);
            AddSkirting(b, WallAxis.AlongX, DockNorth, -1f, DockWest, east);
            AddSkirting(b, WallAxis.AlongZ, east, -1f, DockSouth, DockNorth,
                new Opening(BayCenterZ, BayWidth, 0f, BayHeight));

            AddBeam(b, WallAxis.AlongZ, -7.4f, DockSouth, DockNorth, DockHeight);
            AddBeam(b, WallAxis.AlongX, 0f, DockWest, east, DockHeight);
            AddBeam(b, WallAxis.AlongX, 3f, DockWest, east, DockHeight);

            return b.ToMesh("Lab_DockShell");
        }

        // -- Openings: reveals, sills and signage -------------------------------------------------

        private static void BuildOpeningTrim(GameObject parent, Material palette)
        {
            var doorFrame = SaveMesh(BuildDoorFrameMesh());
            var windowFrame = SaveMesh(BuildWindowFrameMesh());
            var sign = SaveMesh(ProcMesh.Box("Lab_SignPlate", new Vector3(0.26f, 0.16f, 0.012f),
                PaletteUv.Family.DeepBlue, 9));

            const float labNorth = RoomDepth * 0.5f + WallThickness * 0.5f;
            const float labEast = RoomWidth * 0.5f + WallThickness * 0.5f;
            const float partyWall = PartyZ - WallThickness * 0.5f;

            AddFramedDoor(parent, palette, doorFrame, sign, "Labor_Nord",
                WallAxis.AlongX, labNorth, LabNorthDoorX, LabNorthDoorX + 0.92f, 1f);
            AddFramedDoor(parent, palette, doorFrame, sign, "Labor_Ost",
                WallAxis.AlongZ, labEast, LabEastDoorZ, LabEastDoorZ - 0.92f, 1f);
            AddFramedDoor(parent, palette, doorFrame, sign, "Lager",
                WallAxis.AlongX, partyWall, StoreDoorX, StoreDoorX + 0.92f, -1f);
            AddFramedDoor(parent, palette, doorFrame, sign, "Buero",
                WallAxis.AlongX, partyWall, OfficeDoorX, OfficeDoorX + 0.92f, -1f);

            AddFramedWindow(parent, palette, windowFrame, "Labor_Ost",
                WallAxis.AlongZ, labEast, LabEastWindowZ);
            AddFramedWindow(parent, palette, windowFrame, "Lager", WallAxis.AlongX, partyWall, StoreWindowX);
            AddFramedWindow(parent, palette, windowFrame, "Buero", WallAxis.AlongX, partyWall, OfficeWindowX);
        }

        /// <summary>
        /// A door lining, and the plate beside it. <paramref name="signSide"/> is +1 when the plate
        /// belongs on the positive side of the wall — always the side you approach the door from.
        /// </summary>
        private static void AddFramedDoor(GameObject parent, Material palette, Mesh frame, Mesh sign,
                                          string name, WallAxis axis, float wallV, float u, float signU,
                                          float signSide)
        {
            float yaw = axis == WallAxis.AlongX ? 0f : 90f;
            var position = axis == WallAxis.AlongX ? new Vector3(u, 0f, wallV) : new Vector3(wallV, 0f, u);

            var go = AddStatic(parent, $"Tuerzarge_{name}", frame, palette, position, addCollider: true);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            float outward = (WallThickness * 0.5f + 0.008f) * signSide;
            var signPosition = axis == WallAxis.AlongX
                ? new Vector3(signU, 1.70f, wallV + outward)
                : new Vector3(wallV + outward, 1.70f, signU);

            var plate = AddStatic(parent, $"Schild_{name}", sign, palette, signPosition, addCollider: false);
            plate.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static void AddFramedWindow(GameObject parent, Material palette, Mesh frame, string name,
                                            WallAxis axis, float wallV, float u)
        {
            float y = (WindowSill + WindowHead) * 0.5f;
            var position = axis == WallAxis.AlongX ? new Vector3(u, y, wallV) : new Vector3(wallV, y, u);

            var go = AddStatic(parent, $"Innenfenster_{name}", frame, palette, position, addCollider: true);
            go.transform.localRotation = Quaternion.Euler(0f, axis == WallAxis.AlongX ? 0f : 90f, 0f);
        }

        /// <summary>
        /// The lining of a doorway, built once and instanced so every door in the building is the
        /// same door.
        /// <para>
        /// The reveal is <i>subtracted</i> from an oversized structural hole rather than added to a
        /// minimum one: 1.4 x 2.25 of hole less a 0.1 m lining leaves 1.2 x 2.15 clear, so no amount
        /// of trim can ever leave an opening narrower than the 0.64 m capsule that has to fit it.
        /// </para>
        /// </summary>
        private static Mesh BuildDoorFrameMesh()
        {
            const float w = DoorStructWidth, height = DoorStructHeight, d = DoorFrameDepth;
            const float depth = WallThickness + 0.06f;

            return new ProcMesh.Builder()
                .Box(new Vector3(-w * 0.5f + d * 0.5f, height * 0.5f, 0f), new Vector3(d, height, depth),
                    PaletteUv.Family.Steel, 7)
                .Box(new Vector3(w * 0.5f - d * 0.5f, height * 0.5f, 0f), new Vector3(d, height, depth),
                    PaletteUv.Family.Steel, 7)
                .Box(new Vector3(0f, height - d * 0.5f, 0f), new Vector3(w, d, depth),
                    PaletteUv.Family.Steel, 7)
                // A threshold strip, well under the controller's 0.35 m step offset so it is a
                // detail rather than something to catch on.
                .Box(new Vector3(0f, 0.01f, 0f), new Vector3(w, 0.02f, depth), PaletteUv.Family.Sump, 6)
                .ToMesh("Lab_DoorFrame");
        }

        /// <summary>
        /// An interior window's lining, sill and mullion. Every window in the building is between two
        /// enclosed rooms, and its sill at 1.10 m is above the 0.85 m the player's jump reaches, so
        /// glazing is genuinely absent rather than implied — there is nothing to see through it that
        /// is outside, and nothing to climb through it either.
        /// </summary>
        private static Mesh BuildWindowFrameMesh()
        {
            const float w = WindowStructWidth, d = 0.05f;
            const float height = WindowHead - WindowSill;
            const float depth = WallThickness + 0.06f;

            return new ProcMesh.Builder()
                .Box(new Vector3(0f, -height * 0.5f + d * 0.5f, 0f), new Vector3(w, d, depth),
                    PaletteUv.Family.Steel, 8)
                .Box(new Vector3(0f, height * 0.5f - d * 0.5f, 0f), new Vector3(w, d, depth),
                    PaletteUv.Family.Steel, 8)
                .Box(new Vector3(-w * 0.5f + d * 0.5f, 0f, 0f), new Vector3(d, height, depth),
                    PaletteUv.Family.Steel, 8)
                .Box(new Vector3(w * 0.5f - d * 0.5f, 0f, 0f), new Vector3(d, height, depth),
                    PaletteUv.Family.Steel, 8)
                .Box(Vector3.zero, new Vector3(0.06f, height - d * 2f, WallThickness + 0.02f),
                    PaletteUv.Family.Steel, 6)
                .Box(new Vector3(0f, -height * 0.5f - 0.02f, 0f),
                    new Vector3(w + 0.10f, 0.04f, depth + 0.12f), PaletteUv.Family.NeutralCold, 11)
                .ToMesh("Lab_WindowFrame");
        }

        // -- Furniture ----------------------------------------------------------------------------

        /// <summary>
        /// What makes each new room worth walking into. Nothing here is interactive and nothing here
        /// is near a station: every position was checked against the truck's parked footprint
        /// (x -9.7..-5.3, z 0.34..2.86) and against the doorways it must not stand in.
        /// </summary>
        private static void BuildBuildingFixtures(GameObject parent, Material palette)
        {
            var props = new GameObject("Einbauten");
            props.transform.SetParent(parent.transform, false);

            var locker = SaveMesh(new ProcMesh.Builder()
                .Box(new Vector3(0f, 0.95f, 0f), new Vector3(0.40f, 1.90f, 0.48f), PaletteUv.Family.Steel, 6)
                .Box(new Vector3(0f, 1.30f, 0.25f), new Vector3(0.03f, 0.18f, 0.02f), PaletteUv.Family.Sump, 4)
                .Box(new Vector3(0f, 1.80f, 0.25f), new Vector3(0.24f, 0.09f, 0.012f),
                    PaletteUv.Family.DeepBlue, 9)
                .ToMesh("Lab_Locker"));

            var crate = SaveMesh(new ProcMesh.Builder()
                .Box(new Vector3(0f, 0.22f, 0f), new Vector3(0.58f, 0.44f, 0.58f),
                    PaletteUv.Family.NeutralWarm, 8)
                .Box(new Vector3(0f, 0.22f, 0f), new Vector3(0.60f, 0.06f, 0.60f), PaletteUv.Family.Sump, 4)
                .ToMesh("Lab_Crate"));

            var drum = SaveMesh(new ProcMesh.Builder()
                .Cylinder(Vector3.zero, 0.29f, 0.86f, 14, PaletteUv.Family.Sump, 6)
                .Cylinder(new Vector3(0f, 0.24f, 0f), 0.305f, 0.05f, 14, PaletteUv.Family.Steel, 5)
                .Cylinder(new Vector3(0f, 0.57f, 0f), 0.305f, 0.05f, 14, PaletteUv.Family.Steel, 5)
                .ToMesh("Lab_Drum"));

            var pallet = SaveMesh(BuildPalletMesh());
            var shelving = SaveMesh(BuildShelvingMesh());
            var board = SaveMesh(new ProcMesh.Builder()
                .Box(new Vector3(0f, 0f, -0.012f), new Vector3(1.26f, 0.86f, 0.02f),
                    PaletteUv.Family.Sump, 5)
                .Box(Vector3.zero, new Vector3(1.18f, 0.78f, 0.03f), PaletteUv.Family.NeutralWarm, 12)
                .ToMesh("Lab_Pinboard"));

            // Corridor: a locker bank closing the dead end of the south leg, and a notice board at
            // the west one, so neither end of the corridor is a blank wall you walk up to.
            foreach (float x in new[] { 5.75f, 6.20f, 6.65f, 7.10f })
            {
                AddProp(props, $"Spind_{x:0.00}", locker, palette,
                    new Vector3(x, 0f, TailSouth + 0.24f), 0f, addCollider: true);
            }

            AddProp(props, "Anschlagtafel_Flur", board, palette,
                new Vector3(CorrWest + 0.02f, 1.55f, 5.30f), 90f, addCollider: false);

            // Store: two bays of shelving on the back wall, and floor stock clear of the doorway.
            AddProp(props, "Regal_West", shelving, palette,
                new Vector3(-3.9f, 0f, StoreNorth - 0.24f), 0f, addCollider: true);
            AddProp(props, "Regal_Ost", shelving, palette,
                new Vector3(-1.8f, 0f, StoreNorth - 0.24f), 0f, addCollider: true);
            AddProp(props, "Kiste_A", crate, palette, new Vector3(-4.50f, 0f, 7.30f), 12f, addCollider: true);
            AddProp(props, "Kiste_B", crate, palette, new Vector3(-3.85f, 0f, 7.25f), -6f, addCollider: true);
            AddProp(props, "Kiste_C", crate, palette, new Vector3(-4.46f, 0.44f, 7.32f), 4f, addCollider: true);
            AddProp(props, "Palette_Lager", pallet, palette, new Vector3(0.30f, 0f, 8.60f), 0f, addCollider: true);
            AddProp(props, "Fass_Lager", drum, palette, new Vector3(0.40f, 0.14f, 8.60f), 0f, addCollider: true);

            // Office: a desk in the nook facing back into the room, cabinets on the east wall, and a
            // board opposite them.
            var desk = SaveMesh(new ProcMesh.Builder()
                .Box(new Vector3(0f, 0.72f, 0f), new Vector3(1.60f, 0.05f, 0.72f),
                    PaletteUv.Family.NeutralWarm, 11)
                .Box(new Vector3(0f, 0.36f, -0.30f), new Vector3(1.50f, 0.68f, 0.03f),
                    PaletteUv.Family.Steel, 6)
                .Box(new Vector3(-0.75f, 0.35f, 0f), new Vector3(0.06f, 0.70f, 0.68f),
                    PaletteUv.Family.Steel, 5)
                .Box(new Vector3(0.75f, 0.35f, 0f), new Vector3(0.06f, 0.70f, 0.68f),
                    PaletteUv.Family.Steel, 5)
                .ToMesh("Lab_OfficeDesk"));

            var cabinet = SaveMesh(new ProcMesh.Builder()
                .Box(new Vector3(0f, 0.66f, 0f), new Vector3(0.48f, 1.32f, 0.60f), PaletteUv.Family.Steel, 7)
                .Box(new Vector3(0f, 0.35f, 0.31f), new Vector3(0.40f, 0.02f, 0.02f), PaletteUv.Family.Sump, 4)
                .Box(new Vector3(0f, 0.70f, 0.31f), new Vector3(0.40f, 0.02f, 0.02f), PaletteUv.Family.Sump, 4)
                .Box(new Vector3(0f, 1.05f, 0.31f), new Vector3(0.40f, 0.02f, 0.02f), PaletteUv.Family.Sump, 4)
                .ToMesh("Lab_FilingCabinet"));

            AddProp(props, "Schreibtisch", desk, palette,
                new Vector3(4.40f, 0f, NookNorth - 0.42f), 180f, addCollider: true);
            AddProp(props, "Aktenschrank_A", cabinet, palette,
                new Vector3(OfficeEast - 0.31f, 0f, 7.90f), -90f, addCollider: true);
            AddProp(props, "Aktenschrank_B", cabinet, palette,
                new Vector3(OfficeEast - 0.31f, 0f, 8.55f), -90f, addCollider: true);
            AddProp(props, "Anschlagtafel_Buero", board, palette,
                new Vector3(OfficeWest + 0.02f, 1.55f, 8.60f), 90f, addCollider: false);

            // Dock: a closed roller shutter at the far end, plus stock parked clear of the truck.
            AddProp(props, "Rolltor", SaveMesh(BuildRollerShutterMesh()), palette,
                new Vector3(DockWest + 0.03f, 0f, 1.50f), 90f, addCollider: false);
            AddProp(props, "Palette_Halle_A", pallet, palette,
                new Vector3(-8.00f, 0f, -0.45f), 0f, addCollider: true);
            AddProp(props, "Palette_Halle_B", pallet, palette,
                new Vector3(-9.20f, 0f, 3.45f), 8f, addCollider: true);
            AddProp(props, "Fass_Halle_A", drum, palette, new Vector3(-6.20f, 0f, -0.70f), 0f, addCollider: true);
            AddProp(props, "Fass_Halle_B", drum, palette, new Vector3(-6.85f, 0f, -0.62f), 0f, addCollider: true);
            AddProp(props, "Fass_Halle_C", drum, palette, new Vector3(-6.20f, 0f, 3.70f), 0f, addCollider: true);
        }

        private static Mesh BuildShelvingMesh()
        {
            const float w = 1.80f, d = 0.46f, h = 2.00f;
            var b = new ProcMesh.Builder();

            foreach (float x in new[] { -w * 0.5f + 0.03f, w * 0.5f - 0.03f })
            {
                foreach (float z in new[] { -d * 0.5f + 0.03f, d * 0.5f - 0.03f })
                    b.Box(new Vector3(x, h * 0.5f, z), new Vector3(0.06f, h, 0.06f), PaletteUv.Family.Steel, 5);
            }

            for (int i = 0; i < 4; i++)
                b.Box(new Vector3(0f, 0.35f + i * 0.52f, 0f), new Vector3(w, 0.04f, d),
                    PaletteUv.Family.Steel, 8);

            b.Box(new Vector3(0f, h - 0.03f, -d * 0.5f + 0.03f), new Vector3(w, 0.06f, 0.06f),
                PaletteUv.Family.Steel, 5);

            return b.ToMesh("Lab_Shelving");
        }

        private static Mesh BuildPalletMesh()
        {
            var b = new ProcMesh.Builder();

            foreach (float z in new[] { -0.36f, 0f, 0.36f })
                b.Box(new Vector3(0f, 0.05f, z), new Vector3(1.20f, 0.10f, 0.10f),
                    PaletteUv.Family.NeutralWarm, 7);

            for (int i = 0; i < 5; i++)
                b.Box(new Vector3(0f, 0.125f, -0.40f + i * 0.20f), new Vector3(1.20f, 0.03f, 0.12f),
                    PaletteUv.Family.NeutralWarm, 9);

            return b.ToMesh("Lab_Pallet");
        }

        private static Mesh BuildRollerShutterMesh()
        {
            var b = new ProcMesh.Builder()
                .Box(new Vector3(0f, 1.45f, 0f), new Vector3(3.60f, 2.90f, 0.05f), PaletteUv.Family.Steel, 4);

            for (int i = 0; i < 11; i++)
                b.Box(new Vector3(0f, 0.18f + i * 0.26f, 0.035f), new Vector3(3.60f, 0.22f, 0.03f),
                    PaletteUv.Family.Steel, 6);

            b.Box(new Vector3(0f, 3.02f, 0.06f), new Vector3(3.80f, 0.26f, 0.30f), PaletteUv.Family.Sump, 5);
            return b.ToMesh("Lab_RollerShutter");
        }

        private static GameObject AddProp(GameObject parent, string name, Mesh mesh, Material palette,
                                          Vector3 position, float yaw, bool addCollider)
        {
            var go = AddStatic(parent, name, mesh, palette, position, addCollider);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        // -- Lighting -----------------------------------------------------------------------------

        /// <summary>
        /// One fitting per bay in every new space. Nothing here is optional: the sun is outside a
        /// sealed shell and reaches none of these rooms, so a room without a lamp in it is a black
        /// room — and a room that looks lit without one is the inverted-face leak this file already
        /// carries a warning about, not a room that is lit.
        /// </summary>
        private static void BuildBuildingLighting(GameObject lightRoot, Material palette,
                                                  Material emissivePalette, Luminaire bigLamp)
        {
            // Shorter than the lab's fitting, so a run of them fits between the downstand beams.
            var slim = new Luminaire(
                SaveMesh(ProcMesh.Box("Lab_CorridorBallast", new Vector3(1.00f, 0.055f, 0.26f),
                    PaletteUv.Family.Steel, 5)),
                SaveMesh(ProcMesh.Box("Lab_CorridorLuminaire", new Vector3(0.90f, 0.035f, 0.20f),
                    PaletteUv.Family.NeutralCold, 15)),
                palette, emissivePalette);

            var corridorColour = new Color(0.84f, 0.90f, 1f);
            var corridor = new GameObject("Flur");
            corridor.transform.SetParent(lightRoot.transform, false);

            foreach (float x in new[] { -3.0f, -0.7f, 1.7f, 4.2f, 6.2f })
                slim.Place(corridor, $"Flurleuchte_West_{x:0.0}",
                    new Vector3(x, CorridorCeiling - 0.30f, 5.30f), 0f, 6.5f, 1.85f, corridorColour);

            foreach (float z in new[] { -1.3f, 0.4f, 2.4f })
                slim.Place(corridor, $"Flurleuchte_Sued_{z:0.0}",
                    new Vector3(6.30f, CorridorCeiling - 0.30f, z), 90f, 6.5f, 1.85f, corridorColour);

            var store = new GameObject("Lager");
            store.transform.SetParent(lightRoot.transform, false);
            foreach (float x in new[] { -3.4f, -0.8f })
                slim.Place(store, $"Lagerleuchte_{x:0.0}", new Vector3(x, StoreCeiling - 0.30f, 8.0f),
                    0f, 6.0f, 1.70f, new Color(0.86f, 0.90f, 0.98f));

            var office = new GameObject("Buero");
            office.transform.SetParent(lightRoot.transform, false);
            var officeColour = new Color(1f, 0.97f, 0.90f);
            foreach (var p in new[]
                     {
                         new Vector2(2.2f, 8.5f), new Vector2(4.4f, 7.5f),
                         new Vector2(4.4f, 9.5f), new Vector2(6.6f, 8.5f)
                     })
            {
                slim.Place(office, $"Bueroleuchte_{p.x:0.0}_{p.y:0.0}",
                    new Vector3(p.x, OfficeCeiling - 0.30f, p.y), 0f, 6.5f, 1.95f, officeColour);
            }

            slim.Place(office, "Bueroleuchte_Nische",
                new Vector3(4.40f, NookCeiling - 0.28f, 10.90f), 0f, 4.5f, 1.60f, officeColour);

            var dock = new GameObject("Verladehalle");
            dock.transform.SetParent(lightRoot.transform, false);
            foreach (var p in new[]
                     {
                         new Vector2(-6.2f, -0.6f), new Vector2(-8.8f, -0.6f),
                         new Vector2(-6.2f, 3.6f), new Vector2(-8.8f, 3.6f)
                     })
            {
                bigLamp.Place(dock, $"Hallenleuchte_{p.x:0.0}_{p.y:0.0}",
                    new Vector3(p.x, DockHeight - 0.35f, p.y), 0f, 9f, 2.40f, new Color(0.88f, 0.93f, 1f));
            }
        }

        /// <summary>
        /// The truck's exterior, parked outside the bay opening (#33).
        /// <para>
        /// Its own top-level scene root, inactive by default — <c>LabRuntime</c> has no notion of an
        /// active delivery yet, so nothing here decides when a truck should be visible. The delivery
        /// behaviour is expected to <c>SetActive(true)</c> this root for the length of a delivery and
        /// switch it off again once the cargo is unloaded.
        /// </para>
        /// Positioned nose-away-from-the-building: the cargo doors face the bay, the cab is furthest
        /// from it, so a player standing in the opening looks straight at the back of the load.
        /// </summary>
        /// <summary>
        /// The standing places inside the bay opening, and the switch that shows the truck.
        /// <para>
        /// Positioned just inside the room rather than out on the apron: the cartons have to be
        /// somewhere the player walks past, because #30's pressure is the choice between finishing a
        /// run and clearing the bay, and a delivery you cannot see is not a choice. Four places, from
        /// <see cref="DeliveryBay.DefaultCapacity"/> — the courier fills them and keeps the rest on the
        /// truck, so ignoring the bay costs shift time later rather than losing samples.
        /// </para>
        /// </summary>
        private static void BuildDeliveryBay(Scene scene, GameObject truck)
        {
            var go = NewRoot(scene, "DeliveryBay");
            go.transform.position = new Vector3(-RoomWidth * 0.5f + 0.9f, 0f, BayCenterZ);

            var station = go.AddComponent<DeliveryBayStation>();

            var so = new SerializedObject(station);
            so.FindProperty("truck").objectReferenceValue = truck;
            so.FindProperty("spacing").floatValue = 0.8f;
            so.FindProperty("columns").intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject BuildDeliveryTruck(Scene scene, Material palette)
        {
            var root = NewRoot(scene, "DeliveryTruck");
            root.transform.position =
                new Vector3(-RoomWidth * 0.5f - WallThickness - 0.3f, 0f, BayCenterZ);

            const float cargoLength = 3.0f, cargoWidth = 2.2f, cargoHeight = 2.4f, deckHeight = 0.45f;
            const float cabLength = 1.4f, cabWidth = 2.0f, cabHeight = 1.9f;

            var cargoMesh = SaveMesh(ProcMesh.Box("Truck_Cargo",
                new Vector3(cargoLength, cargoHeight, cargoWidth), PaletteUv.Family.NeutralCold, 9));
            AddChild(root, "Cargo", cargoMesh, palette,
                new Vector3(-cargoLength * 0.5f, deckHeight + cargoHeight * 0.5f, 0f), addCollider: true);

            // A shallow panel proud of the cargo box's rear face (x = 0, nearest the bay) so the
            // loading end reads as a pair of doors rather than just the end of a box.
            var doorMesh = SaveMesh(ProcMesh.Box("Truck_CargoDoors",
                new Vector3(0.03f, cargoHeight - 0.1f, cargoWidth - 0.1f), PaletteUv.Family.Steel, 6));
            AddChild(root, "CargoDoors", doorMesh, palette,
                new Vector3(-0.02f, deckHeight + cargoHeight * 0.5f, 0f), addCollider: false);

            var cabMesh = SaveMesh(ProcMesh.Box("Truck_Cab",
                new Vector3(cabLength, cabHeight, cabWidth), PaletteUv.Family.NeutralCold, 11));
            AddChild(root, "Cab", cabMesh, palette,
                new Vector3(-cargoLength - cabLength * 0.5f, deckHeight + cabHeight * 0.5f, 0f),
                addCollider: true);

            var chassisMesh = SaveMesh(ProcMesh.Box("Truck_Chassis",
                new Vector3(cargoLength + cabLength, deckHeight * 0.5f, cargoWidth * 0.8f),
                PaletteUv.Family.Sump, 3));
            AddChild(root, "Chassis", chassisMesh, palette,
                new Vector3(-(cargoLength + cabLength) * 0.5f, deckHeight * 0.25f, 0f), addCollider: false);

            var wheelMesh = SaveMesh(ProcMesh.Cylinder("Truck_Wheel", 0.40f, 0.28f, 14,
                PaletteUv.Family.Sump, 1));

            // Proud of the cargo box's own side faces, so the wheel is visibly outside the body
            // rather than flush with it and fighting its side face for the same plane.
            float halfTrack = cargoWidth * 0.5f + 0.08f;
            AddWheelPair(root, wheelMesh, palette, -0.55f, 0.40f, halfTrack, "Rear");
            AddWheelPair(root, wheelMesh, palette, -cargoLength - cabLength + 0.55f, 0.40f, halfTrack, "Front");

            root.SetActive(false);
            return root;
        }

        /// <summary>
        /// One axle's worth of wheels either side of the truck's centreline.
        /// <para>
        /// <see cref="ProcMesh.Cylinder"/> builds along local +Y with its flat cap faces normal to
        /// Y, so a wheel needs that axis rotated onto world Z (the truck's width) before the tread
        /// sweeps the right way. Both sides take the same rotation — the mesh is radially symmetric,
        /// so which way the caps happen to face does not matter.
        /// </para>
        /// </summary>
        private static void AddWheelPair(GameObject root, Mesh wheelMesh, Material palette,
                                         float x, float y, float halfTrack, string axleName)
        {
            var rotation = Quaternion.Euler(90f, 0f, 0f);

            var left = AddChild(root, $"Wheel_{axleName}L", wheelMesh, palette,
                new Vector3(x, y, -halfTrack), addCollider: false);
            left.transform.localRotation = rotation;

            var right = AddChild(root, $"Wheel_{axleName}R", wheelMesh, palette,
                new Vector3(x, y, halfTrack), addCollider: false);
            right.transform.localRotation = rotation;
        }

        // -- Simulation host ---------------------------------------------------------------------------

        private static LabRuntime BuildRuntime(Scene scene, ContentCatalog catalog,
                                               VialProp vialPrefab, PrintoutProp printoutPrefab,
                                               SolventBottle bottlePrefab,
                                               CartonProp cartonPrefab, DeliveryNoteProp notePrefab)
        {
            var go = new GameObject("LabRuntime");
            SceneManager.MoveGameObjectToScene(go, scene);
            var runtime = go.AddComponent<LabRuntime>();

            var so = new SerializedObject(runtime);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("vialPrefab").objectReferenceValue = vialPrefab;
            so.FindProperty("printoutPrefab").objectReferenceValue = printoutPrefab;
            so.FindProperty("bottlePrefab").objectReferenceValue = bottlePrefab;
            so.FindProperty("cartonPrefab").objectReferenceValue = cartonPrefab;
            so.FindProperty("notePrefab").objectReferenceValue = notePrefab;

            var ids = so.FindProperty("installedMachineIds");
            ids.arraySize = MachineIds.Length;
            for (int i = 0; i < MachineIds.Length; i++)
                ids.GetArrayElementAtIndex(i).stringValue = MachineIds[i];

            so.ApplyModifiedPropertiesWithoutUndo();
            return runtime;
        }

        // -- Stations ----------------------------------------------------------------------------------

        private static TerminalStation BuildStations(
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

                // The loading port is per-instrument now that the tops carry furnaces, baths and
                // rotor drums: it is wherever that machine's chassis has a clear patch left, and the
                // port collar in BuildMachineBody is cut at the same place so a vial always stands in
                // a socket rather than on a lid.
                var vialSocket = new GameObject("VialSocket");
                vialSocket.transform.SetParent(machineGo.transform, false);
                vialSocket.transform.localPosition =
                    new Vector3(visual.Port.x, visual.Size.y + 0.005f, visual.Port.y);

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

            // The intake crate is gone (#30). It spawned the day's vials directly on the bench at
            // 09:00, which is the teleporting-content problem the printout work removed everywhere
            // else — samples now arrive on a truck, in cartons somebody has to carry in and open.
            // Its bench stays: it is where a carton gets put down.

            // Terminal
            var terminalGo = new GameObject("Terminal");
            SceneManager.MoveGameObjectToScene(terminalGo, scene);
            terminalGo.transform.SetParent(root.transform, false);
            terminalGo.transform.position = new Vector3(RoomWidth * 0.5f - 1.1f, BenchHeight, 1.6f);

            var monitorMesh = SaveMesh(ProcMesh.Box("Terminal_Monitor", new Vector3(0.5f, 0.34f, 0.05f),
                PaletteUv.Family.DeepBlue, 5));
            AddChild(terminalGo, "Monitor", monitorMesh, palette, new Vector3(0f, 0.3f, 0f), addCollider: true);

            // A dark panel laid on the monitor's -Z face, which is the side the desk is worked from.
            // Not the screen material: this terminal's readout is the UI overlay, so what the geometry
            // has to say is "there is glass here", and a palette step does that without claiming a
            // second texture exception. No collider — the Monitor behind it is the interaction target.
            var monitorFaceMesh = SaveMesh(ProcMesh.Box("Terminal_MonitorFace",
                new Vector3(0.44f, 0.27f, 0.008f), PaletteUv.Family.DeepBlue, 1));
            AddChild(terminalGo, "MonitorFace", monitorFaceMesh, palette,
                new Vector3(0f, 0.315f, -0.029f), addCollider: false);

            var terminal = terminalGo.AddComponent<TerminalStation>();

            BuildBookRack(root, scene, palette, books);

            return terminal;
        }

        // -- Machine visuals ---------------------------------------------------------------------------

        /// <summary>
        /// Which instrument a chassis is wearing.
        /// <para>
        /// §5.2 gives every machine a real identity — a 900 s cooling curve run, a titration, a
        /// heated bath, a spun bowl — and a rack of five near-identical pale boxes throws all of it
        /// away at exactly the moment the player is scanning the bench to decide which one to walk
        /// to. The chassis stays a shared family so the lab still looks like one lab; what is bolted
        /// to the top of it is what the instrument actually is.
        /// </para>
        /// </summary>
        private enum MachineForm
        {
            Generic,
            CoolingCurve,
            KarlFischer,
            Viscometer,
            FlashPoint,
            AcidTitrator,
            Centrifuge,
            Elemental
        }

        /// <summary>Per-instrument proportions, so they are distinguishable by silhouette alone.</summary>
        private readonly struct MachineVisual
        {
            public readonly Vector3 Size;
            public readonly Vector2 ScreenSize;
            public readonly float ScreenY;
            public readonly bool Panel;
            public readonly int Dials;
            public readonly int Vents;
            public readonly MachineForm Form;
            public readonly PaletteUv.Family Body;
            public readonly int BodyStep;

            /// <summary>Where the vial port is cut in the chassis top, as (x, z) in machine space.
            /// Not always the centre any more: the centre is where the interesting geometry is.</summary>
            public readonly Vector2 Port;

            public MachineVisual(Vector3 size, Vector2 screenSize, float screenY, bool panel,
                                 int dials, int vents, MachineForm form, PaletteUv.Family body,
                                 int bodyStep, Vector2 port)
            {
                Size = size; ScreenSize = screenSize; ScreenY = screenY;
                Panel = panel; Dials = dials; Vents = vents;
                Form = form; Body = body; BodyStep = bodyStep; Port = port;
            }
        }

        // Every chassis obeys the same three rules, because BuildStations anchors real components to
        // them: the screen bezel must clear the button row at y 0.14..0.18 and stay inside the body;
        // the status light sits at (Size.x/2 - 0.08, Size.y - 0.04) on the front face and must not
        // land on the bezel; and the dial row runs along the front-left of the chassis top, so every
        // superstructure below keeps out of that strip.
        private static MachineVisual VisualFor(string id) => id switch
        {
            // The centrepiece and the expensive option: a tube furnace, a quench vessel and a probe
            // on a gantry. Nearly a metre of instrument above the bench, and the only one you can
            // pick out of the row from the doorway.
            "cooling_curve" => new MachineVisual(new Vector3(0.80f, 0.50f, 0.52f), new Vector2(0.42f, 0.24f),
                0.34f, true, 3, 8, MachineForm.CoolingCurve, PaletteUv.Family.NeutralCold, 9,
                new Vector2(0f, -0.06f)),

            // Glass: a titration cell under a capped head, a burette on its stand and two reagent
            // bottles. Small box, tall glassware.
            "karl_fischer" => new MachineVisual(new Vector3(0.56f, 0.36f, 0.42f), new Vector2(0.22f, 0.10f),
                0.27f, false, 3, 2, MachineForm.KarlFischer, PaletteUv.Family.NeutralWarm, 10,
                new Vector2(0f, -0.06f)),

            // It preheats, and the bath is why: an open tank of oil with three capillary tubes
            // standing in it. The heat is the silhouette.
            "viscometer" => new MachineVisual(new Vector3(0.56f, 0.36f, 0.44f), new Vector2(0.20f, 0.10f),
                0.27f, false, 2, 5, MachineForm.Viscometer, PaletteUv.Family.Steel, 9,
                new Vector2(0.16f, -0.06f)),

            // Closed cup, hinged lid arm, ignition head over it.
            "flash_point" => new MachineVisual(new Vector3(0.50f, 0.34f, 0.44f), new Vector2(0.20f, 0.10f),
                0.26f, false, 2, 3, MachineForm.FlashPoint, PaletteUv.Family.NeutralCold, 7,
                new Vector2(0.15f, -0.06f)),

            // A titrator like the Karl Fischer, but with the carousel that says it runs samples in
            // batches — the two are meant to be confusable at a distance and not up close.
            "tan_titrator" => new MachineVisual(new Vector3(0.56f, 0.36f, 0.44f), new Vector2(0.22f, 0.12f),
                0.28f, false, 3, 3, MachineForm.AcidTitrator, PaletteUv.Family.NeutralWarm, 7,
                new Vector2(-0.20f, -0.11f)),

            // Squat and heavy, all drum: a latched rotor bowl on a hinge.
            "centrifuge" => new MachineVisual(new Vector3(0.58f, 0.34f, 0.54f), new Vector2(0.20f, 0.09f),
                0.26f, false, 2, 3, MachineForm.Centrifuge, PaletteUv.Family.Steel, 11,
                new Vector2(0.22f, 0.19f)),

            // Widest of the five: a shielded sample chamber, an optics tower and a carrier gas
            // bottle strapped between them.
            "elemental" => new MachineVisual(new Vector3(0.72f, 0.46f, 0.50f), new Vector2(0.32f, 0.20f),
                0.32f, true, 2, 4, MachineForm.Elemental, PaletteUv.Family.NeutralCold, 6,
                new Vector2(0.06f, 0.13f)),

            _ => new MachineVisual(new Vector3(0.55f, 0.40f, 0.46f), new Vector2(0.26f, 0.14f),
                0.28f, false, 2, 4, MachineForm.Generic, PaletteUv.Family.NeutralWarm, 7,
                new Vector2(0f, -0.06f))
        };

        /// <summary>
        /// One mesh for an instrument: the shared chassis — body, feet, screen bezel, dials, vent
        /// fins, output slot and vial port — plus whatever <see cref="MachineForm"/> bolts to the top
        /// of it. Pivot at base centre per §2.1, so the machine sits on whatever it is placed on.
        /// </summary>
        private static Mesh BuildMachineBody(string name, MachineVisual v)
        {
            var b = new ProcMesh.Builder();
            float front = v.Size.z * 0.5f;
            float top = v.Size.y;
            const float footHeight = 0.018f;

            float bodyHeight = v.Size.y - footHeight;
            b.Box(new Vector3(0f, footHeight + bodyHeight * 0.5f, 0f),
                new Vector3(v.Size.x, bodyHeight, v.Size.z), v.Body, v.BodyStep);

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

            // Dials along the front-LEFT of the top lip. Cylinders build along +Y, so these read as
            // knobs rather than dials on the face. Left rather than right because the status light
            // owns the front-right corner and every superstructure below is laid out around this
            // strip.
            for (int i = 0; i < v.Dials; i++)
            {
                float x = -v.Size.x * 0.5f + 0.075f + i * 0.075f;
                b.Cylinder(new Vector3(x, top - 0.002f, front - 0.06f), 0.024f, 0.016f, 16,
                    PaletteUv.Family.Brass, 9);
            }

            // Vent fins down one side. They stand PROUD of the side face rather than flush with it:
            // flush meant the fin's outer face and the body's side face were the same plane, which
            // is a shimmering stripe down every instrument in the row.
            for (int i = 0; i < v.Vents; i++)
            {
                float y = footHeight + 0.05f + i * 0.030f;
                if (y > v.Size.y - 0.05f) break;
                b.Box(new Vector3(-v.Size.x * 0.5f - 0.005f, y, 0f),
                    new Vector3(0.010f, 0.012f, v.Size.z * 0.55f), PaletteUv.Family.Sump, 3);
            }

            // Output slot the printout emerges from.
            b.Box(new Vector3(0f, 0.075f, front - 0.006f), new Vector3(0.20f, 0.016f, 0.014f),
                PaletteUv.Family.Sump, 1);

            // Top-loading port for the vial, wherever this instrument had room to put it.
            b.Cylinder(new Vector3(v.Port.x, top - 0.006f, v.Port.y), 0.032f, 0.008f, 16,
                PaletteUv.Family.Sump, 2);

            switch (v.Form)
            {
                case MachineForm.CoolingCurve: AddCoolingCurveRig(b, top); break;
                case MachineForm.KarlFischer: AddKarlFischerRig(b, top); break;
                case MachineForm.Viscometer: AddViscometerRig(b, top); break;
                case MachineForm.FlashPoint: AddFlashPointRig(b, top); break;
                case MachineForm.AcidTitrator: AddAcidTitratorRig(b, top); break;
                case MachineForm.Centrifuge: AddCentrifugeRig(b, top); break;
                case MachineForm.Elemental: AddElementalRig(b, top); break;
                default: AddGenericRig(b, top); break;
            }

            return b.ToMesh(name);
        }

        // -- Per-instrument superstructure -----------------------------------------------------------
        //
        // Everything below is authored in machine space with y measured from the bench, and every
        // piece stacks on the one under it rather than intersecting it, so no two solids in an
        // instrument share volume either. Palette use is restricted to the neutrals, steel, brass,
        // deep blue, sump and coolant: hard rule 4 puts the signal row out of bounds, and the oxide
        // and solvent rows are the two that come close enough to amber and green to be worth
        // avoiding on something the player looks at while reading a verdict.

        /// <summary>Tube furnace, quench vessel and a probe hung from a gantry (§5.2's 900 s run).</summary>
        private static void AddCoolingCurveRig(ProcMesh.Builder b, float top)
        {
            b.Cylinder(new Vector3(-0.24f, top, -0.02f), 0.135f, 0.30f, 16, PaletteUv.Family.Steel, 6);
            b.Cylinder(new Vector3(-0.24f, top + 0.30f, -0.02f), 0.150f, 0.035f, 16,
                PaletteUv.Family.NeutralCold, 5);
            b.Cylinder(new Vector3(-0.24f, top + 0.335f, -0.02f), 0.030f, 0.030f, 12,
                PaletteUv.Family.Brass, 8);

            b.Cylinder(new Vector3(0.20f, top, -0.02f), 0.095f, 0.22f, 16, PaletteUv.Family.Steel, 9);
            b.Cylinder(new Vector3(0.20f, top + 0.22f, -0.02f), 0.105f, 0.025f, 16,
                PaletteUv.Family.Sump, 4);

            b.Box(new Vector3(0.36f, top + 0.23f, -0.02f), new Vector3(0.05f, 0.46f, 0.06f),
                PaletteUv.Family.Steel, 5);
            b.Box(new Vector3(0.2325f, top + 0.44f, -0.02f), new Vector3(0.205f, 0.04f, 0.05f),
                PaletteUv.Family.Steel, 5);
            b.Box(new Vector3(0.20f, top + 0.385f, -0.02f), new Vector3(0.05f, 0.07f, 0.05f),
                PaletteUv.Family.Sump, 5);
            b.Cylinder(new Vector3(0.20f, top + 0.245f, -0.02f), 0.011f, 0.105f, 10,
                PaletteUv.Family.Brass, 6);
        }

        /// <summary>Titration cell, burette on its stand, and the reagent bottles feeding it.</summary>
        private static void AddKarlFischerRig(ProcMesh.Builder b, float top)
        {
            b.Cylinder(new Vector3(-0.14f, top, 0.02f), 0.055f, 0.17f, 14, PaletteUv.Family.Coolant, 12);
            b.Cylinder(new Vector3(-0.14f, top + 0.17f, 0.02f), 0.065f, 0.025f, 14,
                PaletteUv.Family.Sump, 5);
            foreach (float x in new[] { -0.175f, -0.105f })
                b.Cylinder(new Vector3(x, top + 0.195f, 0.02f), 0.008f, 0.05f, 8, PaletteUv.Family.Steel, 6);

            b.Cylinder(new Vector3(0.10f, top, 0.02f), 0.020f, 0.34f, 12, PaletteUv.Family.Coolant, 13);
            b.Box(new Vector3(0.10f, top + 0.36f, 0.02f), new Vector3(0.07f, 0.04f, 0.07f),
                PaletteUv.Family.Steel, 5);
            b.Box(new Vector3(0.17f, top + 0.20f, 0.02f), new Vector3(0.035f, 0.40f, 0.035f),
                PaletteUv.Family.Steel, 4);
            b.Box(new Vector3(0.13625f, top + 0.24f, 0.02f), new Vector3(0.0325f, 0.025f, 0.03f),
                PaletteUv.Family.Steel, 6);

            foreach (float z in new[] { -0.09f, 0.09f })
            {
                b.Cylinder(new Vector3(0.235f, top, z), 0.040f, 0.15f, 12, PaletteUv.Family.NeutralCold, 3);
                b.Cylinder(new Vector3(0.235f, top + 0.15f, z), 0.022f, 0.025f, 10,
                    PaletteUv.Family.DeepBlue, 6);
            }
        }

        /// <summary>The heated bath and the capillaries standing in it — this is the one that
        /// preheats, and the bath of oil is why.</summary>
        private static void AddViscometerRig(ProcMesh.Builder b, float top)
        {
            const float cx = -0.12f, cz = -0.10f;

            b.Box(new Vector3(cx, top + 0.10f, cz + 0.09f), new Vector3(0.30f, 0.20f, 0.02f),
                PaletteUv.Family.Steel, 7);
            b.Box(new Vector3(cx, top + 0.10f, cz - 0.09f), new Vector3(0.30f, 0.20f, 0.02f),
                PaletteUv.Family.Steel, 7);
            b.Box(new Vector3(cx - 0.14f, top + 0.10f, cz), new Vector3(0.02f, 0.20f, 0.16f),
                PaletteUv.Family.Steel, 7);
            b.Box(new Vector3(cx + 0.14f, top + 0.10f, cz), new Vector3(0.02f, 0.20f, 0.16f),
                PaletteUv.Family.Steel, 7);

            // The oil, filling the tank exactly to its inner faces.
            b.Box(new Vector3(cx, top + 0.075f, cz), new Vector3(0.26f, 0.15f, 0.16f),
                PaletteUv.Family.Sump, 5);

            // Capillaries in three pieces so the timing bulb sits between them instead of inside.
            foreach (float dx in new[] { -0.07f, 0f, 0.07f })
            {
                b.Cylinder(new Vector3(cx + dx, top + 0.15f, cz), 0.011f, 0.09f, 8,
                    PaletteUv.Family.Coolant, 13);
                b.Cylinder(new Vector3(cx + dx, top + 0.24f, cz), 0.022f, 0.035f, 10,
                    PaletteUv.Family.Coolant, 12);
                b.Cylinder(new Vector3(cx + dx, top + 0.275f, cz), 0.011f, 0.135f, 8,
                    PaletteUv.Family.Coolant, 13);
            }

            // Thermostat head at the back-right, clear of the port collar in front of it.
            b.Box(new Vector3(0.19f, top + 0.07f, -0.16f), new Vector3(0.12f, 0.14f, 0.12f),
                PaletteUv.Family.Steel, 5);
            b.Cylinder(new Vector3(0.19f, top + 0.14f, -0.16f), 0.030f, 0.02f, 12,
                PaletteUv.Family.Brass, 8);
        }

        /// <summary>Closed cup, hinged lid arm, ignition head swung over it.</summary>
        private static void AddFlashPointRig(ProcMesh.Builder b, float top)
        {
            b.Cylinder(new Vector3(-0.07f, top, -0.04f), 0.085f, 0.10f, 14, PaletteUv.Family.Steel, 6);
            b.Cylinder(new Vector3(-0.07f, top + 0.10f, -0.04f), 0.095f, 0.025f, 14,
                PaletteUv.Family.Steel, 9);
            b.Cylinder(new Vector3(-0.11f, top + 0.125f, -0.075f), 0.008f, 0.20f, 8,
                PaletteUv.Family.Coolant, 12);

            b.Box(new Vector3(0.15f, top + 0.13f, -0.12f), new Vector3(0.04f, 0.26f, 0.04f),
                PaletteUv.Family.Steel, 4);
            b.Box(new Vector3(0.045f, top + 0.245f, -0.12f), new Vector3(0.17f, 0.03f, 0.035f),
                PaletteUv.Family.Steel, 5);
            b.Box(new Vector3(-0.04f, top + 0.19f, -0.12f), new Vector3(0.05f, 0.08f, 0.05f),
                PaletteUv.Family.Sump, 5);
            b.Cylinder(new Vector3(-0.04f, top + 0.125f, -0.12f), 0.008f, 0.025f, 8,
                PaletteUv.Family.Brass, 9);
        }

        /// <summary>Burette and electrode over a six-place sample carousel.</summary>
        private static void AddAcidTitratorRig(ProcMesh.Builder b, float top)
        {
            b.Cylinder(new Vector3(0.14f, top, -0.02f), 0.135f, 0.03f, 16, PaletteUv.Family.Steel, 5);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                b.Cylinder(new Vector3(0.14f + Mathf.Cos(a) * 0.085f, top + 0.03f,
                        -0.02f + Mathf.Sin(a) * 0.085f),
                    0.030f, 0.07f, 10, PaletteUv.Family.Coolant, 12);
            }

            b.Cylinder(new Vector3(-0.06f, top, -0.10f), 0.020f, 0.32f, 12, PaletteUv.Family.Coolant, 13);
            b.Box(new Vector3(-0.12f, top + 0.20f, -0.10f), new Vector3(0.035f, 0.40f, 0.035f),
                PaletteUv.Family.Steel, 4);
            b.Box(new Vector3(-0.09125f, top + 0.26f, -0.10f), new Vector3(0.0225f, 0.025f, 0.03f),
                PaletteUv.Family.Steel, 6);
            b.Box(new Vector3(0.019f, top + 0.34f, -0.10f), new Vector3(0.243f, 0.03f, 0.035f),
                PaletteUv.Family.Steel, 5);
            b.Cylinder(new Vector3(0.10f, top + 0.24f, -0.10f), 0.010f, 0.085f, 8,
                PaletteUv.Family.DeepBlue, 6);
        }

        /// <summary>A latched rotor bowl on a hinge. Squat, and all drum.</summary>
        private static void AddCentrifugeRig(ProcMesh.Builder b, float top)
        {
            // Seat, bowl, lid. This is the widest curved surface in the room at standing height, so
            // it is where the segment count is worth spending — a faceted drum is the one place the
            // flat-shaded look stops reading as deliberate.
            b.Cylinder(new Vector3(-0.02f, top, -0.05f), 0.195f, 0.02f, 24, PaletteUv.Family.Sump, 4);
            b.Cylinder(new Vector3(-0.02f, top + 0.02f, -0.05f), 0.185f, 0.14f, 24,
                PaletteUv.Family.Steel, 7);
            b.Cylinder(new Vector3(-0.02f, top + 0.16f, -0.05f), 0.195f, 0.03f, 24,
                PaletteUv.Family.NeutralCold, 11);

            // Sight port, so a closed lid still says something spins under it.
            b.Cylinder(new Vector3(-0.02f, top + 0.19f, -0.05f), 0.075f, 0.012f, 20,
                PaletteUv.Family.Steel, 4);
            b.Cylinder(new Vector3(-0.02f, top + 0.202f, -0.05f), 0.062f, 0.004f, 20,
                PaletteUv.Family.DeepBlue, 2);

            b.Box(new Vector3(-0.02f, top + 0.07625f, -0.26f), new Vector3(0.08f, 0.1525f, 0.02f),
                PaletteUv.Family.Sump, 4);
            b.Box(new Vector3(-0.02f, top + 0.175f, -0.26f), new Vector3(0.10f, 0.045f, 0.02f),
                PaletteUv.Family.Sump, 4);
            b.Box(new Vector3(-0.02f, top + 0.205f, 0.125f), new Vector3(0.09f, 0.03f, 0.03f),
                PaletteUv.Family.Brass, 8);
            b.Box(new Vector3(0.22f, top + 0.015f, -0.05f), new Vector3(0.05f, 0.03f, 0.03f),
                PaletteUv.Family.Sump, 3);
        }

        /// <summary>Shielded sample chamber, optics tower, and the carrier gas bottle between.</summary>
        private static void AddElementalRig(ProcMesh.Builder b, float top)
        {
            b.Box(new Vector3(-0.20f, top + 0.07f, -0.02f), new Vector3(0.28f, 0.14f, 0.28f),
                PaletteUv.Family.NeutralCold, 4);
            // Round shielded port rather than a square hatch: it is what says "do not open this while
            // it is running" without a decal, and the style contract has no decals.
            b.Cylinder(new Vector3(-0.20f, top + 0.14f, -0.02f), 0.115f, 0.03f, 20,
                PaletteUv.Family.Steel, 8);
            b.Box(new Vector3(-0.20f, top + 0.185f, 0.06f), new Vector3(0.10f, 0.03f, 0.025f),
                PaletteUv.Family.Brass, 8);

            b.Box(new Vector3(0.22f, top + 0.15f, -0.03f), new Vector3(0.18f, 0.30f, 0.22f),
                PaletteUv.Family.NeutralCold, 7);
            for (int i = 0; i < 4; i++)
            {
                b.Box(new Vector3(0.22f, top + 0.18f + i * 0.030f, 0.086f),
                    new Vector3(0.16f, 0.012f, 0.012f), PaletteUv.Family.Sump, 3);
            }

            b.Box(new Vector3(0.22f, top + 0.35f, -0.03f), new Vector3(0.14f, 0.10f, 0.16f),
                PaletteUv.Family.Steel, 6);
            b.Cylinder(new Vector3(0.22f, top + 0.40f, -0.03f), 0.035f, 0.08f, 20,
                PaletteUv.Family.Steel, 4);

            // Carrier gas: bottle, neck, regulator and gauge, standing between the two.
            b.Cylinder(new Vector3(0f, top, -0.15f), 0.045f, 0.30f, 20, PaletteUv.Family.Steel, 5);
            b.Cylinder(new Vector3(0f, top + 0.30f, -0.15f), 0.018f, 0.05f, 12,
                PaletteUv.Family.Brass, 7);
            b.Box(new Vector3(0.063f, top + 0.33f, -0.15f), new Vector3(0.09f, 0.06f, 0.06f),
                PaletteUv.Family.Brass, 6);
            b.Cylinder(new Vector3(0.063f, top + 0.36f, -0.15f), 0.028f, 0.015f, 20,
                PaletteUv.Family.NeutralCold, 12);
        }

        /// <summary>Whatever an unrecognised machine id gets: a hood and a stack, so a new instrument
        /// still reads as an instrument rather than as an unfinished box.</summary>
        private static void AddGenericRig(ProcMesh.Builder b, float top)
        {
            b.Box(new Vector3(0.06f, top + 0.06f, 0.04f), new Vector3(0.30f, 0.12f, 0.24f),
                PaletteUv.Family.Steel, 7);
            b.Cylinder(new Vector3(0.06f, top + 0.12f, 0.04f), 0.030f, 0.16f, 12,
                PaletteUv.Family.Steel, 5);
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

            // The room, and nothing in the player's own hands — those belong to the overlay below.
            camera.cullingMask &= ~(1 << HeldItemCamera.HeldItemLayer);
            cameraGo.AddComponent<AudioListener>();
            cameraGo.tag = "MainCamera";

            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;

            var carry = new GameObject("CarrySocket");
            carry.transform.SetParent(rigGo.transform, false);
            carry.transform.localPosition = new Vector3(0.22f, -0.18f, 0.42f);

            var heldItems = BuildHeldItemCamera(cameraGo, camera, cameraData, carry.transform);

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

            // Both of these are built after the layer sweep above, so each sets its own layer: the
            // hands go on the held-item layer so only the overlay camera draws them, and the body
            // goes on its own layer so the owner's camera can cull it while everyone else's still
            // sees it. Neither needs Ignore Raycast — the hands carry no colliders at all, and a
            // carried item has its colliders switched off by Carryable.AttachTo.
            var hands = BuildHands(rigGo, palette, player, interactor);
            SetLayerRecursively(hands, HeldItemCamera.HeldItemLayer);
            BuildCharacterBody(go, palette, player, interactor);

            var thirdPerson = go.AddComponent<ThirdPersonView>();
            var thirdSo = new SerializedObject(thirdPerson);
            thirdSo.FindProperty("player").objectReferenceValue = player;
            thirdSo.FindProperty("eyeCamera").objectReferenceValue = camera;
            thirdSo.FindProperty("hands").objectReferenceValue = hands;
            thirdSo.FindProperty("heldItems").objectReferenceValue = heldItems;
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
        /// §2.6's second camera: an overlay stacked on the eye camera that draws the hands and
        /// whatever is in them, and nothing else.
        /// <para>
        /// Parented to the eye camera rather than to the rig, so it inherits every transform written
        /// to the eye — including the one <see cref="ThirdPersonView"/> writes on F4 — without a
        /// follow script that could disagree with it by a frame.
        /// </para>
        /// The 0.01 m near clip is the §2.6 requirement. The depth clear that comes free with an
        /// overlay is the part that actually stops a bench slicing through a vial; see
        /// <see cref="HeldItemCamera"/>.
        /// </summary>
        private static HeldItemCamera BuildHeldItemCamera(GameObject eyeGo, Camera eye,
                                                          UniversalAdditionalCameraData eyeData,
                                                          Transform carrySocket)
        {
            var go = new GameObject("HeldItemCamera");
            go.transform.SetParent(eyeGo.transform, false);

            var overlay = go.AddComponent<Camera>();
            overlay.nearClipPlane = 0.01f;

            // Nothing held is ever more than an arm away; a 60 m frustum here would only cost
            // culling work on a layer with three objects in it.
            overlay.farClipPlane = 3f;
            overlay.fieldOfView = eye.fieldOfView;
            overlay.cullingMask = 1 << HeldItemCamera.HeldItemLayer;

            var overlayData = overlay.GetUniversalAdditionalCameraData();

            // Order matters: the stack refuses a camera that is not already an Overlay.
            overlayData.renderType = CameraRenderType.Overlay;

            // The base camera grades and antialiases the whole stack. Asking the overlay to do it
            // again would tone-map the hands twice.
            overlayData.renderPostProcessing = false;

            var stack = eyeData.cameraStack;
            if (stack != null && !stack.Contains(overlay)) stack.Add(overlay);

            var component = go.AddComponent<HeldItemCamera>();
            var so = new SerializedObject(component);
            so.FindProperty("baseCamera").objectReferenceValue = eye;
            so.FindProperty("overlayCamera").objectReferenceValue = overlay;
            so.FindProperty("handSocket").objectReferenceValue = carrySocket;
            so.ApplyModifiedPropertiesWithoutUndo();

            return component;
        }

        /// <summary>
        /// Forearm and palm per side, hung off the camera rig so head bob carries into them for free.
        /// Left on the held-item layer by the caller, so they are drawn by the overlay camera and can
        /// no longer be sliced open by a wall the player is standing against.
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
        /// A delivered carton, with a lid kept as a separate transform so opening it reads visually
        /// instead of the whole box just vanishing (#33).
        /// <para>
        /// Geometry only — no <see cref="Carryable"/> subclass is attached here, per this task's
        /// brief. The delivery behaviour is expected to add one and address the child named "Lid" by
        /// name to move or hide it when the carton opens, the same way <c>MachineStation</c> already
        /// addresses named sockets on the machine bodies above.
        /// </para>
        /// </summary>
        private static CartonProp BuildCartonPrefab(Material palette)
        {
            string path = $"{PrefabFolder}/Carton.prefab";
            var go = new GameObject("Carton");

            const float width = 0.40f, depth = 0.30f, bodyHeight = 0.26f, lidHeight = 0.04f;

            var bodyMesh = SaveMesh(ProcMesh.Box("Carton_Body",
                new Vector3(width, bodyHeight, depth), PaletteUv.Family.NeutralWarm, 8));
            var strapMesh = SaveMesh(ProcMesh.Box("Carton_Strap",
                new Vector3(width + 0.004f, bodyHeight + 0.004f, 0.03f), PaletteUv.Family.Sump, 4));
            var lidMesh = SaveMesh(ProcMesh.Box("Carton_Lid",
                new Vector3(width + 0.02f, lidHeight, depth + 0.02f), PaletteUv.Family.NeutralWarm, 5));

            AddChild(go, "Body", bodyMesh, palette, new Vector3(0f, bodyHeight * 0.5f, 0f), addCollider: false);
            AddChild(go, "Strap", strapMesh, palette, new Vector3(0f, bodyHeight * 0.5f, 0f), addCollider: false);

            // Sibling of Body, not its child: whatever opens the carton moves or hides this object on
            // its own, and it must keep working even if Body is later swapped, pooled or destroyed.
            AddChild(go, "Lid", lidMesh, palette,
                new Vector3(0f, bodyHeight + lidHeight * 0.5f, 0f), addCollider: false);

            // The root collider covers the BODY only, not the lid. The two are separate targets: the
            // body is what you pick up, the lid is what you hold Interact on to cut the box open. One
            // collider spanning both would put the lid inside the body's bounds, the ray would resolve
            // to the carton every time, and the box could never be opened.
            var collider = go.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, bodyHeight * 0.5f, 0f);
            collider.size = new Vector3(width + 0.02f, bodyHeight, depth + 0.02f);

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var carton = go.AddComponent<CartonProp>();
            var cartonSo = new SerializedObject(carton);
            cartonSo.FindProperty("lid").objectReferenceValue = go.transform.Find("Lid");
            cartonSo.ApplyModifiedPropertiesWithoutUndo();

            // The lid carries its own collider and its own Interactable, so it can be aimed at
            // independently of the box it sits on.
            var lid = go.transform.Find("Lid").gameObject;
            var lidCollider = lid.AddComponent<BoxCollider>();
            lidCollider.size = new Vector3(width + 0.02f, lidHeight, depth + 0.02f);
            lid.AddComponent<CartonLid>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<CartonProp>();
        }

        /// <summary>
        /// The note that rides along with a delivery, styled like <see cref="PrintoutProp"/>'s sheet
        /// (same sheet and header-band construction, same dimensions) so paper reads as one family
        /// across the lab regardless of which machine or which delivery it came from (#33).
        /// </summary>
        private static DeliveryNoteProp BuildDeliveryNotePrefab(Material palette)
        {
            string path = $"{PrefabFolder}/DeliveryNote.prefab";
            var go = new GameObject("DeliveryNote");

            var sheetMesh = SaveMesh(ProcMesh.Box("DeliveryNote_Sheet",
                new Vector3(0.105f, 0.0015f, 0.145f), PaletteUv.Family.NeutralWarm, 14));
            var bandMesh = SaveMesh(ProcMesh.Box("DeliveryNote_Band",
                new Vector3(0.105f, 0.0018f, 0.020f), PaletteUv.Family.Sump, 5));

            AddChild(go, "Sheet", sheetMesh, palette, Vector3.zero, addCollider: false);
            AddChild(go, "Band", bandMesh, palette, new Vector3(0f, 0.0004f, 0.058f), addCollider: false);

            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.11f, 0.02f, 0.15f);

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;

            go.AddComponent<DeliveryNoteProp>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<DeliveryNoteProp>();
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
