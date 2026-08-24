using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// A generated open-paper spread attached to a physical reference book. Text is rasterised onto
    /// the mesh texture itself; this is not a screen-space reading panel.
    /// </summary>
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
        private int leftPage;

        public int PageCount => pages.Count;
        public int LeftPage => leftPage;

        public void SetContent(string title, IReadOnlyList<BookPage> source)
        {
            EnsureSurface();
            pages.Clear();
            leftPage = 0;

            if (!string.IsNullOrEmpty(title)) AddSection("REFERENCE", title);
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                    AddSection(source[i]?.Title, source[i]?.Body);
            }

            if (pages.Count == 0) pages.Add(new List<string> { "NO CONTENT" });
            DrawSpread();
        }

        public void Show(bool visible)
        {
            EnsureSurface();
            if (pageRenderer == null) return;
            pageRenderer.enabled = true;
            pageRenderer.gameObject.SetActive(visible);
        }

        public void Turn(int direction)
        {
            int next = Mathf.Clamp(leftPage + (direction < 0 ? -2 : 2), 0,
                Mathf.Max(0, ((pages.Count - 1) / 2) * 2));
            if (next == leftPage) return;
            leftPage = next;
            DrawSpread();
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

            string normal = text.Replace("\r", string.Empty);
            foreach (string paragraph in normal.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(paragraph))
                {
                    output.Add(string.Empty);
                    continue;
                }

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
            page.transform.localPosition = new Vector3(0f, 0.031f, 0f);

            mesh = new Mesh { name = $"{name}_OpenPages" };
            mesh.vertices = new[]
            {
                new Vector3(-0.18f, 0f, -0.12f), new Vector3(0.18f, 0f, -0.12f),
                new Vector3(-0.18f, 0f, 0.12f), new Vector3(0.18f, 0f, 0.12f)
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
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
            pageRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pageRenderer.gameObject.SetActive(false);
        }

        private void DrawSpread()
        {
            if (texture == null) return;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Paper;

            FillRect(HalfWidth - 2, 0, 4, TextureHeight, Seam);
            DrawPage(leftPage, 0);
            DrawPage(leftPage + 1, HalfWidth);

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private void DrawPage(int pageIndex, int xOffset)
        {
            if (pageIndex < 0 || pageIndex >= pages.Count) return;
            var lines = pages[pageIndex];
            for (int i = 0; i < lines.Count && i < LinesPerPage; i++)
                DrawText(xOffset + MarginX, MarginY + i * LineHeight, lines[i], i == 0 ? Ink : SoftInk);

            string footer = $"{pageIndex + 1}/{pages.Count}";
            DrawText(xOffset + HalfWidth - MarginX - PixelFont.MeasureWidth(footer, Scale),
                TextureHeight - MarginY - PixelFont.GlyphHeight * Scale, footer, SoftInk);
        }

        private void DrawText(int x, int y, string text, Color32 colour)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (char ch in text)
            {
                string glyph = PixelFont.Glyph(ch);
                for (int gy = 0; gy < PixelFont.GlyphHeight; gy++)
                    for (int gx = 0; gx < PixelFont.GlyphWidth; gx++)
                        if (PixelFont.IsOn(glyph, gx, gy))
                            FillRect(x + gx * Scale, y + gy * Scale, Scale, Scale, colour);
                x += PixelFont.Advance * Scale;
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
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
