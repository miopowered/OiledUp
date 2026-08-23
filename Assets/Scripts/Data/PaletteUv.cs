using UnityEngine;

namespace Residue.Data
{
    /// <summary>
    /// Addresses the §2.2 palette atlas by row and column instead of by raw UV.
    /// <para>
    /// Meshes in this project carry no textures — colour comes entirely from which texel of the
    /// 16x16 palette each face samples. Anything that authors geometry (a procedural generator, a
    /// Blender export script, an agent building a prop in C#) should call
    /// <see cref="TexelCenter"/> rather than hand-computing UVs, so a palette change re-colours
    /// the whole project instead of half of it.
    /// </para>
    /// Row indices match <c>PaletteBootstrap.Rows</c>. Keep them in sync.
    /// </summary>
    public static class PaletteUv
    {
        public const int Size = 16;

        /// <summary>Hue families, one per atlas row.</summary>
        public enum Family
        {
            NeutralCold = 0,
            NeutralWarm = 1,
            Oxide = 2,
            Coolant = 3,

            /// <summary>
            /// Verdict and alarm state ONLY. Never decorate with this row — the whole point is that
            /// a red light in the player's peripheral vision is unambiguous.
            /// </summary>
            Signal = 4,

            Steel = 5,
            Brass = 6,
            DeepBlue = 7,
            Sump = 8,
            Solvent = 9
        }

        /// <summary>Value steps within a family, darkest to lightest.</summary>
        public const int Darkest = 0;
        public const int Dark = 4;
        public const int Mid = 8;
        public const int Light = 11;
        public const int Lightest = 15;

        /// <summary>
        /// UV of the centre of one palette texel. Sampling the centre matters: the texture is
        /// point-filtered and clamped, but a UV on a texel boundary can still land on the wrong
        /// side after rasterisation.
        /// </summary>
        public static Vector2 TexelCenter(int row, int column)
        {
            row = Mathf.Clamp(row, 0, Size - 1);
            column = Mathf.Clamp(column, 0, Size - 1);

            // Row 0 is written to the TOP of the atlas, so v counts down from 1.
            return new Vector2(
                (column + 0.5f) / Size,
                (Size - row - 0.5f) / Size);
        }

        public static Vector2 TexelCenter(Family family, int column) => TexelCenter((int)family, column);

        /// <summary>The three verdict colours. Columns within each band go dark to bright.</summary>
        public static class Signal
        {
            public static Vector2 Critical(int step = 2) => TexelCenter(Family.Signal, Mathf.Clamp(step, 0, 3));
            public static Vector2 Caution(int step = 2) => TexelCenter(Family.Signal, 4 + Mathf.Clamp(step, 0, 3));
            public static Vector2 Normal(int step = 2) => TexelCenter(Family.Signal, 8 + Mathf.Clamp(step, 0, 3));
            public static Vector2 Off(int step = 1) => TexelCenter(Family.Signal, 12 + Mathf.Clamp(step, 0, 3));

            public static Vector2 For(ReadingSeverity severity) => severity switch
            {
                ReadingSeverity.Critical => Critical(),
                ReadingSeverity.Caution => Caution(),
                _ => Normal()
            };

            public static Vector2 For(Verdict verdict) => verdict switch
            {
                Verdict.Critical => Critical(),
                Verdict.Monitor => Caution(),
                _ => Normal()
            };
        }
    }
}
