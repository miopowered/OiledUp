using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The pixel buffer behind an in-world screen: a rectangle of <c>Color32</c> you fill and blit
    /// <see cref="PixelFont"/> glyphs into, then hand to a <c>Texture2D</c>.
    /// <para>
    /// Extracted alongside <see cref="PixelText"/> (#50) because the glyph blit was written out
    /// twice, character loop and all, once in <see cref="MachineDisplay"/> and once in
    /// <see cref="InspectableBookSurface"/> — and so was the y flip underneath it. That flip is the
    /// part worth owning in one place: every caller lays a screen out top-down, in reading order,
    /// while a texture's rows run bottom-up. Doing it per call site means one screen eventually
    /// forgets and draws its footer through the ceiling.
    /// </para>
    /// <para>
    /// Nothing here is text-specific beyond <see cref="DrawText"/>. Rules, seams, progress bars and
    /// the book's page-corner controls are all <see cref="FillRect"/>, which is also why the
    /// rectangle is the primitive and the glyph is built out of it rather than the other way round.
    /// </para>
    /// </summary>
    public sealed class PixelCanvas
    {
        private readonly Color32[] pixels;

        public int Width { get; }
        public int Height { get; }

        /// <summary>
        /// Sized exactly as given, never clamped: the buffer is handed to a texture of the same
        /// dimensions and <c>SetPixels32</c> refuses a length it did not expect, so a canvas that
        /// quietly rounded itself up would fail at the blit rather than at the mistake.
        /// </summary>
        public PixelCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            pixels = new Color32[Mathf.Max(0, width * height)];
        }

        public void Clear(Color32 colour)
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = colour;
        }

        /// <summary>
        /// Fill a rectangle given in screen coordinates — origin top-left, y increasing downwards —
        /// clipped to the canvas. Anything off the edge is dropped rather than wrapped, so a caption
        /// that runs long loses its tail instead of reappearing on the far side of the screen.
        /// </summary>
        public void FillRect(int x, int y, int width, int height, Color32 colour)
        {
            for (int dy = 0; dy < height; dy++)
            {
                int py = Height - 1 - (y + dy);
                if (py < 0 || py >= Height) continue;

                int row = py * Width;
                for (int dx = 0; dx < width; dx++)
                {
                    int px = x + dx;
                    if (px < 0 || px >= Width) continue;
                    pixels[row + px] = colour;
                }
            }
        }

        /// <summary>
        /// Blit a string at <paramref name="scale"/>, with (<paramref name="x"/>, <paramref name="y"/>)
        /// the top-left of the first glyph. Advances by <see cref="PixelFont.Advance"/> per character
        /// including unprintable ones, so a column of text stays aligned whatever is in it.
        /// </summary>
        public void DrawText(int x, int y, string text, Color32 colour, int scale)
        {
            if (string.IsNullOrEmpty(text)) return;

            foreach (char ch in text)
            {
                string glyph = PixelFont.Glyph(ch);
                for (int gy = 0; gy < PixelFont.GlyphHeight; gy++)
                {
                    for (int gx = 0; gx < PixelFont.GlyphWidth; gx++)
                    {
                        if (!PixelFont.IsOn(glyph, gx, gy)) continue;
                        FillRect(x + gx * scale, y + gy * scale, scale, scale, colour);
                    }
                }

                x += PixelFont.Advance * scale;
            }
        }

        /// <summary>
        /// Push the buffer to a texture. No mip chain is regenerated: these screens are point-filtered
        /// and a mipped pixel font is a smear.
        /// </summary>
        public void ApplyTo(Texture2D texture)
        {
            if (texture == null) return;
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }
    }
}
