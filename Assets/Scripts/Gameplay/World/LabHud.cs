using Residue.Gameplay.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Crosshair, interaction prompt, hold progress and the day readout.
    /// <para>
    /// The crosshair changes shape on a valid target rather than drawing an outline on the object
    /// (§2.6) — outlines read as a rendering fault on untextured hard-normal geometry.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LabHud : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor interactor;

        private VisualElement crosshair;
        private Label promptLabel;
        private VisualElement holdBar;
        private VisualElement holdFill;
        private Label toastLabel;
        private Label statusLabel;
        private VisualElement root;

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            Build();
        }

        private void Build()
        {
            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1f;

            // --- top-left status ---
            statusLabel = new Label("—");
            Style(statusLabel, 13, SignalPalette.Dim);
            statusLabel.style.position = Position.Absolute;
            statusLabel.style.left = 16;
            statusLabel.style.top = 12;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(statusLabel);

            // --- centre crosshair ---
            var centre = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    left = 0, right = 0, top = 0, bottom = 0,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center
                }
            };
            root.Add(centre);

            crosshair = new VisualElement();
            crosshair.style.width = 6;
            crosshair.style.height = 6;
            crosshair.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.75f));
            Round(crosshair, 3);
            centre.Add(crosshair);

            promptLabel = new Label();
            Style(promptLabel, 15, SignalPalette.Ink);
            promptLabel.style.marginTop = 22;
            promptLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            centre.Add(promptLabel);

            holdBar = new VisualElement();
            holdBar.style.width = 140;
            holdBar.style.height = 3;
            holdBar.style.marginTop = 8;
            holdBar.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.18f));
            holdBar.style.display = DisplayStyle.None;
            centre.Add(holdBar);

            holdFill = new VisualElement();
            holdFill.style.height = 3;
            holdFill.style.width = Length.Percent(0);
            holdFill.style.backgroundColor = new StyleColor(SignalPalette.Accent);
            holdBar.Add(holdFill);

            // --- bottom toast ---
            toastLabel = new Label();
            Style(toastLabel, 14, SignalPalette.Ink);
            toastLabel.style.position = Position.Absolute;
            toastLabel.style.bottom = 48;
            toastLabel.style.left = 0;
            toastLabel.style.right = 0;
            toastLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            toastLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(toastLabel);
        }

        private void Update()
        {
            if (interactor == null || crosshair == null) return;

            bool hasTarget = interactor.Target != null && !string.IsNullOrEmpty(interactor.Prompt);
            bool blocked = interactor.PromptBlocked;

            // Shape change, not colour change: the crosshair opens into a ring on a valid target.
            float size = hasTarget ? 14f : 6f;
            crosshair.style.width = size;
            crosshair.style.height = size;
            Round(crosshair, size * 0.5f);
            crosshair.style.backgroundColor = new StyleColor(
                hasTarget ? new Color(1f, 1f, 1f, 0f) : new Color(1f, 1f, 1f, 0.75f));
            crosshair.style.borderTopWidth = hasTarget ? 2 : 0;
            crosshair.style.borderBottomWidth = hasTarget ? 2 : 0;
            crosshair.style.borderLeftWidth = hasTarget ? 2 : 0;
            crosshair.style.borderRightWidth = hasTarget ? 2 : 0;
            var edge = new StyleColor(blocked ? SignalPalette.Dim : new Color(1f, 1f, 1f, 0.9f));
            crosshair.style.borderTopColor = edge;
            crosshair.style.borderBottomColor = edge;
            crosshair.style.borderLeftColor = edge;
            crosshair.style.borderRightColor = edge;

            promptLabel.text = interactor.Prompt ?? string.Empty;
            promptLabel.style.color = new StyleColor(blocked ? SignalPalette.Dim : SignalPalette.Ink);

            bool holding = interactor.HoldProgress > 0.001f;
            holdBar.style.display = holding ? DisplayStyle.Flex : DisplayStyle.None;
            holdFill.style.width = Length.Percent(interactor.HoldProgress * 100f);

            toastLabel.text = interactor.Toast ?? string.Empty;

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            var lab = LabRuntime.Instance?.Lab;
            if (lab == null) { statusLabel.text = string.Empty; return; }

            int open = 0;
            foreach (var s in lab.Samples.All)
            {
                if (!s.FiledVerdict.HasValue) open++;
            }

            var span = System.TimeSpan.FromSeconds(Mathf.Max(0f, lab.DaySecondsRemaining));

            statusLabel.text =
                $"DAY {lab.Day}   {span.Minutes:D2}:{span.Seconds:D2} left\n" +
                $"£{lab.Economy.Money:N0}   REP {lab.Economy.Reputation:F0}   " +
                $"SOLVENT {lab.Economy.SolventUnits:F0}\n" +
                $"{open} sample{(open == 1 ? "" : "s")} open";
        }

        internal static void Style(Label label, int size, Color colour)
        {
            label.style.fontSize = size;
            label.style.color = new StyleColor(colour);
            label.style.unityFontStyleAndWeight = FontStyle.Normal;
        }

        internal static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
