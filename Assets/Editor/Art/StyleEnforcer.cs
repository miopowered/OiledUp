using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Residue.Editor.Art
{
    /// <summary>
    /// Enforces the §2.1 style contract at import time.
    /// <para>
    /// Assets from different sources clash because of their materials and textures, not their
    /// geometry. So every model that lands under <c>Assets/Art/Imported/</c> has its materials
    /// stripped, its normals hardened, and one of our four palette materials applied. A purchased
    /// low-poly pack, a Blender export and an AI-generated mesh all arrive wearing the same skin
    /// with no manual work — which is the whole reason the art direction survives mixed sources.
    /// </para>
    /// Poly budgets are advisory: they log, they never block an import.
    /// </summary>
    public sealed class StyleEnforcer : AssetPostprocessor
    {
        /// <summary>Only assets under this path are conformed. Anything else is left alone.</summary>
        public const string EnforcedRoot = "Assets/Art/Imported";

        public const string PaletteMaterialPath = "Assets/Art/Materials/M_Palette_Opaque.mat";

        /// <summary>
        /// Triangle budgets by folder (§2.1). Matched against the asset path, longest key first,
        /// so Assets/Art/Imported/Machines wins over a generic fallback.
        /// </summary>
        private static readonly (string Segment, int Min, int Max)[] Budgets =
        {
            ("/Machines/", 800, 3000),
            ("/Characters/", 0, 1500),
            ("/Props/", 100, 800)
        };

        private bool IsEnforced => assetPath.Replace('\\', '/').StartsWith(EnforcedRoot);

        // -- Import settings -------------------------------------------------------------------------

        private void OnPreprocessModel()
        {
            if (!IsEnforced) return;
            if (assetImporter is not ModelImporter importer) return;

            // 1 unit = 1 metre, regardless of what the DCC tool thought (§2.1).
            importer.useFileScale = false;
            importer.globalScale = 1f;

            // The style contract: no imported materials or textures, ever.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;

            // Hard normals everywhere. This is what produces the flat-shaded read.
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.normalSmoothingAngle = 0f;
            importer.importTangents = ModelImporterTangents.None;

            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.importBlendShapes = false;
        }

        // -- Post-import conformance -----------------------------------------------------------------

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsEnforced) return;

            ApplyPaletteMaterial(root);
            ReportBudget(root);
            ReportChannelViolations(root);
        }

        private void ApplyPaletteMaterial(GameObject root)
        {
            var palette = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterialPath);
            if (palette == null)
            {
                Debug.LogWarning(
                    $"[StyleEnforcer] '{assetPath}' imported without a palette material. " +
                    $"Run Residue > Art > Rebuild Palette to create {PaletteMaterialPath}.");
                return;
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[renderer.sharedMaterials.Length == 0 ? 1 : renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = palette;
                renderer.sharedMaterials = materials;
            }
        }

        private void ReportBudget(GameObject root)
        {
            int triangles = CountTriangles(root);
            string path = assetPath.Replace('\\', '/');

            foreach (var (segment, min, max) in Budgets)
            {
                if (!path.Contains(segment)) continue;

                if (triangles > max)
                {
                    Debug.LogWarning(
                        $"[StyleEnforcer] '{assetPath}' is {triangles} tris, over the {max} budget for " +
                        $"{segment.Trim('/')}. Decimate it in Blender before it ships.", root);
                }
                else if (triangles < min && triangles > 0)
                {
                    Debug.Log(
                        $"[StyleEnforcer] '{assetPath}' is {triangles} tris, under the {min} floor for " +
                        $"{segment.Trim('/')}. Probably fine, but check it does not read as a placeholder.", root);
                }
                return;
            }

            // No category folder matched. Not an error, but the budget cannot be checked.
            Debug.Log(
                $"[StyleEnforcer] '{assetPath}' ({triangles} tris) is not in a Props/Machines/Characters " +
                $"folder, so no poly budget applies.", root);
        }

        /// <summary>
        /// The palette-atlas mode assumes UV0 samples a texel centre and nothing else carries colour.
        /// A mesh arriving with vertex colours or a second UV set will look wrong in a way that is
        /// maddening to debug later, so say so at import.
        /// </summary>
        private void ReportChannelViolations(GameObject root)
        {
            var offenders = new List<string>();

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                if (mesh.uv2 is { Length: > 0 })
                    offenders.Add($"{mesh.name}: has a second UV channel");

                if (mesh.colors is { Length: > 0 })
                    offenders.Add($"{mesh.name}: carries vertex colours");
            }

            if (offenders.Count > 0)
            {
                Debug.LogWarning(
                    $"[StyleEnforcer] '{assetPath}' violates the palette-atlas contract (§2.1):\n  " +
                    string.Join("\n  ", offenders), root);
            }
        }

        private static int CountTriangles(GameObject root)
        {
            int total = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                total += mesh.triangles.Length / 3;
            }
            return total;
        }
    }
}
