using Residue.Data;
using Residue.Gameplay.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Crosshair, interaction prompt, hold progress, the shift header and everything else the player
    /// wears rather than walks up to.
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
    ///
    /// <para>
    /// <b>The overlay is one designed system, and <see cref="HudStyle"/> is the vocabulary.</b> Every
    /// size here is a step on its type scale, every offset a step on its spacing scale, and every
    /// string sits on a <see cref="HudStyle.Plate"/> — because the lab is flat-shaded and the same line
    /// of text is read against a pale wall and a near-black floor within one mouse movement. Text with
    /// no backing has no contrast guarantee in this room at all, which is what made the old corner
    /// readout effectively invisible.
    /// </para>
    ///
    /// <para>
    /// <b>Three anchors, and nothing anywhere else.</b> The header owns the top edge; the crosshair,
    /// prompt and hold bar own the centre; the hands, inventory and controls own the bottom. Both
    /// corner cards hang off <see cref="HudStyle.ContentTop"/> under the header, and every edge-anchored
    /// element sits on <see cref="HudStyle.Inset"/>. A widget that picks its own corner is what the
    /// previous layout was.
    /// </para>
    ///
    /// <para>
    /// <b>No signal colour is drawn anywhere on this overlay (hard rule 4).</b> Not on the balance, not
    /// on the reputation, not on a tick, not on the end of the shift. <c>HudTests</c> asserts it by
    /// walking the built tree, because this is exactly the surface where red-on-negative gets invented
    /// during a hurried change.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LabHud : MonoBehaviour
    {
        /// <summary>Side of an inventory slot's icon well.</summary>
        private const float SlotSize = 88f;

        /// <summary>Height of the name strip along the bottom of a slot.</summary>
        private const float SlotNameHeight = 22f;

        /// <summary>
        /// Border width of an inventory slot, at rest and selected.
        /// <para>
        /// One width, recoloured — never a border that grows on selection. <c>UiKit.Focusable</c>
        /// learned this the hard way: a border that appears reflows everything inside it, so the icon
        /// and the name under it twitch by a pixel every time the player presses [2].
        /// </para>
        /// </summary>
        private const float SlotBorder = 2f;

        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private InteractionDebug interactionDebug;

        private UIDocument document;

        private HudHeader header;
        private VisualElement centre;
        private VisualElement crosshair;
        private VisualElement promptPill;
        private Label promptLabel;
        private VisualElement holdBar;
        private VisualElement holdFill;
        private VisualElement toastPill;
        private Label toastLabel;
        private VisualElement handsPill;
        private Label handsLabel;
        private VisualElement controlsPill;
        private Label debugLabel;
        private VisualElement inventoryBar;
        private readonly List<VisualElement> inventorySlots = new();
        private readonly List<VisualElement> inventoryIcons = new();
        private readonly List<Label> inventoryNames = new();
        private readonly List<Texture2D> inventoryTextures = new();
        private readonly List<Carryable> inventoryItems = new();
        private int paintedSelection = -1;
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

        /// <summary>
        /// The whole tree, in draw order. Read the ordering notes before inserting anything: what is
        /// added last draws on top, and two things on this overlay must never be covered.
        /// </summary>
        private void Build()
        {
            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1f;

            // --- top header ---
            //
            // First, so it sits under everything. It occupies the top edge only and never reaches the
            // middle of the screen, and being underneath is what lets the tutorial compass — whose
            // ring is inset 64 px from the panel edge — still draw its arrow at the top of the screen
            // instead of disappearing behind a bar.
            header = new HudHeader();
            root.Add(header.Root);

            // Under the crosshair and under everything added after it — the toast, the hands line, the
            // inventory, both cards. A transparent full-screen layer that draws one arrow has no
            // business being able to cover a prompt, and TutorialCompass keeps it away from the
            // middle of the screen besides.
            tutorialCompass = new TutorialCompass();
            root.Add(tutorialCompass.Root);

            // --- centre: crosshair, prompt, hold ---
            centre = new VisualElement
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

            // The prompt gets its own backing, like every other string on this overlay. A plate behind
            // it cannot cover it — it is its parent — and it is what makes "Take vial — TANK-4"
            // readable against the pale wall behind an instrument as well as against the floor.
            promptLabel = HudStyle.Text(string.Empty, HudStyle.BodySize, HudStyle.Ink);
            promptLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            promptLabel.style.whiteSpace = WhiteSpace.Normal;
            promptPill = HudStyle.Pill(promptLabel, HudStyle.S3, HudStyle.S1);
            promptPill.style.marginTop = HudStyle.S4;
            promptPill.style.display = DisplayStyle.None;
            centre.Add(promptPill);

            holdBar = new VisualElement { pickingMode = PickingMode.Ignore };
            holdBar.style.width = 160;
            holdBar.style.height = 4;
            holdBar.style.marginTop = HudStyle.S2;
            holdBar.style.backgroundColor = new StyleColor(HudStyle.Plate);
            holdBar.style.display = DisplayStyle.None;
            Round(holdBar, 2);
            centre.Add(holdBar);

            holdFill = new VisualElement { pickingMode = PickingMode.Ignore };
            holdFill.style.height = 4;
            holdFill.style.width = Length.Percent(0);
            holdFill.style.backgroundColor = new StyleColor(HudStyle.Accent);
            Round(holdFill, 2);
            holdBar.Add(holdFill);

            BuildBottom();
            BuildInventory();
            BuildInspectionOverlay();
            BuildShiftBrief();

            // After the brief so it draws over it if both were ever up at once. They never are —
            // UpdateTutorial hides this one whenever the brief is open — but the two occupy the same
            // corner and a stacking order left to chance is a bug waiting for a refactor.
            tutorialCard = new TutorialCard();
            root.Add(tutorialCard.Root);

            // Interaction diagnostics. Deliberately magenta-tinted so it can never be mistaken for
            // game UI, and parked under the header rather than in the corner the header now owns.
            debugLabel = HudStyle.Text(string.Empty, HudStyle.CaptionSize,
                                       new Color(1f, 0.6f, 0.95f));
            debugLabel.style.position = Position.Absolute;
            debugLabel.style.right = HudStyle.Inset;
            debugLabel.style.top = HudStyle.ContentTop;
            debugLabel.style.whiteSpace = WhiteSpace.Normal;
            debugLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            debugLabel.style.maxWidth = 620;
            root.Add(debugLabel);
        }

        /// <summary>
        /// The bottom band: what is being said, what is in your hands, and the four bindings that stay
        /// on screen.
        /// <para>
        /// Stacked upwards from the inventory bar on the spacing scale, so the three lines have a
        /// rhythm rather than three hand-picked offsets. Each is its own pill and each is hidden when
        /// it has nothing to say, so an empty line never leaves an empty box on screen.
        /// </para>
        /// </summary>
        private void BuildBottom()
        {
            toastLabel = HudStyle.Text(string.Empty, HudStyle.BodySize, HudStyle.Ink);
            toastLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            toastLabel.style.whiteSpace = WhiteSpace.Normal;
            toastPill = HudStyle.Pill(toastLabel);
            toastPill.style.display = DisplayStyle.None;
            root.Add(CentredBand(toastPill, ToastBottom));

            handsLabel = HudStyle.Text(string.Empty, HudStyle.BodySize, HudStyle.Ink);
            handsLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            handsLabel.style.whiteSpace = WhiteSpace.Normal;
            handsPill = HudStyle.Pill(handsLabel);
            handsPill.style.display = DisplayStyle.None;
            root.Add(CentredBand(handsPill, HandsBottom));

            // Greybox affordance, cut to the bindings that are not shown anywhere else. Nothing in an
            // untextured room tells you that pickup is [E], and a player who cannot pick a vial up
            // cannot discover anything else in the game — but [G], [Space] and the rotate/zoom pair
            // are already spelled out on the hands line and in the inspection overlay respectively,
            // which is where they apply. The full list moved onto the standing-orders card, which is
            // the surface that already exists for "how does any of this work" and which opens by
            // itself on a first run.
            var controls = HudStyle.Text(ScreenStrings.HudControlsEssential, HudStyle.CaptionSize,
                                         HudStyle.Dim);
            controls.style.whiteSpace = WhiteSpace.NoWrap;
            controlsPill = HudStyle.Pill(controls);
            controlsPill.style.position = Position.Absolute;
            controlsPill.style.alignSelf = Align.FlexStart;
            controlsPill.style.maxWidth = StyleKeyword.None;
            controlsPill.style.left = HudStyle.Inset;
            controlsPill.style.bottom = HudStyle.Inset;
            root.Add(controlsPill);
        }

        /// <summary>
        /// A full-width, transparent, inert strip that centres one pill on it.
        /// <para>
        /// The pill has to keep its own width — a plate stretched across the screen behind one short
        /// sentence is a black bar, not a backing — so it cannot be the thing anchored to both edges.
        /// This is the element that is, and it carries no colour of its own.
        /// </para>
        /// </summary>
        private static VisualElement CentredBand(VisualElement pill, float bottom)
        {
            var band = new VisualElement { pickingMode = PickingMode.Ignore };
            band.style.position = Position.Absolute;
            band.style.left = 0;
            band.style.right = 0;
            band.style.bottom = bottom;
            band.style.flexDirection = FlexDirection.Row;
            band.style.justifyContent = Justify.Center;
            band.Add(pill);
            return band;
        }

        /// <summary>Where the inventory bar's top edge lands, and therefore what the lines above clear.</summary>
        private static float InventoryTop => HudStyle.Inset + SlotSize + SlotNameHeight;

        private static float HandsBottom => InventoryTop + HudStyle.S3;

        private static float ToastBottom => HandsBottom + HudStyle.S8;

        private void Update()
        {
            if (!EnsureUi()) return;
            if (interactor == null || crosshair == null) return;

            // Asked once and handed to everything that reacts to it: the cards share a corner, and the
            // bottom band has to agree with them about whether something else owns the screen — the
            // inspection overlay is deliberately translucent, so an inventory bar left up underneath
            // it draws straight through the item the player is trying to look at.
            bool screenUp = !interactor.enabled
                            || (interactor.Inspection != null && interactor.Inspection.IsOpen)
                            || (interactor.Terminal != null && interactor.Terminal.IsOpen);

            centre.style.display = screenUp ? DisplayStyle.None : DisplayStyle.Flex;
            controlsPill.style.display = screenUp ? DisplayStyle.None : DisplayStyle.Flex;
            inventoryBar.style.display = screenUp ? DisplayStyle.None : DisplayStyle.Flex;

            if (!screenUp) UpdateAim();

            toastLabel.text = interactor.Toast ?? string.Empty;
            toastPill.style.display = !screenUp && !string.IsNullOrEmpty(toastLabel.text)
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            UpdateHands(screenUp);
            UpdateInventory();
            UpdateInspection();
            UpdateShiftBrief(screenUp);
            UpdateTutorial(screenUp);

            debugLabel.text = InteractionDebug.Enabled && interactionDebug != null
                ? interactionDebug.BuildReadout()
                : string.Empty;

            header.Refresh(LabView.Current);
        }

        /// <summary>The crosshair, the prompt and the hold bar — everything the ray produces.</summary>
        private void UpdateAim()
        {
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
            var edge = new StyleColor(blocked ? HudStyle.Dim : new Color(1f, 1f, 1f, 0.9f));
            crosshair.style.borderTopColor = edge;
            crosshair.style.borderBottomColor = edge;
            crosshair.style.borderLeftColor = edge;
            crosshair.style.borderRightColor = edge;

            promptLabel.text = interactor.Prompt ?? string.Empty;
            promptLabel.style.color = new StyleColor(blocked ? HudStyle.Dim : HudStyle.Ink);
            promptPill.style.display = string.IsNullOrEmpty(promptLabel.text)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            bool holding = interactor.HoldProgress > 0.001f;
            holdBar.style.display = holding ? DisplayStyle.Flex : DisplayStyle.None;
            holdFill.style.width = Length.Percent(interactor.HoldProgress * 100f);
        }

        /// <summary>
        /// Tell the player what the thing in their hands does, since a carried item cannot be looked
        /// at and therefore never shows a prompt of its own. This is also where the selected slot's
        /// name is spelled out in full, which is what lets the slot itself cut a long one.
        /// </summary>
        private void UpdateHands(bool screenUp)
        {
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

            handsPill.style.display = !screenUp && !string.IsNullOrEmpty(handsLabel.text)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        /// <summary>
        /// Three slots that say what they hold.
        /// <para>
        /// The icons were never the problem — three unlabelled boxes are, because a rendered silhouette
        /// of a vial and a rendered silhouette of a solvent bottle are the same shape at 64 px in a
        /// palette-only art style. Each slot therefore carries its item's name on a strip along the
        /// bottom, cut with an ellipsis rather than wrapped: the box is fixed, German is longer, and
        /// the full name of whichever slot is selected is on the hands line anyway.
        /// </para>
        /// </summary>
        private void BuildInventory()
        {
            ReleaseInventoryIcons();
            inventorySlots.Clear();
            inventoryIcons.Clear();
            inventoryNames.Clear();
            inventoryItems.Clear();
            paintedSelection = -1;

            inventoryBar = new VisualElement { pickingMode = PickingMode.Ignore };
            inventoryBar.style.position = Position.Absolute;
            inventoryBar.style.left = Length.Percent(50);
            inventoryBar.style.bottom = HudStyle.Inset;
            inventoryBar.style.translate = new Translate(Length.Percent(-50), 0);
            inventoryBar.style.flexDirection = FlexDirection.Row;
            root.Add(inventoryBar);

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                var slot = new VisualElement { pickingMode = PickingMode.Ignore };
                slot.style.width = SlotSize;
                slot.style.height = SlotSize + SlotNameHeight;
                slot.style.marginLeft = HudStyle.S1;
                slot.style.marginRight = HudStyle.S1;
                slot.style.overflow = Overflow.Hidden;
                slot.style.backgroundColor = new StyleColor(HudStyle.Plate);
                HudStyle.Border(slot, SlotBorder, HudStyle.Line);
                Round(slot, HudStyle.Radius);

                // The select key, as a numeral. Not language: [1] is [1] in every locale, and running
                // it through the string table would be the "ids are not data" mistake with a digit.
                var number = HudStyle.Text((i + 1).ToString(), HudStyle.CaptionSize, HudStyle.Faint);
                number.style.position = Position.Absolute;
                number.style.left = HudStyle.S2;
                number.style.top = HudStyle.S1;
                slot.Add(number);

                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.style.position = Position.Absolute;
                icon.style.left = HudStyle.S3;
                icon.style.right = HudStyle.S3;
                icon.style.top = HudStyle.S6;
                icon.style.bottom = SlotNameHeight + HudStyle.S1;
                icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                slot.Add(icon);

                var strip = new VisualElement { pickingMode = PickingMode.Ignore };
                strip.style.position = Position.Absolute;
                strip.style.left = 0;
                strip.style.right = 0;
                strip.style.bottom = 0;
                strip.style.height = SlotNameHeight;
                strip.style.paddingLeft = HudStyle.S1;
                strip.style.paddingRight = HudStyle.S1;
                strip.style.justifyContent = Justify.Center;
                strip.style.backgroundColor = new StyleColor(HudStyle.PlateSunken);
                slot.Add(strip);

                var name = HudStyle.Text(string.Empty, HudStyle.CaptionSize, HudStyle.Dim);
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
                HudStyle.Truncate(name);
                strip.Add(name);

                inventoryBar.Add(slot);
                inventorySlots.Add(slot);
                inventoryIcons.Add(icon);
                inventoryNames.Add(name);
                inventoryTextures.Add(null);
                inventoryItems.Add(null);
            }
        }

        /// <summary>
        /// Repaint only what changed: the icon when the item changes, the edges when the selection
        /// does. The slots are on screen for the whole shift and neither of those happens on a frame
        /// the player is not pressing something.
        /// </summary>
        private void UpdateInventory()
        {
            var inventory = interactor.Inventory;
            if (inventory == null) return;

            bool selectionMoved = inventory.SelectedIndex != paintedSelection;
            paintedSelection = inventory.SelectedIndex;

            for (int i = 0; i < inventorySlots.Count; i++)
            {
                bool selected = i == inventory.SelectedIndex;
                var item = inventory.ItemAt(i);

                // The object, and separately what it currently calls itself: a vial keeps its identity
                // across being registered and changes its label doing it, so neither alone is enough.
                // Compared rather than concatenated into a key — this runs every frame for every slot,
                // and the string it used to build was garbage sixty times a second for ever.
                string label = item != null ? item.DisplayName : ScreenStrings.HudSlotEmpty.Text;
                bool itemChanged = !ReferenceEquals(inventoryItems[i], item) ||
                                   !string.Equals(inventoryNames[i].text, label,
                                                  System.StringComparison.Ordinal);

                if (selectionMoved || itemChanged)
                {
                    // Selection is carried by the edge colour and by the weight of the name under it,
                    // never by colour alone (§2.2) — and never in the signal set (hard rule 4).
                    HudStyle.Border(inventorySlots[i], SlotBorder,
                                    selected ? HudStyle.Accent : HudStyle.Line);

                    inventoryNames[i].style.color = new StyleColor(
                        item == null ? HudStyle.Faint : selected ? HudStyle.Ink : HudStyle.Dim);
                    inventoryNames[i].style.unityFontStyleAndWeight =
                        selected && item != null ? FontStyle.Bold : FontStyle.Normal;
                }

                if (!itemChanged) continue;

                if (inventoryTextures[i] != null) Destroy(inventoryTextures[i]);
                inventoryTextures[i] = item != null ? InventoryIconRenderer.Render(item) : null;
                inventoryItems[i] = item;
                inventoryIcons[i].style.backgroundImage = inventoryTextures[i] != null
                    ? new StyleBackground(inventoryTextures[i])
                    : new StyleBackground(StyleKeyword.Null);

                inventoryNames[i].text = label;
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
            inspectionOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            inspectionOverlay.style.position = Position.Absolute;
            inspectionOverlay.style.left = 0;
            inspectionOverlay.style.right = 0;
            inspectionOverlay.style.top = 0;
            inspectionOverlay.style.bottom = 0;
            // Keep the world quiet without veiling the inspected mesh: book text now lives on the
            // physical page texture and must remain readable through this overlay.
            inspectionOverlay.style.backgroundColor = new StyleColor(new Color(0.015f, 0.018f, 0.02f, 0.18f));
            inspectionOverlay.style.display = DisplayStyle.None;
            root.Add(inspectionOverlay);

            inspectionTitle = HudStyle.Text(string.Empty, HudStyle.HeadingSize, HudStyle.Ink,
                                            bold: true);
            var titlePill = HudStyle.Pill(inspectionTitle, HudStyle.S4, HudStyle.S2);
            titlePill.style.position = Position.Absolute;
            titlePill.style.alignSelf = Align.FlexStart;
            titlePill.style.left = HudStyle.Inset;
            titlePill.style.top = HudStyle.Inset;
            inspectionOverlay.Add(titlePill);

            inspectionBody = HudStyle.Text(string.Empty, HudStyle.BodySize, HudStyle.Ink);
            inspectionBody.style.whiteSpace = WhiteSpace.Normal;
            var bodyPill = HudStyle.Pill(inspectionBody, HudStyle.S4, HudStyle.S3);
            bodyPill.style.position = Position.Absolute;
            bodyPill.style.alignSelf = Align.FlexStart;
            bodyPill.style.left = HudStyle.Inset;
            bodyPill.style.bottom = 72;
            bodyPill.style.width = Length.Percent(34);
            bodyPill.style.maxWidth = StyleKeyword.None;
            inspectionOverlay.Add(bodyPill);

            inspectionHelp = HudStyle.Text(string.Empty, HudStyle.CaptionSize, HudStyle.Dim);
            inspectionHelp.style.unityTextAlign = TextAnchor.MiddleCenter;
            inspectionHelp.style.whiteSpace = WhiteSpace.Normal;
            inspectionOverlay.Add(CentredBand(HudStyle.Pill(inspectionHelp), HudStyle.Inset));
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
        /// for, and — since the controls strip stopped being a permanent wall of text — the place the
        /// full binding list lives.
        /// <para>
        /// Its words come from <see cref="BookContent.ShiftBrief"/>, so this method knows nothing
        /// about the lab and cannot teach a diagnosis even by accident. It exists at all because the
        /// manuals — which already explain everything this room runs on — are objects you have to
        /// notice, pick up and open, and nothing in an untextured grey room suggests doing that. A
        /// book cannot solve "nobody knows the books are there".
        /// </para>
        /// <para>
        /// The bindings belong on it for the same reason: it is already the answer to "how does any of
        /// this work", it opens by itself on a first run, and the four bindings a player needs before
        /// they have opened anything stay on screen permanently regardless.
        /// </para>
        /// <para>
        /// Drawn in <see cref="HudStyle.Accent"/> and <see cref="HudStyle.Dim"/>, never in the verdict
        /// set (hard rule 4). It is a card in the corner rather than a modal because it must gate
        /// nothing: the player can walk, look, carry and run an instrument with it up, and in co-op
        /// nobody else can tell it is open. See #73 in CLAUDE.md for what a mandatory first step did to
        /// the pacing last time.
        /// </para>
        /// </summary>
        private void BuildShiftBrief()
        {
            briefCard = new VisualElement { pickingMode = PickingMode.Ignore };
            briefCard.style.position = Position.Absolute;
            briefCard.style.left = HudStyle.Inset;
            briefCard.style.top = HudStyle.ContentTop;
            briefCard.style.width = CardWidth;
            briefCard.style.paddingTop = HudStyle.S4;
            briefCard.style.paddingBottom = HudStyle.S4;
            briefCard.style.paddingLeft = HudStyle.S4;
            briefCard.style.paddingRight = HudStyle.S4;
            briefCard.style.backgroundColor = new StyleColor(HudStyle.Plate);
            briefCard.style.borderLeftWidth = 2;
            briefCard.style.borderLeftColor = new StyleColor(HudStyle.Accent);
            briefCard.style.display = DisplayStyle.None;
            Round(briefCard, HudStyle.Radius);
            root.Add(briefCard);

            var heading = HudStyle.Text(BookContent.ShiftBriefTitle, HudStyle.MetricSize,
                                        HudStyle.Ink, bold: true);
            heading.style.whiteSpace = WhiteSpace.Normal;
            briefCard.Add(heading);

            foreach (var page in BookContent.ShiftBrief())
            {
                var step = HudStyle.Text(page.Title, HudStyle.BodySize, HudStyle.Accent, bold: true);
                step.style.marginTop = HudStyle.S3;
                step.style.whiteSpace = WhiteSpace.Normal;
                briefCard.Add(step);

                var body = HudStyle.Text(page.Body, HudStyle.BodySize, HudStyle.Dim);
                body.style.marginTop = HudStyle.S1;
                body.style.whiteSpace = WhiteSpace.Normal;
                briefCard.Add(body);
            }

            var rule = new VisualElement { pickingMode = PickingMode.Ignore };
            rule.style.height = 1;
            rule.style.marginTop = HudStyle.S4;
            rule.style.backgroundColor = new StyleColor(HudStyle.Line);
            briefCard.Add(rule);

            var controlsHeading = HudStyle.Caption(ScreenStrings.HudControlsHeading);
            controlsHeading.style.marginTop = HudStyle.S3;
            briefCard.Add(controlsHeading);

            var controls = HudStyle.Text(ScreenStrings.HudControls, HudStyle.BodySize, HudStyle.Dim);
            controls.style.marginTop = HudStyle.S1;
            controls.style.whiteSpace = WhiteSpace.Normal;
            briefCard.Add(controls);

            var closing = HudStyle.Text(
                ScreenStrings.HudBriefClosing.Format(("closing", BookContent.ShiftBriefClosing)),
                HudStyle.CaptionSize, HudStyle.Faint);
            closing.style.marginTop = HudStyle.S4;
            closing.style.whiteSpace = WhiteSpace.Normal;
            briefCard.Add(closing);
        }

        /// <summary>
        /// Width of a corner card.
        /// <para>
        /// Both cards share it so the corner does not change shape as one replaces the other, and it is
        /// sized off the German rather than the English — every line on either card wraps, and a card
        /// cut to the English simply gets taller in every other language.
        /// </para>
        /// </summary>
        internal const float CardWidth = 440f;

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
        /// The label helper the terminal screen still builds on. New HUD work goes through
        /// <see cref="HudStyle"/>; this stays because <c>TerminalScreen</c> is a screen in the room
        /// with its own density constraints, not part of this overlay.
        /// </summary>
        internal static void Style(Label label, int size, Color colour)
        {
            label.style.fontSize = size;
            label.style.color = new StyleColor(colour);
            label.style.unityFontStyleAndWeight = FontStyle.Normal;
        }

        internal static void Round(VisualElement element, float radius) =>
            HudStyle.Round(element, radius);
    }
}
