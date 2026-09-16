using BenchmarkDotNet.Attributes;
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;
using ConsoleForge.Widgets;

[MemoryDiagnoser]
public class RenderBenchmarks
{
    private IWidget _layout = null!;
    private IWidget _singleBlock = null!;
    private IWidget _borderBox = null!;

    // One Renderer per benchmark: Renderer is what an application runs, and it carries
    // the render context and the layout buffers a real frame reuses. They cannot be
    // shared — a renderer driven with two different trees or sizes treats every frame as
    // a full redraw, which is the opposite of what the warm benchmarks measure.
    //
    // Warm renderers are primed in GlobalSetup and never invalidated. Cold ones are
    // invalidated per iteration, which drops the context exactly as a resize does.
    private Renderer _warmTwenty = null!;
    private Renderer _warmSingle = null!;
    private Renderer _warmBorderBox = null!;
    private Renderer _coldTwenty = null!;
    private Renderer _coldSingle = null!;
    private Renderer _coldBorderBox = null!;

    [GlobalSetup]
    public void Setup()
    {
        // --- widgets ---
        var children = Enumerable.Range(0, 20)
            .Select(i => (IWidget)new TextBlock($"Item {i:D2}",
                style: i % 2 == 0
                    ? Style.Default.Foreground(Color.White)
                    : Style.Default.Foreground(Color.Green)))
            .ToArray();

        _layout      = new Container(Axis.Vertical, children);
        _singleBlock = new TextBlock("Hello, ConsoleForge!");
        _borderBox   = new BorderBox(
            title: "ConsoleForge",
            body: new TextBlock("Press Q to quit."),
            style: Style.Default.BorderForeground(Color.Cyan));

        // --- prime renderers (one render each so _prev is populated) ---
        _warmTwenty    = PrimeRenderer(_layout,      width: 80, height: 24);
        _warmSingle    = PrimeRenderer(_singleBlock, width: 80, height: 1);
        _warmBorderBox = PrimeRenderer(_borderBox,   width: 80, height: 24);
        _coldTwenty    = PrimeRenderer(_layout,      width: 80, height: 24);
        _coldSingle    = PrimeRenderer(_singleBlock, width: 80, height: 1);
        _coldBorderBox = PrimeRenderer(_borderBox,   width: 80, height: 24);
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

    // -------------------------------------------------------------------------
    // Cold benchmarks — fresh RenderContext every iteration (no _prev buffer).
    // Measures full layout + render + full-redraw ANSI emit.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cold: full layout + render pipeline for a 20-widget tree at 80×24.
    /// </summary>
    [Benchmark(Baseline = true)]
    public string RenderTwentyWidgets_Cold()
        => ColdFrame(_coldTwenty, _layout, 80, 24);

    /// <summary>
    /// Cold: render a single TextBlock — minimal overhead baseline.
    /// </summary>
    [Benchmark]
    public string RenderSingleTextBlock_Cold()
        => ColdFrame(_coldSingle, _singleBlock, 80, 1);

    /// <summary>
    /// Cold: render a BorderBox with a body TextBlock at full terminal size.
    /// </summary>
    [Benchmark]
    public string RenderBorderBox_Cold()
        => ColdFrame(_coldBorderBox, _borderBox, 80, 24);

    // -------------------------------------------------------------------------
    // Warm-steady benchmarks — persistent RenderContext, identical widget tree
    // each iteration. Measures diff overhead when nothing has changed (best case
    // for the double-buffer: near-zero terminal output).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Warm-steady: 20-widget tree, no changes — only diff overhead measured.
    /// </summary>
    [Benchmark]
    public string RenderTwentyWidgets_WarmSteady()
        => _warmTwenty.Render(_layout, 80, 24, Theme.Default, ColorProfile.TrueColor).Content;

    /// <summary>
    /// Warm-steady: single TextBlock, no changes.
    /// </summary>
    [Benchmark]
    public string RenderSingleTextBlock_WarmSteady()
        => _warmSingle.Render(_singleBlock, 80, 1, Theme.Default, ColorProfile.TrueColor).Content;

    /// <summary>
    /// Warm-steady: BorderBox, no changes.
    /// </summary>
    [Benchmark]
    public string RenderBorderBox_WarmSteady()
        => _warmBorderBox.Render(_borderBox, 80, 24, Theme.Default, ColorProfile.TrueColor).Content;

    // -------------------------------------------------------------------------
    // Dirty-skip benchmarks — Renderer.RenderIfDirty with clean flag.
    // The model is unchanged, dirty flag is NOT set → View()+Render() skipped.
    // Measures the overhead of the guard check only; should be near-zero alloc.
    // -------------------------------------------------------------------------

    private Renderer _dirtyRenderer = null!;
    private IModel   _staticModel   = null!;
    private NullTerminal _nullTerminal = null!;

    // This setup is called once; the renderer is primed so _isDirty=false.
    [GlobalSetup(Targets = new[] { nameof(RenderTwentyWidgets_DirtySkip) })]
    public void SetupDirtySkip()
    {
        Setup(); // build widgets

        var children = Enumerable.Range(0, 20)
            .Select(i => (IWidget)new TextBlock($"Item {i:D2}",
                style: i % 2 == 0
                    ? Style.Default.Foreground(Color.White)
                    : Style.Default.Foreground(Color.Green)))
            .ToArray();
        _staticModel  = new StaticModel(new Container(Axis.Vertical, children));
        _nullTerminal = new NullTerminal();
        _dirtyRenderer = new Renderer();
        // Prime: first call sets _isDirty=false after rendering.
        _dirtyRenderer.RenderIfDirty(_staticModel, 80, 24, Theme.Default, ColorProfile.TrueColor, _nullTerminal);
    }

    /// <summary>
    /// Dirty-skip: renderer is clean (model unchanged, no MarkDirty call).
    /// RenderIfDirty returns immediately — near-zero allocations expected.
    /// </summary>
    [Benchmark]
    public bool RenderTwentyWidgets_DirtySkip()
        => _dirtyRenderer.RenderIfDirty(_staticModel, 80, 24, Theme.Default, ColorProfile.TrueColor, _nullTerminal);
}

// ---------------------------------------------------------------------------
// Minimal IModel that always returns the same widget tree (same reference).
// Returning 'this' from Update means the outer loop won't mark dirty.
// ---------------------------------------------------------------------------
internal sealed class StaticModel(IWidget root) : IModel
{
    public ICmd?  Init()              => null;
    public IWidget View()             => root;
    public (IModel Model, ICmd? Cmd) Update(IMsg msg) => (this, null);
}

// ---------------------------------------------------------------------------
// Null terminal: discards all output so benchmarks don't measure I/O.
// ---------------------------------------------------------------------------
internal sealed class NullTerminal : ITerminal
{
    public int  Width  => 80;
    public int  Height => 24;
    public void Write(string text)                    { }
    public void Flush()                               { }
    public void SetTitle(string title)                { }
    public void SetCursorVisible(bool visible)        { }
    public void SetCursorPosition(int col, int row)   { }
    public void Clear()                               { }
    public void EnterAlternateScreen()                { }
    public void ExitAlternateScreen()                 { }
    public void EnterRawMode()                        { }
    public void ExitRawMode()                         { }
    public void EnableMouse(MouseMode mode = ConsoleForge.Terminal.MouseMode.ButtonEvents) { }
    public void DisableMouse()                        { }
    public void Dispose()                             { }
    public IObservable<InputEvent> Input              => System.Reactive.Linq.Observable.Empty<InputEvent>();
    public event EventHandler<TerminalResizedEventArgs>? Resized { add { } remove { } }
}