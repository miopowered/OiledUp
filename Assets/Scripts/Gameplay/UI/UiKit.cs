using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Gameplay.UI
{
    /// <summary>
    /// The shared widget vocabulary every screen is built from: panels, type, buttons, fields, tabs
    /// and — the part that actually matters — one implementation of focus.
    /// <para>
    /// This lives in <c>Residue.Gameplay</c> rather than <c>Residue.Net</c> because both ends need
    /// it. The in-lab screens (terminal, pause, settings) are Gameplay, and the menu screens are
    /// Net; Net references Gameplay and not the other way round, so Gameplay is the only assembly
    /// both can reach. Nothing here touches the network, a lab, or a sample — it is presentation
    /// only, and it must stay that way or the menus start dragging simulation with them.
    /// </para>
    /// <para>
    /// Built in C# with no UXML, matching <c>ConnectScreen</c>, <c>TerminalScreen</c> and
    /// <c>LabHud</c>. Those three each grew their own <c>StyleButton</c> / <c>Round</c> / label
    /// helper, which is fine at three screens and unmaintainable at eight — this is that code,
    /// promoted, before the divergence is expensive to undo.
    /// </para>
    /// <para>
    /// Every control built here is already wrapped by <see cref="Focusable{T}"/>, so a caller never
    /// has to remember to ask for keyboard and gamepad navigation. That is the whole point:
    /// retrofitting focus order onto five finished screens costs more than building it once, and a
    /// screen that forgot is not visibly broken until someone unplugs a mouse.
    /// </para>
    /// </summary>
    public static class UiKit
    {
        // -- Type scale and rhythm ---------------------------------------------------------------------
        // Five sizes and two gaps, deliberately. A scale wide enough to express every intent is a
        // scale wide enough for two screens to pick different sizes for the same thing.

        public const float TitleSize = 40f;
        public const float HeadingSize = 20f;
        public const float BodySize = 14f;
        public const float LabelSize = 13f;
        public const float HintSize = 11f;

        public const float Gap = 8f;
        public const float GapWide = 18f;
        public const float Radius = 4f;

        /// <summary>Padding inside a <see cref="Panel"/>, and the inset a screen aligns to.</summary>
        public const float PanelPadding = 24f;

        /// <summary>Width of the label column in a <see cref="Field"/>, so stacked fields line up.</summary>
        public const float LabelColumn = 148f;

        /// <summary>Width of the readout column in a <see cref="SliderField"/>.</summary>
        public const float ValueColumn = 58f;

        /// <summary>
        /// Ring thickness. Reserved on every focusable control at rest (drawn transparent) rather
        /// than added on focus, because a border that appears on focus reflows the row under it and
        /// the whole screen twitches as the player tabs through.
        /// </summary>
        private const float RingWidth = 1f;

        /// <summary>Hover ring: the focus colour, faded, so hover reads as "you could" and focus as
        /// "you are" without introducing a second hue.</summary>
        private static readonly Color HoverRing = new(
            UiPalette.Focus.r, UiPalette.Focus.g, UiPalette.Focus.b, 0.30f);

        /// <summary>Hover face for the neutral buttons. Lifted off
        /// <see cref="UiPalette.SurfaceRaised"/> rather than dropped towards
        /// <see cref="UiPalette.Surface"/> — darkening on hover reads as pressed-and-stuck.</summary>
        private static readonly Color QuietHover = new(0.23f, 0.245f, 0.265f, 0.96f);

        // -- Structure ---------------------------------------------------------------------------------

        /// <summary>
        /// A full-screen layer that centres its content. Pass <paramref name="translucent"/> for a
        /// menu drawn over live play — an opaque backdrop over the lab loses the player their
        /// bearings, and a translucent one behind the main menu shows an empty scene.
        /// </summary>
        public static VisualElement Backdrop(bool translucent = false)
        {
            var element = new VisualElement();
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.right = 0;
            element.style.top = 0;
            element.style.bottom = 0;
            element.style.alignItems = Align.Center;
            element.style.justifyContent = Justify.Center;
            element.style.backgroundColor = new StyleColor(
                translucent ? UiPalette.Scrim : UiPalette.Backdrop);
            return element;
        }

        /// <summary>A card: the only container that draws a surface. Nesting two of them is a sign
        /// the screen wants a <see cref="Divider"/>.</summary>
        public static VisualElement Panel(float width = 460f)
        {
            var element = new VisualElement();
            element.style.width = width;
            element.style.paddingTop = PanelPadding;
            element.style.paddingBottom = PanelPadding;
            element.style.paddingLeft = PanelPadding;
            element.style.paddingRight = PanelPadding;
            element.style.backgroundColor = new StyleColor(UiPalette.Surface);
            Round(element);
            return element;
        }

        /// <summary>A vertical stack that owns the spacing between its children.</summary>
        public static VisualElement Column(float gap = Gap) => Stack(FlexDirection.Column, gap);

        /// <summary>A horizontal, vertically centred stack that owns the spacing between its children.</summary>
        public static VisualElement Row(float gap = Gap) => Stack(FlexDirection.Row, gap);

        /// <summary>Eats the leftover space in a <see cref="Row"/> or <see cref="Column"/>. Use this
        /// to push a pair of buttons to the right rather than guessing a margin.</summary>
        public static VisualElement Spacer()
        {
            var element = new VisualElement();
            element.style.flexGrow = 1f;
            element.pickingMode = PickingMode.Ignore;
            return element;
        }

        /// <summary>A hairline rule.</summary>
        public static VisualElement Divider()
        {
            var element = new VisualElement();
            element.style.height = 1;
            element.style.marginTop = Gap;
            element.style.marginBottom = Gap;
            element.style.flexShrink = 0f;
            element.style.backgroundColor = new StyleColor(UiPalette.Line);
            element.pickingMode = PickingMode.Ignore;
            return element;
        }

        /// <summary>
        /// Spacing lives on the container, not on the children, so a screen never sets a margin.
        /// <para>
        /// UI Toolkit in this Editor version has no flex <c>gap</c>, so the container applies a
        /// leading margin to each child itself. It re-runs on every layout change because there is
        /// no "child added" event to hang it on, and skips children hidden with
        /// <see cref="DisplayStyle.None"/> — otherwise collapsing the first row of a panel leaves
        /// its gap behind as an unexplained indent.
        /// </para>
        /// </summary>
        private static VisualElement Stack(FlexDirection direction, float gap)
        {
            var element = new VisualElement();
            element.style.flexDirection = direction;
            if (direction == FlexDirection.Row) element.style.alignItems = Align.Center;

            bool horizontal = direction == FlexDirection.Row;
            void Apply() => ApplyGap(element, gap, horizontal);

            element.RegisterCallback<GeometryChangedEvent>(_ => Apply());
            element.schedule.Execute(Apply);
            return element;
        }

        private static void ApplyGap(VisualElement container, float gap, bool horizontal)
        {
            bool first = true;
            foreach (var child in container.Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None) continue;

                float lead = first ? 0f : gap;
                if (horizontal)
                {
                    SetMargin(child, lead, leading: true, horizontal: true);
                    SetMargin(child, 0f, leading: false, horizontal: true);
                }
                else
                {
                    SetMargin(child, lead, leading: true, horizontal: false);
                    SetMargin(child, 0f, leading: false, horizontal: false);
                }
                first = false;
            }
        }

        /// <summary>
        /// Write a margin only when it actually differs from the one already resolved.
        /// <para>
        /// <b>This guard is what stops the layout engine chasing its own tail.</b> The gap is applied
        /// from a <c>GeometryChangedEvent</c> handler, and assigning an inline style marks the element
        /// dirty for layout — which produces another geometry change, which runs this again. With an
        /// unconditional write, that loop never reaches a fixed point: it re-lays out every frame and
        /// climbs, which presents as an editor or player that slowly stops responding rather than as
        /// anything identifiable in a stack trace. Comparing first means the second pass writes
        /// nothing, so the cycle terminates after one correction.
        /// </para>
        /// Compared against <c>resolvedStyle</c> rather than the inline value, because that is the
        /// number the next layout pass will actually use — an inline style that has not been resolved
        /// yet would compare equal while the layout still disagreed.
        /// </summary>
        private static void SetMargin(VisualElement child, float value, bool leading, bool horizontal)
        {
            var resolved = child.resolvedStyle;

            float current = horizontal
                ? (leading ? resolved.marginLeft : resolved.marginRight)
                : (leading ? resolved.marginTop : resolved.marginBottom);

            if (Mathf.Approximately(current, value)) return;

            if (horizontal)
            {
                if (leading) child.style.marginLeft = value;
                else child.style.marginRight = value;
            }
            else
            {
                if (leading) child.style.marginTop = value;
                else child.style.marginBottom = value;
            }
        }

        // -- Type --------------------------------------------------------------------------------------

        /// <summary>The one thing on the screen. There is never a second.</summary>
        public static Label Title(string text) =>
            Text(text, TitleSize, UiPalette.Ink, FontStyle.Bold);

        /// <summary>Names a group of controls.</summary>
        public static Label Heading(string text) =>
            Text(text, HeadingSize, UiPalette.Ink, FontStyle.Bold);

        /// <summary>Prose. Wraps, because every string that reaches a player is longer in German.</summary>
        public static Label Body(string text) =>
            Text(text, BodySize, UiPalette.Ink, FontStyle.Normal);

        /// <summary>The consequence of a control, in the player's words. Never load-bearing.</summary>
        public static Label Hint(string text) =>
            Text(text, HintSize, UiPalette.InkFaint, FontStyle.Normal);

        /// <summary>A readout. Right-aligned and fixed-width so a column of them does not jitter as
        /// the digits change.</summary>
        public static Label Value(string text)
        {
            var label = Text(text, LabelSize, UiPalette.Ink, FontStyle.Bold);
            label.style.unityTextAlign = TextAnchor.MiddleRight;
            label.style.minWidth = ValueColumn;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            return label;
        }

        private static Label Text(string text, float size, Color colour, FontStyle weight)
        {
            var label = new Label(text ?? string.Empty);
            label.style.fontSize = size;
            label.style.color = new StyleColor(colour);
            label.style.unityFontStyleAndWeight = weight;
            label.style.whiteSpace = WhiteSpace.Normal;
            ZeroMargins(label);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        // -- Buttons -----------------------------------------------------------------------------------

        /// <summary>The one action the screen wants. Accent-faced.</summary>
        public static Button ActionButton(string text, Action onClick)
        {
            var button = MakeButton(text, onClick, UiPalette.Accent, UiPalette.Ink);
            Hover(button, UiPalette.Accent, UiPalette.AccentSoft);
            return button;
        }

        /// <summary>Everything else. Reads as a button without competing with the action.</summary>
        public static Button QuietButton(string text, Action onClick)
        {
            var button = MakeButton(text, onClick, UiPalette.SurfaceRaised, UiPalette.Ink);
            Hover(button, UiPalette.SurfaceRaised, QuietHover);
            return button;
        }

        /// <summary>
        /// Something the player cannot undo. Marked by <see cref="UiPalette.Warn"/> <i>text</i> on
        /// an ordinary face, not by a red fill — hard rule 4, and see <see cref="UiPalette"/> for
        /// why "but this one really is dangerous" is not an exception to it.
        /// </summary>
        public static Button DangerButton(string text, Action onClick)
        {
            var button = MakeButton(text, onClick, UiPalette.SurfaceRaised, UiPalette.Warn);
            Hover(button, UiPalette.SurfaceRaised, QuietHover);
            return button;
        }

        /// <summary>
        /// Strips Unity's default runtime theme off a button. Without this every screen inherits the
        /// theme's borders and 4px margins, and a kit-built panel stops matching a hand-built one —
        /// which is the failure this whole file exists to prevent.
        /// </summary>
        private static Button MakeButton(string text, Action onClick, Color face, Color ink)
        {
            var button = new Button(() => onClick?.Invoke()) { text = text ?? string.Empty };

            button.style.backgroundColor = new StyleColor(face);
            button.style.color = new StyleColor(ink);
            button.style.fontSize = BodySize;
            button.style.paddingTop = 10;
            button.style.paddingBottom = 10;
            button.style.paddingLeft = 14;
            button.style.paddingRight = 14;
            ZeroMargins(button);
            Round(button);

            return Focusable(button);
        }

        private static void Hover(Button button, Color rest, Color hover)
        {
            button.RegisterCallback<PointerEnterEvent>(_ =>
                button.style.backgroundColor = new StyleColor(hover));
            button.RegisterCallback<PointerLeaveEvent>(_ =>
                button.style.backgroundColor = new StyleColor(rest));
        }

        // -- Fields ------------------------------------------------------------------------------------

        /// <summary>
        /// A labelled control on one line, with the label column fixed so stacked fields align
        /// without a screen measuring anything. This is the only place a control is handed its focus
        /// ring, so the typed builders below all route through it rather than each remembering.
        /// </summary>
        public static VisualElement Field(string label, VisualElement control)
        {
            var row = Row();
            row.Add(FieldLabel(label));

            if (control != null)
            {
                control.style.flexGrow = 1f;
                row.Add(Focusable(control));
            }
            return row;
        }

        /// <summary>
        /// The row a typed field builder put its control in, ready to add to a panel.
        /// <para>
        /// <see cref="SliderField"/> and friends return the <i>control</i>, because that is what a
        /// screen needs later to push a value back in without firing its own change callback. The
        /// row is simply its parent; this exists so calling code says what it means instead of
        /// <c>slider.parent</c>.
        /// </para>
        /// </summary>
        public static VisualElement RowFor(VisualElement control) => control?.parent;

        private static Label FieldLabel(string text)
        {
            var label = Text(text, LabelSize, UiPalette.InkDim, FontStyle.Normal);
            label.style.width = LabelColumn;
            label.style.flexShrink = 0f;
            return label;
        }

        /// <summary>Unity's default theme puts margins on every control; spacing is the container's
        /// job here, so take them off.</summary>
        private static void ZeroMargins(VisualElement element)
        {
            element.style.marginTop = 0;
            element.style.marginBottom = 0;
            element.style.marginLeft = 0;
            element.style.marginRight = 0;
        }

        /// <summary>
        /// Label, slider, live readout — add <c>RowFor(slider)</c> to the panel.
        /// <para>
        /// The readout is not decoration. A slider with no number is a control the player can only
        /// set by feel, and mouse sensitivity and master volume are both settings people want to
        /// reproduce on a second machine. <paramref name="format"/> is how a screen says "%" or
        /// "1.25×"; the default is a rounded integer.
        /// </para>
        /// </summary>
        public static Slider SliderField(string label, float min, float max, float value,
                                         Action<float> changed, Func<float, string> format = null)
        {
            format ??= v => Mathf.RoundToInt(v).ToString();

            var slider = new Slider(min, max) { value = Mathf.Clamp(value, min, max) };
            ZeroMargins(slider);

            var readout = Value(format(slider.value));

            slider.RegisterValueChangedCallback(evt =>
            {
                readout.text = format(evt.newValue);
                changed?.Invoke(evt.newValue);
            });

            // Runtime UI navigation maps the Player action map's movement axis onto sliders, so with
            // a slider focused the walking keys silently drag it — the player strafes and the master
            // volume moves with them. It is not a slider bug and it cannot be fixed per-screen
            // without every screen remembering, which is exactly the failure this kit exists to
            // stop. Swallow navigation here, once; adjustment stays with the pointer and with
            // whatever explicit shortcut the screen advertises. First found on ConnectScreen's voice
            // volume slider, which solved it locally by refusing focus altogether.
            slider.RegisterCallback<NavigationMoveEvent>(
                evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);

            var row = Field(label, slider);
            row.Add(readout);
            return slider;
        }

        /// <summary>
        /// An on/off setting — add <c>RowFor(toggle)</c> to the panel. "On" is drawn in
        /// <see cref="UiPalette.Accent"/> and never in green: a toggle is not a verdict, and
        /// spending the signal set on chrome is what stops red meaning critical everywhere else.
        /// </summary>
        public static Toggle ToggleField(string label, bool value, Action<bool> changed)
        {
            var toggle = new Toggle { value = value };
            ZeroMargins(toggle);
            toggle.RegisterValueChangedCallback(evt => changed?.Invoke(evt.newValue));

            Field(label, toggle);
            return toggle;
        }

        /// <summary>
        /// One of a short list — add <c>RowFor(dropdown)</c> to the panel.
        /// <para>
        /// Reports the <i>index</i> rather than the chosen string, because the visible text of an
        /// option is a display concern: callers that matched on it break the moment a label is
        /// reworded or localised.
        /// </para>
        /// </summary>
        public static DropdownField ChoiceField(string label, List<string> options, int index,
                                                Action<int> changed)
        {
            var choices = options ?? new List<string>();
            int start = choices.Count == 0 ? -1 : Mathf.Clamp(index, 0, choices.Count - 1);

            var dropdown = new DropdownField(choices, start);
            ZeroMargins(dropdown);
            dropdown.RegisterValueChangedCallback(_ => changed?.Invoke(dropdown.index));

            Field(label, dropdown);
            return dropdown;
        }

        /// <summary>
        /// Free text — add <c>RowFor(field)</c> to the panel. Reports on every keystroke, so a
        /// screen that wants commit-on-enter registers its own <c>KeyDownEvent</c>.
        /// </summary>
        public static TextField TextEntry(string label, string value, Action<string> changed)
        {
            var field = new TextField { value = value ?? string.Empty };
            ZeroMargins(field);
            field.style.fontSize = BodySize;
            field.RegisterValueChangedCallback(evt => changed?.Invoke(evt.newValue));

            Field(label, field);
            return field;
        }

        /// <summary>
        /// A tab strip. The selected tab is marked by face and weight rather than by colour, so it
        /// costs nothing from the signal set and still survives on a washed-out monitor. The strip
        /// repaints itself on click before telling the caller, because a tab that waits for a screen
        /// rebuild to look selected feels broken at anything under 60fps.
        /// </summary>
        public static VisualElement Tabs(string[] names, int selected, Action<int> changed)
        {
            var strip = Row(2f);
            if (names == null || names.Length == 0) return strip;

            var buttons = new List<Button>(names.Length);
            int current = Mathf.Clamp(selected, 0, names.Length - 1);

            void Paint()
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    bool on = i == current;
                    buttons[i].style.backgroundColor = new StyleColor(
                        on ? UiPalette.SurfaceRaised : UiPalette.SurfaceSunken);
                    buttons[i].style.color = new StyleColor(on ? UiPalette.Ink : UiPalette.InkDim);
                    buttons[i].style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
                }
            }

            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var button = MakeButton(names[i], () =>
                {
                    current = index;
                    Paint();
                    changed?.Invoke(index);
                }, UiPalette.SurfaceSunken, UiPalette.InkDim);

                button.style.flexGrow = 1f;
                button.style.paddingTop = 8;
                button.style.paddingBottom = 8;
                buttons.Add(button);
                strip.Add(button);
            }

            Paint();
            return strip;
        }

        // -- Focus -------------------------------------------------------------------------------------

        /// <summary>
        /// Gives an element a focus ring and makes it a tab stop.
        /// <para>
        /// The ring is drawn by recolouring a border that is always present but transparent at rest,
        /// so focus never changes an element's size and the screen does not reflow as the player
        /// tabs. Focus is tracked with <c>FocusIn</c>/<c>FocusOut</c> rather than
        /// <c>Focus</c>/<c>Blur</c> because those bubble: a composite like <see cref="TextField"/>
        /// or <see cref="DropdownField"/> delegates focus to an inner input element, and the
        /// non-bubbling pair never reaches the field the player thinks they are on.
        /// </para>
        /// <para>
        /// Wrap a leaf control, not a container. Anything passed here becomes focusable, so wrapping
        /// a row of two buttons adds a tab stop that lands on neither of them.
        /// </para>
        /// </summary>
        public static T Focusable<T>(T element) where T : VisualElement
        {
            if (element == null) return null;

            element.focusable = true;
            element.style.borderTopWidth = RingWidth;
            element.style.borderBottomWidth = RingWidth;
            element.style.borderLeftWidth = RingWidth;
            element.style.borderRightWidth = RingWidth;

            bool focused = false;
            bool hovered = false;

            void Paint() => Ring(element,
                focused ? UiPalette.Focus : hovered ? HoverRing : Color.clear);

            element.RegisterCallback<FocusInEvent>(_ => { focused = true; Paint(); });
            element.RegisterCallback<FocusOutEvent>(_ => { focused = false; Paint(); });
            element.RegisterCallback<PointerEnterEvent>(_ => { hovered = true; Paint(); });
            element.RegisterCallback<PointerLeaveEvent>(_ => { hovered = false; Paint(); });

            Paint();
            return element;
        }

        private static void Ring(VisualElement element, Color colour)
        {
            var style = new StyleColor(colour);
            element.style.borderTopColor = style;
            element.style.borderBottomColor = style;
            element.style.borderLeftColor = style;
            element.style.borderRightColor = style;
        }

        /// <summary>
        /// Puts the caret somewhere sensible when a screen opens, so a player who arrived on a
        /// gamepad is not looking at a panel with no cursor on it.
        /// <para>
        /// Scheduled rather than immediate: an element added to the tree this frame has no resolved
        /// layout and no panel yet, and <c>Focus()</c> on it is silently dropped. Deferring one tick
        /// is the difference between "focus is broken" and "focus was called too early".
        /// </para>
        /// </summary>
        public static void FocusFirst(VisualElement root)
        {
            if (root == null) return;
            root.schedule.Execute(() => FirstFocusable(root)?.Focus());
        }

        private static VisualElement FirstFocusable(VisualElement element)
        {
            foreach (var child in element.Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None) continue;

                if (child.focusable && child.canGrabFocus && child.enabledInHierarchy) return child;

                var nested = FirstFocusable(child);
                if (nested != null) return nested;
            }
            return null;
        }

        /// <summary>Rounds all four corners. Every screen wrote this; now none of them do.</summary>
        public static void Round(VisualElement element, float radius = Radius)
        {
            if (element == null) return;
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
