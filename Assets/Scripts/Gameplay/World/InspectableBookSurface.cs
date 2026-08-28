using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>An open, thick two-page book whose reference text is part of its 3D material.</summary>
    [DisallowMultipleComponent]
    public sealed class InspectableBookSurface : MonoBehaviour
    {
        private const int TextureWidth = 512;
        private const int TextureHeight = 384;
        private const int HalfWidth = TextureWidth / 2;
        private const int Scale = 2;
        private const int MarginX = 14;
        private const int MarginY = 14;

        // Page metrics, from the same arithmetic the instrument screens use (PixelText, #50). No
        // longer const: a compile-time copy of a shared formula is how two screens quietly stop
        // agreeing about what a line is. Order matters here — LinesPerPage reads LineHeight, and
        // static initialisers run top to bottom.
        private static readonly int LineHeight = PixelText.LineHeight(Scale);
        private static readonly int Columns = PixelText.Columns(HalfWidth - MarginX * 2, Scale);
        private static readonly int LinesPerPage = (TextureHeight - MarginY * 2 - LineHeight) / LineHeight;

        private const float BookHalfWidth = 0.18f;
        private const float BookHalfDepth = 0.12f;
        private const float PageHalfThickness = 0.014f;
        private const float FlipSeconds = 0.24f;

        // -- The page-corner controls, in texture pixels ------------------------------------------------
        //
        // Drawing and hit-testing both derive from these, through CornerButtonRect. They used to be
        // two independent sets of magic numbers, and they disagreed: the printed control sat in the
        // outer 24px of the page while the hit test accepted anything past the 105mm mark and most of
        // the page's depth, so the corner could be turned by clicking a paragraph.

        private const int ButtonWidth = 24;
        private const int ButtonHeight = 26;

        /// <summary>Gap between the control and the outer edge of the page.</summary>
        private const int ButtonInset = 10;

        /// <summary>Gap between the control and the foot of the page.</summary>
        private const int ButtonBottomInset = 12;

        private const int ArrowScale = 4;

        /// <summary>
        /// Extra grab room around the printed control, in texture pixels.
        /// <para>
        /// The control is 24x26 on a 512x384 page, which works out at about 17mm across the physical
        /// book — a target that is accurate to the pixel and unpleasant to hit. The padding is
        /// symmetric, so the hitbox still agrees with what is drawn rather than drifting off it; it
        /// only forgives the edge.
        /// </para>
        /// </summary>
        private const int ButtonTouchPadding = 6;

        private static readonly Color32 Paper = new(232, 226, 209, 255);
        private static readonly Color32 Ink = new(42, 38, 33, 255);
        private static readonly Color32 SoftInk = new(105, 96, 84, 255);
        private static readonly Color32 Seam = new(181, 170, 148, 255);

        private readonly List<List<string>> pages = new();
        private Texture2D texture;
        private PixelCanvas canvas;
        private Mesh mesh;
        private Material material;
        private MeshRenderer pageRenderer;
        private Transform pageTransform;

        private GameObject flipObject;
        private Mesh flipMesh;
        private MeshRenderer flipRenderer;
        private Material flipMaterial;
        private Texture2D flipTexture;
        private bool turning;
        private int leftPage;

        public int PageCount => pages.Count;
        public int LeftPage => leftPage;
        public bool IsTurning => turning;

        public void SetContent(string title, IReadOnlyList<BookPage> source)
        {
            EnsureSurface();
            pages.Clear();
            leftPage = 0;

            if (!string.IsNullOrEmpty(title)) AddSection("REFERENCE", title);
            if (source != null)
                for (int i = 0; i < source.Count; i++) AddSection(source[i]?.Title, source[i]?.Body);

            if (pages.Count == 0) pages.Add(new List<string> { "NO CONTENT" });
            DrawSpread();
        }

        public void Show(bool visible)
        {
            EnsureSurface();
            if (pageRenderer == null) return;
            pageRenderer.enabled = visible;
            pageRenderer.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Turns when the pointer presses one of the two printed page-corner controls.
        /// <para>
        /// A press is only taken when it lands on a control that is actually <i>printed</i> — the
        /// same <c>leftPage</c> tests that decide whether to draw one decide whether it can be
        /// pressed. Returning true for a corner with nothing in it would swallow the click, and the
        /// caller reads that as "the book handled it" and stops the player rotating the item.
        /// </para>
        /// </summary>
        public bool TryPressPageCorner(Camera camera, Vector2 screenPosition)
        {
            if (camera == null || pageTransform == null || turning) return false;

            var ray = camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(pageTransform.up, pageTransform.position + pageTransform.up * PageHalfThickness);
            if (!plane.Raycast(ray, out float distance)) return false;

            Vector3 local = pageTransform.InverseTransformPoint(ray.GetPoint(distance));

            if (leftPage > 0 && HitsCornerButton(left: true, local))
            {
                Turn(-1);
                return true;
            }

            if (leftPage + 2 < pages.Count && HitsCornerButton(left: false, local))
            {
                Turn(1);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Where a corner control sits on the page, in texture pixels with y measured from the top —
        /// the same convention <see cref="PixelCanvas.FillRect"/> uses.
        /// </summary>
        private static RectInt CornerButtonRect(bool left) => new(
            left ? ButtonInset : TextureWidth - ButtonInset - ButtonWidth,
            TextureHeight - ButtonBottomInset - ButtonHeight,
            ButtonWidth,
            ButtonHeight);

        /// <summary>
        /// Is this point on the page inside the printed control?
        /// <para>
        /// The rectangle is converted from texture space rather than being restated in metres, so
        /// moving the control moves what can press it, and the two cannot drift apart again.
        /// </para>
        /// </summary>
        private static bool HitsCornerButton(bool left, Vector3 local)
        {
            var rect = CornerButtonRect(left);

            Vector2 near = PageToLocal(rect.x - ButtonTouchPadding, rect.y - ButtonTouchPadding);
            Vector2 far = PageToLocal(rect.xMax + ButtonTouchPadding, rect.yMax + ButtonTouchPadding);

            // near/far carry (x, z): the page lies in its own XZ plane and local.y is height above
            // the paper, which is not a thing a rectangle on the page has an opinion about.
            return local.x >= Mathf.Min(near.x, far.x) && local.x <= Mathf.Max(near.x, far.x) &&
                   local.z >= Mathf.Min(near.y, far.y) && local.z <= Mathf.Max(near.y, far.y);
        }

        /// <summary>
        /// Texture pixel to a position on the page, returned as (x, z) in the page's own space.
        /// <para>
        /// Mirrors the top face's UVs exactly: u runs left to right across the full spread, and v runs
        /// bottom to top, which is the opposite of the y <see cref="PixelCanvas.FillRect"/> takes — hence the
        /// inversion here rather than at every call site.
        /// </para>
        /// </summary>
        private static Vector2 PageToLocal(float px, float py)
        {
            float u = px / TextureWidth;
            float v = 1f - py / TextureHeight;

            return new Vector2(-BookHalfWidth + 2f * BookHalfWidth * u,
                               -BookHalfDepth + 2f * BookHalfDepth * v);
        }

        public void Turn(int direction)
        {
            if (turning) return;
            int next = Mathf.Clamp(leftPage + (direction < 0 ? -2 : 2), 0,
                Mathf.Max(0, ((pages.Count - 1) / 2) * 2));
            if (next == leftPage) return;
            StartCoroutine(AnimateTurn(next, direction < 0 ? -1 : 1));
        }

        /// <summary>
        /// Turn a leaf, changing each half of the spread only while something is covering it.
        /// <para>
        /// <b>This is where the page change used to flash.</b> The whole spread was redrawn the
        /// instant the turn began, so the half the player was still reading was replaced a fifth of a
        /// second before the animation gave any reason for it — and the corner control for a page
        /// they had not reached yet appeared along with it. The turning leaf only ever covers one
        /// half, so only that half may change at the start.
        /// </para>
        /// Turning forward, the leaf lifts off the right and sweeps left: the right half can show the
        /// page being uncovered immediately, and the left half must wait until the leaf lands on it.
        /// Turning back is the mirror of that.
        /// </summary>
        private IEnumerator AnimateTurn(int next, int direction)
        {
            turning = true;
            SnapshotFlipPage(direction);

            int previous = leftPage;
            leftPage = next;

            // Controls are suppressed for the duration. Which corners are live is a statement about
            // where you are in the book, and during a turn that is briefly neither answer.
            if (direction > 0) DrawSpread(previous, next + 1, showControls: false);
            else DrawSpread(next, previous + 1, showControls: false);

            flipObject.SetActive(true);
            float elapsed = 0f;
            while (elapsed < FlipSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / FlipSeconds));
                flipObject.transform.localRotation = Quaternion.Euler(0f, 0f, direction * 180f * t);
                yield return null;
            }

            // Drawn before the leaf is taken away, never after: at 180 degrees it is lying flat over
            // the half that is about to change, so the swap happens underneath it and is not seen.
            DrawSpread();

            flipObject.SetActive(false);
            flipObject.transform.localRotation = Quaternion.identity;
            if (flipTexture != null) Destroy(flipTexture);
            flipTexture = null;
            turning = false;
        }

        private void SnapshotFlipPage(int direction)
        {
            EnsureFlipObject(direction);
            if (flipTexture != null) Destroy(flipTexture);
            flipTexture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = $"{name}_TurningPage"
            };
            flipTexture.SetPixels32(texture.GetPixels32());
            flipTexture.Apply(false);
            flipMaterial.mainTexture = flipTexture;
            if (flipMaterial.HasProperty("_BaseMap")) flipMaterial.SetTexture("_BaseMap", flipTexture);
        }

        private void EnsureFlipObject(int direction)
        {
            if (flipObject == null)
            {
                flipObject = new GameObject("TurningPage");
                flipObject.transform.SetParent(pageTransform, false);
                flipObject.transform.localPosition = new Vector3(0f, PageHalfThickness + 0.001f, 0f);
                flipMesh = new Mesh { name = $"{name}_TurningPageMesh" };
                flipObject.AddComponent<MeshFilter>().sharedMesh = flipMesh;
                flipRenderer = flipObject.AddComponent<MeshRenderer>();
                flipMaterial = new Material(material) { name = $"{name}_TurningPageMaterial" };
                if (flipMaterial.HasProperty("_Cull")) flipMaterial.SetFloat("_Cull", 0f);
                flipRenderer.sharedMaterial = flipMaterial;
                flipRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            float x0 = direction > 0 ? 0f : -BookHalfWidth;
            float x1 = direction > 0 ? BookHalfWidth : 0f;
            float u0 = direction > 0 ? 0.5f : 0f;
            float u1 = direction > 0 ? 1f : 0.5f;
            flipMesh.Clear();
            flipMesh.vertices = new[]
            {
                new Vector3(x0, 0f, -BookHalfDepth), new Vector3(x1, 0f, -BookHalfDepth),
                new Vector3(x0, 0f, BookHalfDepth), new Vector3(x1, 0f, BookHalfDepth)
            };
            flipMesh.uv = new[] { new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u0, 1f), new Vector2(u1, 1f) };
            flipMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            flipMesh.RecalculateNormals();
            flipMesh.RecalculateBounds();
            flipObject.transform.localRotation = Quaternion.identity;
            flipObject.SetActive(false);
        }

        /// <summary>
        /// Lay a section's prose out as pages.
        /// <para>
        /// The book reflows where the instrument screens cut (see <see cref="PixelText"/>): a page is
        /// two dozen lines of continuous text and losing the tail of a sentence loses the sentence,
        /// whereas an instrument's line is a labelled field that must stay where the eye left it.
        /// </para>
        /// </summary>
        private void AddSection(string heading, string body)
        {
            var lines = new List<string>();

            // Truncated, unlike the body, which reflows. A heading is one line by definition — wrapping
            // it would push the first paragraph down and leave a page whose second line is the tail of
            // a title. Before this it was neither wrapped nor cut, so a section name longer than the
            // column count simply ran off the paper and out past the margin.
            if (!string.IsNullOrEmpty(heading))
                lines.Add(PixelText.Truncate(heading.ToUpperInvariant(), Columns));
            if (lines.Count > 0) lines.Add(string.Empty);
            PixelText.Wrap(body, Columns, lines);

            for (int start = 0; start < lines.Count; start += LinesPerPage)
            {
                int count = Mathf.Min(LinesPerPage, lines.Count - start);
                pages.Add(lines.GetRange(start, count));
            }
        }

        private void EnsureSurface()
        {
            if (pageRenderer != null) return;

            var page = new GameObject("OpenPages");
            page.transform.SetParent(transform, false);
            page.transform.localPosition = new Vector3(0f, 0.014f, 0f);
            pageTransform = page.transform;

            mesh = BuildThickBookMesh();
            page.AddComponent<MeshFilter>().sharedMesh = mesh;

            texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false, false)
            {
                name = $"{name}_Pages",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            canvas = new PixelCanvas(TextureWidth, TextureHeight);

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            material = new Material(shader) { name = $"{name}_PageMaterial", mainTexture = texture };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            pageRenderer = page.AddComponent<MeshRenderer>();
            pageRenderer.sharedMaterial = material;
            pageRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            pageRenderer.gameObject.SetActive(false);
        }

        private Mesh BuildThickBookMesh()
        {
            float x = BookHalfWidth, z = BookHalfDepth, y = PageHalfThickness;
            var built = new Mesh { name = $"{name}_ThickOpenPages" };
            built.vertices = new[]
            {
                // top
                new Vector3(-x,y,-z), new Vector3(x,y,-z), new Vector3(-x,y,z), new Vector3(x,y,z),
                // bottom
                new Vector3(-x,-y,-z), new Vector3(-x,-y,z), new Vector3(x,-y,-z), new Vector3(x,-y,z),
                // front / back
                new Vector3(-x,-y,-z), new Vector3(x,-y,-z), new Vector3(-x,y,-z), new Vector3(x,y,-z),
                new Vector3(x,-y,z), new Vector3(-x,-y,z), new Vector3(x,y,z), new Vector3(-x,y,z),
                // left / right
                new Vector3(-x,-y,z), new Vector3(-x,-y,-z), new Vector3(-x,y,z), new Vector3(-x,y,-z),
                new Vector3(x,-y,-z), new Vector3(x,-y,z), new Vector3(x,y,-z), new Vector3(x,y,z)
            };
            built.uv = new[]
            {
                new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1),
                Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero,
                Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero,
                Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero,
                Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero,
                Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero
            };
            built.triangles = new[]
            {
                0,2,1,2,3,1, 4,6,5,5,6,7,
                8,10,9,10,11,9, 12,14,13,14,15,13,
                16,18,17,18,19,17, 20,22,21,22,23,21
            };
            built.RecalculateNormals();
            built.RecalculateBounds();
            return built;
        }

        /// <summary>The spread as it should look at rest: both halves current, both controls live.</summary>
        private void DrawSpread() => DrawSpread(leftPage, leftPage + 1, showControls: true);

        /// <summary>
        /// Draw an arbitrary pair of pages. Mid-turn the two halves legitimately come from different
        /// spreads — see <see cref="AnimateTurn"/> — which is why this does not simply read
        /// <see cref="leftPage"/>.
        /// </summary>
        private void DrawSpread(int leftIndex, int rightIndex, bool showControls)
        {
            if (texture == null) return;
            canvas.Clear(Paper);

            canvas.FillRect(HalfWidth - 2, 0, 4, TextureHeight, Seam);
            DrawPage(leftIndex, 0);
            DrawPage(rightIndex, HalfWidth);

            if (showControls)
            {
                if (leftPage > 0) DrawCornerButton(left: true);
                if (leftPage + 2 < pages.Count) DrawCornerButton(left: false);
            }

            canvas.ApplyTo(texture);
        }

        private void DrawPage(int pageIndex, int xOffset)
        {
            if (pageIndex < 0 || pageIndex >= pages.Count) return;
            var lines = pages[pageIndex];
            for (int i = 0; i < lines.Count && i < LinesPerPage; i++)
                canvas.DrawText(xOffset + MarginX, MarginY + i * LineHeight, lines[i],
                    i == 0 ? Ink : SoftInk, Scale);

            string footer = $"{pageIndex + 1}/{pages.Count}";
            canvas.DrawText(xOffset + PixelText.CentreOffset(footer, Scale, HalfWidth),
                TextureHeight - MarginY - PixelFont.GlyphHeight * Scale, footer, SoftInk, Scale);
        }

        /// <summary>
        /// The folded-corner control: a rule along the foot and a stem down the outer edge, with the
        /// arrow centred between them.
        /// <para>
        /// The arrow is centred rather than offset from the outer edge. Mirroring the left-hand
        /// offset to the right without allowing for the glyph's own width is what put the "next"
        /// arrow outside its own bracket and four pixels into the body text — the two controls did
        /// not look like the same control facing opposite ways, because they were not drawn the same
        /// way.
        /// </para>
        /// </summary>
        private void DrawCornerButton(bool left)
        {
            var rect = CornerButtonRect(left);

            canvas.FillRect(rect.x, rect.yMax - 2, rect.width, 2, Ink);
            canvas.FillRect(left ? rect.x : rect.xMax - 2, rect.y, 2, rect.height, Ink);

            string arrow = left ? "<" : ">";
            int arrowHeight = PixelFont.GlyphHeight * ArrowScale;

            canvas.DrawText(rect.x + PixelText.CentreOffset(arrow, ArrowScale, rect.width),
                            rect.y + (rect.height - arrowHeight) / 2 - 1,
                            arrow, Ink, ArrowScale);
        }

        private void OnDestroy()
        {
            if (texture != null) Destroy(texture);
            if (flipTexture != null) Destroy(flipTexture);
            if (mesh != null) Destroy(mesh);
            if (flipMesh != null) Destroy(flipMesh);
            if (material != null) Destroy(material);
            if (flipMaterial != null) Destroy(flipMaterial);
        }
    }
}
