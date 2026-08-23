using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The three verdict colours, matching palette row 4 (§2.2) so the screen and the world agree.
    /// <para>
    /// These are the <b>only</b> place red, amber and green are allowed. If red only ever means
    /// critical, a player glancing at a results table reads it before they read it. Using them for
    /// chrome anywhere else spends that for nothing.
    /// </para>
    /// </summary>
    public static class SignalPalette
    {
        public static readonly Color Critical = new(1.00f, 0.22f, 0.18f);
        public static readonly Color Caution = new(1.00f, 0.70f, 0.12f);
        public static readonly Color Normal = new(0.20f, 0.80f, 0.38f);
        public static readonly Color Off = new(0.34f, 0.36f, 0.38f);

        // Neutral chrome. Everything that is not a verdict lives here.
        public static readonly Color Ink = new(0.88f, 0.89f, 0.90f);
        public static readonly Color Dim = new(0.58f, 0.60f, 0.62f);
        public static readonly Color Panel = new(0.11f, 0.12f, 0.13f, 0.96f);
        public static readonly Color PanelSoft = new(0.16f, 0.17f, 0.19f, 0.96f);
        public static readonly Color Accent = new(0.32f, 0.62f, 0.70f);

        public static Color For(ReadingSeverity severity) => severity switch
        {
            ReadingSeverity.Critical => Critical,
            ReadingSeverity.Caution => Caution,
            _ => Normal
        };

        public static Color For(Verdict verdict) => verdict switch
        {
            Verdict.Critical => Critical,
            Verdict.Monitor => Caution,
            _ => Normal
        };

        public static string Label(ReadingSeverity severity) => severity switch
        {
            ReadingSeverity.Critical => "CRITICAL",
            ReadingSeverity.Caution => "CAUTION",
            _ => "NORMAL"
        };
    }
}
