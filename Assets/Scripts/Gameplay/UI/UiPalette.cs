using Residue.Gameplay.World;
using UnityEngine;

namespace Residue.Gameplay.UI
{
    /// <summary>
    /// The chrome palette: every colour a screen needs that is <b>not</b> a verdict and not a
    /// connection state.
    /// <para>
    /// Hard rule 4 reserves red, amber and green (§2.2 row 4) for verdict state. A UI kit is where
    /// that rule dies quietly, because every convention outside this project says a destructive
    /// button is red and a satisfied toggle is green. Neither exists here. <see cref="Warn"/> is an
    /// oxidised orange from the warm family (row 2) — close enough to read as "this one bites",
    /// far enough from <see cref="SignalPalette.Critical"/> in hue and saturation that a results
    /// table on the same monitor still reads at a glance. Affirmative state is carried by
    /// <see cref="Accent"/>, a cool teal, for the same reason.
    /// </para>
    /// <para>
    /// The neutrals are <i>aliases</i> of <see cref="SignalPalette"/> rather than copies. Two
    /// hand-tuned greys that started identical do not stay identical, and a HUD drawn in one and a
    /// pause menu drawn in the other is the exact drift this kit exists to prevent.
    /// </para>
    /// <para>
    /// Three palettes, three jobs, no overlap: verdict colour is <see cref="SignalPalette"/>'s,
    /// connection state is <c>Residue.Net.UI.ConnectPalette</c>'s, and chrome is this one's. This
    /// type deliberately does not reference <c>ConnectPalette</c> — it lives above us in
    /// <c>Residue.Net</c> and Gameplay cannot see it — so <see cref="Warn"/> restates that
    /// terracotta rather than sharing it. Keep them in step by eye if either moves.
    /// </para>
    /// </summary>
    public static class UiPalette
    {
        /// <summary>Behind a full-screen menu. Opaque, so nothing of the lab bleeds through.</summary>
        public static readonly Color Backdrop = new(0.05f, 0.06f, 0.07f, 1f);

        /// <summary>
        /// Behind a menu drawn <i>over</i> live play. Translucent on purpose: a pause menu that
        /// hides the room makes the player forget where they were standing.
        /// </summary>
        public static readonly Color Scrim = new(0.03f, 0.035f, 0.042f, 0.74f);

        /// <summary>Panel body.</summary>
        public static readonly Color Surface = SignalPalette.Panel;

        /// <summary>A control sitting on a panel — button faces, tab strips, list rows.</summary>
        public static readonly Color SurfaceRaised = SignalPalette.PanelSoft;

        /// <summary>A well cut into a panel — slider tracks, text entry, scroll gutters.</summary>
        public static readonly Color SurfaceSunken = new(0.06f, 0.065f, 0.075f, 0.96f);

        /// <summary>Dividers and resting control edges. Tinted light, not dark, so it survives on
        /// both <see cref="Surface"/> and <see cref="SurfaceRaised"/>.</summary>
        public static readonly Color Line = new(1f, 1f, 1f, 0.08f);

        /// <summary>Primary text.</summary>
        public static readonly Color Ink = SignalPalette.Ink;

        /// <summary>Secondary text: field labels, captions, anything read after the primary.</summary>
        public static readonly Color InkDim = SignalPalette.Dim;

        /// <summary>Hints and the identity line — present, but never competing to be read.</summary>
        public static readonly Color InkFaint = SignalPalette.Off;

        /// <summary>The primary action on a screen, and the "on" state of anything that has one.</summary>
        public static readonly Color Accent = SignalPalette.Accent;

        /// <summary>Hover and pressed state of <see cref="Accent"/>. Lifted rather than darkened,
        /// because a darkened teal on a near-black panel reads as disabled.</summary>
        public static readonly Color AccentSoft = new(0.42f, 0.73f, 0.81f);

        /// <summary>
        /// The focus ring. The brightest cool on any screen, because it is the only thing telling a
        /// gamepad or keyboard player where they are — if it loses a contrast argument to a panel
        /// behind it, navigation is unusable rather than ugly.
        /// </summary>
        public static readonly Color Focus = new(0.55f, 0.85f, 0.92f);

        /// <summary>
        /// Destructive actions — abandon the run, wipe the save. Oxidised orange, <b>not</b> red:
        /// see the type comment. If this ever needs to shout louder, make the copy blunter, not the
        /// hue redder.
        /// </summary>
        public static readonly Color Warn = new(0.80f, 0.45f, 0.28f);

        /// <summary>Text and faces of a control that cannot be used yet.</summary>
        public static readonly Color Disabled = new(0.36f, 0.38f, 0.40f);
    }
}
