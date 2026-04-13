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

    /// <summary>Mark dirty: next <see cref="RenderIfDirty"/> will re-render.</summary>
    public void MarkDirty() => _isDirty = true;

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
        var layout = LayoutEngine.Resolve(root, width, height);
        var rootRegion = layout.GetRegion(root) ?? new Region(0, 0, width, height);

        if (_ctx is null)
            _ctx = new RenderContext(rootRegion, theme, colorProfile, layout);
        else
            _ctx.Reset(rootRegion, theme, colorProfile, layout);

        root.Render(_ctx);

        _lastView = new ViewDescriptor
        {
            Content    = _ctx.ToAnsiFrame(),
            Cursor     = new CursorDescriptor(Visible: false),
            RootWidget = root
        };

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

        var layout     = LayoutEngine.Resolve(root, width, height);
        var rootRegion = layout.GetRegion(root) ?? new Region(0, 0, width, height);

        if (_ctx is null)
            _ctx = new RenderContext(rootRegion, theme, colorProfile, layout);
        else
            _ctx.Reset(rootRegion, theme, colorProfile, layout);

        root.Render(_ctx);

        var view = new ViewDescriptor
        {
            Content    = _ctx.ToAnsiFrame(),
            Cursor     = new CursorDescriptor(Visible: false),
            RootWidget = root
        };
        _lastView = view;
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

        terminal.Write(view.Content);

        terminal.SetCursorVisible(view.Cursor.Visible);
        if (view.Cursor.Visible)
            terminal.SetCursorPosition(view.Cursor.Col, view.Cursor.Row);

        terminal.Flush();
    }

    /// <summary>
    /// Invalidate the previous frame buffer, forcing a full redraw on the next flush.
    /// Call after terminal resize or external clear.
    /// </summary>
    public void Invalidate()
    {
        _ctx = null;
        _isDirty = true;
    }
}
