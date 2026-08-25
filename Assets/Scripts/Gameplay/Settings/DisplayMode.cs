using System;
using UnityEngine;

namespace Residue.Gameplay.Settings
{
    /// <summary>
    /// One selectable screen configuration: size, refresh rate and window mode as a single value.
    /// <para>
    /// A struct rather than a bare <see cref="Resolution"/> because the window mode is inseparable
    /// from the resolution in the one case that matters — the failure mode this whole type exists to
    /// contain. A resolution or a refresh rate the monitor cannot display leaves the player staring
    /// at "signal out of range" with a game still running behind it and no way to click anything.
    /// That is why <see cref="GameSettings"/> splits applying a mode from committing one, and why a
    /// mode has to be comparable as a whole: reverting means restoring the size, the rate <i>and</i>
    /// the window mode that were known to work together.
    /// </para>
    /// <see cref="RefreshHz"/> of 0 means "no preference" — the platform picks. That is the honest
    /// value for a windowed mode, where the compositor owns the rate, and it is what a saved profile
    /// carrying no rate falls back to rather than guessing 60 and forcing a downgrade.
    /// </summary>
    public readonly struct DisplayMode : IEquatable<DisplayMode>
    {
        public readonly int Width;
        public readonly int Height;

        /// <summary>Whole Hz. 0 = let the platform choose.</summary>
        public readonly int RefreshHz;

        public readonly FullScreenMode Mode;

        public DisplayMode(int width, int height, int refreshHz, FullScreenMode mode)
        {
            Width = Mathf.Max(0, width);
            Height = Mathf.Max(0, height);
            RefreshHz = Mathf.Max(0, refreshHz);
            Mode = mode;
        }

        /// <summary>True once this names a real size. A default-constructed value does not.</summary>
        public bool IsValid => Width > 0 && Height > 0;

        public string Label => RefreshHz > 0
            ? $"{Width} x {Height} @ {RefreshHz} Hz"
            : $"{Width} x {Height}";

        /// <summary>
        /// Size and rate only, ignoring the window mode. The screen lists resolutions and window
        /// modes as two separate controls, so "which row is selected" must not stop matching the
        /// moment somebody toggles fullscreen.
        /// </summary>
        public bool SameResolution(DisplayMode other) =>
            Width == other.Width && Height == other.Height && RefreshHz == other.RefreshHz;

        /// <summary>This mode with a different window mode. Used when the two controls are moved independently.</summary>
        public DisplayMode WithMode(FullScreenMode mode) => new(Width, Height, RefreshHz, mode);

        public bool Equals(DisplayMode other) => SameResolution(other) && Mode == other.Mode;

        public override bool Equals(object obj) => obj is DisplayMode other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Width;
                hash = (hash * 397) ^ Height;
                hash = (hash * 397) ^ RefreshHz;
                hash = (hash * 397) ^ (int)Mode;
                return hash;
            }
        }

        public static bool operator ==(DisplayMode a, DisplayMode b) => a.Equals(b);

        public static bool operator !=(DisplayMode a, DisplayMode b) => !a.Equals(b);

        public override string ToString() => $"{Label} ({Mode})";
    }
}
