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

    public Region Region { get; }
    public Theme Theme => _parent.Theme;
    public ColorProfile ColorProfile => _parent.ColorProfile;
    public ResolvedLayout Layout => _parent.Layout;

    public SubRenderContext(IRenderContext parent, Region region)
    {
        _parent = parent;
        Region = region;
    }

    public void Write(int col, int row, string text, Style style)
    {
        // Clip to this sub-region before forwarding
        if (row < Region.Row || row >= Region.Row + Region.Height) return;
        if (col >= Region.Col + Region.Width) return;
        _parent.Write(col, row, text, style);
    }
}
