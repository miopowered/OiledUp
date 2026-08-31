using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A door sign with the room's name actually printed on it.
    ///
    /// <para>
    /// The plates were flat blue rectangles carrying no information at all — the room name existed
    /// only in the GameObject's name, which is not somewhere a player can read. A building whose doors
    /// are unlabelled is one you navigate by memory, and the lab now has four rooms and a corridor
    /// that turns.
    /// </para>
    ///
    /// <para>
    /// <b>The fourth generated-text surface, not a new exception to §2.1.</b> Instrument screens, the
    /// reference book's pages and <see cref="PrintedSheetSurface"/> all already rasterise text into a
    /// texture, for the same reason: the text <i>is</i> the content, so it cannot be geometry. This
    /// follows <see cref="PrintedSheetSurface"/> closely — its own quad, its own material, sized from
    /// the plate renderer's bounds — because that type already worked out the traps, chiefly that the
    /// shared palette material must never be written to or every palette object in the lab acquires
    /// the texture.
    /// </para>
    ///
    /// <para>
    /// Set in <see cref="BookFont"/> rather than <see cref="PixelFont"/>. A door sign is printed
    /// signage, not a CRT: it wants the proportional face with real letterforms, and the pixel font
    /// would make the building look like it was labelled by an instrument panel.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SignPlate : MonoBehaviour
    {
        private const int TextureWidth = 208;
        private const int TextureHeight = 128;

        /// <summary>
        /// Anti-aliased like the book, and it costs nothing extra: <see cref="BookCanvas"/> shares one
        /// supersample buffer across every caller, and a sign is a fraction of the page the book has
        /// already sized it for.
        /// </summary>
        private const int Supersample = 3;

        /// <summary>Glyph pixel in supersample units, so the type lands off the final pixel grid.</summary>
        private const int GlyphPixel = 4;

        /// <summary>Sign background and lettering. Cool and neutral — hard rule 4 reserves the signal row.</summary>
        private static readonly Color32 Face = new(48, 68, 104, 255);
        private static readonly Color32 Letter = new(226, 232, 240, 255);

        /// <summary>How far the printed face floats off the plate. Small; the plate is 12 mm thick.</summary>
        private const float Lift = 0.0016f;

        [SerializeField] private MeshRenderer plate;

        private Texture2D texture;
        private Material material;
        private Mesh mesh;
        private GameObject face;
        private string drawn;

        /// <summary>
        /// What the sign says. A <see cref="LocKey"/> rather than a string, so the building relabels
        /// itself with the language like everything else (#55).
        /// </summary>
        public LocKey Caption { get; private set; }

        public void Show(LocKey caption)
        {
            Caption = caption;
            Redraw();
        }

        private void OnEnable() => Redraw();

        /// <summary>
        /// Paint the caption, skipping the work when the words have not changed — this is called from
        /// <c>OnEnable</c> as well as on assignment, and a sign redraws for exactly one reason.
        /// </summary>
        private void Redraw()
        {
            if (plate == null) plate = GetComponent<MeshRenderer>();
            if (plate == null) return;

            string text = Caption.Text;
            if (string.IsNullOrEmpty(text)) return;
            if (face != null && drawn == text) return;

            drawn = text;
            EnsureSurface();

            var canvas = new BookCanvas(TextureWidth, TextureHeight, Supersample);
            canvas.Clear();

            // Centred both ways, in supersample units. A door sign with one short word on it looks
            // wrong anywhere else, and the German captions are a different length from the English.
            int width = BookFont.Measure(text, GlyphPixel);
            int x = Mathf.Max(GlyphPixel, (canvas.SampleWidth - width) / 2);
            int y = (canvas.SampleHeight - BookFont.Height * GlyphPixel) / 2;

            canvas.DrawText(x, y, text, BookInk.Ink, GlyphPixel);

            // One flat face colour rather than a gradient: a sign is not paper and has no gutter to
            // shade towards. Ink index 1 is the lettering; index 0 is never read, the face is the
            // per-column colour underneath.
            var pixels = new Color32[TextureWidth * TextureHeight];
            var inks = new[] { Face, Letter, Letter, Letter };
            var faceColumns = new Color32[TextureWidth];
            for (int i = 0; i < faceColumns.Length; i++) faceColumns[i] = Face;

            canvas.Resolve(pixels, TextureWidth, TextureHeight, 0, 0, 0, TextureHeight,
                           inks, faceColumns);

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private void EnsureSurface()
        {
            if (face != null) return;

            texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false, false)
            {
                name = $"{name}_Sign",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            material = new Material(shader) { name = $"{name}_SignMaterial", mainTexture = texture };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            mesh = BuildFaceQuad(plate.localBounds);

            face = new GameObject($"{name}_Face");
            face.transform.SetParent(plate.transform, false);
            face.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = face.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// A quad on the plate's local -Z face.
        ///
        /// <para>
        /// Always -Z, never conditionally +Z: the builder yaws a plate whose reader stands on the
        /// other side of the wall by a further 180 degrees, so this one orientation serves both. Two
        /// mirrored variants would be two chances to get the winding wrong.
        /// </para>
        ///
        /// <para>
        /// <b>u runs toward local -X, which looks backwards and is not.</b> This is the trap
        /// <c>ProcMesh.ScreenQuad</c> documents from the other side. Winding follows the house rule —
        /// <c>cross(right, up)</c> must point along the normal, so for a -Z face the in-plane right is
        /// <i>-X</i> — but a reader standing at -Z looking back along +Z has their own right at +X.
        /// Handing u=0 to the vertex the winding calls "bottom left" would therefore start the text on
        /// the reader's right and print it mirrored. The u values are flipped against the winding for
        /// exactly that reason.
        /// </para>
        /// </summary>
        private static Mesh BuildFaceQuad(Bounds local)
        {
            float halfWidth = Mathf.Max(0.001f, local.extents.x);
            float halfHeight = Mathf.Max(0.001f, local.extents.y);
            float z = local.center.z - local.extents.z - Lift;

            var built = new Mesh { name = "SignPlateFace" };
            built.SetVertices(new[]
            {
                new Vector3(local.center.x + halfWidth, local.center.y - halfHeight, z),
                new Vector3(local.center.x - halfWidth, local.center.y - halfHeight, z),
                new Vector3(local.center.x - halfWidth, local.center.y + halfHeight, z),
                new Vector3(local.center.x + halfWidth, local.center.y + halfHeight, z)
            });
            built.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            built.SetUVs(0, new[]
            {
                new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f)
            });
            built.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            built.RecalculateBounds();
            return built;
        }

        private void OnDestroy()
        {
            bool immediate = !Application.isPlaying;
            Release(face, immediate);
            Release(mesh, immediate);
            Release(texture, immediate);
            Release(material, immediate);
        }

        private static void Release(Object target, bool immediate)
        {
            if (target == null) return;
            if (immediate) DestroyImmediate(target);
            else Destroy(target);
        }
    }
}
