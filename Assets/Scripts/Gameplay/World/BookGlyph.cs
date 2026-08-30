namespace Residue.Gameplay.World
{
    /// <summary>
    /// One character of <see cref="BookFont"/>: how wide it is, and which cells are inked.
    ///
    /// <para>
    /// <b>Width is per glyph, unlike the instrument font.</b> <see cref="PixelFont"/> is monospaced
    /// because an instrument readout is a table — a column of labelled fields has to line up, and a
    /// proportional font would make every row start somewhere slightly different. A book is the
    /// opposite: it is continuous prose, nothing lines up between lines, and monospacing is the
    /// single most obvious reason text reads as a terminal rather than as print. An "i" three cells
    /// wide surrounded by the same air as an "m" is the look this is getting rid of.
    /// </para>
    /// </summary>
    public readonly struct BookGlyph
    {
        /// <summary>
        /// Inked columns, 1..<see cref="BookFont.CellWidth"/>. The advance is this plus
        /// <see cref="BookFont.Tracking"/>.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// <see cref="BookFont.CellWidth"/> * <see cref="BookFont.Height"/> characters of '0' and
        /// '1', row-major from the top. Columns past <see cref="Width"/> are ignored rather than
        /// required to be blank, so a glyph can be narrowed without re-typing its rows.
        /// </summary>
        public string Rows { get; }

        public BookGlyph(int width, string rows)
        {
            Width = width;
            Rows = rows;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Rows);
    }
}
