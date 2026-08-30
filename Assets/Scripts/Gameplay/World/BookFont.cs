using System.Collections.Generic;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The typeface the reference manuals are printed in — proportional, with real lowercase and
    /// descenders.
    ///
    /// <para>
    /// <b>Why the book does not share <see cref="PixelFont"/>.</b> That font is 3x5, monospaced and
    /// uppercase-only: <c>PixelFont.Glyph</c> folds every lowercase letter to a capital. On an
    /// instrument readout all three of those are correct — the panel is a period CRT, the values sit
    /// in columns that have to line up, and shouting a four-character caption is what such a panel
    /// does. On a book they are three separate reasons the text is hard to read, and the worst is the
    /// capitals: continuous prose in all caps loses the word-shape that fluent reading depends on,
    /// which is why no book has ever been set that way. A manual is printed paper and gets a printed
    /// face.
    /// </para>
    ///
    /// <para>
    /// <b>The cell.</b> Five columns by nine rows, baseline at row <see cref="Baseline"/>. Capitals
    /// and ascenders occupy rows 0-6, x-height sits at rows 2-6, and rows 7-8 are descender space so
    /// g, j, p, q and y hang below the line properly instead of being squashed onto it. That is what
    /// makes a line of this look like type rather than like a dot-matrix label.
    /// </para>
    ///
    /// <para>
    /// Rendering is anti-aliased by supersampling — see <c>BookPage</c> — so the glyph table stays a
    /// hard bitmap and the smoothing is a property of how it is drawn. Blurring the bitmap itself
    /// would be a worse version of the same idea, because it would smooth the letterforms without
    /// smoothing the edges the eye actually follows.
    /// </para>
    /// </summary>
    public static class BookFont
    {
        /// <summary>Widest a glyph may be. The cell every <see cref="BookGlyph.Rows"/> is written in.</summary>
        public const int CellWidth = 5;

        /// <summary>Rows per cell, descender space included.</summary>
        public const int Height = 9;

        /// <summary>
        /// Rows 0..6 sit on the line; 7 and 8 hang below it. A glyph with nothing in rows 7-8 is an
        /// ordinary letter, and one with ink there is a descender — the table does not have to say
        /// which, the rows do.
        /// </summary>
        public const int Baseline = 7;

        /// <summary>Blank columns between one glyph and the next.</summary>
        public const int Tracking = 1;

        /// <summary>
        /// Blank rows between one baseline and the next, before scaling. Generous next to
        /// <see cref="PixelText.LineGap"/>, because a book is read in paragraphs rather than glanced
        /// at a line at a time, and leading is most of what makes a block of text approachable.
        /// </summary>
        public const int Leading = 4;

        /// <summary>Baseline to baseline, in texture pixels at the given scale.</summary>
        public static int LineHeight(int scale) => (Height + Leading) * scale;

        /// <summary>
        /// The glyph for a character, or <see cref="Missing"/> if the face has none.
        ///
        /// <para>
        /// Unlike <see cref="PixelFont.Glyph"/> this does <b>not</b> fold case — having both is the
        /// entire point of the face.
        /// </para>
        ///
        /// <para>
        /// <b>There is no transliteration fallback here, and there cannot be one.</b> A character
        /// this table lacks draws as <see cref="Missing"/>, which is a blank — a hole in the word,
        /// not an AE. <see cref="PixelFont.Transliterate"/> works because it rewrites a whole
        /// <i>string</i>, and one letter becoming two is not something a method returning a single
        /// glyph can express. So a language whose letters are missing has to be handled a level up,
        /// by transliterating before layout, exactly as the instrument screens do — and until then
        /// the answer is to draw the letters. German is drawn, umlauts and eszett included.
        /// </para>
        /// </summary>
        public static BookGlyph Glyph(char c) =>
            Glyphs.TryGetValue(c, out var glyph) ? glyph : Missing;

        public static bool Has(char c) => Glyphs.ContainsKey(c);

        /// <summary>A blank of average width, so unknown characters space rather than collapse.</summary>
        public static readonly BookGlyph Missing = new(3, new string('0', CellWidth * Height));

        /// <summary>
        /// Painted width of a string, excluding the trailing tracking. Proportional, so this is the
        /// only correct way to measure — a caller multiplying a character count by a fixed advance
        /// will be wrong by a different amount for every string.
        /// </summary>
        public static int Measure(string text, int scale)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int width = 0;
            foreach (char c in text) width += Glyph(c).Width + Tracking;

            return (width - Tracking) * scale;
        }

        /// <summary>
        /// The face. Row-major, five columns per row, nine rows — read the '1's as ink and it is a
        /// picture of the letter, which is how these are meant to be edited.
        /// </summary>
        private static readonly Dictionary<char, BookGlyph> Glyphs = new()
        {
            // Worked examples of the encoding. The rest of the face is filled in beside these.
            //
            //  space: no ink, three columns of air.
            [' '] = new(3, "000000000000000000000000000000000000000000000"),

            //  A: cap, rows 0-6, nothing below the baseline.
            //     .###.
            //     #...#
            //     #...#
            //     #####
            //     #...#
            //     #...#
            //     #...#
            //     .....
            //     .....
            ['A'] = new(5, "01110" + "10001" + "10001" + "11111" + "10001" + "10001" + "10001" +
                           "00000" + "00000"),

            //  a: x-height only, rows 2-6.
            ['a'] = new(5, "00000" + "00000" + "01110" + "00001" + "01111" + "10001" + "01111" +
                           "00000" + "00000"),

            //  g: the descender case — rows 7-8 carry the tail below the line.
            ['g'] = new(5, "00000" + "00000" + "01111" + "10001" + "10001" + "01111" + "00001" +
                           "00001" + "01110"),

            //  i: narrow, and the reason widths are per glyph.
            ['i'] = new(1, "00000" + "10000" + "00000" + "10000" + "10000" + "10000" + "10000" +
                           "00000" + "00000"),

            ['.'] = new(1, "00000" + "00000" + "00000" + "00000" + "00000" + "00000" + "10000" +
                           "00000" + "00000"),

            // -- Capitals ----------------------------------------------------------------------
            //
            // Rows 0-6, five columns wide except I and J, which are narrow because they are narrow
            // in every face that was ever cut. Round caps (C G O Q S) chamfer their corners rather
            // than filling them, which is the only way five columns reads as a curve.

            ['B'] = new(5, "11110" + "10001" + "10001" + "11110" + "10001" + "10001" + "11110" +
                           "00000" + "00000"),

            ['C'] = new(5, "01111" + "10000" + "10000" + "10000" + "10000" + "10000" + "01111" +
                           "00000" + "00000"),

            ['D'] = new(5, "11110" + "10001" + "10001" + "10001" + "10001" + "10001" + "11110" +
                           "00000" + "00000"),

            ['E'] = new(5, "11111" + "10000" + "10000" + "11110" + "10000" + "10000" + "11111" +
                           "00000" + "00000"),

            ['F'] = new(5, "11111" + "10000" + "10000" + "11110" + "10000" + "10000" + "10000" +
                           "00000" + "00000"),

            //  G: the crossbar is what stops it reading as C at a glance.
            //     .####
            //     #....
            //     #....
            //     #..##
            //     #...#
            //     #...#
            //     .####
            ['G'] = new(5, "01111" + "10000" + "10000" + "10011" + "10001" + "10001" + "01111" +
                           "00000" + "00000"),

            ['H'] = new(5, "10001" + "10001" + "10001" + "11111" + "10001" + "10001" + "10001" +
                           "00000" + "00000"),

            ['I'] = new(3, "11100" + "01000" + "01000" + "01000" + "01000" + "01000" + "11100" +
                           "00000" + "00000"),

            ['J'] = new(4, "00110" + "00010" + "00010" + "00010" + "10010" + "10010" + "01100" +
                           "00000" + "00000"),

            //  K: one junction row, not two. Two would be a two-pixel stem.
            //     #...#
            //     #..#.
            //     #.#..
            //     ##...
            //     #.#..
            //     #..#.
            //     #...#
            ['K'] = new(5, "10001" + "10010" + "10100" + "11000" + "10100" + "10010" + "10001" +
                           "00000" + "00000"),

            ['L'] = new(5, "10000" + "10000" + "10000" + "10000" + "10000" + "10000" + "11111" +
                           "00000" + "00000"),

            //  M: the vertex stops halfway down, or it is a W upside down.
            //     #...#
            //     ##.##
            //     #.#.#
            //     #.#.#
            //     #...#
            //     #...#
            //     #...#
            ['M'] = new(5, "10001" + "11011" + "10101" + "10101" + "10001" + "10001" + "10001" +
                           "00000" + "00000"),

            //  N: the diagonal walks col 1 -> 2 -> 3, so it never reads as H.
            //     #...#
            //     ##..#
            //     #.#.#
            //     #.#.#
            //     #..##
            //     #...#
            //     #...#
            ['N'] = new(5, "10001" + "11001" + "10101" + "10101" + "10011" + "10001" + "10001" +
                           "00000" + "00000"),

            ['O'] = new(5, "01110" + "10001" + "10001" + "10001" + "10001" + "10001" + "01110" +
                           "00000" + "00000"),

            ['P'] = new(5, "11110" + "10001" + "10001" + "11110" + "10000" + "10000" + "10000" +
                           "00000" + "00000"),

            //  Q: the tail leaves the bowl rather than crossing it, so the counter stays open.
            //     .###.
            //     #...#
            //     #...#
            //     #...#
            //     #.#.#
            //     #..#.
            //     .##.#
            ['Q'] = new(5, "01110" + "10001" + "10001" + "10001" + "10101" + "10010" + "01101" +
                           "00000" + "00000"),

            //  R: the leg splays. A straight one is a P with a stroke stuck on.
            //     ####.
            //     #...#
            //     #...#
            //     ####.
            //     #.#..
            //     #..#.
            //     #...#
            ['R'] = new(5, "11110" + "10001" + "10001" + "11110" + "10100" + "10010" + "10001" +
                           "00000" + "00000"),

            //  S: open ends top-left and bottom-right. Compare '5', which has a flat top bar.
            //     .####
            //     #....
            //     #....
            //     .###.
            //     ....#
            //     ....#
            //     ####.
            ['S'] = new(5, "01111" + "10000" + "10000" + "01110" + "00001" + "00001" + "11110" +
                           "00000" + "00000"),

            ['T'] = new(5, "11111" + "00100" + "00100" + "00100" + "00100" + "00100" + "00100" +
                           "00000" + "00000"),

            ['U'] = new(5, "10001" + "10001" + "10001" + "10001" + "10001" + "10001" + "01110" +
                           "00000" + "00000"),

            ['V'] = new(5, "10001" + "10001" + "10001" + "10001" + "10001" + "01010" + "00100" +
                           "00000" + "00000"),

            //  W: three stems that meet at the feet. The middle one rises, unlike M's.
            //     #...#
            //     #...#
            //     #...#
            //     #.#.#
            //     #.#.#
            //     #.#.#
            //     .#.#.
            ['W'] = new(5, "10001" + "10001" + "10001" + "10101" + "10101" + "10101" + "01010" +
                           "00000" + "00000"),

            ['X'] = new(5, "10001" + "10001" + "01010" + "00100" + "01010" + "10001" + "10001" +
                           "00000" + "00000"),

            ['Y'] = new(5, "10001" + "10001" + "01010" + "00100" + "00100" + "00100" + "00100" +
                           "00000" + "00000"),

            ['Z'] = new(5, "11111" + "00001" + "00010" + "00100" + "01000" + "10000" + "11111" +
                           "00000" + "00000"),

            // -- Lowercase ---------------------------------------------------------------------
            //
            // x-height is rows 2-6 for every one of them without exception — an x-height that
            // wanders by a pixel is the defect the eye catches first in a block of prose.
            // Ascenders (b d f h k l t) start at row 0; descenders (g j p q y) ink rows 7-8.

            ['b'] = new(5, "10000" + "10000" + "11110" + "10001" + "10001" + "10001" + "11110" +
                           "00000" + "00000"),

            ['c'] = new(5, "00000" + "00000" + "01111" + "10000" + "10000" + "10000" + "01111" +
                           "00000" + "00000"),

            ['d'] = new(5, "00001" + "00001" + "01111" + "10001" + "10001" + "10001" + "01111" +
                           "00000" + "00000"),

            //  e: the bar sits on the middle row and the aperture opens bottom-right.
            //     .###.
            //     #...#
            //     #####
            //     #....
            //     .###.
            ['e'] = new(5, "00000" + "00000" + "01110" + "10001" + "11111" + "10000" + "01110" +
                           "00000" + "00000"),

            //  f: hooks right at the top, crossbar on the x-height line.
            //     ..##
            //     .#..
            //     ####
            //     .#..
            //     .#..
            //     .#..
            //     .#..
            ['f'] = new(4, "00110" + "01000" + "11110" + "01000" + "01000" + "01000" + "01000" +
                           "00000" + "00000"),

            ['h'] = new(5, "10000" + "10000" + "11110" + "10001" + "10001" + "10001" + "10001" +
                           "00000" + "00000"),

            //  j: dotted like i, and the only other glyph whose tail leaves the cell to the left.
            //     ..#
            //     ...
            //     ..#
            //     ..#
            //     ..#
            //     ..#
            //     ..#
            //     ..#   <- row 7, already below the line
            //     ##.
            ['j'] = new(3, "00000" + "00100" + "00000" + "00100" + "00100" + "00100" + "00100" +
                           "00100" + "11000"),

            ['k'] = new(4, "10000" + "10000" + "10010" + "10100" + "11000" + "10100" + "10010" +
                           "00000" + "00000"),

            //  l: given a foot, or it is indistinguishable from I and from 1.
            //     #.
            //     #.
            //     #.
            //     #.
            //     #.
            //     #.
            //     ##
            ['l'] = new(2, "10000" + "10000" + "10000" + "10000" + "10000" + "10000" + "11000" +
                           "00000" + "00000"),

            //  m: three stems under one shoulder. Set beside 'rn' — r is 4 wide and breaks at the
            //  top — the pair does not collapse into this.
            //     #####
            //     #.#.#
            //     #.#.#
            //     #.#.#
            //     #.#.#
            ['m'] = new(5, "00000" + "00000" + "11111" + "10101" + "10101" + "10101" + "10101" +
                           "00000" + "00000"),

            ['n'] = new(5, "00000" + "00000" + "11110" + "10001" + "10001" + "10001" + "10001" +
                           "00000" + "00000"),

            ['o'] = new(5, "00000" + "00000" + "01110" + "10001" + "10001" + "10001" + "01110" +
                           "00000" + "00000"),

            //  p: bowl on the line, stem hanging into rows 7-8.
            //     ####.
            //     #...#
            //     #...#
            //     #...#
            //     ####.
            //     #....
            //     #....
            ['p'] = new(5, "00000" + "00000" + "11110" + "10001" + "10001" + "10001" + "11110" +
                           "10000" + "10000"),

            ['q'] = new(5, "00000" + "00000" + "01111" + "10001" + "10001" + "10001" + "01111" +
                           "00001" + "00001"),

            //  r: shoulder that stops, which is the whole letter.
            //     #.##
            //     ##..
            //     #...
            //     #...
            //     #...
            ['r'] = new(4, "00000" + "00000" + "10110" + "11000" + "10000" + "10000" + "10000" +
                           "00000" + "00000"),

            //  s: narrower than the round letters, as an s is.
            //     .###
            //     #...
            //     .##.
            //     ...#
            //     ###.
            ['s'] = new(4, "00000" + "00000" + "01110" + "10000" + "01100" + "00010" + "11100" +
                           "00000" + "00000"),

            //  t: crossbar on the x-height line, foot curling right.
            //     .#..
            //     .#..
            //     ####
            //     .#..
            //     .#..
            //     .#..
            //     .##.
            ['t'] = new(4, "01000" + "01000" + "11110" + "01000" + "01000" + "01000" + "01100" +
                           "00000" + "00000"),

            ['u'] = new(5, "00000" + "00000" + "10001" + "10001" + "10001" + "10001" + "01111" +
                           "00000" + "00000"),

            ['v'] = new(5, "00000" + "00000" + "10001" + "10001" + "10001" + "01010" + "00100" +
                           "00000" + "00000"),

            ['w'] = new(5, "00000" + "00000" + "10001" + "10001" + "10101" + "10101" + "01010" +
                           "00000" + "00000"),

            ['x'] = new(5, "00000" + "00000" + "10001" + "01010" + "00100" + "01010" + "10001" +
                           "00000" + "00000"),

            //  y: built like g on purpose — same join, same tail, so the pair look related.
            //     #...#
            //     #...#
            //     #...#
            //     .####
            //     ....#
            //     ....#   <- row 7
            //     .###.   <- row 8
            ['y'] = new(5, "00000" + "00000" + "10001" + "10001" + "10001" + "01111" + "00001" +
                           "00001" + "01110"),

            ['z'] = new(4, "00000" + "00000" + "11110" + "00010" + "00100" + "01000" + "11110" +
                           "00000" + "00000"),

            // -- Figures -----------------------------------------------------------------------
            //
            // Cap height, and all five wide except 1, so a column of them in a threshold table
            // stays a column. 1 is narrow because a five-wide 1 leaves a hole in "Grade 1".

            //  0: slashed, because the threshold tables print both this and O.
            //     .###.
            //     #...#
            //     #..##
            //     #.#.#
            //     ##..#
            //     #...#
            //     .###.
            ['0'] = new(5, "01110" + "10001" + "10011" + "10101" + "11001" + "10001" + "01110" +
                           "00000" + "00000"),

            //  1: flag and foot, so it is not l and not I.
            //     .#..
            //     ##..
            //     .#..
            //     .#..
            //     .#..
            //     .#..
            //     ###.
            ['1'] = new(4, "01000" + "11000" + "01000" + "01000" + "01000" + "01000" + "11100" +
                           "00000" + "00000"),

            ['2'] = new(5, "01110" + "10001" + "00001" + "00010" + "00100" + "01000" + "11111" +
                           "00000" + "00000"),

            ['3'] = new(5, "11110" + "00001" + "00001" + "01110" + "00001" + "10001" + "01110" +
                           "00000" + "00000"),

            //  4: closed counter, or at this size it reads as a badly kerned 'y'.
            //     ...#.
            //     ..##.
            //     .#.#.
            //     #..#.
            //     #####
            //     ...#.
            //     ...#.
            ['4'] = new(5, "00010" + "00110" + "01010" + "10010" + "11111" + "00010" + "00010" +
                           "00000" + "00000"),

            //  5: flat top bar. This is the only thing separating it from S, so it stays flat.
            //     #####
            //     #....
            //     ####.
            //     ....#
            //     ....#
            //     #...#
            //     .###.
            ['5'] = new(5, "11111" + "10000" + "11110" + "00001" + "00001" + "10001" + "01110" +
                           "00000" + "00000"),

            ['6'] = new(5, "00110" + "01000" + "10000" + "11110" + "10001" + "10001" + "01110" +
                           "00000" + "00000"),

            ['7'] = new(5, "11111" + "00001" + "00010" + "00100" + "01000" + "01000" + "01000" +
                           "00000" + "00000"),

            ['8'] = new(5, "01110" + "10001" + "10001" + "01110" + "10001" + "10001" + "01110" +
                           "00000" + "00000"),

            //  9: tail leaves the bowl to the left, mirroring 6.
            //     .###.
            //     #...#
            //     #...#
            //     .####
            //     ....#
            //     ...#.
            //     .##..
            ['9'] = new(5, "01110" + "10001" + "10001" + "01111" + "00001" + "00010" + "01100" +
                           "00000" + "00000"),

            // -- Points and marks ----------------------------------------------------------------
            //
            // The comma and the semicolon hang below the line, which is what tells them from the
            // full stop and the colon in a hurry.

            [','] = new(2, "00000" + "00000" + "00000" + "00000" + "00000" + "00000" + "01000" +
                           "10000" + "00000"),

            [':'] = new(1, "00000" + "00000" + "00000" + "10000" + "00000" + "00000" + "10000" +
                           "00000" + "00000"),

            [';'] = new(2, "00000" + "00000" + "00000" + "01000" + "00000" + "00000" + "01000" +
                           "10000" + "00000"),

            ['\''] = new(1, "10000" + "10000" + "00000" + "00000" + "00000" + "00000" + "00000" +
                            "00000" + "00000"),

            ['"'] = new(3, "10100" + "10100" + "00000" + "00000" + "00000" + "00000" + "00000" +
                           "00000" + "00000"),

            ['!'] = new(1, "10000" + "10000" + "10000" + "10000" + "10000" + "00000" + "10000" +
                           "00000" + "00000"),

            //  ?: the gap before the point is the letter, so it is a whole row.
            //     .##.
            //     #..#
            //     ...#
            //     ..#.
            //     .#..
            //     ....
            //     .#..
            ['?'] = new(4, "01100" + "10010" + "00010" + "00100" + "01000" + "00000" + "01000" +
                           "00000" + "00000"),

            // Three dashes of three lengths. The manuals set em dashes in prose and hyphens inside
            // words, and drawing both as one stroke would lose the distinction the copy relies on.
            ['-'] = new(3, "00000" + "00000" + "00000" + "00000" + "11100" + "00000" + "00000" +
                           "00000" + "00000"),

            ['–'] = new(4, "00000" + "00000" + "00000" + "00000" + "11110" + "00000" + "00000" +
                           "00000" + "00000"),

            ['—'] = new(5, "00000" + "00000" + "00000" + "00000" + "11111" + "00000" + "00000" +
                           "00000" + "00000"),

            // Brackets descend one row below the baseline, as cut brackets do.
            ['('] = new(2, "01000" + "10000" + "10000" + "10000" + "10000" + "10000" + "10000" +
                           "01000" + "00000"),

            [')'] = new(2, "10000" + "01000" + "01000" + "01000" + "01000" + "01000" + "01000" +
                           "10000" + "00000"),

            ['['] = new(2, "11000" + "10000" + "10000" + "10000" + "10000" + "10000" + "10000" +
                           "11000" + "00000"),

            [']'] = new(2, "11000" + "01000" + "01000" + "01000" + "01000" + "01000" + "01000" +
                           "11000" + "00000"),

            ['{'] = new(3, "01100" + "01000" + "01000" + "10000" + "01000" + "01000" + "01000" +
                           "01100" + "00000"),

            ['}'] = new(3, "11000" + "01000" + "01000" + "00100" + "01000" + "01000" + "01000" +
                           "11000" + "00000"),

            ['/'] = new(4, "00010" + "00010" + "00100" + "00100" + "01000" + "01000" + "10000" +
                           "00000" + "00000"),

            ['\\'] = new(4, "10000" + "10000" + "01000" + "01000" + "00100" + "00100" + "00010" +
                            "00000" + "00000"),

            //  %: the two counters are solid blocks. There is no room to hollow them, and a hollow
            //  one at this size is a dot with noise round it.
            //     ##...
            //     ##..#
            //     ...#.
            //     ..#..
            //     .#...
            //     #..##
            //     ...##
            ['%'] = new(5, "11000" + "11001" + "00010" + "00100" + "01000" + "10011" + "00011" +
                           "00000" + "00000"),

            // Maths sits on the x-height, not on the cap line, so "<= 0.5" reads as one row.
            ['+'] = new(5, "00000" + "00000" + "00100" + "00100" + "11111" + "00100" + "00100" +
                           "00000" + "00000"),

            ['='] = new(5, "00000" + "00000" + "00000" + "11111" + "00000" + "11111" + "00000" +
                           "00000" + "00000"),

            ['<'] = new(3, "00000" + "00000" + "00100" + "01000" + "10000" + "01000" + "00100" +
                           "00000" + "00000"),

            ['>'] = new(3, "00000" + "00000" + "10000" + "01000" + "00100" + "01000" + "10000" +
                           "00000" + "00000"),

            //  <=: the chevron rides one row up to make room for the rule under it.
            //     ..#
            //     .#.
            //     #..
            //     .#.
            //     ..#
            //     ###
            ['≤'] = new(3, "00000" + "00100" + "01000" + "10000" + "01000" + "00100" + "11100" +
                           "00000" + "00000"),

            ['≥'] = new(3, "00000" + "10000" + "01000" + "00100" + "01000" + "10000" + "11100" +
                           "00000" + "00000"),

            //  °: a ring, not a dot — a dot up there is an apostrophe.
            //     .#.
            //     #.#
            //     .#.
            ['°'] = new(3, "01000" + "10100" + "01000" + "00000" + "00000" + "00000" + "00000" +
                           "00000" + "00000"),

            //  £: the costs on the manual pages are all in this.
            //     ..##
            //     .#..
            //     .#..
            //     ###.
            //     .#..
            //     .#..
            //     ####
            ['£'] = new(4, "00110" + "01000" + "01000" + "11100" + "01000" + "01000" + "11110" +
                           "00000" + "00000"),

            //  §: two s-bowls stacked. The spec citations in the refusals print it.
            //     .##.
            //     #...
            //     .##.
            //     #..#
            //     .##.
            //     ...#
            //     .##.
            ['§'] = new(4, "01100" + "10000" + "01100" + "10010" + "01100" + "00010" + "01100" +
                           "00000" + "00000"),

            ['·'] = new(1, "00000" + "00000" + "00000" + "00000" + "10000" + "00000" + "00000" +
                           "00000" + "00000"),

            //  &: "Elements & Sources" is a chapter heading, so this one is read large.
            //     .##..
            //     #..#.
            //     #..#.
            //     .##..
            //     #.#.#
            //     #..#.
            //     .##.#
            ['&'] = new(5, "01100" + "10010" + "10010" + "01100" + "10101" + "10010" + "01101" +
                           "00000" + "00000"),

            ['#'] = new(5, "00000" + "00000" + "01010" + "11111" + "01010" + "11111" + "01010" +
                           "00000" + "00000"),

            ['*'] = new(5, "10101" + "01110" + "10101" + "00000" + "00000" + "00000" + "00000" +
                           "00000" + "00000"),

            ['_'] = new(5, "00000" + "00000" + "00000" + "00000" + "00000" + "00000" + "00000" +
                           "11111" + "00000"),

            ['@'] = new(5, "01110" + "10001" + "10111" + "10101" + "10110" + "10000" + "01110" +
                           "00000" + "00000"),

            // -- German ------------------------------------------------------------------------
            //
            // Lowercase is free: x-height starts at row 2, so row 0 is empty and the dots go there
            // with a clear row between. Capitals are not free — a seven-row cap fills the cell — so
            // the umlauted capitals are set one row shorter, rows 1-6, with the diaeresis on row 0.
            //
            // The dots sit at columns 0 and 4 on A and O and at columns 1 and 3 on U. That looks
            // like an inconsistency and is the opposite: the rule is that a dot never sits directly
            // over a stroke. U's stems are at columns 0 and 4, so dots there would extend them and
            // draw a plain U; A's apex and O's shoulder are at columns 1-3, so dots there would
            // merge into the top of the letter.

            ['ä'] = new(5, "01010" + "00000" + "01110" + "00001" + "01111" + "10001" + "01111" +
                           "00000" + "00000"),

            ['ö'] = new(5, "01010" + "00000" + "01110" + "10001" + "10001" + "10001" + "01110" +
                           "00000" + "00000"),

            ['ü'] = new(5, "01010" + "00000" + "10001" + "10001" + "10001" + "10001" + "01111" +
                           "00000" + "00000"),

            //  ß: tall left stem, two bowls, and the bottom right left open — that opening is the
            //  only thing between this and a B.
            //     .##.
            //     #..#
            //     #..#
            //     ###.
            //     #..#
            //     #..#
            //     #.#.
            ['ß'] = new(4, "01100" + "10010" + "10010" + "11100" + "10010" + "10010" + "10100" +
                           "00000" + "00000"),

            //  A-umlaut: pointed apex, so the dots clear it on both sides.
            //     #...#
            //     ..#..
            //     .#.#.
            //     #####
            //     #...#
            //     #...#
            //     #...#
            ['Ä'] = new(5, "10001" + "00100" + "01010" + "11111" + "10001" + "10001" + "10001" +
                           "00000" + "00000"),

            //  O-umlaut: six-row bowl, dots at the shoulders.
            //     #...#
            //     .###.
            //     #...#
            //     #...#
            //     #...#
            //     #...#
            //     .###.
            ['Ö'] = new(5, "10001" + "01110" + "10001" + "10001" + "10001" + "10001" + "01110" +
                           "00000" + "00000"),

            //  U-umlaut: dots inboard of the stems, which is the only placement that does not
            //  simply make the stems a row taller.
            //     .#.#.
            //     #...#
            //     #...#
            //     #...#
            //     #...#
            //     #...#
            //     .###.
            ['Ü'] = new(5, "01010" + "10001" + "10001" + "10001" + "10001" + "10001" + "01110" +
                           "00000" + "00000")
        };

        /// <summary>Is this cell inked? Columns past the glyph's own width read as blank.</summary>
        public static bool IsOn(BookGlyph glyph, int x, int y)
        {
            if (glyph.IsEmpty || x < 0 || x >= glyph.Width || y < 0 || y >= Height) return false;

            return glyph.Rows[y * CellWidth + x] == '1';
        }
    }
}
