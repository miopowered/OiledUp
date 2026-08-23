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

        /// <summary>
        /// What is currently rasterised, as a fold of the reading that produced it. Zero for anything
        /// that is not a reading — idle, a progress bar, a notice.
        /// <para>
        /// This is what lets the client-side pull below coexist with the station driving the same
        /// screen: the station clears to <see cref="ShowIdle"/> when a run ends because on its side
        /// there is nothing to draw, and the pull then notices that what is on the glass is not the
        /// reading the host published and puts it back. Without it the two would fight at 4 Hz.
        /// </para>
        /// </summary>
        private int drawn;

        private float nextPull;
        private string resolvedInstanceId;

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

        /// <summary>
        /// The instrument's name in screen case, or a stand-in before the definition is known — which
        /// on a client is the first few frames after the lab scene loads.
        /// </summary>
        private static string Title(IMachineView machine) =>
            machine != null && machine.Def != null ? machine.Def.DisplayName : "INSTRUMENT";

        public void ShowIdle(IMachineView machine)
        {
            Clear();
            DrawText(2, 2, Shorten(Title(machine), Columns), ink);
            DrawText(2, 2 + LineHeight, "READY", dim);
            Apply();
            drawn = 0;
        }

        /// <summary>
        /// Put the host's last reading back on the glass, on a process that has no run-completed event
        /// to draw from.
        /// <para>
        /// Pulled rather than pushed, for the reason the record feed gives: the host republishes on
        /// its own clock and on every accepted command, and a screen that rasterised on arrival would
        /// rasterise at times this component has no say over. Four times a second against a fold of
        /// what is already drawn, so the common case costs a dictionary lookup and a comparison.
        /// </para>
        /// Off entirely where this process simulates. There the station draws the result the instant
        /// the run completes, from the object itself, and a second source would only ever be later.
        /// </summary>
        private void Update()
        {
            if (!RecordFeed.IsReplicated) return;
            if (Time.time < nextPull) return;
            nextPull = Time.time + 0.25f;

            var machine = LabView.Current?.Machine(InstanceId);

            // While it runs the screen belongs to the station's progress bar, which is the same
            // readout every player in the room is watching.
            if (machine == null || machine.IsRunning) return;

            var feed = RecordFeed.Source;
            if (feed == null || !feed.TryLastReading(InstanceId, out var reading, out var sample)) return;

            if (Fold(reading, sample) == drawn) return;
            Show(machine, reading, sample);
        }

        /// <summary>
        /// Which instrument this screen belongs to, taken off the station it hangs under. Resolved
        /// once: the scene builder parents every display to its machine, and a screen that moved to a
        /// different instrument mid-session would be a different bug entirely.
        /// </summary>
        private string InstanceId
        {
            get
            {
                if (resolvedInstanceId != null) return resolvedInstanceId;

                var station = GetComponentInParent<MachineStation>();
                resolvedInstanceId = station != null ? station.InstanceId : "";
                return resolvedInstanceId;
            }
        }

        /// <summary>
        /// The progress readout. Everything it draws is replicated (§3.1), so this is the same screen
        /// on the host's monitor and on a joined client's — which matters because the run clock is how
        /// a player decides whether to wait at the machine or go and do something else.
        /// </summary>
        public void ShowRunning(IMachineView machine)
        {
            if (machine == null) return;

            Clear();
            DrawText(2, 2, Shorten(Title(machine), Columns), dim);
            DrawText(2, 2 + LineHeight, $"RUNNING {machine.SecondsRemaining:F0}S", ink);
            DrawProgressBar(2, 2 + LineHeight * 2, Columns * PixelFont.Advance * scale - 4, machine.Progress);
            Apply();
            drawn = 0;
        }

        /// <summary>
        /// A short notice for something that happened to the instrument rather than to a sample.
        /// A recalibration has no reading to draw, and a screen still showing the previous result
        /// would leave the player with no sign at the machine that anything happened at all.
        /// </summary>
        public void ShowNotice(IMachineView machine, string headline, string detail)
        {
            Clear();
            DrawText(2, 2, Shorten(Title(machine), Columns), dim);
            DrawText(2, 2 + LineHeight, Shorten(headline ?? "", Columns), ink);
            DrawText(2, 2 + LineHeight * 2, Shorten(detail ?? "", Columns), dim);
            Apply();
            drawn = 0;
        }

        /// <summary>
        /// Draw a finished reading, from the vial the instrument is holding. The host's path: the
        /// bottle is physically in the machine, so its paper label is what the screen captions.
        /// </summary>
        public void Show(IMachineView machine, TestResult result, SampleState sample)
        {
            if (result == null) { ShowIdle(machine); return; }

            Draw(machine, result, Caption(result, sample?.EquipmentTag), Fold(result, sample?.Id ?? SampleId.None));
        }

        /// <summary>
        /// Draw a finished reading a client read off the wire, captioned with the sample's id.
        /// <para>
        /// <b>Not the tank tag.</b> Neither one, in fact: the paper label reaches a client through
        /// <c>VialView</c> and must never reach a screen (§5.1 — a display that could show it beside
        /// what someone typed corrects the mis-log for free), and the typed tag would caption this
        /// screen differently from the host's, which is the co-op divergence the whole view layer
        /// exists to prevent. The id is what both sides can print, and the terminal prints it beside
        /// the record so the two can be matched.
        /// </para>
        /// </summary>
        public void Show(IMachineView machine, TestResult result, SampleId sample)
        {
            if (result == null) { ShowIdle(machine); return; }

            Draw(machine, result, Caption(result, sample.IsValid ? sample.ToString() : null),
                 Fold(result, sample));
        }

        private void Draw(IMachineView machine, TestResult result, string caption, int fold)
        {
            RecordHistory(result, caption);

            Clear();
            if (style == DisplayStyle.Numeric) DrawNumeric(result, caption);
            else DrawPanel(machine, result, caption);
            Apply();
            drawn = fold;
        }

        /// <summary>
        /// A run's identity as far as this screen is concerned. Order-independent, because a
        /// <see cref="TestResult.Values"/> map has no order to depend on.
        /// </summary>
        private static int Fold(TestResult result, SampleId sample)
        {
            if (result == null) return 0;

            int fold = result.DayRun * 397 ^ sample.Value ^ (result.IsBlank ? 7 : 0) ^
                       (result.IsReference ? 13 : 0);

            foreach (var kv in result.Values) fold ^= kv.Key.GetHashCode() * 31 ^ kv.Value.GetHashCode();

            // Zero means "not a reading" to the puller above, so never hand it back as one.
            return fold == 0 ? 1 : fold;
        }

        // -- Layouts ---------------------------------------------------------------------------------

        private void DrawNumeric(TestResult result, string caption)
        {
            int y = 2;
            DrawText(2, y, Shorten(caption, Columns), dim);
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

        private void DrawPanel(IMachineView machine, TestResult result, string caption)
        {
            int y = 2;
            DrawText(2, y, Shorten(Title(machine), Columns), ink);
            y += LineHeight;

            DrawText(2, y, Shorten(caption, Columns), dim);
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

        private void RecordHistory(TestResult result, string caption)
        {
            history.Insert(0, $"D{result.DayRun} {Shorten(caption, 14)}");
            while (history.Count > 6) history.RemoveAt(history.Count - 1);
        }

        /// <summary>
        /// What the run was of. A standard has to name itself: an instrument screen showing a full
        /// panel of plausible numbers with no sample named beside it reads as somebody else's sample.
        /// </summary>
        private static string Caption(TestResult result, string sample)
        {
            if (result.IsBlank) return "SOLVENT BLANK";
            if (result.IsReference) return "CERT STANDARD";
            return string.IsNullOrEmpty(sample) ? "-" : sample;
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
