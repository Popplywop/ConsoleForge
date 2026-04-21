using ConsoleForge.Styling;

namespace ConsoleForge.Layout;

/// <summary>
/// A delegating <see cref="IRenderContext"/> that forwards writes to a parent context
/// but with a restricted sub-region. Used by <c>BorderBox</c> to give the body widget
/// its own clipped region.
/// </summary>
public sealed class SubRenderContext : IRenderContext
{
    private readonly IRenderContext _parent;

    /// <summary>The allocated region this sub-context is restricted to.</summary>
    public Region Region { get; }
    /// <inheritdoc/>
    public Theme Theme => _parent.Theme;
    /// <inheritdoc/>
    public ColorProfile ColorProfile => _parent.ColorProfile;
    /// <inheritdoc/>
    public ResolvedLayout Layout => _parent.Layout;

    public CursorDescriptor? Cursor => _parent.Cursor;

    /// <summary>
    /// Initialises a sub-context that forwards writes to <paramref name="parent"/>
    /// but restricts rendering to <paramref name="region"/>.
    /// </summary>
    public SubRenderContext(IRenderContext parent, Region region)
    {
        _parent = parent;
        Region = region;
    }

    /// <inheritdoc/>
    public void Write(int col, int row, string text, Style style)
    {
        if (row < Region.Row || row >= Region.Row + Region.Height) return;
        if (col >= Region.Col + Region.Width) return;
        _parent.Write(col, row, text, style);
    }

    /// <inheritdoc/>
    public bool TryReuseWidget(IWidget widget, Region region) =>
        _parent.TryReuseWidget(widget, region);

    /// <inheritdoc/>
    public void RegisterWidget(IWidget widget, Region region) =>
        _parent.RegisterWidget(widget, region);

    public void SetCursorDescriptor(CursorDescriptor cursor)
        => _parent.SetCursorDescriptor(cursor);
}