namespace ConsoleForge.Widgets;

/// <summary>
/// Pre-decoded RGBA image data for use with <see cref="ImageWidget"/>'s half-block
/// Unicode fallback renderer. Each pixel is stored as four consecutive bytes in
/// row-major order: R, G, B, A (alpha is read but currently ignored in blending).
/// </summary>
/// <param name="Pixels">
/// Raw RGBA bytes. Length must equal <c>Width × Height × 4</c>.
/// </param>
/// <param name="Width">Image width in pixels.</param>
/// <param name="Height">Image height in pixels.</param>
public readonly record struct RgbaImageData(byte[] Pixels, int Width, int Height)
{
    /// <summary>
    /// Read the RGBA components of the pixel at (<paramref name="x"/>, <paramref name="y"/>).
    /// Returns opaque black for out-of-bounds coordinates.
    /// </summary>
    internal (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return (0, 0, 0, 255);
        int idx = (y * Width + x) * 4;
        if (idx + 3 >= Pixels.Length)
            return (0, 0, 0, 255);
        return (Pixels[idx], Pixels[idx + 1], Pixels[idx + 2], Pixels[idx + 3]);
    }
}
