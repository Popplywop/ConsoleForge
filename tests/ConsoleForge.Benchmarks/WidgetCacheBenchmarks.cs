using BenchmarkDotNet.Attributes;
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

/// <summary>
/// Exercises the widget render cache (<see cref="RenderContext.TryReuseWidget"/>).
///
/// The warm benchmarks in <see cref="RenderBenchmarks"/> use <see cref="TextBlock"/>
/// children, which are leaves — <see cref="Container"/> never registers them, so those
/// benchmarks measure the diff and never touch the cache. Reuse only applies to
/// composites (<see cref="IContainer"/>, <see cref="ISingleBodyWidget"/>,
/// <see cref="ILayeredContainer"/>) held by reference across frames, which is what
/// these build.
/// </summary>
[MemoryDiagnoser]
public class WidgetCacheBenchmarks
{
    // Held by reference across iterations: a model that reuses its widget instances
    // is the precondition for a cache hit, so rebuilding the tree per frame would
    // measure something else entirely.
    private IWidget _rows = null!;
    private IWidget _oneRowChanges = null!;

    private RenderContext _ctxRows = null!;
    private RenderContext _ctxOneRowChanges = null!;

    private const int RowCount = 12;

    [GlobalSetup]
    public void Setup()
    {
        _rows           = BuildRows(changedRow: -1);
        _oneRowChanges  = BuildRows(changedRow: -1);

        _ctxRows          = PrimeContext(_rows, 80, 24);
        _ctxOneRowChanges = PrimeContext(_oneRowChanges, 80, 24);
    }

    private static IWidget BuildRows(int changedRow)
    {
        var rows = new IWidget[RowCount];
        for (int i = 0; i < RowCount; i++)
        {
            rows[i] = new BorderBox(
                title: $"Row {i:D2}",
                body: new TextBlock(i == changedRow ? $"changed {i}" : $"stable {i}"),
                style: Style.Default.BorderForeground(Color.Cyan));
        }
        return new Container(Axis.Vertical, rows);
    }

    private static RenderContext PrimeContext(IWidget root, int width, int height)
    {
        var layout = LayoutEngine.Resolve(root, width, height);
        var region = layout.GetRegion(root) ?? new Region(0, 0, width, height);
        var ctx    = new RenderContext(region, Theme.Default, ColorProfile.TrueColor, layout);
        root.Render(ctx);
        ctx.ToAnsiFrame();
        return ctx;
    }

    /// <summary>
    /// Every row is the same instance at the same region as last frame, so every row
    /// is reusable. This is the steady state of a list that is on screen but idle.
    /// </summary>
    [Benchmark(Baseline = true)]
    public string AllRowsReusable()
        => ViewDescriptor.From(_rows, existingCtx: _ctxRows, width: 80, height: 24).Content;

    /// <summary>
    /// One row is rebuilt per frame — the shape of a selection moving through a list.
    /// The other <c>RowCount - 1</c> rows are still the same instances and should be
    /// served from the cache.
    /// </summary>
    [Benchmark]
    public string OneRowRebuilt()
    {
        // Swap a single child for a fresh instance; the rest keep their identity.
        var container = (Container)_oneRowChanges;
        var children  = container.Children;
        int row       = _tick++ % RowCount;
        var next      = new IWidget[RowCount];
        for (int i = 0; i < RowCount; i++)
            next[i] = i == row
                ? new BorderBox($"Row {i:D2}", new TextBlock($"changed {_tick}"),
                                Style.Default.BorderForeground(Color.Cyan))
                : children[i];

        _oneRowChanges = new Container(Axis.Vertical, next);
        return ViewDescriptor.From(_oneRowChanges, existingCtx: _ctxOneRowChanges,
                                   width: 80, height: 24).Content;
    }

    private int _tick;
}
