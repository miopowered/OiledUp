using Residue.Gameplay.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The HUD's own vocabulary: a type scale, a spacing scale, and the dark plate every line of text
    /// in the overlay sits on.
    ///
    /// <para>
    /// <b>Why this is not <see cref="UiKit"/>.</b> A modal card and an always-on overlay are not the
    /// same design problem, and the kit is built for the first one. Every control it hands out is
    /// wrapped in <c>UiKit.Focusable</c>, which makes it a tab stop — a HUD element that can take focus
    /// steals gamepad navigation from whatever screen is actually open. Its stacks apply spacing from a
    /// <c>GeometryChangedEvent</c> handler that writes inline margins, which is a fine trade on a panel
    /// built once and a per-frame layout callback on a bar whose numbers change every second. And its
    /// scale is authored for text read at rest on a fixed backdrop; this text is read in peripheral
    /// vision, over live geometry, while the player is walking, and needs to be bigger and further
    /// apart. So the HUD keeps its own vocabulary — but it <i>aliases</i> the kit's colours rather than
    /// restating them, because the one thing that must not drift between the pause menu and the
    /// overlay behind it is what grey means.
    /// </para>
    ///
    /// <para>
    /// <b>Legibility is the constraint the plate exists for.</b> The lab is flat-shaded and
    /// low-contrast, and the same HUD is read against a pale wall and a near-black floor within one
    /// mouse movement — so text with no backing has no contrast guarantee at all. Every string the HUD
    /// draws therefore sits on <see cref="Plate"/>, whose alpha is chosen so that
    /// <see cref="SignalPalette.Ink"/> clears a 4.5:1 contrast ratio even composited over a white wall
    /// (see the constant). Measured with <see cref="SignalPalette.Luminance"/>, which is the ranking
    /// this project already uses rather than a photometric one.
    /// </para>
    ///
    /// <para>
    /// <b>No verdict colour lives here (hard rule 4).</b> There is deliberately no "bad" colour on this
    /// type: red, amber and green are the results table's, and a HUD is exactly where they get spent on
    /// an overdrawn balance. What the HUD has instead is <see cref="UiPalette.Warn"/> — the warm-family
    /// orange the menus already use for a destructive button — and, more importantly, the non-colour
    /// channels §2.2 asks for: a word, a sign, a weight, a position.
    /// </para>
    /// </summary>
    public static class HudStyle
    {
        // -- Type scale ------------------------------------------------------------------------------
        //
        // A 1.25 modular scale anchored on 15: 12, 15, 19, 23, 29. Five steps, because four cannot
        // separate "the clock" from "a number in the header" from "a caption naming it", and six is
        // wide enough for two widgets to pick different sizes for the same job. Authored against the
        // panel's 1920x1080 reference resolution, so these are the same apparent size on every display.
        //
        // The old HUD ran 11-15 across the whole overlay, which is a menu's scale used for something
        // read at a glance while moving.

        /// <summary>Names a number. Never load-bearing on its own.</summary>
        public const float CaptionSize = 12f;

        /// <summary>Anything the player reads as a sentence: the prompt, a toast, the hands line.</summary>
        public const float BodySize = 15f;

        /// <summary>A header number.</summary>
        public const float MetricSize = 19f;

        /// <summary>A title on a full-screen overlay.</summary>
        public const float HeadingSize = 23f;

        /// <summary>The clock, and nothing else. There is one of these on screen.</summary>
        public const float HeroSize = 29f;

        // -- Spacing ---------------------------------------------------------------------------------
        //
        // Multiples of four, and only these. Every margin, padding and offset in the HUD is one of
        // them, which is what stops a screen built over six months from having eleven different gaps.

        public const float S1 = 4f;
        public const float S2 = 8f;
        public const float S3 = 12f;
        public const float S4 = 16f;
        public const float S6 = 24f;
        public const float S8 = 32f;

        /// <summary>
        /// The margin every edge-anchored thing on the HUD aligns to. One number: the header's padding,
        /// the controls strip, the debug readout and both corner cards all sit on it, so the overlay
        /// reads as one frame rather than as five widgets that each chose a corner.
        /// </summary>
        public const float Inset = S6;

        /// <summary>Height of the header's metrics row.</summary>
        public const float HeaderHeight = 64f;

        /// <summary>Height of the band under it that appears only when the shift is over.</summary>
        public const float HeaderAlertHeight = 28f;

        /// <summary>
        /// Where anything anchored to the top of the screen starts. Clears the header <i>and</i> its
        /// alert band, so the standing-orders card does not end up under the one line telling the
        /// player the shift has ended.
        /// </summary>
        public const float ContentTop = HeaderHeight + HeaderAlertHeight + S3;

        /// <summary>Matches the radius the terminal and both cards already use.</summary>
        public const float Radius = 3f;

        // -- Surfaces --------------------------------------------------------------------------------

        /// <summary>
        /// The backing under every HUD string.
        /// <para>
        /// Alpha 0.94 is not a taste call. Composited over the brightest thing the lab can put behind
        /// it — a white wall, luminance 1.0 — this resolves to luminance 0.143, against which
        /// <see cref="SignalPalette.Ink"/> (0.889) sits at 4.9:1 and <see cref="SignalPalette.Dim"/>
        /// (0.597) at 3.4:1. Ink therefore clears 4.5:1 in the worst case the room can produce, and Dim
        /// clears the 3:1 that its role — captions naming a value that is itself drawn in Ink — needs.
        /// Drop the alpha and the first of those stops being true.
        /// </para>
        /// </summary>
        public static readonly Color Plate = new(0.075f, 0.085f, 0.095f, 0.94f);

        /// <summary>A well cut into a <see cref="Plate"/>: the name strip under an inventory icon.</summary>
        public static readonly Color PlateSunken = new(0.04f, 0.05f, 0.055f, 0.94f);

        /// <summary>Hairline rules and resting slot edges. Tinted light so it survives on the plate.</summary>
        public static readonly Color Line = new(1f, 1f, 1f, 0.10f);

        // -- Ink -------------------------------------------------------------------------------------
        //
        // Aliases, never copies, for the reason UiPalette gives: two hand-tuned greys that started
        // identical do not stay identical.

        /// <summary>What the player is meant to read.</summary>
        public static readonly Color Ink = SignalPalette.Ink;

        /// <summary>Captions, and a value that is currently nothing to act on.</summary>
        public static readonly Color Dim = SignalPalette.Dim;

        /// <summary>Present but never competing to be read.</summary>
        public static readonly Color Faint = SignalPalette.Off;

        /// <summary>Selection and progress. The HUD's only saturated colour in the ordinary case.</summary>
        public static readonly Color Accent = SignalPalette.Accent;

        /// <summary>
        /// "This one bites", without touching the signal set (hard rule 4). The same oxidised orange
        /// <c>UiKit.DangerButton</c> uses, and it is never the only channel: everything drawn in it
        /// also changes its word, its weight or its sign.
        /// </summary>
        public static readonly Color Warn = UiPalette.Warn;

        // -- Builders --------------------------------------------------------------------------------

        /// <summary>
        /// The one place a HUD label is made. Ignores picking, because nothing on this overlay is
        /// clickable and a label that eats a pointer event is a bug nobody finds until the terminal
        /// stops responding under it.
        /// </summary>
        public static Label Text(string text, float size, Color colour, bool bold = false)
        {
            var label = new Label(text ?? string.Empty)
            {
                pickingMode = PickingMode.Ignore
            };
            label.style.fontSize = size;
            label.style.color = new StyleColor(colour);
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.marginTop = 0;
            label.style.marginBottom = 0;
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            return label;
        }

        /// <summary>
        /// A caption naming the value under it. Tracked, because small uppercase text set solid reads
        /// as a block rather than as words.
        /// </summary>
        public static Label Caption(string text)
        {
            var label = Text(text, CaptionSize, Dim);
            label.style.letterSpacing = 1f;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            return label;
        }

        /// <summary>A vertical stack that owns nothing but its direction.</summary>
        public static VisualElement Column()
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.style.flexDirection = FlexDirection.Column;
            return element;
        }

        /// <summary>A horizontal, vertically centred stack.</summary>
        public static VisualElement Row()
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.Center;
            return element;
        }

        /// <summary>Eats the slack in a <see cref="Row"/>. This is what absorbs a longer translation.</summary>
        public static VisualElement Spacer()
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.style.flexGrow = 1f;
            element.style.flexShrink = 1f;
            return element;
        }

        /// <summary>A hairline separating two groups in the header.</summary>
        public static VisualElement Rule()
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.style.width = 1;
            element.style.height = 28;
            element.style.flexShrink = 0f;
            element.style.marginLeft = S6;
            element.style.marginRight = S6;
            element.style.backgroundColor = new StyleColor(Line);
            return element;
        }

        /// <summary>
        /// Wraps a floating line of text in its own backing, so it survives a pale wall. Auto-width and
        /// self-centring, so the plate is the length of the sentence rather than a fixed box a
        /// translation would overflow.
        /// </summary>
        public static VisualElement Pill(Label label, float horizontalPadding = S3,
                                         float verticalPadding = S1)
        {
            var pill = new VisualElement { pickingMode = PickingMode.Ignore };
            pill.style.flexDirection = FlexDirection.Row;
            pill.style.alignSelf = Align.Center;
            pill.style.maxWidth = Length.Percent(60f);
            pill.style.paddingLeft = horizontalPadding;
            pill.style.paddingRight = horizontalPadding;
            pill.style.paddingTop = verticalPadding;
            pill.style.paddingBottom = verticalPadding;
            pill.style.backgroundColor = new StyleColor(Plate);
            Round(pill);
            pill.Add(label);
            return pill;
        }

        /// <summary>Rounds all four corners at the HUD's one radius.</summary>
        public static void Round(VisualElement element, float radius = Radius) =>
            UiKit.Round(element, radius);

        /// <summary>Sets all four border widths at once.</summary>
        public static void Border(VisualElement element, float width, Color colour)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;

            var style = new StyleColor(colour);
            element.style.borderTopColor = style;
            element.style.borderBottomColor = style;
            element.style.borderLeftColor = style;
            element.style.borderRightColor = style;
        }

        /// <summary>
        /// One line, cut with an ellipsis rather than wrapped or clipped.
        /// <para>
        /// Used where the box is fixed and the text is not — an item name in an 88 px inventory slot,
        /// where German runs roughly 30% longer than the English the box was drawn for. The full name
        /// is never only here: whatever is in the selected slot is also spelled out in full on the
        /// hands line, so the cut costs the player nothing.
        /// </para>
        /// </summary>
        public static void Truncate(Label label)
        {
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
        }
    }
}

