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
        private PixelCanvas canvas;
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
            canvas = new PixelCanvas(pixelWidth, pixelHeight);

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
            machine != null && machine.Def != null
                ? machine.Def.DisplayName
                : ScreenStrings.ScreenInstrumentUnknown;

        public void ShowIdle(IMachineView machine)
        {
            Clear();
            DrawText(2, 2, PixelText.Truncate(Title(machine), Columns), ink);
            DrawText(2, 2 + LineHeight, PixelText.Truncate(ScreenStrings.ScreenReady, Columns), dim);
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
            DrawText(2, 2, PixelText.Truncate(Title(machine), Columns), dim);
            DrawText(2, 2 + LineHeight, PixelText.Truncate(
                ScreenStrings.ScreenRunning.Format(
                    ("seconds", machine.SecondsRemaining.ToString("F0"))),
                Columns), ink);
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
            DrawText(2, 2, PixelText.Truncate(Title(machine), Columns), dim);
            DrawText(2, 2 + LineHeight, PixelText.Truncate(headline ?? "", Columns), ink);
            DrawText(2, 2 + LineHeight * 2, PixelText.Truncate(detail ?? "", Columns), dim);
            Apply();
            drawn = 0;
        }

        /// <summary>
        /// Draw a finished reading. <b>One entry point, deliberately (#56).</b>
        /// <para>
        /// This used to be two overloads — one taking the host's <see cref="SampleState"/> and
        /// captioning with its label, one taking a <see cref="SampleId"/> off the wire and captioning
        /// with the id — and they disagreed, so two players at the same instrument read different
        /// captions for the same run. There is now nothing to disagree: the caller passes the id,
        /// <see cref="RunCaption"/> resolves it the same way on both sides, and a second overload
        /// cannot quietly grow a second answer. See <see cref="RunCaption"/> for why the label is
        /// allowed on a screen at all now that booking-in is gone.
        /// </para>
        /// </summary>
        public void Show(IMachineView machine, TestResult result, SampleId sample)
        {
            if (result == null) { ShowIdle(machine); return; }

            Draw(machine, result, RunCaption.For(result, sample), Fold(result, sample));
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
            DrawText(2, y, PixelText.Truncate(caption, Columns), dim);
            y += LineHeight;

            // A small readout shows the values large rather than many. Two lines at double size.
            int shown = 0;
            foreach (var kv in result.Values)
            {
                if (shown >= 2) break;
                var element = LabRuntime.Instance?.Catalog?.Element(kv.Key);
                string unit = element != null ? element.Unit : "";

                DrawText(2, y, kv.Key, dim);
                DrawText(2 + PixelFont.MeasureWidth(kv.Key + " ", scale), y,
                    ScreenStrings.ScreenValue.Format(
                        ("value", kv.Value.ToString("0.###")), ("unit", unit)),
                    ink, scale + 1);
                y += LineHeight + 3;
                shown++;
            }

            if (shown == 0)
                DrawText(2, y, PixelText.Truncate(ScreenStrings.ScreenNoReading, Columns), dim);
        }

        private void DrawPanel(IMachineView machine, TestResult result, string caption)
        {
            int y = 2;
            DrawText(2, y, PixelText.Truncate(Title(machine), Columns), ink);
            y += LineHeight;

            DrawText(2, y, PixelText.Truncate(caption, Columns), dim);
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
            DrawText(2, historyY, PixelText.Truncate(ScreenStrings.ScreenHistory, Columns), dim);

            for (int i = 0; i < history.Count && i < 2; i++)
                DrawText(2, historyY + LineHeight * (i + 1), PixelText.Truncate(history[i], Columns), dim);
        }

        private void RecordHistory(TestResult result, string caption)
        {
            history.Insert(0, ScreenStrings.ScreenHistoryLine.Format(
                ("day", result.DayRun),
                ("caption", PixelText.Truncate(caption, 14))));
            while (history.Count > 6) history.RemoveAt(history.Count - 1);
        }

        // -- Raster ----------------------------------------------------------------------------------

        private int LineHeight => PixelText.LineHeight(scale);

        /// <summary>
        /// Characters across the glass, less a two-pixel margin at each edge — the same inset every
        /// <c>DrawText</c> call below starts from.
        /// <para>
        /// Everything on this screen is cut to it rather than wrapped to it. Each line here is a
        /// labelled field at a fixed y — title, caption, value, history — so a caption that reflowed
        /// would push the number under it off the glass, which is the one thing the player walked
        /// over to read. <see cref="PixelText"/> holds both behaviours and the reasoning for the
        /// split; the book's pages take the other branch.
        /// </para>
        /// <para>
        /// This is also the layout's only defence against a translation (#55). A German READY is
        /// longer than an English one and nothing in <c>ScreenStrings</c> can know how wide this
        /// screen is, so every line drawn here is cut here — including the fixed notices, which
        /// were literals short enough not to need it and are not any more.
        /// </para>
        /// </summary>
        private int Columns => PixelText.Columns(pixelWidth - 4, scale);

        private void Clear() => canvas.Clear(background);

        private void Apply() => canvas.ApplyTo(texture);

        private void DrawText(int x, int y, string text, Color colour) => DrawText(x, y, text, colour, scale);

        private void DrawText(int x, int y, string text, Color colour, int glyphScale) =>
            canvas.DrawText(x, y, text, colour, glyphScale);

        private void DrawRule(int x, int y, int width) => canvas.FillRect(x, y, width, 1, dim);

        private void DrawProgressBar(int x, int y, int width, float fraction)
        {
            canvas.FillRect(x, y, width, scale, dim);
            canvas.FillRect(x, y, Mathf.RoundToInt(width * Mathf.Clamp01(fraction)), scale, ink);
        }
    }
}
