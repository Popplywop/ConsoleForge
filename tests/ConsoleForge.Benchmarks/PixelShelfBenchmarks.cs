using BenchmarkDotNet.Attributes;
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;
using ConsoleForge.Widgets;

/// <summary>
/// A <see cref="HorizontalShelf"/> of Kitty images, driven through <see cref="Renderer"/>,
/// across the three kinds of frame a poster shelf produces.
/// </summary>
/// <remarks>
/// <para>
/// The frame kinds differ in what they emit, and the difference is the point:
/// <list type="bullet">
///   <item><b>Steady</b> — nothing moved. Outside tmux no graphics bytes at all; inside
///   it, one <c>a=p</c> renewal per image.</item>
///   <item><b>Scrolling</b> — every image moved one column. One <c>a=p</c> per image,
///   never a re-upload.</item>
///   <item><b>OneNew</b> — exactly one image is new this frame: one upload, one delete,
///   and the rest as in Steady.</item>
/// </list>
/// </para>
/// <para>
/// Every frame rebuilds its payloads from the widgets, as an application's
/// <c>View()</c> does, so the per-frame cost of <see cref="KittyProtocol.CreatePayload"/>
/// is included and depends on its encoding cache still hitting.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class PixelShelfBenchmarks
{
    private const int ImageCount = 6;
    private const int PosterBytes = 14 * 1024; // about one Plex poster, as JPEG
    private const int W = 120;
    private const int H = 12;

    [Params(false, true)]
    public bool InsideTmux { get; set; }

    private byte[][] _posters = null!;
    private byte[] _alternate = null!;
    private TerminalCapabilities _caps = null!;

    private HorizontalShelf _steady = null!;
    private HorizontalShelf[] _scroll = null!;
    private HorizontalShelf[] _oneNew = null!;

    private Renderer _steadyRenderer = null!;
    private Renderer _scrollRenderer = null!;
    private Renderer _oneNewRenderer = null!;
    private int _tick;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        byte[] Poster()
        {
            var bytes = new byte[PosterBytes];
            rng.NextBytes(bytes);
            return bytes;
        }

        _posters   = [.. Enumerable.Range(0, ImageCount).Select(_ => Poster())];
        _alternate = Poster();
        _caps      = new TerminalCapabilities { SupportsKittyGraphics = true, InsideTmux = InsideTmux };

        _steady = Shelf(_posters, scrollOffset: 0);
        _scroll = [Shelf(_posters, scrollOffset: 0), Shelf(_posters, scrollOffset: 1)];

        // The last card swaps artwork each frame: its previous image leaves, a new one arrives.
        var swapped = (byte[][])_posters.Clone();
        swapped[^1] = _alternate;
        _oneNew = [Shelf(_posters, scrollOffset: 0), Shelf(swapped, scrollOffset: 0)];

        _steadyRenderer = Primed(_steady);
        _scrollRenderer = Primed(_scroll[0]);
        _oneNewRenderer = Primed(_oneNew[0]);
    }

    private HorizontalShelf Shelf(byte[][] posters, int scrollOffset) =>
        new([.. posters.Select((p, i) => new ShelfItem($"Title {i}", p, _caps))],
            scrollOffset: scrollOffset, cardWidth: 18, cardHeight: H - 1, cardGap: 2);

    private static Renderer Primed(IWidget root)
    {
        var renderer = new Renderer();
        renderer.Render(root, W, H, Theme.Default, ColorProfile.TrueColor);
        return renderer;
    }

    /// <summary>Nothing moved since last frame.</summary>
    [Benchmark(Baseline = true)]
    public string Steady()
        => _steadyRenderer.Render(_steady, W, H, Theme.Default, ColorProfile.TrueColor).Content;

    /// <summary>Every image moved one column since last frame.</summary>
    [Benchmark]
    public string Scrolling()
        => _scrollRenderer.Render(_scroll[++_tick & 1], W, H, Theme.Default, ColorProfile.TrueColor).Content;

    /// <summary>One image is new since last frame; the others held still.</summary>
    [Benchmark]
    public string OneNew()
        => _oneNewRenderer.Render(_oneNew[++_tick & 1], W, H, Theme.Default, ColorProfile.TrueColor).Content;
}
