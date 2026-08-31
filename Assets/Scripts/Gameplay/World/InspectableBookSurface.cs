using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// An open, thick two-page book whose reference text is part of its 3D material.
    ///
    /// <para>
    /// <b>It is set as print, not as a readout.</b> The type is <see cref="BookFont"/> —
    /// proportional, real lowercase, descenders — laid out by <see cref="BookLayout"/> on measured
    /// width, and rasterised through <see cref="BookCanvas"/> at
    /// <see cref="BookLayout.Supersample"/>x and box-filtered down, so glyph edges land as grey and
    /// the texture can be filtered bilinearly. The instrument panels keep <see cref="PixelCanvas"/>
    /// and <see cref="PixelFont"/> and are untouched: they are period CRTs and want to be crisp,
    /// monospaced and uppercase. This is paper.
    /// </para>
    ///
    /// <para>
    /// <b>The turning leaf carries a different page on each face.</b> It is two single-sided quads
    /// back to back — front the page being carried away, back the page arriving — rather than one
    /// double-sided quad, which could only ever show the same image mirrored on its reverse. Nothing
    /// here sets <c>_Cull</c>: each face is culled by its own winding, which is also what makes the
    /// swap at 180 degrees invisible.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InspectableBookSurface : MonoBehaviour
    {
        // -- The physical book ---------------------------------------------------------------------
        //
        // 0.36 x 0.24 m open, which is 3:2 — the same aspect as the spread texture, so a texel is
        // square. The old 512x384 spread stretched every glyph by a ninth horizontally.

        private const float BookHalfWidth = 0.18f;
        private const float BookHalfDepth = 0.12f;
        private const float PageHalfThickness = 0.014f;

        private const float FlipSeconds = 0.42f;

        /// <summary>Segments across the turning leaf, so it can bow rather than stay a rigid plate.</summary>
        private const int LeafSegments = 10;

        /// <summary>Metres the leaf bows at mid-turn. Small: paper flexes, it does not fold.</summary>
        private const float LeafCurl = 0.010f;

        // -- The page-corner controls, in final texture pixels ---------------------------------------
        //
        // Drawing and hit-testing both derive from CornerRectOnPage, and nothing else states where
        // the control is. They used to be two independent sets of magic numbers, and they disagreed:
        // the printed control sat in the outer 24px of the page while the hit test accepted most of
        // the page's depth, so the corner could be turned by clicking a paragraph.

        private const int CornerWidth = 60;
        private const int CornerHeight = 64;

        /// <summary>Gap between the control and the outer edge of the page.</summary>
        private const int CornerInset = 18;

        /// <summary>Gap between the control and the foot of the page.</summary>
        private const int CornerBottomInset = 20;

        /// <summary>
        /// Extra grab room around the printed control, in final texture pixels.
        /// <para>
        /// The control is 60x64 on a 576px page, which is about 19mm across the physical book — a
        /// target that is accurate to the pixel and unpleasant to hit. The padding is symmetric, so
        /// the hitbox still agrees with what is drawn rather than drifting off it; it only forgives
        /// the edge.
        /// </para>
        /// </summary>
        private const int CornerTouchPadding = 16;

        /// <summary>
        /// Top of the band a page turn repaints rather than re-rendering. Everything below this is
        /// margin, folio and page-corner control — never body text — so the band can be cleared and
        /// redrawn on its own.
        /// </summary>
        private const int FootBandTop = 672;

        // -- Paper and ink ---------------------------------------------------------------------------
        //
        // Four values, and none of them is a signal colour: §2.2 row 4 means verdict state and
        // nothing else, so the page is built entirely out of warm neutrals.

        private static readonly Color32 Paper = new(232, 226, 209, 255);
        private static readonly Color32 Ink = new(42, 38, 33, 255);
        private static readonly Color32 SoftInk = new(105, 96, 84, 255);
        private static readonly Color32 Seam = new(181, 170, 148, 255);

        /// <summary>The paper where it turns into the binding: the same family, further down it.</summary>
        private static readonly Color32 GutterPaper = new(203, 193, 173, 255);

        /// <summary>Indexed by <see cref="BookInk"/>. Entry 0 is never read; the paper is per column.</summary>
        private static readonly Color32[] InkColours = { Paper, Ink, SoftInk, Seam };

        /// <summary>Final pixels the gutter shading fades over.</summary>
        private const int GutterShadeWidth = 132;

        /// <summary>Final pixels of hard seam right at the spine. Two pages of it meet as four.</summary>
        private const int SeamWidth = 2;

        private static readonly Color32[] VersoPaper = BuildPaperColumns(verso: true);
        private static readonly Color32[] RectoPaper = BuildPaperColumns(verso: false);

        // -- State ------------------------------------------------------------------------------------

        private readonly List<TypesetPage> pages = new();
        private string bookTitle = string.Empty;

        private Texture2D texture;
        private Color32[] spread;
        private BookCanvas canvas;
        private Mesh mesh;
        private Material material;
        private MeshRenderer pageRenderer;
        private Transform pageTransform;

        private GameObject leafObject;
        private Mesh leafMesh;
        private MeshRenderer leafRenderer;
        private Material leafMaterial;
        private Texture2D leafTexture;
        /// <summary>
        /// Pixels for the turning leaf, shared by every book.
        /// <para>
        /// One book turns at a time — turning one requires holding it, and a player has one pair of
        /// hands — so this is scratch for the duration of a single turn, like <see cref="BookCanvas"/>'s
        /// sample buffer. Per instance it was 3.4 MB standing idle in seven books that were not being
        /// read, on top of the same again for whichever one was.
        /// </para>
        /// </summary>
        private static Color32[] sharedLeafPixels;

        /// <summary>
        /// Statics survive an Enter Play Mode that skips the domain reload, so without this last
        /// session's leaf buffer is still resident before any book has been picked up.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetSharedBuffers() => sharedLeafPixels = null;

        private Color32[] leafPixels;
        private Vector3[] leafFlat;
        private Vector3[] leafPosed;
        private float[] leafFractions;

        private bool turning;
        private int leftPage;

        public int PageCount => pages.Count;
        public int LeftPage => leftPage;
        public bool IsTurning => turning;

        // -- Content ------------------------------------------------------------------------------------

        /// <summary>
        /// Set the whole book.
        /// <para>
        /// Page one is a title page, which is both what a book has and what makes
        /// <see cref="PageCount"/> non-zero before the content catalog exists — a reference book is
        /// built during <c>Awake</c>, when the catalog may not have run its own yet.
        /// </para>
        /// </summary>
        public void SetContent(string title, IReadOnlyList<BookPage> source)
        {
            EnsureSurface();
            CancelTurn();

            bookTitle = title ?? string.Empty;
            pages.Clear();
            leftPage = 0;

            pages.Add(BookLayout.TitlePage(bookTitle));

            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    BookPage section = source[i];
                    if (section == null) continue;

                    var lines = BookLayout.Typeset(section.Title, section.Body);
                    if (lines.Count == 0) continue;

                    pages.AddRange(BookLayout.Paginate(lines, section.Title));
                }
            }

            DrawSpread();
        }

        public void Show(bool visible)
        {
            EnsureSurface();
            if (pageRenderer == null) return;

            pageRenderer.enabled = visible;
            pageRenderer.gameObject.SetActive(visible);
        }

        // -- The page-corner controls -----------------------------------------------------------------

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
        /// Where a corner control sits on a single page, in final texture pixels with y measured from
        /// the top — the convention <see cref="BookCanvas"/> and every screen in the game lay out in.
        /// <para>
        /// This is the only statement of where the control is. Both halves of the spread and both the
        /// drawing and the hit test are derived from it, which is what stops them drifting apart
        /// again.
        /// </para>
        /// </summary>
        public static RectInt CornerRectOnPage(bool left) => new(
            left ? CornerInset : BookLayout.PageWidth - CornerInset - CornerWidth,
            BookLayout.PageHeight - CornerBottomInset - CornerHeight,
            CornerWidth,
            CornerHeight);

        /// <summary>The same rectangle placed on the spread, which is what a press is tested against.</summary>
        public static RectInt CornerButtonRect(bool left)
        {
            RectInt rect = CornerRectOnPage(left);
            return new RectInt(rect.x + (left ? 0 : BookLayout.PageWidth), rect.y, rect.width, rect.height);
        }

        /// <summary>
        /// Is this point on the page inside the printed control?
        /// <para>
        /// The rectangle is converted from texture space rather than being restated in metres, so
        /// moving the control moves what can press it, and the two cannot drift apart again.
        /// </para>
        /// </summary>
        public static bool HitsCornerButton(bool left, Vector3 local)
        {
            RectInt rect = CornerButtonRect(left);

            Vector2 near = PageToLocal(rect.x - CornerTouchPadding, rect.y - CornerTouchPadding);
            Vector2 far = PageToLocal(rect.xMax + CornerTouchPadding, rect.yMax + CornerTouchPadding);

            // near/far carry (x, z): the page lies in its own XZ plane and local.y is height above
            // the paper, which is not a thing a rectangle on the page has an opinion about.
            return local.x >= Mathf.Min(near.x, far.x) && local.x <= Mathf.Max(near.x, far.x) &&
                   local.z >= Mathf.Min(near.y, far.y) && local.z <= Mathf.Max(near.y, far.y);
        }

        /// <summary>
        /// Texture pixel to a position on the page, returned as (x, z) in the page's own space.
        /// <para>
        /// Mirrors the top face's UVs exactly: u runs left to right across the full spread, and v runs
        /// bottom to top, which is the opposite of the y <see cref="BookCanvas.Fill"/> takes — hence
        /// the inversion here rather than at every call site.
        /// </para>
        /// </summary>
        public static Vector2 PageToLocal(float px, float py)
        {
            float u = px / BookLayout.SpreadWidth;
            float v = 1f - py / BookLayout.SpreadHeight;

            return new Vector2(-BookHalfWidth + 2f * BookHalfWidth * u,
                               -BookHalfDepth + 2f * BookHalfDepth * v);
        }

        // -- Turning -------------------------------------------------------------------------------------

        public void Turn(int direction)
        {
            if (turning || !isActiveAndEnabled) return;

            int next = Mathf.Clamp(leftPage + (direction < 0 ? -2 : 2), 0,
                Mathf.Max(0, ((pages.Count - 1) / 2) * 2));
            if (next == leftPage) return;

            StartCoroutine(AnimateTurn(next, direction < 0 ? -1 : 1));
        }

        /// <summary>
        /// Turn a leaf, changing each half of the spread only while something is covering it.
        ///
        /// <para>
        /// <b>This is where the page change used to flash.</b> The whole spread was redrawn the
        /// instant the turn began, so the half the player was still reading was replaced a fifth of a
        /// second before the animation gave any reason for it — and the corner control for a page
        /// they had not reached yet appeared along with it. The turning leaf only ever covers one
        /// half, so only that half may change at the start.
        /// </para>
        ///
        /// <para>
        /// Turning forward, the leaf lifts off the right and sweeps left: the right half can show the
        /// page being uncovered immediately, and the left half must wait until the leaf lands on it.
        /// Turning back is the mirror of that.
        /// </para>
        ///
        /// <para>
        /// Controls are suppressed for the duration — on both halves and on the leaf itself. Which
        /// corners are live is a statement about where you are in the book, and during a turn that is
        /// briefly neither answer.
        /// </para>
        /// </summary>
        private IEnumerator AnimateTurn(int next, int direction)
        {
            turning = true;

            int previous = leftPage;
            leftPage = next;

            EnsureLeaf();
            BuildLeafMesh(direction);
            PrepareTurn(previous, next, direction);

            leafObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < FlipSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float raw = Mathf.Clamp01(elapsed / FlipSeconds);

                // Smoothstep twice: the leaf leaves and lands almost still, which is what a hinged
                // sheet of paper does and what a single smoothstep is a little too brisk to sell.
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.SmoothStep(0f, 1f, raw));

                // The bow displaces along the leaf's own +Y, and the leaf turns 180 degrees about the
                // spine — so past vertical its "up" points down into the paper. Without the cosine
                // the sheet sank about 3 mm through a 1.2 mm clearance for the last fifth of the
                // turn, and the page underneath showed through the one being laid on top of it.
                // Multiplying by cos of the angle keeps the displacement on the outside of the sheet
                // whichever way up it is, and lands it at zero at both ends and edge-on in the middle,
                // where a bow could not be seen anyway.
                UpdateLeafBow(LeafCurl * Mathf.Sin(Mathf.PI * raw) * Mathf.Cos(Mathf.PI * eased));
                leafObject.transform.localRotation = Quaternion.Euler(0f, 0f, direction * 180f * eased);
                yield return null;
            }

            leafObject.transform.localRotation = Quaternion.Euler(0f, 0f, direction * 180f);
            UpdateLeafBow(0f);

            // Swapped before the leaf is taken away, never after: at 180 degrees it is lying flat
            // over the half that is about to change, so the change happens underneath it.
            CompleteTurn(next, direction);

            leafObject.SetActive(false);
            leafObject.transform.localRotation = Quaternion.identity;
            turning = false;
        }

        /// <summary>
        /// Paint the four page images a turn needs, of which only two have to be rendered.
        /// <para>
        /// The leaf's front face is the page it is carrying away, and that page is already resolved
        /// in the spread texture — so it is copied rather than set again. Its foot band is then
        /// repainted to take the control off it. Two full page renders per turn instead of four is
        /// the difference between a hitch the player sees and one they do not.
        /// </para>
        /// </summary>
        private void PrepareTurn(int previous, int next, int direction)
        {
            const int leftHalf = 0;
            int rightHalf = BookLayout.PageWidth;

            if (direction > 0)
            {
                CopyHalf(spread, rightHalf, leafPixels, leftHalf);
                RefreshFoot(leafPixels, leftHalf, previous + 1, verso: false, showControl: false);

                RenderPage(spread, rightHalf, next + 1, verso: false, showControl: false);
                RenderPage(leafPixels, rightHalf, next, verso: true, showControl: false);

                RefreshFoot(spread, leftHalf, previous, verso: true, showControl: false);
            }
            else
            {
                CopyHalf(spread, leftHalf, leafPixels, leftHalf);
                RefreshFoot(leafPixels, leftHalf, previous, verso: true, showControl: false);

                RenderPage(spread, leftHalf, next, verso: true, showControl: false);
                RenderPage(leafPixels, rightHalf, next + 1, verso: false, showControl: false);

                RefreshFoot(spread, rightHalf, previous + 1, verso: false, showControl: false);
            }

            ApplySpread();

            leafTexture.SetPixels32(leafPixels);
            leafTexture.Apply(false);
        }

        /// <summary>
        /// Land the turn. The arriving page was already set on the leaf's back face, so the half it
        /// covers is a copy rather than a third render; the two foot bands then put the controls back
        /// in whatever state the new spread calls for.
        /// </summary>
        private void CompleteTurn(int next, int direction)
        {
            const int leftHalf = 0;
            int rightHalf = BookLayout.PageWidth;

            CopyHalf(leafPixels, rightHalf, spread, direction > 0 ? leftHalf : rightHalf);

            RefreshFoot(spread, leftHalf, next, verso: true, showControl: next > 0);
            RefreshFoot(spread, rightHalf, next + 1, verso: false,
                        showControl: next + 2 < pages.Count);

            ApplySpread();
        }

        private void CancelTurn()
        {
            StopAllCoroutines();
            turning = false;

            if (leafObject == null) return;
            leafObject.transform.localRotation = Quaternion.identity;
            leafObject.SetActive(false);
        }

        // -- Drawing ----------------------------------------------------------------------------------

        /// <summary>The spread as it should look at rest: both halves current, both controls live.</summary>
        private void DrawSpread()
        {
            RenderPage(spread, 0, leftPage, verso: true, showControl: leftPage > 0);
            RenderPage(spread, BookLayout.PageWidth, leftPage + 1, verso: false,
                       showControl: leftPage + 2 < pages.Count);
            ApplySpread();
        }

        private void ApplySpread()
        {
            if (texture == null) return;

            texture.SetPixels32(spread);

            // Mipped, unlike the instrument screens: this texture is filtered bilinearly and the book
            // is a world object that is also seen from across the room, where an unmipped page of
            // 12-pixel type shimmers.
            texture.Apply(true);
        }

        private TypesetPage Page(int index) =>
            index >= 0 && index < pages.Count ? pages[index] : null;

        private static int TextLeft(bool verso) =>
            verso ? BookLayout.OuterMargin : BookLayout.GutterMargin;

        private void RenderPage(Color32[] destination, int destinationX, int pageIndex,
                                bool verso, bool showControl)
        {
            canvas.Clear();

            TypesetPage page = Page(pageIndex);
            if (page != null)
            {
                DrawRunningHead(page, verso);
                DrawLines(page, verso);
                DrawFoot(page, pageIndex, verso, showControl);
            }

            canvas.Resolve(destination, BookLayout.SpreadWidth, BookLayout.SpreadHeight,
                           destinationX, 0, 0, BookLayout.PageHeight,
                           InkColours, verso ? VersoPaper : RectoPaper);
        }

        /// <summary>Repaint only the foot margin — folio and page-corner control — of one half.</summary>
        private void RefreshFoot(Color32[] destination, int destinationX, int pageIndex,
                                 bool verso, bool showControl)
        {
            canvas.Clear();

            TypesetPage page = Page(pageIndex);
            if (page != null) DrawFoot(page, pageIndex, verso, showControl);

            canvas.Resolve(destination, BookLayout.SpreadWidth, BookLayout.SpreadHeight,
                           destinationX, 0, FootBandTop, BookLayout.PageHeight - FootBandTop,
                           InkColours, verso ? VersoPaper : RectoPaper);
        }

        /// <summary>
        /// The running head and the rule under it.
        /// <para>
        /// Verso carries the book, recto carries the chapter — the convention every printed reference
        /// uses, and the one that answers the two questions a reader of a manual actually has. Set in
        /// letterspaced capitals at two thirds the size of the text, so it reads as furniture rather
        /// than as a line of the page.
        /// </para>
        /// </summary>
        private void DrawRunningHead(TypesetPage page, bool verso)
        {
            if (page.IsTitlePage) return;

            int left = TextLeft(verso);

            string head = verso || string.IsNullOrEmpty(page.Section) ? bookTitle : page.Section;
            if (string.IsNullOrEmpty(head)) head = page.Section;

            if (!string.IsNullOrEmpty(head))
            {
                head = BookLayout.Truncate(head.ToUpperInvariant(), BookLayout.ColumnWidth,
                                           BookLayout.SmallGlyph, BookLayout.RunningHeadTracking);

                int width = BookLayout.Measure(head, BookLayout.SmallGlyph,
                                               BookLayout.RunningHeadTracking);
                int x = verso ? left : left + BookLayout.ColumnWidth - width;

                canvas.DrawText(x, BookLayout.RunningHeadTop, head, BookInk.Soft,
                                BookLayout.SmallGlyph, BookLayout.RunningHeadTracking);
            }

            canvas.Fill(left, BookLayout.RunningHeadRuleY, BookLayout.ColumnWidth,
                        BookLayout.RunningHeadRuleThickness, BookInk.Seam);
        }

        private void DrawLines(TypesetPage page, bool verso)
        {
            int left = TextLeft(verso);

            if (page.IsTitlePage)
            {
                DrawTitlePage(page, left);
                return;
            }

            int y = BookLayout.TextTop;
            for (int i = 0; i < page.Lines.Count; i++)
            {
                BookLine line = page.Lines[i];

                if (line.Style != BookLineStyle.Blank && line.Text.Length > 0)
                {
                    int x = left + line.Indent;

                    switch (line.Style)
                    {
                        case BookLineStyle.Tabular:
                            canvas.DrawTextMono(x, y, line.Text, BookInk.Ink, BookLayout.BodyGlyph);
                            break;
                        case BookLineStyle.Heading:
                            canvas.DrawText(x, y, line.Text, BookInk.Ink, BookLayout.HeadingGlyph,
                                            0, BookLayout.HeadingWeight);
                            break;
                        default:
                            canvas.DrawText(x, y, line.Text, BookInk.Ink, BookLayout.BodyGlyph);
                            break;
                    }
                }

                y += line.Advance;
            }
        }

        /// <summary>
        /// The title, ruled above and below, sitting on the optical centre of the measure rather than
        /// its geometric one — a block placed by measurement looks like it has slipped.
        /// </summary>
        private void DrawTitlePage(TypesetPage page, int left)
        {
            int block = 0;
            for (int i = 0; i < page.Lines.Count; i++) block += page.Lines[i].Advance;

            int top = BookLayout.TextTop +
                      Mathf.Max(0, (BookLayout.TextHeight - block) * 5 / 12);
            int y = top;

            for (int i = 0; i < page.Lines.Count; i++)
            {
                BookLine line = page.Lines[i];
                int width = BookLayout.Measure(line.Text, BookLayout.HeadingGlyph);

                canvas.DrawText(left + (BookLayout.ColumnWidth - width) / 2, y, line.Text,
                                BookInk.Ink, BookLayout.HeadingGlyph, 0, BookLayout.HeadingWeight);
                y += line.Advance;
            }

            int ruleWidth = BookLayout.ColumnWidth * 3 / 5;
            int ruleX = left + (BookLayout.ColumnWidth - ruleWidth) / 2;

            canvas.Fill(ruleX, top - 90, ruleWidth, 3, BookInk.Seam);
            canvas.Fill(ruleX, y + 30, ruleWidth, 3, BookInk.Seam);
        }

        /// <summary>The folio, centred in the foot margin, and the page-corner control beside it.</summary>
        private void DrawFoot(TypesetPage page, int pageIndex, bool verso, bool showControl)
        {
            if (!page.IsTitlePage)
            {
                string folio = (pageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                int width = BookLayout.Measure(folio, BookLayout.SmallGlyph);

                canvas.DrawText(TextLeft(verso) + (BookLayout.ColumnWidth - width) / 2,
                                BookLayout.FolioTop, folio, BookInk.Soft, BookLayout.SmallGlyph);
            }

            if (showControl) DrawCornerControl(left: verso);
        }

        /// <summary>
        /// The folded-corner control: a rule along the foot and a stem down the outer edge, with the
        /// arrow centred between them.
        /// <para>
        /// The arrow is centred rather than offset from the outer edge, and both sides use the very
        /// same expression to do it. Mirroring the left-hand offset to the right without allowing for
        /// the glyph's own width is what put the "next" arrow outside its own bracket and four pixels
        /// into the body text — the two controls did not look like the same control facing opposite
        /// ways, because they were not drawn the same way.
        /// </para>
        /// <para>
        /// The arrow is geometry, not a character: a triangle cannot go missing because the face
        /// happens not to carry a chevron, and a diagonal is the shape that gains most from being
        /// drawn at 3x and averaged down.
        /// </para>
        /// </summary>
        private void DrawCornerControl(bool left)
        {
            RectInt rect = CornerRectOnPage(left);
            int scale = BookLayout.Supersample;

            int x = rect.x * scale;
            int y = rect.y * scale;
            int width = rect.width * scale;
            int height = rect.height * scale;
            int stroke = 2 * scale;

            canvas.Fill(x, y + height - stroke, width, stroke, BookInk.Ink);
            canvas.Fill(left ? x : x + width - stroke, y, stroke, height, BookInk.Ink);

            int arrowWidth = width / 2;
            int arrowHeight = height * 2 / 5;

            canvas.FillArrow(x + (width - arrowWidth) / 2, y + (height - arrowHeight) / 2,
                             arrowWidth, arrowHeight, left, BookInk.Ink);
        }

        /// <summary>
        /// The paper's own colour, per final column. It warms and darkens into the spine, which is
        /// what an open book's paper does, and the last couple of columns are the seam itself — two
        /// pages of it meeting as one line down the middle of the spread.
        /// <para>
        /// Per column rather than per pixel because nothing here varies vertically, and because a
        /// column table is what lets the downsample blend partial glyph coverage onto the real paper
        /// underneath instead of onto a flat average of it.
        /// </para>
        /// </summary>
        private static Color32[] BuildPaperColumns(bool verso)
        {
            var columns = new Color32[BookLayout.PageWidth];

            for (int x = 0; x < BookLayout.PageWidth; x++)
            {
                int fromSpine = verso ? BookLayout.PageWidth - 1 - x : x;

                if (fromSpine < SeamWidth)
                {
                    columns[x] = Seam;
                    continue;
                }

                float t = 1f - Mathf.Clamp01((fromSpine - SeamWidth) / (float)GutterShadeWidth);
                columns[x] = Color32.Lerp(Paper, GutterPaper, t * t);
            }

            return columns;
        }

        private static void CopyHalf(Color32[] source, int sourceX,
                                     Color32[] destination, int destinationX)
        {
            for (int row = 0; row < BookLayout.SpreadHeight; row++)
                System.Array.Copy(source, row * BookLayout.SpreadWidth + sourceX,
                                  destination, row * BookLayout.SpreadWidth + destinationX,
                                  BookLayout.PageWidth);
        }

        // -- Geometry ------------------------------------------------------------------------------------

        private void EnsureSurface()
        {
            if (pageRenderer != null) return;

            var page = new GameObject("OpenPages");
            page.transform.SetParent(transform, false);
            page.transform.localPosition = new Vector3(0f, 0.014f, 0f);
            pageTransform = page.transform;

            mesh = BuildThickBookMesh();
            page.AddComponent<MeshFilter>().sharedMesh = mesh;

            texture = new Texture2D(BookLayout.SpreadWidth, BookLayout.SpreadHeight,
                                    TextureFormat.RGBA32, true, false)
            {
                name = $"{name}_Pages",

                // Bilinear, not point: the page is anti-aliased on the way into this texture, and
                // point sampling would throw that away at every angle except dead on.
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4
            };

            spread = new Color32[BookLayout.SpreadWidth * BookLayout.SpreadHeight];
            canvas = new BookCanvas(BookLayout.PageWidth, BookLayout.PageHeight, BookLayout.Supersample);

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            material = new Material(shader) { name = $"{name}_PageMaterial", mainTexture = texture };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            pageRenderer = page.AddComponent<MeshRenderer>();
            pageRenderer.sharedMaterial = material;
            pageRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            pageRenderer.gameObject.SetActive(false);
        }

        private void EnsureLeaf()
        {
            if (leafObject != null) return;

            leafObject = new GameObject("TurningPage");
            leafObject.transform.SetParent(pageTransform, false);
            leafObject.transform.localPosition = new Vector3(0f, PageHalfThickness + 0.0012f, 0f);

            leafMesh = new Mesh { name = $"{name}_TurningPageMesh" };
            leafObject.AddComponent<MeshFilter>().sharedMesh = leafMesh;
            leafRenderer = leafObject.AddComponent<MeshRenderer>();

            leafTexture = new Texture2D(BookLayout.SpreadWidth, BookLayout.SpreadHeight,
                                        TextureFormat.RGBA32, false, false)
            {
                name = $"{name}_TurningPage",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            int leafSize = BookLayout.SpreadWidth * BookLayout.SpreadHeight;
            if (sharedLeafPixels == null || sharedLeafPixels.Length < leafSize)
                sharedLeafPixels = new Color32[leafSize];
            leafPixels = sharedLeafPixels;

            leafMaterial = new Material(material) { name = $"{name}_TurningPageMaterial" };
            leafMaterial.mainTexture = leafTexture;
            if (leafMaterial.HasProperty("_BaseMap")) leafMaterial.SetTexture("_BaseMap", leafTexture);

            leafRenderer.sharedMaterial = leafMaterial;
            leafRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            leafObject.SetActive(false);
        }

        /// <summary>
        /// Build the turning leaf: two single-sided sheets back to back over the half of the spread
        /// being turned, subdivided so the paper can bow.
        ///
        /// <para>
        /// The front samples the left half of the leaf texture and the back samples the right half,
        /// and the back's u runs the other way — because after 180 degrees about the spine a point at
        /// local x sits at -x, so the arriving page has to be laid on in reverse to come up the right
        /// way round. Getting this wrong is invisible until the leaf passes vertical, which is why it
        /// is arithmetic here rather than a flag somewhere.
        /// </para>
        /// </summary>
        private void BuildLeafMesh(int direction)
        {
            float x0 = direction > 0 ? 0f : -BookHalfWidth;
            float x1 = direction > 0 ? BookHalfWidth : 0f;

            int columns = LeafSegments + 1;
            int perFace = columns * 2;

            var vertices = new Vector3[perFace * 2];
            var uv = new Vector2[perFace * 2];
            var triangles = new int[LeafSegments * 12];

            leafFractions = new float[perFace * 2];

            for (int c = 0; c < columns; c++)
            {
                float s = c / (float)LeafSegments;
                float x = Mathf.Lerp(x0, x1, s);

                float front = 0.5f * s;
                float back = 1f - 0.5f * s;

                vertices[c] = new Vector3(x, 0f, -BookHalfDepth);
                vertices[columns + c] = new Vector3(x, 0f, BookHalfDepth);
                uv[c] = new Vector2(front, 0f);
                uv[columns + c] = new Vector2(front, 1f);

                vertices[perFace + c] = vertices[c];
                vertices[perFace + columns + c] = vertices[columns + c];
                uv[perFace + c] = new Vector2(back, 0f);
                uv[perFace + columns + c] = new Vector2(back, 1f);

                leafFractions[c] = s;
                leafFractions[columns + c] = s;
                leafFractions[perFace + c] = s;
                leafFractions[perFace + columns + c] = s;
            }

            int t = 0;
            for (int c = 0; c < LeafSegments; c++)
            {
                int a = c, b = c + 1, d = columns + c, e = columns + c + 1;

                // Wound as the book's own top face is, so the front faces up.
                triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                triangles[t++] = d; triangles[t++] = e; triangles[t++] = b;

                // And the same quad the other way round, so the back faces down until it is turned.
                triangles[t++] = perFace + a; triangles[t++] = perFace + b; triangles[t++] = perFace + d;
                triangles[t++] = perFace + d; triangles[t++] = perFace + b; triangles[t++] = perFace + e;
            }

            leafFlat = vertices;
            leafPosed = (Vector3[])vertices.Clone();

            leafMesh.Clear();
            leafMesh.vertices = vertices;
            leafMesh.uv = uv;
            leafMesh.triangles = triangles;
            leafMesh.RecalculateNormals();
            leafMesh.RecalculateBounds();
        }

        /// <summary>
        /// Bow the leaf. Zero at the spine and at the free edge, greatest across the middle — a sheet
        /// hinged along one edge and lifted, rather than a rigid plate on a hinge.
        /// </summary>
        private void UpdateLeafBow(float bow)
        {
            if (leafMesh == null || leafFlat == null) return;

            for (int i = 0; i < leafFlat.Length; i++)
            {
                Vector3 vertex = leafFlat[i];
                vertex.y = bow * Mathf.Sin(Mathf.PI * leafFractions[i]);
                leafPosed[i] = vertex;
            }

            leafMesh.vertices = leafPosed;
            leafMesh.RecalculateBounds();
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

        private void OnDestroy()
        {
            if (texture != null) Destroy(texture);
            if (leafTexture != null) Destroy(leafTexture);
            if (mesh != null) Destroy(mesh);
            if (leafMesh != null) Destroy(leafMesh);
            if (material != null) Destroy(material);
            if (leafMaterial != null) Destroy(leafMaterial);
        }
    }
}
