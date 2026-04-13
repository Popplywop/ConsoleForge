using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A layout container that arranges child widgets along a given axis.
/// Participates in two-pass layout via <see cref="IContainer"/>.
/// Supports optional scrolling along the main axis.
/// </summary>
public sealed class Container : IWidget, IContainer
{
    /// <summary>
    /// Primary constructor matching quickstart usage:
    /// <code>
    /// new Container(Axis.Vertical, children)
    /// new Container(Axis.Horizontal, width: SizeConstraint.Fixed(24), children: [...])
    /// </code>
    /// </summary>
    public Container(
        Axis direction,
        IWidget[]? children = null,
        SizeConstraint? width = null,
        SizeConstraint? height = null,
        Style? style = null,
        bool scrollable = false)
    {
        Direction  = direction;
        Children   = (IReadOnlyList<IWidget>)(children ?? []);
        Width      = width  ?? SizeConstraint.Flex(1);
        Height     = height ?? SizeConstraint.Flex(1);
        if (style is not null) Style = style.Value;
        Scrollable = scrollable;
    }

    // ── IContainer ──────────────────────────────────────────────────────────
    public Axis Direction { get; init; }
    public IReadOnlyList<IWidget> Children { get; init; } = [];

    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);

    // ── Container-specific ──────────────────────────────────────────────────
    /// <summary>Visual style applied as background fill before children render.</summary>
    public Style Style      { get; init; } = Style.Default;
    /// <summary>When true, children that overflow the container's main axis are clipped and scrollable via <see cref="ScrollOffset"/>.</summary>
    public bool  Scrollable { get; init; }

    /// <summary>
    /// Row/column offset for scrollable containers. 0-based lines/columns
    /// from the start of the children along the main axis.
    /// </summary>
    public int ScrollOffset { get; init; }

    // ── Render ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Renders each child into a sub-region computed at render time from
    /// <see cref="IRenderContext.Region"/>. This avoids dependency on
    /// <see cref="ResolvedLayout"/> child regions, which are only valid when
    /// the container sits directly at the layout root (not inside a
    /// <see cref="BorderBox"/> or other non-<see cref="IContainer"/> wrapper).
    /// </summary>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        // T054: fill background using theme BaseStyle if the effective style has any properties
        var bgStyle = Style.Inherit(ctx.Theme.BaseStyle);
        if (!bgStyle.Equals(Styling.Style.Default))
        {
            var fill = new string(' ', region.Width);
            for (var r = 0; r < region.Height; r++)
                ctx.Write(region.Col, region.Row + r, fill, bgStyle);
        }

        if (Children.Count == 0) return;

        // Re-run layout math relative to this context's region.
        bool isHorizontal = Direction == Axis.Horizontal;
        int available = isHorizontal ? region.Width : region.Height;

        // Pass 1: resolve fixed sizes
        var resolved = new int[Children.Count];
        int totalFixed = 0;
        int totalFlexWeight = 0;

        for (var i = 0; i < Children.Count; i++)
        {
            var constraint = isHorizontal ? Children[i].Width : Children[i].Height;
            int size = ResolveFixed(constraint);
            if (size >= 0)
            {
                resolved[i] = size;
                totalFixed += size;
            }
            else
            {
                int weight = GetFlexWeight(constraint);
                resolved[i] = -weight;
                totalFlexWeight += weight;
            }
        }

        // Pass 2: distribute free space to flex children
        int freeSpace = Math.Max(0, available - totalFixed);
        int distributed = 0;
        int lastFlexIndex = -1;
        for (var i = 0; i < Children.Count; i++)
        {
            if (resolved[i] < 0)
            {
                int weight = -resolved[i];
                int share = totalFlexWeight > 0 ? freeSpace * weight / totalFlexWeight : 0;
                resolved[i] = share;
                distributed += share;
                lastFlexIndex = i;
            }
        }
        if (lastFlexIndex >= 0)
            resolved[lastFlexIndex] += freeSpace - distributed;

        // Render each child in its computed sub-region
        int cursor = isHorizontal ? region.Col : region.Row;
        for (var i = 0; i < Children.Count; i++)
        {
            int size = Math.Max(0, resolved[i]);
            if (size == 0) { cursor += size; continue; }

            Region childRegion = isHorizontal
                ? new Region(cursor, region.Row, size, region.Height)
                : new Region(region.Col, cursor, region.Width, size);
            cursor += size;

            // Apply scroll offset for scrollable containers
            if (Scrollable && ScrollOffset > 0)
            {
                childRegion = Direction == Axis.Vertical
                    ? childRegion with { Row = childRegion.Row - ScrollOffset }
                    : childRegion with { Col = childRegion.Col - ScrollOffset };
            }

            // Skip children fully outside the container's region
            if (!Overlaps(childRegion, region)) continue;
            Region clipped = Clip(childRegion, region);
            if (clipped.Width <= 0 || clipped.Height <= 0) continue;

            var sub = new SubRenderContext(ctx, clipped);
            Children[i].Render(sub);
        }
    }

    // ── Layout helpers (mirrors LayoutEngine logic) ──────────────────────────

    private static int ResolveFixed(SizeConstraint constraint) =>
        constraint switch
        {
            SizeConstraint.FixedConstraint f  => f.Size,
            SizeConstraint.AutoConstraint     => -1,   // Auto = flex weight 1 in a Container
            SizeConstraint.MinConstraint m    => Math.Max(m.MinSize, ResolveFixed(m.Inner)),
            SizeConstraint.MaxConstraint mx   => ResolveFixed(mx.Inner) is int inner and >= 0
                                                    ? Math.Min(mx.MaxSize, inner)
                                                    : -1,
            SizeConstraint.FlexConstraint     => -1,
            _                                 => -1
        };

    private static int GetFlexWeight(SizeConstraint constraint) =>
        constraint switch
        {
            SizeConstraint.FlexConstraint f => f.Weight,
            SizeConstraint.MinConstraint m  => GetFlexWeight(m.Inner),
            SizeConstraint.MaxConstraint mx => GetFlexWeight(mx.Inner),
            _                               => 1
        };

    private static bool Overlaps(Region a, Region b) =>
        a.Col < b.Col + b.Width  &&
        a.Col + a.Width  > b.Col &&
        a.Row < b.Row + b.Height &&
        a.Row + a.Height > b.Row;

    private static Region Clip(Region child, Region container)
    {
        int col    = Math.Max(child.Col,  container.Col);
        int row    = Math.Max(child.Row,  container.Row);
        int right  = Math.Min(child.Col  + child.Width,  container.Col  + container.Width);
        int bottom = Math.Min(child.Row  + child.Height, container.Row  + container.Height);
        return new Region(col, row, Math.Max(0, right - col), Math.Max(0, bottom - row));
    }
}
