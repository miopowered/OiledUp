using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Residue.Editor.Art
{
    /// <summary>
    /// Generates the §2.2 palette atlas and the four project materials.
    /// <para>
    /// The palette is generated from code rather than hand-painted so it is reviewable in a pull
    /// request, reproducible, and editable by an agent: change a row definition, re-run the menu
    /// item, every mesh in the project re-colours. Meshes address it through
    /// <see cref="Residue.Data.PaletteUv"/> rather than by sampling arbitrary UVs.
    /// </para>
    /// Row 4 is the signal row and is deliberately NOT a value ramp — those colours mean verdict
    /// state and nothing else (§2.2). If red only ever means critical, the player reads the room
    /// instantly.
    /// </summary>
    public static class PaletteBootstrap
    {
        public const string ArtRoot = "Assets/Art";
        public const string PalettePath = ArtRoot + "/Palette/T_Palette.png";
        public const string MaterialsFolder = ArtRoot + "/Materials";

        public const int Size = 16;

        /// <summary>A hue family occupying one atlas row, rendered as a 16-step value ramp.</summary>
        private readonly struct Row
        {
            public readonly string Name;
            public readonly float Hue, Saturation, MinValue, MaxValue;

            public Row(string name, float hue, float saturation, float minValue, float maxValue)
            {
                Name = name; Hue = hue / 360f; Saturation = saturation;
                MinValue = minValue; MaxValue = maxValue;
            }
        }

        private static readonly Row[] Rows =
        {
            new("neutral_cold",   210f, 0.04f, 0.10f, 0.94f), // 0 - the lab body
            new("neutral_warm",    35f, 0.07f, 0.14f, 0.96f), // 1 - concrete, off-white
            new("oxide",           20f, 0.58f, 0.10f, 0.80f), // 2 - oil, rust, corrosion
            new("coolant",        186f, 0.46f, 0.10f, 0.86f), // 3 - coolant, water, screens
            default,                                          // 4 - SIGNAL, built separately
            new("steel",          215f, 0.20f, 0.12f, 0.88f), // 5
            new("brass",           42f, 0.52f, 0.12f, 0.86f), // 6
            new("deep_blue",      228f, 0.44f, 0.08f, 0.72f), // 7
            new("sump",            58f, 0.32f, 0.04f, 0.40f), // 8 - dark oil, shadowed interiors
            new("solvent",        135f, 0.22f, 0.14f, 0.88f), // 9 - solvent, glassware tint
        };

        /// <summary>
        /// Row 4. Reserved for verdict and alarm state. Never decorate with these.
        /// Columns 0-3 red, 4-7 amber, 8-11 green, 12-15 unlit/off states.
        /// </summary>
        private static readonly Color[] SignalRow =
        {
            new(0.72f, 0.09f, 0.09f), new(0.86f, 0.13f, 0.13f), new(1.00f, 0.22f, 0.18f), new(1.00f, 0.42f, 0.36f),
            new(0.68f, 0.42f, 0.05f), new(0.85f, 0.55f, 0.07f), new(1.00f, 0.70f, 0.12f), new(1.00f, 0.82f, 0.40f),
            new(0.08f, 0.44f, 0.20f), new(0.12f, 0.60f, 0.28f), new(0.20f, 0.80f, 0.38f), new(0.48f, 0.92f, 0.60f),
            new(0.14f, 0.15f, 0.16f), new(0.20f, 0.21f, 0.22f), new(0.26f, 0.28f, 0.29f), new(0.34f, 0.36f, 0.38f)
        };

        [MenuItem("Residue/Art/Rebuild Palette", priority = 20)]
        public static void Rebuild()
        {
            EnsureFolders();
            WritePalette();
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
            var created = BuildMaterials(palette);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Residue] Palette rebuilt at {PalettePath} ({Size}x{Size}). " +
                      $"Materials: {string.Join(", ", created)}");
        }

        // -- Texture ---------------------------------------------------------------------------------

        private static void WritePalette()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false, linear: false);

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    texture.SetPixel(x, Size - 1 - y, ColorAt(y, x));
                }
            }

            texture.Apply();
            File.WriteAllBytes(Path.GetFullPath(PalettePath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(PalettePath, ImportAssetOptions.ForceUpdate);
            ConfigureImporter();
        }

        private static Color ColorAt(int row, int column)
        {
            if (row == 4) return SignalRow[column];
            if (row >= Rows.Length) return new Color(0.5f, 0.5f, 0.5f); // reserved rows

            var def = Rows[row];
            float t = column / (float)(Size - 1);
            return Color.HSVToRGB(def.Hue, def.Saturation, Mathf.Lerp(def.MinValue, def.MaxValue, t));
        }

        /// <summary>
        /// Point filtering and no mipmaps below the top level. Bilinear filtering would blend
        /// neighbouring palette entries at texel edges and produce colours that are not in the palette.
        /// </summary>
        private static void ConfigureImporter()
        {
            if (AssetImporter.GetAtPath(PalettePath) is not TextureImporter importer) return;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = Size;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        // -- Materials -------------------------------------------------------------------------------

        private static List<string> BuildMaterials(Texture2D palette)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[Residue] URP Lit shader not found. Is the render pipeline package installed?");
                return new List<string>();
            }

            var made = new List<string>
            {
                Upsert("M_Palette_Opaque", shader, palette, SurfaceKind.Opaque),
                Upsert("M_Palette_Emissive", shader, palette, SurfaceKind.Emissive),
                Upsert("M_Palette_Transparent", shader, palette, SurfaceKind.Transparent),
                Upsert("M_Palette_Cutout", shader, palette, SurfaceKind.Cutout)
            };
            return made;
        }

        private enum SurfaceKind { Opaque, Emissive, Transparent, Cutout }

        private static string Upsert(string name, Shader shader, Texture2D palette, SurfaceKind kind)
        {
            string path = $"{MaterialsFolder}/{name}.mat";

            // Update in place so the GUID survives; every mesh in the project points at it.
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = material == null;
            if (isNew)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", palette);
            material.SetColor("_BaseColor", Color.white);

            // Flat-shaded look: no specular response, no texture-driven detail (§2.1).
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.SetFloat("_EnvironmentReflections", 0f);
            material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");

            switch (kind)
            {
                case SurfaceKind.Opaque:
                    SetOpaque(material);

                    // Emission enabled but black: nothing glows by default, and the look is
                    // unchanged. This exists so EmissivePulse can raise emission per-renderer for
                    // the §2.6 targeting highlight — a MaterialPropertyBlock can set a property but
                    // cannot enable a shader keyword, so without _EMISSION on the shared material
                    // the highlight writes a value the shader never reads and silently does nothing.
                    material.EnableKeyword("_EMISSION");
                    material.SetTexture("_EmissionMap", null);
                    material.SetColor("_EmissionColor", Color.black);

                    // Explicit, because Unity sets EmissiveIsBlack when it sees a black emission
                    // colour and then strips the emission pass entirely — which would undo the above.
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    break;

                case SurfaceKind.Emissive:
                    SetOpaque(material);
                    material.EnableKeyword("_EMISSION");
                    material.SetTexture("_EmissionMap", palette);
                    material.SetColor("_EmissionColor", Color.white);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    break;

                case SurfaceKind.Transparent:
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_Blend", 0f);
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0f);
                    material.SetFloat("_AlphaClip", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    break;

                case SurfaceKind.Cutout:
                    SetOpaque(material);
                    material.SetFloat("_AlphaClip", 1f);
                    material.SetFloat("_Cutoff", 0.5f);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    break;
            }

            EditorUtility.SetDirty(material);
            return isNew ? $"{name} (created)" : $"{name} (updated)";
        }

        private static void SetOpaque(Material material)
        {
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(ArtRoot)) AssetDatabase.CreateFolder("Assets", "Art");
            foreach (string child in new[] { "Palette", "Materials", "Imported", "Generated" })
            {
                if (!AssetDatabase.IsValidFolder($"{ArtRoot}/{child}"))
                    AssetDatabase.CreateFolder(ArtRoot, child);
            }
            foreach (string child in new[] { "Props", "Machines", "Characters" })
            {
                if (!AssetDatabase.IsValidFolder($"{ArtRoot}/Imported/{child}"))
                    AssetDatabase.CreateFolder($"{ArtRoot}/Imported", child);
            }
        }
    }
}
