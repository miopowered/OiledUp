using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The terminal: sample list, results, verdict filing, end-of-day report.
    /// <para>
    /// This screen is where the game actually happens. Everything it shows comes from
    /// <see cref="SampleState"/> — measured values, thresholds, the player's own notes — and
    /// nothing from ground truth. The fault name appears exactly once: in the end-of-day report,
    /// after the consequence has already landed (§4.3).
    /// </para>
    /// </summary>
    public sealed class TerminalScreen : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerInteractor interactor;

        private VisualElement root;
        private SampleId selected = SampleId.None;
        private RootCauseDef pendingCause;
        private IReadOnlyList<ConsequenceReport> reportOverlay;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            root.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        }

        public void Open()
        {
            IsOpen = true;
            root.style.display = DisplayStyle.Flex;
            PlayerController.SetCursorLocked(false);
            if (player != null) player.enabled = false;
            if (interactor != null) interactor.enabled = false;
            Rebuild();
        }

        public void Close()
        {
            IsOpen = false;
            reportOverlay = null;
            root.style.display = DisplayStyle.None;
            PlayerController.SetCursorLocked(true);
            if (player != null) player.enabled = true;
            if (interactor != null) interactor.enabled = true;
        }

        // -- Build ------------------------------------------------------------------------------------

        private void Rebuild()
        {
            var lab = LabRuntime.Instance?.Lab;
            root.Clear();
            if (lab == null) return;

            root.style.flexGrow = 1f;
            root.style.backgroundColor = new StyleColor(new Color(0.05f, 0.06f, 0.07f, 0.97f));
            root.style.paddingLeft = 24;
            root.style.paddingRight = 24;
            root.style.paddingTop = 18;
            root.style.paddingBottom = 18;

            root.Add(Header(lab));

            if (reportOverlay != null) { root.Add(ReportPanel(lab)); return; }

            var body = Row();
            body.style.flexGrow = 1f;
            body.style.marginTop = 12;
            body.Add(SampleList(lab));
            body.Add(Detail(lab));
            root.Add(body);
        }

        private VisualElement Header(LabState lab)
        {
            var bar = Row();
            bar.style.justifyContent = Justify.SpaceBetween;
            bar.style.alignItems = Align.Center;

            var left = new Label($"SAMPLE TERMINAL — DAY {lab.Day}");
            left.style.fontSize = 18;
            left.style.color = new StyleColor(SignalPalette.Ink);
            left.style.unityFontStyleAndWeight = FontStyle.Bold;
            bar.Add(left);

            var right = Row();
            right.style.alignItems = Align.Center;

            var money = new Label($"£{lab.Economy.Money:N0}    REP {lab.Economy.Reputation:F0}");
            money.style.fontSize = 15;
            money.style.color = new StyleColor(SignalPalette.Dim);
            money.style.marginRight = 16;
            right.Add(money);

            var endDay = new Button(() =>
            {
                reportOverlay = lab.EndDay();
                Rebuild();
            })
            { text = "END DAY" };
            StyleButton(endDay, SignalPalette.Accent);
            right.Add(endDay);

            var close = new Button(Close) { text = "CLOSE  (Esc)" };
            StyleButton(close, SignalPalette.PanelSoft);
            right.Add(close);

            bar.Add(right);
            return bar;
        }

        private VisualElement SampleList(LabState lab)
        {
            var panel = Panel();
            panel.style.width = 320;
            panel.style.marginRight = 12;

            panel.Add(SectionTitle("OPEN SAMPLES"));

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;

            var open = lab.OpenSamples();
            if (open.Count == 0) scroll.Add(Dim("Nothing open. End the day."));

            foreach (var sample in open)
            {
                var row = new Button(() => { selected = sample.Id; pendingCause = null; Rebuild(); });
                row.style.flexDirection = FlexDirection.Column;
                row.style.alignItems = Align.FlexStart;
                row.style.marginBottom = 4;
                row.style.paddingTop = 7;
                row.style.paddingBottom = 7;
                row.style.paddingLeft = 9;
                row.style.paddingRight = 9;
                row.style.backgroundColor = new StyleColor(
                    sample.Id == selected ? SignalPalette.PanelSoft : new Color(0.09f, 0.10f, 0.11f));
                row.style.borderLeftWidth = 3;
                row.style.borderLeftColor = new StyleColor(
                    sample.Results.Count == 0 ? SignalPalette.Off : SignalPalette.For(sample.WorstReading()));
                LabHud.Round(row, 3);

                var tag = new Label(sample.EquipmentTag);
                tag.style.fontSize = 14;
                tag.style.color = new StyleColor(SignalPalette.Ink);
                row.Add(tag);

                var meta = new Label(
                    $"{sample.Profile.DisplayName} · {sample.VolumeMl:F0} ml · " +
                    $"{sample.Results.Count} run{(sample.Results.Count == 1 ? "" : "s")}");
                meta.style.fontSize = 11;
                meta.style.color = new StyleColor(SignalPalette.Dim);
                row.Add(meta);

                scroll.Add(row);
            }

            panel.Add(scroll);
            return panel;
        }

        private VisualElement Detail(LabState lab)
        {
            var panel = Panel();
            panel.style.flexGrow = 1f;

            if (!selected.IsValid || !lab.Samples.TryGet(selected, out var sample))
            {
                panel.Add(Dim("Select a sample."));
                return panel;
            }

            panel.Add(SectionTitle(sample.EquipmentTag));

            var sub = new Label(
                $"{sample.Profile.DisplayName} · {sample.Profile.BaseOilGrade} · " +
                $"{sample.HoursSinceOilChange:F0} h on the oil · {sample.VolumeMl:F1} ml remaining");
            sub.style.fontSize = 12;
            sub.style.color = new StyleColor(SignalPalette.Dim);
            sub.style.marginBottom = 6;
            panel.Add(sub);

            if (!string.IsNullOrEmpty(sample.FieldTechNote))
            {
                var note = new Label($"Field note: \"{sample.FieldTechNote}\"");
                note.style.fontSize = 12;
                note.style.color = new StyleColor(SignalPalette.Dim);
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.marginBottom = 8;
                panel.Add(note);
            }

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;
            scroll.Add(ResultsTable(sample));
            scroll.Add(RunLog(sample));
            panel.Add(scroll);

            panel.Add(VerdictBar(lab, sample));
            return panel;
        }

        private VisualElement ResultsTable(SampleState sample)
        {
            var box = new VisualElement();
            box.style.marginBottom = 10;

            if (sample.Results.Count == 0)
            {
                box.Add(Dim("No results yet. Run this sample on an instrument."));
                return box;
            }

            box.Add(ColumnHeader());

            foreach (var threshold in sample.Profile.Thresholds)
            {
                if (threshold?.Element == null) continue;
                if (!sample.TryGetLatest(threshold.Element.Id, out float value, out var source)) continue;

                var severity = threshold.Evaluate(value);

                var row = Row();
                row.style.paddingTop = 3;
                row.style.paddingBottom = 3;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new StyleColor(new Color(1f, 1f, 1f, 0.05f));

                row.Add(Cell(threshold.Element.DisplayName, 190, SignalPalette.Ink));
                row.Add(Cell($"{value:0.###} {threshold.Element.Unit}", 130, SignalPalette.Ink));
                row.Add(Cell(LimitText(threshold), 150, SignalPalette.Dim));
                row.Add(Cell(SignalPalette.Label(severity), 90, SignalPalette.For(severity)));
                row.Add(Cell(source != null && source.Suspect ? "SUSPECT" : "", 80, SignalPalette.Caution));

                box.Add(row);
            }

            return box;
        }

        private static string LimitText(Threshold t) => t.Mode switch
        {
            ThresholdMode.UpperLimit => $"normal ≤ {t.NormalLimit:0.###}",
            ThresholdMode.LowerLimit => $"normal ≥ {t.NormalLimit:0.###}",
            ThresholdMode.DeviationBand => $"{t.Baseline:0.#} ±{t.NormalLimit * 100f:0}%",
            _ => ""
        };

        private VisualElement RunLog(SampleState sample)
        {
            var box = new VisualElement();
            if (sample.Results.Count == 0) return box;

            box.Add(SectionTitle("RUNS"));
            foreach (var r in sample.Results)
            {
                var line = new Label(
                    $"day {r.DayRun} · {r.MachineId}{(r.IsBlank ? " · BLANK" : "")} · " +
                    $"{r.VolumeConsumedMl:F0} ml · £{r.Cost:F0}{(r.Suspect ? " · SUSPECT" : "")}");
                line.style.fontSize = 11;
                line.style.color = new StyleColor(r.Suspect ? SignalPalette.Caution : SignalPalette.Dim);
                box.Add(line);
            }
            return box;
        }

        private VisualElement VerdictBar(LabState lab, SampleState sample)
        {
            var box = new VisualElement();
            box.style.marginTop = 10;
            box.style.paddingTop = 10;
            box.style.borderTopWidth = 1;
            box.style.borderTopColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));

            var causes = new List<string> { "(no root cause)" };
            foreach (var c in lab.Content.Causes)
            {
                if (c != null) causes.Add(c.DisplayName);
            }

            var dropdown = new DropdownField("Root cause", causes, 0);
            dropdown.style.marginBottom = 8;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                pendingCause = null;
                foreach (var c in lab.Content.Causes)
                {
                    if (c != null && c.DisplayName == evt.newValue) pendingCause = c;
                }
            });
            box.Add(dropdown);

            var buttons = Row();
            buttons.Add(VerdictButton(lab, sample, Verdict.Normal, "FILE NORMAL"));
            buttons.Add(VerdictButton(lab, sample, Verdict.Monitor, "FILE MONITOR"));
            buttons.Add(VerdictButton(lab, sample, Verdict.Critical, "FILE CRITICAL — PULL"));
            box.Add(buttons);

            return box;
        }

        private Button VerdictButton(LabState lab, SampleState sample, Verdict verdict, string text)
        {
            var button = new Button(() =>
            {
                lab.Samples.FileVerdict(sample.Id, verdict, pendingCause, lab.Day);
                selected = SampleId.None;
                pendingCause = null;
                Rebuild();
            })
            { text = text };

            StyleButton(button, SignalPalette.PanelSoft);
            button.style.color = new StyleColor(SignalPalette.For(verdict));
            button.style.flexGrow = 1f;
            return button;
        }

        private VisualElement ReportPanel(LabState lab)
        {
            var panel = Panel();
            panel.style.flexGrow = 1f;
            panel.style.marginTop = 12;
            panel.Add(SectionTitle($"END OF DAY {lab.Day}"));

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;

            if (reportOverlay.Count == 0)
                scroll.Add(Dim("Nothing came due today."));

            float net = 0f;
            foreach (var report in reportOverlay)
            {
                net += report.MoneyDelta;

                var card = new VisualElement();
                card.style.marginBottom = 6;
                card.style.paddingTop = 8;
                card.style.paddingBottom = 8;
                card.style.paddingLeft = 10;
                card.style.paddingRight = 10;
                card.style.backgroundColor = new StyleColor(new Color(0.09f, 0.10f, 0.11f));
                card.style.borderLeftWidth = 3;
                card.style.borderLeftColor = new StyleColor(
                    report.IsGood ? SignalPalette.Normal : SignalPalette.Critical);
                LabHud.Round(card, 3);

                var headline = new Label(report.Headline);
                headline.style.fontSize = 13;
                headline.style.color = new StyleColor(SignalPalette.Ink);
                headline.style.whiteSpace = WhiteSpace.Normal;
                card.Add(headline);

                var money = new Label(
                    $"{(report.MoneyDelta >= 0 ? "+" : "−")}£{Mathf.Abs(report.MoneyDelta):N0}" +
                    (report.RootCauseCorrect ? "   root cause bonus" : ""));
                money.style.fontSize = 12;
                money.style.color = new StyleColor(
                    report.MoneyDelta >= 0 ? SignalPalette.Normal : SignalPalette.Critical);
                card.Add(money);

                scroll.Add(card);
            }

            panel.Add(scroll);

            var total = new Label($"NET  {(net >= 0 ? "+" : "−")}£{Mathf.Abs(net):N0}    " +
                                  $"BALANCE £{lab.Economy.Money:N0}");
            total.style.fontSize = 15;
            total.style.marginTop = 8;
            total.style.color = new StyleColor(net >= 0 ? SignalPalette.Normal : SignalPalette.Critical);
            panel.Add(total);

            var next = new Button(() =>
            {
                reportOverlay = null;
                lab.BeginDay();
                Rebuild();
            })
            { text = "START NEXT DAY" };
            StyleButton(next, SignalPalette.Accent);
            next.style.marginTop = 8;
            panel.Add(next);

            return panel;
        }

        // -- Small builders ---------------------------------------------------------------------------

        private static VisualElement Row()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            return e;
        }

        private static VisualElement Panel()
        {
            var e = new VisualElement();
            e.style.backgroundColor = new StyleColor(SignalPalette.Panel);
            e.style.paddingTop = 12;
            e.style.paddingBottom = 12;
            e.style.paddingLeft = 12;
            e.style.paddingRight = 12;
            LabHud.Round(e, 4);
            return e;
        }

        private static Label SectionTitle(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 13;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(SignalPalette.Accent);
            label.style.marginBottom = 6;
            return label;
        }

        private static Label Dim(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 12;
            label.style.color = new StyleColor(SignalPalette.Dim);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static VisualElement ColumnHeader()
        {
            var row = Row();
            row.style.marginBottom = 4;
            row.Add(Cell("ELEMENT", 190, SignalPalette.Dim));
            row.Add(Cell("MEASURED", 130, SignalPalette.Dim));
            row.Add(Cell("LIMIT", 150, SignalPalette.Dim));
            row.Add(Cell("STATE", 90, SignalPalette.Dim));
            row.Add(Cell("", 80, SignalPalette.Dim));
            return row;
        }

        private static Label Cell(string text, float width, Color colour)
        {
            var label = new Label(text);
            label.style.width = width;
            label.style.fontSize = 12;
            label.style.color = new StyleColor(colour);
            return label;
        }

        private static void StyleButton(Button button, Color background)
        {
            button.style.backgroundColor = new StyleColor(background);
            button.style.color = new StyleColor(SignalPalette.Ink);
            button.style.fontSize = 12;
            button.style.paddingTop = 7;
            button.style.paddingBottom = 7;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.marginLeft = 4;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            LabHud.Round(button, 3);
        }
    }
}
