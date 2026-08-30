using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>Which of the page's four inks a sample carries. Index 0 is bare paper.</summary>
    public enum BookInk
    {
        None = 0,
        Ink = 1,
        Soft = 2,
        Seam = 3
    }

    /// <summary>
    /// The page raster: an anti-aliased sheet of paper you set type on.
    ///
    /// <para>
    /// <b>Why this is not <see cref="PixelCanvas"/>.</b> That buffer is one <c>Color32</c> per final
    /// pixel and blits <see cref="PixelFont"/> at whole-pixel scales, which is exactly right for the
    /// instrument screens — they are period CRTs and a hard-edged glyph <i>is</i> the look. A book is
    /// printed paper. Here the buffer is one <b>byte per supersample</b>, rasterised at
    /// <see cref="BookLayout.Supersample"/>x and box-filtered down on the way out, so a glyph edge
    /// resolves to grey rather than to a step. Nothing in <see cref="PixelCanvas"/> changes; the two
    /// screens want opposite things and now say so in different types.
    /// </para>
    ///
    /// <para>
    /// <b>A byte, not a colour.</b> Storing <c>Color32</c> at 3x would cost four times the memory to
    /// carry a palette of four entries. Storing an index instead means the supersampled buffer for a
    /// whole page is under 4 MB, and — the part that matters — the paper underneath can be a
    /// per-column gradient computed at <i>final</i> resolution and blended in during the downsample.
    /// Partial coverage therefore lands on the real paper colour, gutter shading included, instead of
    /// on a flat one baked into the samples.
    /// </para>
    ///
    /// <para>
    /// Coordinates are supersample pixels with the origin at the <b>top left</b>, the same reading
    /// order every screen in the game lays itself out in. The flip to a texture's bottom-up rows
    /// happens once, in <see cref="Resolve"/>.
    /// </para>
    /// </summary>
    public sealed class BookCanvas
    {
        /// <summary>
        /// The supersample buffer, shared by every book in the lab.
        ///
        /// <para>
        /// <b>Why it is safe to share, and what would break it.</b> This is scratch and nothing else:
        /// every draw begins at <see cref="Clear"/> and ends at <see cref="Resolve"/>, and nothing
        /// reads it in between or afterwards. The one invariant is that a draw must not span a frame —
        /// if a coroutine ever yielded halfway through rasterising a page, a second book drawing in
        /// that gap would paint over the first. No caller does that today; the page turn yields
        /// between whole draws, never inside one.
        /// </para>
        ///
        /// <para>
        /// <b>Why it is worth sharing.</b> There are eight books in the lab and every one of them
        /// rasterises its pages at load, so a per-instance buffer was about 30 MB of identical scratch
        /// sitting idle — more than the finished textures of all eight put together. One buffer costs
        /// 3.8 MB no matter how many manuals the lab grows.
        /// </para>
        ///
        /// <para>
        /// Grown, never shrunk, and only ever by the largest page anyone asks for. Every book uses the
        /// same <see cref="BookLayout"/> metrics, so in practice it is allocated once.
        /// </para>
        /// </summary>
        private static byte[] shared;

        private readonly byte[] samples;

        /// <summary>Final page size in texture pixels.</summary>
        public int Width { get; }

        public int Height { get; }
        public int Supersample { get; }

        public int SampleWidth { get; }
        public int SampleHeight { get; }

        public BookCanvas(int width, int height, int supersample)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            Supersample = Mathf.Max(1, supersample);
            SampleWidth = Width * Supersample;
            SampleHeight = Height * Supersample;

            int needed = SampleWidth * SampleHeight;
            if (shared == null || shared.Length < needed) shared = new byte[needed];
            samples = shared;
        }

        /// <summary>
        /// Drop the shared buffer. Statics survive an Enter Play Mode that skips the domain reload, so
        /// without this the buffer from the last session is still resident before a single book has
        /// been opened.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Reset() => shared = null;

        /// <summary>
        /// Wipe this canvas's own region. Only its own: the shared buffer may be larger than this
        /// page needs, and clearing the surplus would be work nobody asked for on every redraw.
        /// </summary>
        public void Clear() => System.Array.Clear(samples, 0, SampleWidth * SampleHeight);

        /// <summary>
        /// Fill a rectangle of supersample pixels, clipped to the page. Anything off the edge is
        /// dropped rather than wrapped, so a line that runs long loses its tail instead of
        /// reappearing on the far side of the paper.
        /// </summary>
        public void Fill(int x, int y, int width, int height, BookInk ink)
        {
            byte value = (byte)ink;

            int x0 = Mathf.Max(0, x);
            int x1 = Mathf.Min(SampleWidth, x + width);
            int y0 = Mathf.Max(0, y);
            int y1 = Mathf.Min(SampleHeight, y + height);

            for (int py = y0; py < y1; py++)
            {
                int row = py * SampleWidth;
                for (int px = x0; px < x1; px++) samples[row + px] = value;
            }
        }

        /// <summary>
        /// Set a string proportionally, with (<paramref name="x"/>, <paramref name="top"/>) the top
        /// left of the first glyph cell — the cell, not the baseline, so a line with descenders in it
        /// sits where a line without them does.
        /// <para>
        /// <paramref name="embolden"/> widens every inked cell horizontally. Two thirds of a final
        /// pixel is a semibold; it is how a heading gets weight without a second glyph table.
        /// </para>
        /// </summary>
        public void DrawText(int x, int top, string text, BookInk ink, int glyphPixel,
                             int extraTracking = 0, int embolden = 0)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Spelt here as well as at measurement time, so the string that was measured is the
            // string that is painted even if a caller forgets (#55).
            text = BookLayout.Spell(text);

            foreach (char c in text)
            {
                BookGlyph glyph = BookFont.Glyph(c);

                for (int gy = 0; gy < BookFont.Height; gy++)
                for (int gx = 0; gx < glyph.Width; gx++)
                {
                    if (!BookFont.IsOn(glyph, gx, gy)) continue;
                    Fill(x + gx * glyphPixel, top + gy * glyphPixel,
                         glyphPixel + embolden, glyphPixel, ink);
                }

                x += (glyph.Width + BookFont.Tracking) * glyphPixel + extraTracking;
            }
        }

        /// <summary>
        /// Set a string on a fixed advance, each glyph centred in its cell — tabular matter, whose
        /// columns were aligned with spaces and would otherwise dissolve in a proportional face.
        /// </summary>
        public void DrawTextMono(int x, int top, string text, BookInk ink, int glyphPixel)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = BookLayout.Spell(text);

            int advance = BookLayout.MonoAdvance(glyphPixel);

            foreach (char c in text)
            {
                BookGlyph glyph = BookFont.Glyph(c);
                int centring = (BookFont.CellWidth - glyph.Width) * glyphPixel / 2;

                for (int gy = 0; gy < BookFont.Height; gy++)
                for (int gx = 0; gx < glyph.Width; gx++)
                {
                    if (!BookFont.IsOn(glyph, gx, gy)) continue;
                    Fill(x + centring + gx * glyphPixel, top + gy * glyphPixel,
                         glyphPixel, glyphPixel, ink);
                }

                x += advance;
            }
        }

        /// <summary>
        /// A solid triangle filling the given box, apex on the left or the right. Geometry rather
        /// than a glyph: the page-corner arrow must not depend on the face happening to carry a
        /// chevron, and a diagonal drawn at supersample resolution is the one shape that most
        /// obviously benefits from being averaged down.
        /// </summary>
        public void FillArrow(int x, int y, int width, int height, bool pointLeft, BookInk ink)
        {
            if (width <= 0 || height <= 0) return;

            int half = height / 2;
            int span = Mathf.Max(1, width - 1);

            for (int dx = 0; dx < width; dx++)
            {
                int fromApex = pointLeft ? dx : width - 1 - dx;
                int extent = fromApex * half / span;
                Fill(x + dx, y + half - extent, 1, extent * 2 + 1, ink);
            }
        }

        /// <summary>
        /// Box-filter the page into a destination buffer, blending partial coverage onto the paper.
        ///
        /// <para>
        /// <paramref name="paperColumns"/> is the paper's own colour per final column — the gutter
        /// shading — so it varies across the page and is what an uninked sample resolves to. The
        /// destination is written bottom-up, ready for <c>SetPixels32</c>, which is where the one
        /// vertical flip in this file lives.
        /// </para>
        ///
        /// <para>
        /// <paramref name="top"/> and <paramref name="rows"/> restrict the work to a band of the
        /// page. A page turn only needs to repaint the foot of a spread it otherwise copied
        /// wholesale, and resolving 4 MB of samples to move a page-corner control would be the
        /// difference between a hitch and no hitch.
        /// </para>
        /// </summary>
        public void Resolve(Color32[] destination, int destinationWidth, int destinationHeight,
                            int destinationX, int destinationY, int top, int rows,
                            Color32[] inkColours, Color32[] paperColumns)
        {
            if (destination == null || inkColours == null || paperColumns == null) return;

            int taps = Supersample * Supersample;
            int last = Mathf.Min(Height, top + rows);

            for (int y = Mathf.Max(0, top); y < last; y++)
            {
                int sampleTop = y * Supersample;
                int destinationRow = (destinationHeight - 1 - (destinationY + y)) * destinationWidth +
                                     destinationX;
                if (destinationRow < 0 || destinationRow + Width > destination.Length) continue;

                for (int x = 0; x < Width; x++)
                {
                    int sampleLeft = x * Supersample;
                    Color32 paper = paperColumns[x];

                    // Bare paper is most of a page. One pass to find out, and no colour lookups at
                    // all for the pixels that carry no ink.
                    int any = 0;
                    for (int j = 0; j < Supersample; j++)
                    {
                        int row = (sampleTop + j) * SampleWidth + sampleLeft;
                        for (int i = 0; i < Supersample; i++) any |= samples[row + i];
                    }

                    if (any == 0)
                    {
                        destination[destinationRow + x] = paper;
                        continue;
                    }

                    int r = 0, g = 0, b = 0;
                    for (int j = 0; j < Supersample; j++)
                    {
                        int row = (sampleTop + j) * SampleWidth + sampleLeft;
                        for (int i = 0; i < Supersample; i++)
                        {
                            byte index = samples[row + i];
                            Color32 colour = index == 0 ? paper : inkColours[index];
                            r += colour.r;
                            g += colour.g;
                            b += colour.b;
                        }
                    }

                    destination[destinationRow + x] =
                        new Color32((byte)(r / taps), (byte)(g / taps), (byte)(b / taps), 255);
                }
            }
        }
    }
}
