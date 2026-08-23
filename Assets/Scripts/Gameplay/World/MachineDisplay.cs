using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// An instrument's own readout, drawn as a pixel texture onto an emissive panel.
    /// <para>
    /// This is where a result first becomes visible. Instruments no longer write into the sample's
    /// history — you read the numbers here, and carry the slip to the desk if you want them on the
    /// record. That also keeps the printout mechanic fair: losing a slip costs you the walk, not
    /// the information.
    /// </para>
    /// <see cref="DisplayStyle.Numeric"/> is a small single-value readout for a titrator;
    /// <see cref="DisplayStyle.Panel"/> is the larger multi-line screen a spectrometer would have,
    /// including its own run history.
    /// </summary>
    public sealed class MachineDisplay : MonoBehaviour
    {
        public enum DisplayStyle
        {
            /// <summary>A couple of large values. Karl Fischer, viscometer.</summary>
            Numeric,

            /// <summary>Dense multi-line table with history. ICP, FTIR, ferrography.</summary>
            Panel
        }

        [SerializeField] private Renderer screen;
        [SerializeField] private DisplayStyle style = DisplayStyle.Numeric;
        [SerializeField] private int pixelWidth = 128;
        [SerializeField] private int pixelHeight = 64;
        [SerializeField] private int scale = 2;

        [Header("Colours (never palette row 4 — this is data, not verdict state)")]
        [SerializeField] private Color background = new(0.04f, 0.06f, 0.07f);
        [SerializeField] private Color ink = new(0.55f, 0.92f, 0.85f);
        [SerializeField] private Color dim = new(0.20f, 0.42f, 0.42f);

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private Texture2D texture;
        private Color32[] buffer;
        private Material instance;

        /// <summary>Most recent runs on this instrument, newest first. Purely local to the screen.</summary>
        private readonly List<string> history = new();

        private void Awake()
        {
            texture = new Texture2D(pixelWidth, pixelHeight, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = $"{name}_Screen"
            };
            buffer = new Color32[pixelWidth * pixelHeight];

            if (screen != null)
            {
                instance = screen.material; // per-instance so each machine has its own screen
                instance.SetTexture(BaseMap, texture);
                instance.EnableKeyword("_EMISSION");
                instance.SetTexture(EmissionMap, texture);
                instance.SetColor(EmissionColor, Color.white);
            }

            ShowIdle(null);
        }

        private void OnDestroy()
        {
            if (texture != null) Destroy(texture);
            if (instance != null) Destroy(instance);
        }

        // -- Public API ------------------------------------------------------------------------------

        public void ShowIdle(MachineInstance machine)
        {
            Clear();
            string title = machine?.Def != null ? machine.Def.DisplayName : "INSTRUMENT";
            DrawText(2, 2, Shorten(title, Columns), ink);
            DrawText(2, 2 + LineHeight, "READY", dim);
            Apply();
        }

        public void ShowRunning(MachineInstance machine)
        {
            Clear();
            DrawText(2, 2, Shorten(machine.Def.DisplayName, Columns), dim);
            DrawText(2, 2 + LineHeight, $"RUNNING {machine.SecondsRemaining:F0}S", ink);
            DrawProgressBar(2, 2 + LineHeight * 2, Columns * PixelFont.Advance * scale - 4, machine.Progress);
            Apply();
        }

        public void Show(MachineInstance machine, TestResult result, SampleState sample)
        {
            if (result == null) { ShowIdle(machine); return; }

            RecordHistory(result, sample);

            Clear();
            if (style == DisplayStyle.Numeric) DrawNumeric(machine, result, sample);
            else DrawPanel(machine, result, sample);
            Apply();
        }

        // -- Layouts ---------------------------------------------------------------------------------

        private void DrawNumeric(MachineInstance machine, TestResult result, SampleState sample)
        {
            int y = 2;
            DrawText(2, y, result.IsBlank ? "SOLVENT BLANK" : Shorten(sample?.EquipmentTag ?? "-", Columns), dim);
            y += LineHeight;

            // A small readout shows the values large rather than many. Two lines at double size.
            int shown = 0;
            foreach (var kv in result.Values)
            {
                if (shown >= 2) break;
                var element = LabRuntime.Instance?.Catalog?.Element(kv.Key);
                string unit = element != null ? element.Unit : "";

                DrawText(2, y, kv.Key, dim);
                DrawText(2 + PixelFont.MeasureWidth(kv.Key + " ", scale), y, $"{kv.Value:0.###} {unit}", ink, scale + 1);
                y += LineHeight + 3;
                shown++;
            }

            if (shown == 0) DrawText(2, y, "NO READING", dim);
        }

        private void DrawPanel(MachineInstance machine, TestResult result, SampleState sample)
        {
            int y = 2;
            DrawText(2, y, Shorten(machine.Def.DisplayName, Columns), ink);
            y += LineHeight;

            DrawText(2, y, result.IsBlank ? "SOLVENT BLANK" : Shorten(sample?.EquipmentTag ?? "-", Columns), dim);
            y += LineHeight;

            DrawRule(2, y + 1, Columns * PixelFont.Advance * scale - 4);
            y += 4;

            foreach (var kv in result.Values)
            {
                if (y + LineHeight > pixelHeight - LineHeight * 3) break;

                var element = LabRuntime.Instance?.Catalog?.Element(kv.Key);
                string unit = element != null ? element.Unit : "";

                DrawText(2, y, kv.Key, dim);
                DrawText(2 + PixelFont.MeasureWidth("XXXXXXX", scale), y, $"{kv.Value:0.###}", ink);
                DrawText(2 + PixelFont.MeasureWidth("XXXXXXXXXXXXXX", scale), y, unit, dim);
                y += LineHeight;
            }

            // Local run history — this instrument's own log, not the sample's record.
            int historyY = pixelHeight - LineHeight * (Mathf.Min(history.Count, 2) + 1) - 2;
            DrawRule(2, historyY - 2, Columns * PixelFont.Advance * scale - 4);
            DrawText(2, historyY, "HISTORY", dim);

            for (int i = 0; i < history.Count && i < 2; i++)
                DrawText(2, historyY + LineHeight * (i + 1), Shorten(history[i], Columns), dim);
        }

        private void RecordHistory(TestResult result, SampleState sample)
        {
            string label = result.IsBlank ? "BLANK" : Shorten(sample?.EquipmentTag ?? "-", 14);
            history.Insert(0, $"D{result.DayRun} {label}");
            while (history.Count > 6) history.RemoveAt(history.Count - 1);
        }

        // -- Raster ----------------------------------------------------------------------------------

        private int LineHeight => (PixelFont.GlyphHeight + 2) * scale;
        private int Columns => Mathf.Max(1, (pixelWidth - 4) / (PixelFont.Advance * scale));

        private void Clear()
        {
            var bg = (Color32)background;
            for (int i = 0; i < buffer.Length; i++) buffer[i] = bg;
        }

        private void Apply()
        {
            texture.SetPixels32(buffer);
            texture.Apply(false);
        }

        private void DrawText(int x, int y, string text, Color colour) => DrawText(x, y, text, colour, scale);

        private void DrawText(int x, int y, string text, Color colour, int glyphScale)
        {
            if (string.IsNullOrEmpty(text)) return;
            var c = (Color32)colour;

            foreach (char ch in text)
            {
                string glyph = PixelFont.Glyph(ch);
                for (int gy = 0; gy < PixelFont.GlyphHeight; gy++)
                {
                    for (int gx = 0; gx < PixelFont.GlyphWidth; gx++)
                    {
                        if (!PixelFont.IsOn(glyph, gx, gy)) continue;
                        FillRect(x + gx * glyphScale, y + gy * glyphScale, glyphScale, glyphScale, c);
                    }
                }
                x += PixelFont.Advance * glyphScale;
            }
        }

        private void DrawRule(int x, int y, int width) => FillRect(x, y, width, 1, dim);

        private void DrawProgressBar(int x, int y, int width, float fraction)
        {
            FillRect(x, y, width, scale, dim);
            FillRect(x, y, Mathf.RoundToInt(width * Mathf.Clamp01(fraction)), scale, ink);
        }

        /// <summary>Rows are written top-down; textures are bottom-up, so y is flipped here once.</summary>
        private void FillRect(int x, int y, int w, int h, Color32 colour)
        {
            for (int dy = 0; dy < h; dy++)
            {
                int py = pixelHeight - 1 - (y + dy);
                if (py < 0 || py >= pixelHeight) continue;

                int row = py * pixelWidth;
                for (int dx = 0; dx < w; dx++)
                {
                    int px = x + dx;
                    if (px < 0 || px >= pixelWidth) continue;
                    buffer[row + px] = colour;
                }
            }
        }

        private static string Shorten(string text, int columns)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= columns ? text : text.Substring(0, Mathf.Max(1, columns));
        }
    }
}
