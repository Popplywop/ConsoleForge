using BenchmarkDotNet.Attributes;
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

/// <summary>
/// What the <see cref="IMeasurable"/> pass costs, measured the way AGENTS.md prescribes:
/// both variants in one run, so BenchmarkDotNet computes the ratio against identical
/// machine state.
/// </summary>
/// <remarks>
/// The alternative — running the old code, then the new code, and subtracting — is not
/// reliable here. Across this machine's runs the same unchanged benchmark drifted from
/// 24.7us to 36.2us, which is larger than the effect being measured. The two trees below
/// render the same thing (twenty single-row blocks in twenty-four rows); the only
/// difference is whether the layout pass has to ask each child for its content size.
/// </remarks>
[MemoryDiagnoser]
public class MeasureBenchmarks
{
    private const int Rows = 20;

    private IWidget _flex = null!;
    private IWidget _auto = null!;

    private RenderContext _ctxFlex = null!;
    private RenderContext _ctxAuto = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Flex(1) children: twenty-four rows over twenty children is one row each, so this
        // renders identically to the Auto tree and skips the measure pass entirely.
        _flex = new Container(Axis.Vertical, [.. Enumerable.Range(0, Rows)
            .Select(i => (IWidget)new TextBlock($"Item {i:D2}") { Height = SizeConstraint.Flex(1) })]);

        // TextBlock's own defaults: Auto on both axes.
        _auto = new Container(Axis.Vertical, [.. Enumerable.Range(0, Rows)
            .Select(i => (IWidget)new TextBlock($"Item {i:D2}"))]);

        _ctxFlex = Prime(_flex);
        _ctxAuto = Prime(_auto);
    }

    private static RenderContext Prime(IWidget root, int width = 80, int height = 24)
    {
        var layout = LayoutEngine.Resolve(root, width, height);
        var region = layout.GetRegion(root) ?? new Region(0, 0, width, height);
        var ctx    = new RenderContext(region, Theme.Default, ColorProfile.TrueColor, layout);
        root.Render(ctx);
        ctx.ToAnsiFrame();
        return ctx;
    }

    /// <summary>Layout only, no measure pass: the cost to beat.</summary>
    [Benchmark(Baseline = true)]
    public ResolvedLayout ResolveLayout_Flex() => LayoutEngine.Resolve(_flex, 80, 24);

    /// <summary>Layout with every child measured. The ratio against the baseline is the
    /// measure pass, isolated from rendering.</summary>
    [Benchmark]
    public ResolvedLayout ResolveLayout_Auto() => LayoutEngine.Resolve(_auto, 80, 24);

    /// <summary>A full steady-state frame without the measure pass.</summary>
    [Benchmark]
    public string Frame_Flex()
        => ViewDescriptor.From(_flex, existingCtx: _ctxFlex, width: 80, height: 24).Content;

    /// <summary>A full steady-state frame with it — what an application actually pays.</summary>
    [Benchmark]
    public string Frame_Auto()
        => ViewDescriptor.From(_auto, existingCtx: _ctxAuto, width: 80, height: 24).Content;
}
