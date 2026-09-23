using BenchmarkDotNet.Attributes;
using ConsoleForge.Core;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

/// <summary>
/// <see cref="ImageWidget"/>'s half-block fallback — the path every terminal without Kitty
/// graphics takes — driven through <see cref="Renderer"/>.
/// </summary>
/// <remarks>
/// A per-cell loop: two <c>GetPixel</c> reads, a <see cref="Style"/> and a rendered cell
/// string per cell. Steady state is the one measured, since the widget rebuilds all of that
/// every frame even when the diff then emits nothing.
/// </remarks>
[MemoryDiagnoser]
public class PixelHalfBlockBenchmarks
{
    [Params(18, 36)]
    public int Columns { get; set; }

    private int Rows => Columns / 2;

    private ImageWidget _image = null!;
    private Renderer _renderer = null!;

    [GlobalSetup]
    public void Setup()
    {
        // A poster-shaped 160x240 image, the size a Plex thumbnail is requested at.
        var pixels = new byte[160 * 240 * 4];
        new Random(42).NextBytes(pixels);
        _image = new ImageWidget(new RgbaImageData(pixels, 160, 240));

        _renderer = new Renderer();
        _renderer.Render(_image, Columns, Rows, Theme.Default, ColorProfile.TrueColor);
    }

    [Benchmark]
    public string HalfBlockSteady()
        => _renderer.Render(_image, Columns, Rows, Theme.Default, ColorProfile.TrueColor).Content;
}
