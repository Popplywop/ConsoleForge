using System.Diagnostics.CodeAnalysis;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;

namespace ConsoleForge.Core;

/// <summary>
/// Renders widget trees to the terminal using a persistent double-buffered
/// <see cref="RenderContext"/>. Only changed cells are emitted each frame.
/// <para>
/// Supports a dirty flag: when clean, <see cref="RenderIfDirty"/> skips
/// <c>View()</c> and <c>Render()</c> entirely — zero allocations on static frames.
/// Mark dirty via <see cref="MarkDirty"/> whenever the model or theme changes.
/// </para>
/// </summary>
internal sealed class Renderer
{
    private ViewDescriptor _lastView;

    /// <summary>
    /// Persistent render context shared across frames for double-buffered diff rendering.
    /// Created lazily on first <see cref="Render"/> call; reset each subsequent frame.
    /// </summary>
    private RenderContext? _ctx;

    // Dirty flag: true = model/theme changed since last render, must re-render.
    // Starts true so first frame always renders.
    private volatile bool _isDirty = true;

    // Last-seen dimensions and theme — dirty if these change even without model change.
    private int _lastWidth;
    private int _lastHeight;
    private Theme _lastTheme = Theme.Default;

    // Two layout buffers, ping-ponged. Reuse alone would be enough to stop allocating,
    // but hit-testing reads the layout of the frame on screen, and a single buffer is
    // cleared and refilled by the next frame while that read is still the caller's only
    // source of regions — handing it the next frame's positions with no error. The same
    // hazard RenderContext ping-pongs _prevWidgets/_prevRegions to avoid.
    private readonly ResolvedLayout _layoutA = new();
    private readonly ResolvedLayout _layoutB = new();

    /// <summary>Buffer describing the frame on screen. Null before the first frame.</summary>
    private ResolvedLayout? _lastLayout;

    /// <summary>The buffer not currently holding the on-screen frame.</summary>
    private ResolvedLayout NextLayoutBuffer()
        => ReferenceEquals(_lastLayout, _layoutA) ? _layoutB : _layoutA;

    /// <summary>
    /// The widget tree and resolved layout of the frame currently on screen; false before
    /// the first frame has been drawn.
    /// <para>
    /// Hit-testing uses this instead of calling <c>View()</c> and resolving again. The
    /// frame on screen is the one the user actually clicked, so testing against it is more
    /// correct than testing against a tree they have not been shown — and it is already
    /// resolved, so a click costs no tree rebuild and no layout pass.
    /// </para>
    /// <para>
    /// Callers must hold the render lock for as long as they read the returned layout: the
    /// next frame reclaims the other buffer, and a frame boundary crossed mid-read swaps
    /// the regions underneath them.
    /// </para>
    /// </summary>
    public bool TryGetLastFrame(
        [NotNullWhen(true)] out IWidget? root,
        [NotNullWhen(true)] out ResolvedLayout? layout)
    {
        root   = _lastView.RootWidget;
        layout = _lastLayout;
        return root is not null && layout is not null;
    }

    /// <summary>Mark dirty: next <see cref="RenderIfDirty"/> will re-render.</summary>
    public void MarkDirty() => _isDirty = true;

    /// <summary>
    /// Sequences that remove any pixel graphics still on screen, or null if there are none.
    /// Written during teardown — see <see cref="RenderContext.BuildRawCleanup"/> for why
    /// leaving the alternate screen is not enough.
    /// </summary>
    public string? BuildRawCleanup() => _ctx?.BuildRawCleanup();

    /// <summary>
    /// Render only if dirty. Returns true if a frame was produced and flushed.
    /// If clean (model/theme/size unchanged), skips View()+Render() entirely — zero alloc.
    /// </summary>
    public bool RenderIfDirty(
        IModel model,
        int width, int height,
        Theme theme,
        ColorProfile colorProfile,
        ITerminal terminal)
    {
        // Size or theme change always forces re-render regardless of dirty flag.
        bool sizeChanged  = width != _lastWidth || height != _lastHeight;
        bool themeChanged = !ReferenceEquals(theme, _lastTheme) && theme != _lastTheme;

        if (!_isDirty && !sizeChanged && !themeChanged) return false;

        _isDirty    = false;
        _lastWidth  = width;
        _lastHeight = height;
        _lastTheme  = theme;

        var root   = model.View();
        var layout = LayoutEngine.ResolveInto(NextLayoutBuffer(), root, width, height);
        var rootRegion = layout.GetRegion(root) ?? new Region(0, 0, width, height);

        if (_ctx is null)
            _ctx = new RenderContext(rootRegion, theme, colorProfile, layout);
        else
            _ctx.Reset(rootRegion, theme, colorProfile, layout);

        root.Render(_ctx);

        _lastView   = new ViewDescriptor
        {
            Content    = _ctx.ToAnsiFrame(),
            Cursor     = _ctx.Cursor ?? new(Visible: false),
            RootWidget = root
        };
        _lastLayout = layout;

        Flush(terminal);
        return true;
    }

    /// <summary>
    /// Produce a rendered frame unconditionally (ignores dirty flag).
    /// Used by resize path which needs an immediate full redraw.
    /// </summary>
    public ViewDescriptor Render(
        IWidget root,
        int width, int height,
        Theme theme,
        ColorProfile colorProfile)
    {
        _lastWidth  = width;
        _lastHeight = height;
        _lastTheme  = theme;
        _isDirty    = false;

        var layout     = LayoutEngine.ResolveInto(NextLayoutBuffer(), root, width, height);
        var rootRegion = layout.GetRegion(root) ?? new Region(0, 0, width, height);

        if (_ctx is null)
            _ctx = new RenderContext(rootRegion, theme, colorProfile, layout);
        else
            _ctx.Reset(rootRegion, theme, colorProfile, layout);

        root.Render(_ctx);

        var view = new ViewDescriptor
        {
            Content    = _ctx.ToAnsiFrame(),
            Cursor     = _ctx.Cursor ?? new(Visible: false),
            RootWidget = root
        };
        _lastView   = view;
        _lastLayout = layout;
        return view;
    }

    /// <summary>
    /// Flush the last rendered frame to <paramref name="terminal"/>.
    /// Updates title if set in the descriptor.
    /// </summary>
    public void Flush(ITerminal terminal)
    {
        var view = _lastView;

        if (!string.IsNullOrEmpty(view.Title))
            terminal.SetTitle(view.Title);

        terminal.SetCursorVisible(false);
        terminal.Write(view.Content);

        if (view.Cursor.Visible)
            terminal.SetCursorPosition(view.Cursor.Col, view.Cursor.Row);
        terminal.SetCursorVisible(view.Cursor.Visible);

        terminal.Flush();
    }

    /// <summary>
    /// Invalidate the previous frame buffer, forcing a full redraw on the next flush.
    /// Call after terminal resize or external clear.
    /// </summary>
    public void Invalidate()
    {
        _ctx = null;
        _lastLayout = null;
        _isDirty = true;
    }
}