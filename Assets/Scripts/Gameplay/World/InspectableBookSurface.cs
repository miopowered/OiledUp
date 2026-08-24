using System.Collections;
using System.Collections.Generic;
using System.Text;
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
        private const int LineHeight = (PixelFont.GlyphHeight + 2) * Scale;
        private const int Columns = (HalfWidth - MarginX * 2) / (PixelFont.Advance * Scale);
        private const int LinesPerPage = (TextureHeight - MarginY * 2 - LineHeight) / LineHeight;
        private const float BookHalfWidth = 0.18f;
        private const float BookHalfDepth = 0.12f;
        private const float PageHalfThickness = 0.014f;
        private const float FlipSeconds = 0.24f;

        private static readonly Color32 Paper = new(232, 226, 209, 255);
        private static readonly Color32 Ink = new(42, 38, 33, 255);
        private static readonly Color32 SoftInk = new(105, 96, 84, 255);
        private static readonly Color32 Seam = new(181, 170, 148, 255);

        private readonly List<List<string>> pages = new();
        private Texture2D texture;
        private Color32[] pixels;
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

        /// <summary>Turns when the pointer presses one of the two printed page-corner controls.</summary>
        public bool TryPressPageCorner(Camera camera, Vector2 screenPosition)
        {
            if (camera == null || pageTransform == null || turning) return false;

            var ray = camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(pageTransform.up, pageTransform.position + pageTransform.up * PageHalfThickness);
            if (!plane.Raycast(ray, out float distance)) return false;

            Vector3 local = pageTransform.InverseTransformPoint(ray.GetPoint(distance));
            if (local.z > -0.055f || Mathf.Abs(local.x) > BookHalfWidth ||
                Mathf.Abs(local.z) > BookHalfDepth) return false;

            if (local.x < -0.105f)
            {
                if (leftPage > 0) Turn(-1);
                return true;
            }
            if (local.x > 0.105f)
            {
                if (leftPage + 2 < pages.Count) Turn(1);
                return true;
            }
            return false;
        }

        public void Turn(int direction)
        {
            if (turning) return;
            int next = Mathf.Clamp(leftPage + (direction < 0 ? -2 : 2), 0,
                Mathf.Max(0, ((pages.Count - 1) / 2) * 2));
            if (next == leftPage) return;
            StartCoroutine(AnimateTurn(next, direction < 0 ? -1 : 1));
        }

        private IEnumerator AnimateTurn(int next, int direction)
        {
            turning = true;
            SnapshotFlipPage(direction);
            leftPage = next;
            DrawSpread();

            flipObject.SetActive(true);
            float elapsed = 0f;
            while (elapsed < FlipSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / FlipSeconds));
                flipObject.transform.localRotation = Quaternion.Euler(0f, 0f, direction * 180f * t);
                yield return null;
            }

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

        private void AddSection(string heading, string body)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(heading)) lines.Add(heading.ToUpperInvariant());
            if (lines.Count > 0) lines.Add(string.Empty);
            Wrap(body, lines);

            for (int start = 0; start < lines.Count; start += LinesPerPage)
            {
                int count = Mathf.Min(LinesPerPage, lines.Count - start);
                pages.Add(lines.GetRange(start, count));
            }
        }

        private static void Wrap(string text, List<string> output)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(paragraph)) { output.Add(string.Empty); continue; }
                var line = new StringBuilder();
                foreach (string word in paragraph.Trim().Split(' '))
                {
                    if (word.Length == 0) continue;
                    if (line.Length > 0 && line.Length + 1 + word.Length > Columns)
                    {
                        output.Add(line.ToString());
                        line.Clear();
                    }
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word.Length <= Columns ? word : word.Substring(0, Columns));
                }
                if (line.Length > 0) output.Add(line.ToString());
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
            pixels = new Color32[TextureWidth * TextureHeight];

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

        private void DrawSpread()
        {
            if (texture == null) return;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Paper;

            FillRect(HalfWidth - 2, 0, 4, TextureHeight, Seam);
            DrawPage(leftPage, 0);
            DrawPage(leftPage + 1, HalfWidth);
            if (leftPage > 0) DrawCornerButton(left: true);
            if (leftPage + 2 < pages.Count) DrawCornerButton(left: false);

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private void DrawPage(int pageIndex, int xOffset)
        {
            if (pageIndex < 0 || pageIndex >= pages.Count) return;
            var lines = pages[pageIndex];
            for (int i = 0; i < lines.Count && i < LinesPerPage; i++)
                DrawText(xOffset + MarginX, MarginY + i * LineHeight, lines[i], i == 0 ? Ink : SoftInk, Scale);

            string footer = $"{pageIndex + 1}/{pages.Count}";
            DrawText(xOffset + (HalfWidth - PixelFont.MeasureWidth(footer, Scale)) / 2,
                TextureHeight - MarginY - PixelFont.GlyphHeight * Scale, footer, SoftInk, Scale);
        }

        private void DrawCornerButton(bool left)
        {
            int bottom = TextureHeight - 12;
            int outer = left ? 10 : TextureWidth - 10;
            int inward = left ? 34 : TextureWidth - 34;
            int x = Mathf.Min(outer, inward);
            FillRect(x, bottom - 2, Mathf.Abs(inward - outer), 2, Ink);
            FillRect(left ? outer : outer - 2, bottom - 26, 2, 26, Ink);
            DrawText(left ? 14 : TextureWidth - 38, bottom - 22, left ? "<" : ">", Ink, 4);
        }

        private void DrawText(int x, int y, string text, Color32 colour, int glyphScale)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (char ch in text)
            {
                string glyph = PixelFont.Glyph(ch);
                for (int gy = 0; gy < PixelFont.GlyphHeight; gy++)
                    for (int gx = 0; gx < PixelFont.GlyphWidth; gx++)
                        if (PixelFont.IsOn(glyph, gx, gy))
                            FillRect(x + gx * glyphScale, y + gy * glyphScale,
                                glyphScale, glyphScale, colour);
                x += PixelFont.Advance * glyphScale;
            }
        }

        private void FillRect(int x, int y, int width, int height, Color32 colour)
        {
            for (int dy = 0; dy < height; dy++)
            {
                int py = TextureHeight - 1 - (y + dy);
                if (py < 0 || py >= TextureHeight) continue;
                for (int dx = 0; dx < width; dx++)
                {
                    int px = x + dx;
                    if (px >= 0 && px < TextureWidth) pixels[py * TextureWidth + px] = colour;
                }
            }
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
