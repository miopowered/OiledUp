using System.Collections.Generic;
using NUnit.Framework;
using Residue.Chemistry;
using Residue.Gameplay.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards #82: a delivery note and a printout carry their own text on the physical page now, not
    /// only in the bottom-left HUD overlay.
    /// <para>
    /// The interesting failure modes here are not "does the wrapping work" — <see cref="PixelText"/>
    /// already owns that and is pinned on its own. They are the two traps the issue calls out by name:
    /// a prop bound with no physical "Sheet" under it must not throw, because several EditMode
    /// fixtures build exactly that bare shape (see <c>LabViewTests.NewRuntime</c>'s fake
    /// <c>printoutPrefab</c>, and <c>PlayerScreenTests</c>'s <c>Loose&lt;PrintoutProp&gt;</c>), and a
    /// prop bound more than once — every reconcile pass, for a printout — must reuse its texture
    /// rather than leaking a fresh one per bind.
    /// </para>
    /// </summary>
    public sealed class PrintedSheetSurfaceTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private GameObject Spawn(string label)
        {
            var go = new GameObject(label);
            spawned.Add(go);
            return go;
        }

        /// <summary>
        /// A stand-in for what <c>LabSceneBuilder</c> actually gives a prop: a child named "Sheet"
        /// carrying a real mesh, so its bounds are real rather than the empty default a bare
        /// <see cref="MeshRenderer"/> reports.
        /// </summary>
        private static MeshRenderer FixtureSheet(GameObject root)
        {
            var sheet = new GameObject("Sheet");
            sheet.transform.SetParent(root.transform, false);
            sheet.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh();
            return sheet.AddComponent<MeshRenderer>();
        }

        /// <summary>Unity's own built-in cube mesh — shared and never destroyed, only borrowed.</summary>
        private static Mesh PrimitiveMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }

        private static int OverlayCount(Transform sheet)
        {
            int count = 0;
            foreach (Transform child in sheet)
                if (child.name.EndsWith("_Text")) count++;
            return count;
        }

        // -- PrintedSheetSurface itself -----------------------------------------------------------------

        [Test]
        public void NullSheet_IsRefused()
        {
            Assert.Throws<System.ArgumentNullException>(() => new PrintedSheetSurface(null, "x"));
        }

        [Test]
        public void ASheet_GetsATextChild_WithItsOwnMaterial_NotTheSheetsSharedOne()
        {
            var root = Spawn("Fixture");
            var sheetRenderer = FixtureSheet(root);
            var palette = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            sheetRenderer.sharedMaterial = palette;

            try
            {
                var surface = new PrintedSheetSurface(sheetRenderer, "Fixture");

                Assert.AreEqual(1, OverlayCount(sheetRenderer.transform),
                    "The overlay must be a child of the sheet it writes on.");

                var overlayRenderer = FindOverlayRenderer(sheetRenderer.transform);
                Assert.IsNotNull(overlayRenderer);

                Assert.AreNotSame(palette, overlayRenderer.sharedMaterial,
                    "Section 2.1's texture exception is the paper, not every palette object — the " +
                    "overlay must not share the sheet's own material.");
                Assert.IsNotNull(overlayRenderer.sharedMaterial.mainTexture,
                    "The overlay's material must actually carry the rasterised texture.");

                surface.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(palette);
            }
        }

        private static MeshRenderer FindOverlayRenderer(Transform sheet)
        {
            foreach (Transform child in sheet)
                if (child.name.EndsWith("_Text")) return child.GetComponent<MeshRenderer>();
            return null;
        }

        [Test]
        public void Redrawing_ReusesTheSameTexture_RatherThanAllocatingANewOnePerDraw()
        {
            var root = Spawn("Fixture");
            var sheetRenderer = FixtureSheet(root);
            var surface = new PrintedSheetSurface(sheetRenderer, "Fixture");
            var overlayRenderer = FindOverlayRenderer(sheetRenderer.transform);
            var textureBefore = overlayRenderer.sharedMaterial.mainTexture;

            surface.Draw(new List<string> { "FIRST DRAW" });
            surface.Draw(new List<string> { "SECOND DRAW", "MORE" });

            Assert.AreSame(textureBefore, overlayRenderer.sharedMaterial.mainTexture,
                "A printout is bound on every reconcile pass; redrawing must not allocate a fresh " +
                "texture each time, or a long contract leaks one per bind.");

            surface.Dispose();
        }

        [Test]
        public void MoreLinesThanFit_AreDropped_NotThrown()
        {
            var root = Spawn("Fixture");
            var sheetRenderer = FixtureSheet(root);
            var surface = new PrintedSheetSurface(sheetRenderer, "Fixture");

            var lines = new List<string>();
            for (int i = 0; i < surface.MaxLines + 20; i++) lines.Add($"LINE {i}");

            Assert.DoesNotThrow(() => surface.Draw(lines));
            surface.Dispose();
        }

        [Test]
        public void Dispose_RemovesTheOverlayObject()
        {
            var root = Spawn("Fixture");
            var sheetRenderer = FixtureSheet(root);
            var surface = new PrintedSheetSurface(sheetRenderer, "Fixture");
            Assert.AreEqual(1, OverlayCount(sheetRenderer.transform));

            surface.Dispose();

            Assert.AreEqual(0, OverlayCount(sheetRenderer.transform),
                "Dispose must take the overlay object with it, not just null out a field.");
        }

        // -- The props: must survive being bound with no sheet at all -----------------------------------

        /// <summary>
        /// Mirrors <c>LabViewTests.NewRuntime</c>'s fake <c>printoutPrefab</c> exactly: a bare
        /// GameObject with nothing under it. Several EditMode fixtures bind real printouts shaped like
        /// this, and #82 must not turn every one of them into a <see cref="System.NullReferenceException"/>.
        /// </summary>
        [Test]
        public void APrintout_WithNoSheet_BindsWithoutThrowing()
        {
            var go = Spawn("Printout_Bare");
            var slip = go.AddComponent<PrintoutProp>();

            Assert.DoesNotThrow(() => slip.Bind(
                ticket: 1, sampleId: new SampleId(3),
                result: new TestResult { Values = { ["water_ppm"] = 100f } },
                machineName: "Karl Fischer", recordTag: "TAG-1"));

            // A second bind is the reconcile-pass shape and must be equally harmless.
            Assert.DoesNotThrow(() => slip.Bind(
                ticket: 1, sampleId: new SampleId(3),
                result: new TestResult { Values = { ["water_ppm"] = 100f } },
                machineName: "Karl Fischer", recordTag: "TAG-1"));
        }

        [Test]
        public void ANote_WithNoSheet_BindsWithoutThrowing()
        {
            var go = Spawn("Note_Bare");
            var note = go.AddComponent<DeliveryNoteProp>();

            Assert.DoesNotThrow(() =>
                note.Bind("carton-1", "JOB-1", "Acme", "DELIVERY NOTE JOB-1\nline\n"));
        }

        // -- The props: a real sheet, bound more than once -----------------------------------------------

        [Test]
        public void APrintout_WithARealSheet_RendersOnce_AndReusesItOnEveryRebind()
        {
            var go = Spawn("Printout_Real");
            var slip = go.AddComponent<PrintoutProp>();
            var sheetRenderer = FixtureSheet(go);

            var so = new UnityEditor.SerializedObject(slip);
            so.FindProperty("paper").objectReferenceValue = sheetRenderer;
            so.ApplyModifiedPropertiesWithoutUndo();

            slip.Bind(ticket: 5, sampleId: new SampleId(9),
                result: new TestResult { Values = { ["water_ppm"] = 42f } },
                machineName: "Karl Fischer", recordTag: "TAG-9");

            Assert.AreEqual(1, OverlayCount(sheetRenderer.transform),
                "A bound printout with a real sheet must get its overlay.");

            slip.Bind(ticket: 5, sampleId: new SampleId(9),
                result: new TestResult { Values = { ["water_ppm"] = 55f } },
                machineName: "Karl Fischer", recordTag: "TAG-9");

            Assert.AreEqual(1, OverlayCount(sheetRenderer.transform),
                "Rebinding (a reconcile pass, on a client) must redraw the existing overlay, not build " +
                "a second one.");
        }

        [Test]
        public void ANote_WithARealSheet_FindsItByName_AndRendersOnlyOneOverlay()
        {
            var go = Spawn("Note_Real");
            var note = go.AddComponent<DeliveryNoteProp>();
            var sheetRenderer = FixtureSheet(go);

            note.Bind("carton-1", "JOB-2", "Acme", "DELIVERY NOTE JOB-2\n1. TANK-1\n");
            note.Bind("carton-1", "JOB-2", "Acme", "DELIVERY NOTE JOB-2\n1. TANK-1\n2. TANK-2\n");

            Assert.AreEqual(1, OverlayCount(sheetRenderer.transform),
                "A note is only bound once in play, but must not double up if it ever is rebound.");
        }
    }
}
