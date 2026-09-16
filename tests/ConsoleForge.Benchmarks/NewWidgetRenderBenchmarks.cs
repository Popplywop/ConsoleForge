using BenchmarkDotNet.Attributes;
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;
using ConsoleForge.Widgets;

/// <summary>
/// Render benchmarks for the three new widgets added in v0.1.0:
/// <see cref="ProgressBar"/>, <see cref="Spinner"/>, and <see cref="Table"/>.
///
/// Follows the same cold / warm-steady pattern established in
/// <see cref="RenderBenchmarks"/>:
/// <list type="bullet">
///   <item>Cold — fresh <see cref="RenderContext"/> every iteration; full layout +
///   render + full-frame ANSI emit.</item>
///   <item>Warm-steady — persistent context primed in GlobalSetup; measures the
///   diff/no-change overhead only.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class NewWidgetRenderBenchmarks
{
    // ── Widgets under test ───────────────────────────────────────────────────

    private IWidget _progressBar     = null!;
    private IWidget _spinner         = null!;
    private IWidget _table           = null!;

    // Renderers, not raw contexts: Renderer is the path an application runs, and it
    // owns the layout buffers a real frame reuses. Warm renderers are primed once and
    // never invalidated; cold ones are invalidated per iteration.
    private Renderer _warmProgressBar = null!;
    private Renderer _warmSpinner     = null!;
    private Renderer _warmTable       = null!;
    private Renderer _coldProgressBar = null!;
    private Renderer _coldSpinner     = null!;
    private Renderer _coldTable       = null!;

    [GlobalSetup]
    public void Setup()
    {
        // ProgressBar at 50% with percent label.
        _progressBar = new ProgressBar(
            value: 50,
            maximum: 100,
            showPercent: true,
            fillChar: '█',
            emptyChar: '░',
            style: Style.Default.Foreground(Color.Green));

        // Spinner at frame 3 with a short label.
        _spinner = new Spinner(
            frame: 3,
            label: "Loading…",
            frames: Spinner.BrailleFrames,
            style: Style.Default.Foreground(Color.Cyan));

        // Table: 3 columns, 10 data rows.
        var columns = new TableColumn[]
        {
            new("Name",   Width: 20),
            new("Status", Width: 10),
            new("Value",  Width: 0),   // flex
        };
        var rows = Enumerable.Range(0, 10)
            .Select(i => (IReadOnlyList<string>)new[] { $"Item {i:D2}", i % 2 == 0 ? "OK" : "Warn", $"{i * 3.14:F2}" })
            .ToList();
        _table = new Table(columns, rows, selectedIndex: 2);

        // Prime renderers.
        _warmProgressBar = PrimeRenderer(_progressBar, width: 80, height: 1);
        _warmSpinner     = PrimeRenderer(_spinner,     width: 80, height: 1);
        _warmTable       = PrimeRenderer(_table,       width: 80, height: 24);
        _coldProgressBar = PrimeRenderer(_progressBar, width: 80, height: 1);
        _coldSpinner     = PrimeRenderer(_spinner,     width: 80, height: 1);
        _coldTable       = PrimeRenderer(_table,       width: 80, height: 24);
    }

    private static Renderer PrimeRenderer(IWidget root, int width, int height)
    {
        var renderer = new Renderer();
        renderer.Render(root, width, height, Theme.Default, ColorProfile.TrueColor);
        return renderer;
    }

    /// <summary>One cold frame: no previous buffer, so the whole screen is emitted.</summary>
    private static string ColdFrame(Renderer renderer, IWidget root, int width, int height)
    {
        renderer.Invalidate();
        return renderer.Render(root, width, height, Theme.Default, ColorProfile.TrueColor).Content;
    }

    // ── Cold benchmarks ──────────────────────────────────────────────────────

    /// <summary>
    /// Cold: full layout + render pipeline for a ProgressBar at 80×1.
    /// </summary>
    [Benchmark(Baseline = true)]
    public string RenderProgressBar_Cold()
    {
        return ColdFrame(_coldProgressBar, _progressBar, 80, 1);
    }

    /// <summary>
    /// Cold: full layout + render pipeline for a Spinner at 80×1.
    /// </summary>
    [Benchmark]
    public string RenderSpinner_Cold()
    {
        return ColdFrame(_coldSpinner, _spinner, 80, 1);
    }

    /// <summary>
    /// Cold: full layout + render pipeline for a 10-row Table at 80×24.
    /// </summary>
    [Benchmark]
    public string RenderTable_Cold()
    {
        return ColdFrame(_coldTable, _table, 80, 24);
    }

    // ── Warm-steady benchmarks ───────────────────────────────────────────────

    /// <summary>
    /// Warm-steady: ProgressBar unchanged — only diff overhead measured.
    /// </summary>
    [Benchmark]
    public string RenderProgressBar_WarmSteady()
    {
        return _warmProgressBar.Render(_progressBar, 80, 1, Theme.Default, ColorProfile.TrueColor).Content;
    }

    /// <summary>
    /// Warm-steady: Spinner unchanged — only diff overhead measured.
    /// </summary>
    [Benchmark]
    public string RenderSpinner_WarmSteady()
    {
        return _warmSpinner.Render(_spinner, 80, 1, Theme.Default, ColorProfile.TrueColor).Content;
    }

    /// <summary>
    /// Warm-steady: Table unchanged (same selected index, same data) —
    /// only diff overhead measured.
    /// </summary>
    [Benchmark]
    public string RenderTable_WarmSteady()
    {
        return _warmTable.Render(_table, 80, 24, Theme.Default, ColorProfile.TrueColor).Content;
    }

    // ── Large-dataset benchmarks — prove O(viewport) not O(total items) ──────

    private IWidget _list1000     = null!;
    private IWidget _table1000    = null!;
    private Renderer _warmList1000  = null!;
    private Renderer _warmTable1000 = null!;
    private Renderer _coldList1000  = null!;
    private Renderer _coldTable1000 = null!;

    [GlobalSetup(Targets = [
        nameof(RenderList1000_Cold),
        nameof(RenderTable1000_Cold),
        nameof(RenderList1000_WarmSteady),
        nameof(RenderTable1000_WarmSteady)])]
    public void SetupLargeDataset()
    {
        var listItems = Enumerable.Range(0, 1000)
            .Select(i => $"Item {i:D4}")
            .ToArray();
        _list1000 = new List(
            listItems, selectedIndex: 500, scrollOffset: 490);

        var cols = new TableColumn[] { new("Name", Width: 20), new("Value", Width: 0) };
        var tableRows = Enumerable.Range(0, 1000)
            .Select(i => (IReadOnlyList<string>)new[] { $"Row {i:D4}", $"{i}" })
            .ToList();
        _table1000 = new Table(cols, tableRows, selectedIndex: 500, scrollOffset: 490);

        _warmList1000  = PrimeRenderer(_list1000,  width: 80, height: 24);
        _warmTable1000 = PrimeRenderer(_table1000, width: 80, height: 24);
        _coldList1000  = PrimeRenderer(_list1000,  width: 80, height: 24);
        _coldTable1000 = PrimeRenderer(_table1000, width: 80, height: 24);
    }

    /// <summary>
    /// Cold: 1 000-item List, viewport 24 rows, scrolled to mid-list.
    /// Cost must be flat (O(viewport)) regardless of total item count.
    /// </summary>
    [Benchmark]
    public string RenderList1000_Cold()
    {
        return ColdFrame(_coldList1000, _list1000, 80, 24);
    }

    /// <summary>
    /// Cold: 1 000-row Table, viewport 24 rows (1 header + 23 data), scrolled to mid.
    /// </summary>
    [Benchmark]
    public string RenderTable1000_Cold()
    {
        return ColdFrame(_coldTable1000, _table1000, 80, 24);
    }

    /// <summary>Warm-steady: 1 000-item List, no changes — only diff overhead.</summary>
    [Benchmark]
    public string RenderList1000_WarmSteady()
    {
        return _warmList1000.Render(_list1000, 80, 24, Theme.Default, ColorProfile.TrueColor).Content;
    }

    /// <summary>Warm-steady: 1 000-row Table, no changes — only diff overhead.</summary>
    [Benchmark]
    public string RenderTable1000_WarmSteady()
    {
        return _warmTable1000.Render(_table1000, 80, 24, Theme.Default, ColorProfile.TrueColor).Content;
    }
}