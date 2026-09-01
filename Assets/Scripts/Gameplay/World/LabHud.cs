using Residue.Data;
using Residue.Gameplay.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private readonly List<VisualElement> inventoryIcons = new();
        private readonly List<Texture2D> inventoryTextures = new();
        private readonly List<string> inventoryTextureKeys = new();
        private VisualElement inspectionOverlay;
        private Label inspectionTitle;
        private Label inspectionBody;
        private Label inspectionHelp;
        private VisualElement briefCard;
        private bool briefOpen;
        private TutorialCard tutorialCard;
        private TutorialCompass tutorialCompass;
        private bool tutorialCardHidden;
        private VisualElement root;

        // The tutorial's in-world pointing. One resolver shared by the arrow in the room and the
        // arrow at the screen edge, so the two cannot end up pointing at different things — and it is
        // built here rather than in either drawer because it holds the standing answer that keeps the
        // marker from hopping between two instruments as the player walks.
        private readonly TutorialTargets tutorialTargets = new();
        private readonly TutorialMarker tutorialMarker = new();

        private void Awake()
        {
            // Whoever this HUD hangs under is whose crosshair it draws. Wiring still wins if the
            // scene set it, but a player prefab has no build step left to do the wiring at M4.
            if (interactor == null) interactor = GetComponentInParent<PlayerInteractor>();
            if (interactionDebug == null) interactionDebug = GetComponentInParent<InteractionDebug>();

            // Read once, here, rather than in Build(). The tree is rebuilt whenever the panel's root
            // element changes identity, and a card that re-opened on every rebuild would keep coming
            // back at a player who had already put it away. GameSettings.Load() runs at
            // BeforeSceneLoad, so the answer is already on disk by now.
            //
            // On a tutorial run it starts closed even for a first-time player, and the flag is left
            // exactly as it was. The two cards are complements, not substitutes — the objective card
            // says what to do next, the brief says why any of it is worth doing — but they share a
            // corner, and opening a first run on both is two walls of text. So the tutorial is what
            // greets you, [Tab] swaps to the standing orders whenever you want the reasoning, and a
            // player who does the tutorial and then starts a real run still gets the card, unread and
            // owed, because doing a tutorial is not the same as having been told why the lab works
            // the way it does. LabRuntime runs first (DefaultExecutionOrder -100), so the tracker
            // already exists by the time this is asked.
            briefOpen = !GameSettings.ShiftBriefSeen && TutorialObjectives.Current == null;
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
            // Under the crosshair and under everything added after it — the toast, the hands line, the
            // inventory, both cards. A transparent full-screen layer that draws one arrow has no
            // business being able to cover a prompt, and TutorialCompass keeps it away from the
            // middle of the screen besides.
            tutorialCompass = new TutorialCompass();
            root.Add(tutorialCompass.Root);

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

            // Greybox affordance. Nothing in an untextured room tells you that pickup is E, and a
            // player who cannot pick a vial up cannot discover anything else in the game. Setting an
            // item down earns its place here for the same reason: without it, the first item you pick
            // up occupies a slot for the rest of the shift and nothing on screen suggests otherwise.
            var controls = new Label(ScreenStrings.HudControls);
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
            BuildShiftBrief();

            // After the brief so it draws over it if both were ever up at once. They never are —
            // UpdateTutorial hides this one whenever the brief is open — but the two occupy the same
            // corner and a stacking order left to chance is a bug waiting for a refactor.
            tutorialCard = new TutorialCard();
            root.Add(tutorialCard.Root);

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

            // Asked once and handed to both cards: they share a corner, so they have to agree about
            // whether something else owns the screen or one of them draws through a terminal.
            bool screenUp = !interactor.enabled
                            || (interactor.Inspection != null && interactor.Inspection.IsOpen)
                            || (interactor.Terminal != null && interactor.Terminal.IsOpen);

            UpdateShiftBrief(screenUp);
            UpdateTutorial(screenUp);

            // Tell the player what the thing in their hands does, since a carried item cannot be
            // looked at and therefore never shows a prompt of its own.
            var carried = interactor.Carried;
            if (carried != null)
            {
                string hint = carried.UseHint;
                handsLabel.text = hint == null
                    ? ScreenStrings.HudHands.Format(("item", carried.DisplayName))
                    : ScreenStrings.HudHandsWithUse.Format(
                        ("item", carried.DisplayName), ("use", hint));
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
            ReleaseInventoryIcons();
            inventorySlots.Clear();
            inventoryIcons.Clear();
            inventoryTextureKeys.Clear();

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
                slot.style.width = 72;
                slot.style.height = 72;
                slot.style.marginLeft = 4;
                slot.style.marginRight = 4;
                slot.style.paddingTop = 7;
                slot.style.paddingLeft = 9;
                slot.style.paddingRight = 9;
                slot.style.overflow = Overflow.Hidden;
                slot.style.backgroundColor = new StyleColor(new Color(0.04f, 0.05f, 0.06f, 0.82f));
                slot.style.borderTopWidth = 1;
                slot.style.borderBottomWidth = 1;
                slot.style.borderLeftWidth = 1;
                slot.style.borderRightWidth = 1;
                Round(slot, 3);

                var number = new Label((i + 1).ToString());
                Style(number, 11, SignalPalette.Dim);
                number.style.position = Position.Absolute;
                number.style.left = 6;
                number.style.top = 4;
                slot.Add(number);

                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.style.position = Position.Absolute;
                icon.style.left = 9;
                icon.style.right = 9;
                icon.style.top = 9;
                icon.style.bottom = 9;
                icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                slot.Add(icon);

                inventoryBar.Add(slot);
                inventorySlots.Add(slot);
                inventoryIcons.Add(icon);
                inventoryTextures.Add(null);
                inventoryTextureKeys.Add(null);
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

                string key = item != null ? $"{item.GetEntityId()}:{item.DisplayName}" : null;
                if (inventoryTextureKeys[i] == key) continue;

                if (inventoryTextures[i] != null) Destroy(inventoryTextures[i]);
                inventoryTextures[i] = item != null ? InventoryIconRenderer.Render(item) : null;
                inventoryTextureKeys[i] = key;
                inventoryIcons[i].style.backgroundImage = inventoryTextures[i] != null
                    ? new StyleBackground(inventoryTextures[i])
                    : new StyleBackground(StyleKeyword.Null);
            }
        }

        private void OnDestroy()
        {
            ReleaseInventoryIcons();

            // The marker owns a GameObject, a Mesh and a Material that Unity will not collect on its
            // own. A HUD torn down with a scene change would otherwise leave an arrow hanging in the
            // room it was pointing at.
            tutorialMarker.Dispose();
        }

        private void ReleaseInventoryIcons()
        {
            for (int i = 0; i < inventoryTextures.Count; i++)
                if (inventoryTextures[i] != null) Destroy(inventoryTextures[i]);
            inventoryTextures.Clear();
        }

        private void BuildInspectionOverlay()
        {
            inspectionOverlay = new VisualElement();
            inspectionOverlay.style.position = Position.Absolute;
            inspectionOverlay.style.left = 0;
            inspectionOverlay.style.right = 0;
            inspectionOverlay.style.top = 0;
            inspectionOverlay.style.bottom = 0;
            // Keep the world quiet without veiling the inspected mesh: book text now lives on the
            // physical page texture and must remain readable through this overlay.
            inspectionOverlay.style.backgroundColor = new StyleColor(new Color(0.015f, 0.018f, 0.02f, 0.18f));
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

            inspectionHelp = new Label();
            Style(inspectionHelp, 12, SignalPalette.Dim);
            inspectionHelp.style.position = Position.Absolute;
            inspectionHelp.style.left = 0;
            inspectionHelp.style.right = 0;
            inspectionHelp.style.bottom = 20;
            inspectionHelp.style.unityTextAlign = TextAnchor.MiddleCenter;
            inspectionOverlay.Add(inspectionHelp);
        }

        private void UpdateInspection()
        {
            var view = interactor.Inspection;
            bool open = view != null && view.IsOpen;
            inspectionOverlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) return;
            inspectionTitle.text = view.Item.DisplayName;
            inspectionBody.text = view.Item.InspectionText ?? string.Empty;
            // Two whole lines rather than a stem with the item's own hint spliced into the middle:
            // the controls either side of the hint are a sentence a translator has to be able to
            // reorder around it.
            inspectionHelp.text = string.IsNullOrEmpty(view.Item.InspectionHelp)
                ? ScreenStrings.HudInspectHelp.Text
                : ScreenStrings.HudInspectHelpWithHint.Format(("hint", view.Item.InspectionHelp));
        }

        /// <summary>
        /// The standing orders card (#47): the one thing in the lab that appears without being asked
        /// for.
        /// <para>
        /// Its words come from <see cref="BookContent.ShiftBrief"/>, so this method knows nothing
        /// about the lab and cannot teach a diagnosis even by accident. It exists at all because the
        /// manuals — which already explain everything this room runs on — are objects you have to
        /// notice, pick up and open, and nothing in an untextured grey room suggests doing that. A
        /// book cannot solve "nobody knows the books are there".
        /// </para>
        /// <para>
        /// Drawn in <see cref="SignalPalette.Accent"/> and <see cref="SignalPalette.Dim"/>, never in
        /// the verdict set (hard rule 4). It is a card in the corner rather than a modal because it
        /// must gate nothing: the player can walk, look, carry and run an instrument with it up, and
        /// in co-op nobody else can tell it is open. See #73 in CLAUDE.md for what a mandatory first
        /// step did to the pacing last time.
        /// </para>
        /// </summary>
        private void BuildShiftBrief()
        {
            briefCard = new VisualElement { pickingMode = PickingMode.Ignore };
            briefCard.style.position = Position.Absolute;
            briefCard.style.left = 16;
            briefCard.style.top = 96;
            briefCard.style.width = 372;
            briefCard.style.paddingTop = 14;
            briefCard.style.paddingBottom = 14;
            briefCard.style.paddingLeft = 16;
            briefCard.style.paddingRight = 16;
            briefCard.style.backgroundColor = new StyleColor(SignalPalette.Panel);
            briefCard.style.borderLeftWidth = 2;
            briefCard.style.borderLeftColor = new StyleColor(SignalPalette.Accent);
            briefCard.style.display = DisplayStyle.None;
            Round(briefCard, 3);
            root.Add(briefCard);

            var heading = new Label(BookContent.ShiftBriefTitle);
            Style(heading, 14, SignalPalette.Ink);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            briefCard.Add(heading);

            foreach (var page in BookContent.ShiftBrief())
            {
                var step = new Label(page.Title);
                Style(step, 12, SignalPalette.Accent);
                step.style.marginTop = 11;
                step.style.whiteSpace = WhiteSpace.Normal;
                briefCard.Add(step);

                var body = new Label(page.Body);
                Style(body, 12, SignalPalette.Dim);
                body.style.marginTop = 2;
                body.style.whiteSpace = WhiteSpace.Normal;
                briefCard.Add(body);
            }

            var closing = new Label(ScreenStrings.HudBriefClosing.Format(
                ("closing", BookContent.ShiftBriefClosing)));
            Style(closing, 11, SignalPalette.Off);
            closing.style.marginTop = 14;
            closing.style.whiteSpace = WhiteSpace.Normal;
            briefCard.Add(closing);
        }

        /// <summary>
        /// Toggles the card and records the first time it is put away.
        /// <para>
        /// No timer anywhere: the pause menu stops <see cref="Time.timeScale"/> in single player, so
        /// anything counting down would freeze behind it and a card that dismissed itself would
        /// either never leave or leave while the player was reading. The player closes it, or it
        /// stays.
        /// </para>
        /// <para>
        /// <paramref name="screenUp"/> comes from <c>interactor.enabled</c> because that is the one
        /// signal every screen in the game already produces — the terminal, item inspection and the
        /// pause menu all disable the interactor while they hold the keyboard. Watching it lets
        /// <c>Residue.Gameplay</c> notice a <c>Residue.Net</c> menu without referencing the assembly
        /// it must not reference.
        /// </para>
        /// </summary>
        private void UpdateShiftBrief(bool screenUp)
        {
            if (briefCard == null) return;

            var keyboard = Keyboard.current;
            if (!screenUp && keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            {
                briefOpen = !briefOpen;

                // Written on dismissal, not on display: a brief nobody acknowledged has not been
                // read, and quitting halfway through it should not cost the player the rest.
                if (!briefOpen) GameSettings.ShiftBriefSeen = true;
            }

            briefCard.style.display = briefOpen && !screenUp ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// The tutorial's objective card, on any run that is one and on no other.
        ///
        /// <para>
        /// <b>[F1] puts it away and brings it back, and that is the whole of "skippable".</b> The
        /// brief owns [Tab] and the two cards share this corner, so a second key is what lets a
        /// player have either, both in turn, or neither. Per-session rather than written to
        /// <c>GameSettings</c> like <c>ShiftBriefSeen</c>: a card the player asked for from the menu a
        /// minute ago is not something to remember having refused.
        /// </para>
        ///
        /// <para>
        /// Hidden while the brief is open because they would otherwise draw on top of each other, and
        /// while any screen has the keyboard, for the reason the brief is. Nothing here can gate
        /// anything — the card is a label, the tracker is an observer, and a player who never presses
        /// [F1] and never reads a word of it plays an ordinary two-day contract.
        /// </para>
        ///
        /// <para>
        /// <b>The arrows go away with it.</b> The card names the next objective and
        /// <see cref="TutorialMarker"/> says where it is; they are one instrument, and dismissing half
        /// of it is not something anybody asked for. So the card, the marker and
        /// <see cref="TutorialCompass"/> all hang off the same <c>show</c> — which includes
        /// <see cref="TutorialObjectives.Current"/> being non-null, and that is what keeps a real run
        /// free of every part of this.
        /// </para>
        /// </summary>
        private void UpdateTutorial(bool screenUp)
        {
            if (tutorialCard == null) return;

            var objectives = TutorialObjectives.Current;

            var keyboard = Keyboard.current;
            if (objectives != null && !screenUp && keyboard != null &&
                keyboard.f1Key.wasPressedThisFrame)
            {
                tutorialCardHidden = !tutorialCardHidden;
            }

            bool show = objectives != null && !screenUp && !briefOpen && !tutorialCardHidden;
            tutorialCard.Refresh(objectives, show);

            // Resolved once and handed to both, so the arrow in the room and the arrow at the edge
            // can never be pointing at two different things. The eye camera rather than Camera.main:
            // with four players in one process there is no such thing as "the" camera, and this HUD
            // belongs to exactly one of them.
            var eye = interactor.Eye;
            var target = show && eye != null
                ? tutorialTargets.ResolveCurrent(eye.transform.position, interactor.Inventory)
                : TutorialTarget.None;

            tutorialMarker.Refresh(target, eye, show);

            // Exact complements: the compass draws precisely when the marker cannot be seen, so a
            // player with a live objective always has one of the two on screen and never both.
            tutorialCompass.Refresh(tutorialMarker.HasTarget && !tutorialMarker.Visible,
                                    tutorialMarker.Point, tutorialMarker.OnScreen, eye);
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
                ? ScreenStrings.HudShiftOver.Text
                : ScreenStrings.HudTimeLeft.Format(
                    ("time", $"{span.Minutes:D2}:{span.Seconds:D2}"));

            // Still chosen from ShiftOver alone, whichever line was resolved (hard rule 4).
            statusLabel.style.color = new StyleColor(lab.ShiftOver ? SignalPalette.Caution : SignalPalette.Dim);

            // "1 sample open" and "4 samples open" are two keys rather than a stem and an "s": a
            // translator handed the letter cannot inflect the noun to agree with the count.
            string openLine = open == 1
                ? ScreenStrings.HudOpenSamplesOne.Text
                : ScreenStrings.HudOpenSamplesMany.Format(("count", open));

            // DRUM, not SOLVENT: since #14 this is the stock at the wash station rather than
            // flushes in hand, and what is in the bottle you are carrying is on the hands line.
            statusLabel.text = ScreenStrings.HudStatus.Format(
                ("day", lab.Day),
                ("clock", clock),
                ("money", lab.Money.ToString("N0")),
                ("reputation", lab.Reputation.ToString("F0")),
                ("solvent", lab.SolventUnits.ToString("F0")),
                ("standards", lab.ReferenceStandards),
                ("open", openLine));
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
