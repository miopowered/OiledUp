using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Crosshair, interaction prompt, hold progress and the day readout.
    /// <para>
    /// The crosshair changes shape on a valid target rather than drawing an outline on the object
    /// (§2.6) — outlines read as a rendering fault on untextured hard-normal geometry.
    /// </para>
    /// <para>
    /// One of these belongs to each player rather than to the scene. It reads only from the
    /// <see cref="PlayerInteractor"/> it was given, so four of them in one process show four
    /// different crosshairs over the same lab — and a replica's copy, switched off with the rest of
    /// that avatar, simply never builds.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LabHud : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private InteractionDebug interactionDebug;

        private UIDocument document;

        private VisualElement crosshair;
        private Label promptLabel;
        private VisualElement holdBar;
        private VisualElement holdFill;
        private Label toastLabel;
        private Label statusLabel;
        private Label handsLabel;
        private Label debugLabel;
        private VisualElement inventoryBar;
        private readonly List<VisualElement> inventorySlots = new();
        private readonly List<Label> inventoryLabels = new();
        private VisualElement inspectionOverlay;
        private Label inspectionTitle;
        private Label inspectionBody;
        private VisualElement root;

        private void Awake()
        {
            // Whoever this HUD hangs under is whose crosshair it draws. Wiring still wins if the
            // scene set it, but a player prefab has no build step left to do the wiring at M4.
            if (interactor == null) interactor = GetComponentInParent<PlayerInteractor>();
            if (interactionDebug == null) interactionDebug = GetComponentInParent<InteractionDebug>();
        }

        private void OnEnable() => EnsureUi();

        /// <summary>
        /// Attach to the panel, building the tree the first time it appears.
        /// <para>
        /// A <see cref="UIDocument"/> only owns a <c>rootVisualElement</c> while it is enabled, and a
        /// remote player's HUD is switched off with the rest of that avatar — so the panel may be
        /// absent at <c>OnEnable</c> and may be a <i>different</i> element by the time it comes back.
        /// Asking each frame rather than caching once is what makes both cases quiet instead of a
        /// null reference, and is why every widget below is rebuilt against whatever root is current.
        /// </para>
        /// </summary>
        private bool EnsureUi()
        {
            if (document == null) document = GetComponent<UIDocument>();

            var current = document != null ? document.rootVisualElement : null;
            if (current == null)
            {
                root = null;
                crosshair = null;
                return false;
            }

            if (!ReferenceEquals(current, root))
            {
                root = current;
                Build();
            }
            return true;
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
            toastLabel.style.bottom = 126;
            toastLabel.style.left = 0;
            toastLabel.style.right = 0;
            toastLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            toastLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(toastLabel);

            // Greybox affordance. Nothing in an untextured room tells you that pickup is E and
            // agitation is the mouse, and a player who cannot pick a vial up cannot discover
            // anything else in the game.
            var controls = new Label(
                "[WASD] move    [E] interact    [1–3] select    [Space] inspect    [LMB drag] rotate");
            Style(controls, 12, SignalPalette.Dim);
            controls.style.position = Position.Absolute;
            controls.style.left = 16;
            controls.style.bottom = 12;
            root.Add(controls);

            handsLabel = new Label();
            Style(handsLabel, 13, SignalPalette.Ink);
            handsLabel.style.position = Position.Absolute;
            handsLabel.style.left = 0;
            handsLabel.style.right = 0;
            handsLabel.style.bottom = 94;
            handsLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.Add(handsLabel);

            BuildInventory();
            BuildInspectionOverlay();

            // Interaction diagnostics. Deliberately monospaced-ish and magenta-tinted so it can
            // never be mistaken for game UI.
            debugLabel = new Label();
            Style(debugLabel, 12, new Color(1f, 0.6f, 0.95f));
            debugLabel.style.position = Position.Absolute;
            debugLabel.style.right = 16;
            debugLabel.style.top = 12;
            debugLabel.style.whiteSpace = WhiteSpace.Normal;
            debugLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            debugLabel.style.maxWidth = 620;
            root.Add(debugLabel);
        }

        private void Update()
        {
            if (!EnsureUi()) return;
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

            UpdateInventory();
            UpdateInspection();

            // Tell the player what the thing in their hands does, since a carried item cannot be
            // looked at and therefore never shows a prompt of its own.
            var carried = interactor.Carried;
            if (carried != null)
            {
                string hint = carried.UseHint;
                handsLabel.text = hint == null
                    ? $"holding: {carried.DisplayName}"
                    : $"in hands: {carried.DisplayName}    [LMB] {hint}    [Space] inspect";
            }
            else
            {
                handsLabel.text = string.Empty;
            }

            debugLabel.text = InteractionDebug.Enabled && interactionDebug != null
                ? interactionDebug.BuildReadout()
                : string.Empty;

            UpdateStatus();
        }

        private void BuildInventory()
        {
            inventorySlots.Clear();
            inventoryLabels.Clear();

            inventoryBar = new VisualElement();
            inventoryBar.style.position = Position.Absolute;
            inventoryBar.style.left = Length.Percent(50);
            inventoryBar.style.bottom = 22;
            inventoryBar.style.translate = new Translate(Length.Percent(-50), 0);
            inventoryBar.style.flexDirection = FlexDirection.Row;
            root.Add(inventoryBar);

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                var slot = new VisualElement();
                slot.style.width = 150;
                slot.style.height = 62;
                slot.style.marginLeft = 4;
                slot.style.marginRight = 4;
                slot.style.paddingTop = 7;
                slot.style.paddingLeft = 9;
                slot.style.paddingRight = 9;
                slot.style.backgroundColor = new StyleColor(new Color(0.04f, 0.05f, 0.06f, 0.82f));
                slot.style.borderTopWidth = 1;
                slot.style.borderBottomWidth = 1;
                slot.style.borderLeftWidth = 1;
                slot.style.borderRightWidth = 1;
                Round(slot, 3);

                var number = new Label((i + 1).ToString());
                Style(number, 11, SignalPalette.Dim);
                slot.Add(number);

                var label = new Label("EMPTY");
                Style(label, 12, SignalPalette.Dim);
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.flexGrow = 1;
                slot.Add(label);

                inventoryBar.Add(slot);
                inventorySlots.Add(slot);
                inventoryLabels.Add(label);
            }
        }

        private void UpdateInventory()
        {
            var inventory = interactor.Inventory;
            if (inventory == null) return;

            for (int i = 0; i < inventorySlots.Count; i++)
            {
                bool selected = i == inventory.SelectedIndex;
                var item = inventory.ItemAt(i);
                var edge = new StyleColor(selected ? SignalPalette.Accent : new Color(1f, 1f, 1f, 0.18f));
                inventorySlots[i].style.borderTopColor = edge;
                inventorySlots[i].style.borderBottomColor = edge;
                inventorySlots[i].style.borderLeftColor = edge;
                inventorySlots[i].style.borderRightColor = edge;
                inventorySlots[i].style.borderTopWidth = selected ? 2 : 1;
                inventorySlots[i].style.borderBottomWidth = selected ? 2 : 1;
                inventorySlots[i].style.borderLeftWidth = selected ? 2 : 1;
                inventorySlots[i].style.borderRightWidth = selected ? 2 : 1;
                inventoryLabels[i].text = item != null ? item.DisplayName : "EMPTY";
                inventoryLabels[i].style.color = new StyleColor(item != null ? SignalPalette.Ink : SignalPalette.Dim);
            }
        }

        private void BuildInspectionOverlay()
        {
            inspectionOverlay = new VisualElement();
            inspectionOverlay.style.position = Position.Absolute;
            inspectionOverlay.style.left = 0;
            inspectionOverlay.style.right = 0;
            inspectionOverlay.style.top = 0;
            inspectionOverlay.style.bottom = 0;
            inspectionOverlay.style.backgroundColor = new StyleColor(new Color(0.015f, 0.018f, 0.02f, 0.78f));
            inspectionOverlay.style.display = DisplayStyle.None;
            inspectionOverlay.pickingMode = PickingMode.Ignore;
            root.Add(inspectionOverlay);

            inspectionTitle = new Label();
            Style(inspectionTitle, 20, SignalPalette.Ink);
            inspectionTitle.style.position = Position.Absolute;
            inspectionTitle.style.left = 28;
            inspectionTitle.style.top = 24;
            inspectionOverlay.Add(inspectionTitle);

            inspectionBody = new Label();
            Style(inspectionBody, 14, SignalPalette.Ink);
            inspectionBody.style.position = Position.Absolute;
            inspectionBody.style.left = 28;
            inspectionBody.style.bottom = 54;
            inspectionBody.style.width = Length.Percent(34);
            inspectionBody.style.whiteSpace = WhiteSpace.Normal;
            inspectionOverlay.Add(inspectionBody);

            var help = new Label("Hold LMB + move mouse to rotate    Space / Esc to close");
            Style(help, 12, SignalPalette.Dim);
            help.style.position = Position.Absolute;
            help.style.left = 0;
            help.style.right = 0;
            help.style.bottom = 20;
            help.style.unityTextAlign = TextAnchor.MiddleCenter;
            inspectionOverlay.Add(help);
        }

        private void UpdateInspection()
        {
            var view = interactor.Inspection;
            bool open = view != null && view.IsOpen;
            inspectionOverlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) return;
            inspectionTitle.text = view.Item.DisplayName;
            inspectionBody.text = view.Item.InspectionText ?? string.Empty;
        }

        /// <summary>
        /// The corner readout: the clock, the books, and how much is still open.
        /// <para>
        /// Read through <see cref="LabView.Current"/> rather than off <c>LabRuntime.Lab</c>, so it is
        /// drawn on a joined client too. The shift clock is the pressure the whole game runs on (§6.1)
        /// and a player who could not see it was playing a different game from the person hosting.
        /// </para>
        /// </summary>
        private void UpdateStatus()
        {
            var lab = LabView.Current;
            if (lab == null) { statusLabel.text = string.Empty; return; }

            int open = lab.OpenSampleCount;
            var span = System.TimeSpan.FromSeconds(Mathf.Max(0f, lab.DaySecondsRemaining));

            string clock = lab.ShiftOver
                ? "SHIFT OVER — file your verdicts"
                : $"{span.Minutes:D2}:{span.Seconds:D2} left";
            statusLabel.style.color = new StyleColor(lab.ShiftOver ? SignalPalette.Caution : SignalPalette.Dim);

            statusLabel.text =
                $"DAY {lab.Day}   {clock}\n" +
                $"£{lab.Money:N0}   REP {lab.Reputation:F0}   " +
                // DRUM, not SOLVENT: since #14 this is the stock at the wash station rather than
                // flushes in hand, and what is in the bottle you are carrying is on the hands line.
                $"DRUM {lab.SolventUnits:F0}   STD {lab.ReferenceStandards}\n" +
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
