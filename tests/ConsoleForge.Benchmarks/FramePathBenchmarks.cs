using BenchmarkDotNet.Attributes;
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

/// <summary>
/// The two ways a steady-state frame gets produced, measured against each other.
/// </summary>
/// <remarks>
/// <para>
/// Every other render benchmark drives <see cref="ViewDescriptor.From"/>, which no
/// framework code calls: <see cref="Renderer"/> builds its descriptor directly. So the
/// path an application actually runs was unmeasured, and a change that removed the
/// per-frame layout allocation from it moved no benchmark at all.
/// </para>
/// <para>
/// Both variants live here with a baseline so BenchmarkDotNet computes the ratio against
/// identical machine state — the two-run diff that prompted this class put a 30-55% swing
/// on benchmarks the change could not reach.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class FramePathBenchmarks
{
    private const int Rows = 20;
    private const int W = 80;
    private const int H = 24;

    private IWidget _tree = null!;
    private RenderContext _ctx = null!;
    private Renderer _renderer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tree = new Container(Axis.Vertical, [.. Enumerable.Range(0, Rows)
            .Select(i => (IWidget)new TextBlock($"Item {i:D2}") { Height = SizeConstraint.Flex(1) })]);

        // Both paths start warm: a primed context for one, a renderer that has already
        // drawn a frame for the other. Cold-start costs are measured elsewhere.
        var layout = LayoutEngine.Resolve(_tree, W, H);
        var region = layout.GetRegion(_tree) ?? new Region(0, 0, W, H);
        _ctx = new RenderContext(region, Theme.Default, ColorProfile.TrueColor, layout);
        _tree.Render(_ctx);
        _ctx.ToAnsiFrame();

        _renderer = new Renderer();
        _renderer.Render(_tree, W, H, Theme.Default, ColorProfile.TrueColor);
    }

    /// <summary>What the benchmark suite has been measuring: a fresh layout every frame.</summary>
    [Benchmark(Baseline = true)]
    public string Frame_ViaViewDescriptor()
        => ViewDescriptor.From(_tree, existingCtx: _ctx, width: W, height: H).Content;

    /// <summary>What an application runs: the renderer's ping-ponged layout buffers.</summary>
    [Benchmark]
    public string Frame_ViaRenderer()
        => _renderer.Render(_tree, W, H, Theme.Default, ColorProfile.TrueColor).Content;
}
