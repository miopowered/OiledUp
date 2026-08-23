using Residue.Gameplay.World;
using UnityEngine;

namespace Residue.Net.UI
{
    /// <summary>
    /// Colours for connection state, chosen so that none of them is a signal colour.
    /// <para>
    /// Hard rule 4 reserves red, amber and green for verdict state, and a connect screen is exactly
    /// where that rule is most tempting to break — "connecting" wants amber and "failed" wants red
    /// by reflex. Spending the signal set on plumbing is what stops a player reading a results table
    /// at a glance later, so connection state is drawn entirely from the cool family (§2.2 row 3)
    /// with failures in oxidised orange from the warm family (row 2).
    /// </para>
    /// <see cref="Fault"/> is a muted terracotta, not a red: it sits well away from
    /// <see cref="SignalPalette.Critical"/> in both hue and saturation, and it never appears on the
    /// same screen as a verdict, so there is nothing for it to be confused with.
    /// </summary>
    public static class ConnectPalette
    {
        /// <summary>Something is in flight. The teal already used for chrome accents.</summary>
        public static readonly Color Working = SignalPalette.Accent;

        /// <summary>A session exists. A brighter cool, so "connected" reads without a green.</summary>
        public static readonly Color Live = new(0.45f, 0.80f, 0.86f);

        /// <summary>The join code itself, given the strongest cool on the screen.</summary>
        public static readonly Color Code = new(0.66f, 0.90f, 0.96f);

        /// <summary>A failure. Oxidised orange (row 2) — deliberately not red.</summary>
        public static readonly Color Fault = new(0.80f, 0.45f, 0.28f);

        /// <summary>Backdrop for the full-screen connect panel.</summary>
        public static readonly Color Backdrop = new(0.05f, 0.06f, 0.07f, 1f);
    }
}
