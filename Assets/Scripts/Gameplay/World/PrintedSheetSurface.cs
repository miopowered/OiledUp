using System;
using System.Collections.Generic;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The physical text on a delivery note or a results slip (#82) — the same paper/ink language
    /// <see cref="InspectableBookSurface"/> uses for the reference manual, sized for a caption and a
    /// dozen short fields rather than pages of prose.
    /// <para>
    /// <b>Why this is not a material swap on the prop's existing "Sheet" mesh.</b> That mesh is built
    /// by <c>Residue.Editor.Build.LabSceneBuilder</c> with <c>ProcMesh.Box</c>, which — correctly, for
    /// flat untextured geometry (§2.1) — gives every vertex on every face the same single palette-atlas
    /// texel. Painting a varying texture through those UVs would sample one colour across the whole
    /// sheet. Rather than ask for a UV change on shared build code neither prop owns, this builds its
    /// own thin quad with real 0..1 UVs and parents it a hair above the sheet's own top face — exactly
    /// what <see cref="InspectableBookSurface"/> already does for the book (its "OpenPages" object owes
    /// nothing to whatever geometry the case around it was built from), and what
    /// <c>ProcMesh.ScreenQuad</c> does for instrument screens. The sheet and its header band keep the
    /// shared palette material untouched — this overlay is the only thing that ever gets its own.
    /// </para>
    /// <para>
    /// The overlay is sized from the sheet renderer's own <see cref="Renderer.localBounds"/> rather
    /// than a copy of <c>LabSceneBuilder</c>'s dimensions, so it stays correct if that geometry is ever
    /// resized without this file changing too.
    /// </para>
    /// <para>
    /// Texture is 128x176 — a little over a tenth of <see cref="InspectableBookSurface"/>'s 512x384,
    /// because a slip or a note carries a caption and a short list, not a spread of paragraphs, and an
    /// oversized texture on a prop that gets created and destroyed all day is the same leak by a
    /// different route. <see cref="Dispose"/> must be called from the owning prop's <c>OnDestroy</c>;
    /// nothing here is a Unity asset that cleans itself up.
    /// </para>
    /// </summary>
    public sealed class PrintedSheetSurface
    {
        private const int TextureWidth = 128;
        private const int TextureHeight = 176;
        private const int Scale = 1;
        private const int MarginX = 6;
        private const int MarginY = 6;

        /// <summary>
        /// How far above the sheet's own top face the overlay sits, as a fraction of the sheet's own
        /// half-thickness. Clear of z-fighting against the sheet's top face on one side; still under
        /// the header band moulded proud of the paper on the other, so the band keeps reading as
        /// printed onto the sheet instead of floating over our text.
        /// </summary>
        private const float LiftFraction = 0.5f;

        private static readonly Color32 Paper = new(232, 226, 209, 255);
        private static readonly Color32 Ink = new(42, 38, 33, 255);
        private static readonly Color32 SoftInk = new(105, 96, 84, 255);

        private readonly Texture2D texture;
        private readonly PixelCanvas canvas;
        private readonly Material material;
        private readonly Mesh mesh;
        private readonly GameObject surfaceObject;

        private readonly List<string> wrapped = new();

        /// <summary>
        /// The last text handed to <see cref="Draw(string,int)"/>, so an unchanged one costs nothing.
        /// <para>
        /// Not an optimisation looking for a problem: <c>SlipReconciler</c> re-binds every slip it can
        /// see once a frame, by design — a result key can arrive a publish after the paper does, and
        /// re-binding unconditionally is what lets the numbers land late. Redrawing on every one of
        /// those would re-wrap the text, allocate a list and push the whole 128x176 buffer to the GPU
        /// per slip per frame, for a piece of paper whose words change at most once in its life.
        /// </para>
        /// <para>
        /// Null rather than empty initially, so the first draw of genuinely empty text still runs and
        /// clears the sheet to paper instead of leaving it whatever the texture was born as.
        /// </para>
        /// </summary>
        private string lastText;

        /// <summary>Characters that fit one line, for callers feeding this through <see cref="PixelText.Wrap"/>.</summary>
        public int Columns { get; }

        /// <summary>Lines that fit the sheet. <see cref="Draw"/> drops anything past this rather than shrinking to fit.</summary>
        public int MaxLines { get; }

        /// <param name="sheet">
        /// The prop's own "Sheet" renderer. Required — this is the paper the overlay is sized to and
        /// parented under, and a caller with no sheet to write on should not build one (see the null
        /// checks in <c>DeliveryNoteProp</c> and <c>PrintoutProp</c>, which exist because several
        /// EditMode fixtures build a bare prop with no sheet at all).
        /// </param>
        /// <param name="debugName">
        /// Prefixes the generated texture/mesh/material names — useful in a profiler capture or a
        /// memory snapshot, where "Printout_7_Paper" beats a dozen objects all called "Texture2D".
        /// </param>
        public PrintedSheetSurface(MeshRenderer sheet, string debugName)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));

            texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = $"{debugName}_Paper"
            };
            canvas = new PixelCanvas(TextureWidth, TextureHeight);

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            material = new Material(shader) { name = $"{debugName}_PaperMaterial", mainTexture = texture };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            mesh = BuildOverlayQuad(sheet.localBounds);

            surfaceObject = new GameObject($"{debugName}_Text");
            surfaceObject.transform.SetParent(sheet.transform, false);
            surfaceObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = surfaceObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Columns = PixelText.Columns(TextureWidth - MarginX * 2, Scale);
            MaxLines = Mathf.Max(1, (TextureHeight - MarginY * 2) / PixelText.LineHeight(Scale));
        }

        /// <summary>
        /// Redraw the sheet. The first <paramref name="emphasizedLines"/> lines are the darker ink the
        /// book uses for a heading; the rest are the softer body tone — the same hierarchy
        /// <see cref="InspectableBookSurface"/> draws a page with.
        /// <para>
        /// Anything past <see cref="MaxLines"/> is dropped, never shrunk to fit. The bottom-left HUD
        /// overlay this is additive to (#82) always has the complete text, so nothing a player needs
        /// is lost — only what does not fit on a sheet this size, which is the same limit a real slip
        /// of paper has.
        /// </para>
        /// </summary>
        /// <summary>
        /// Wrap <paramref name="text"/> to the sheet and draw it, skipping the whole job when the words
        /// have not changed since last time — see <see cref="lastText"/> for why that guard is load
        /// bearing rather than tidy. This is the overload props should call; the list one below is for
        /// a caller that has already decided where its own lines break.
        /// </summary>
        public void Draw(string text, int emphasizedLines = 1)
        {
            string incoming = text ?? string.Empty;
            if (lastText == incoming) return;
            lastText = incoming;

            wrapped.Clear();
            PixelText.Wrap(incoming, Columns, wrapped);
            DrawLines(wrapped, emphasizedLines);
        }

        public void Draw(IReadOnlyList<string> lines, int emphasizedLines = 1)
        {
            lastText = null;
            DrawLines(lines, emphasizedLines);
        }

        private void DrawLines(IReadOnlyList<string> lines, int emphasizedLines)
        {
            canvas.Clear(Paper);

            int shown = lines == null ? 0 : Mathf.Min(lines.Count, MaxLines);
            int lineHeight = PixelText.LineHeight(Scale);
            for (int i = 0; i < shown; i++)
                canvas.DrawText(MarginX, MarginY + i * lineHeight, lines[i],
                    i < emphasizedLines ? Ink : SoftInk, Scale);

            canvas.ApplyTo(texture);
        }

        /// <summary>
        /// A single up-facing quad, matching the winding and UV convention of the top face of
        /// <c>ProcMesh.Box</c> (normal <see cref="Vector3.up"/>, right = +X, in-plane "up" = -Z) so it
        /// agrees with every other flat-shaded surface in the lab, and matching
        /// <see cref="InspectableBookSurface"/>'s own top face so u runs left-to-right and v runs
        /// bottom-to-top exactly as <see cref="PixelCanvas"/> already assumes.
        /// </summary>
        private static Mesh BuildOverlayQuad(Bounds localBounds)
        {
            float halfWidth = Mathf.Max(0.001f, localBounds.extents.x);
            float halfDepth = Mathf.Max(0.001f, localBounds.extents.z);
            float halfThickness = Mathf.Max(0.0002f, localBounds.extents.y);

            float cx = localBounds.center.x;
            float cz = localBounds.center.z;
            float y = localBounds.center.y + halfThickness + halfThickness * LiftFraction;

            var built = new Mesh { name = "PrintedSheetOverlay" };
            built.SetVertices(new List<Vector3>
            {
                new(cx - halfWidth, y, cz + halfDepth),
                new(cx + halfWidth, y, cz + halfDepth),
                new(cx + halfWidth, y, cz - halfDepth),
                new(cx - halfWidth, y, cz - halfDepth)
            });
            built.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            built.SetUVs(0, new List<Vector2>
            {
                new(0f, 1f), new(1f, 1f), new(1f, 0f), new(0f, 0f)
            });
            built.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
            built.RecalculateBounds();
            return built;
        }

        /// <summary>
        /// Release the texture, mesh, material and overlay object. None of these are Unity assets on
        /// disk and none of them belong to a GameObject Unity is already tearing down on its own, so
        /// nothing here is freed except by this call.
        /// <para>
        /// <c>Destroy</c> outside play mode logs an error and does nothing rather than freeing
        /// anything — several EditMode fixtures build and tear down props through exactly this path
        /// (see <c>LabRuntime</c>'s own Destroy/DestroyImmediate split) — so this mirrors that split
        /// rather than calling <c>Destroy</c> unconditionally.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            bool immediate = !Application.isPlaying;
            DestroyOne(surfaceObject, immediate);
            DestroyOne(mesh, immediate);
            DestroyOne(texture, immediate);
            DestroyOne(material, immediate);
        }

        private static void DestroyOne(UnityEngine.Object target, bool immediate)
        {
            if (target == null) return;
            if (immediate) UnityEngine.Object.DestroyImmediate(target);
            else UnityEngine.Object.Destroy(target);
        }
    }
}
