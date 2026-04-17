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
    /// <summary>Child widgets in declaration order.</summary>
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
    /// <summary>
    /// Returns <see langword="true"/> for composite widgets whose render cost
    /// justifies cache lookup overhead. Leaf widgets (TextBlock, TextInput, etc.)
    /// render faster than the cache lookup — skip them.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool IsComposite(IWidget w) =>
        w is IContainer or ISingleBodyWidget or ILayeredContainer;

        /// </summary>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        // Fill background using theme BaseStyle when the effective style has any properties
        var bgStyle = Style.Inherit(ctx.Theme.BaseStyle);
        if (!bgStyle.Equals(Styling.Style.Default))
        {
            var fill = new string(' ', region.Width);
            for (var r = 0; r < region.Height; r++)
                ctx.Write(region.Col, region.Row + r, fill, bgStyle);
        }

        if (Children.Count == 0) return;

        // ── Fast-path gate: no container padding and no child margins ───────────────
        // Pre-check is O(1) + O(n) scan with early exit.
        // When true, falls through to the original compact loop (zero overhead vs baseline).
        if (!Style.HasPadding)
        {
            bool anyMargin = false;
            for (int k = 0; k < Children.Count; k++)
                if (Children[k].Style.HasMargin) { anyMargin = true; break; }

            if (!anyMargin)
            {
                // ── ORIGINAL TIGHT LOOP — exact baseline code ──────────────────
                bool isHO = Direction == Axis.Horizontal;
                int availO = isHO ? region.Width : region.Height;
                var resO = new int[Children.Count];
                int tFO = 0, tWO = 0;
                for (var i = 0; i < Children.Count; i++)
                {
                    int sz = ResolveFixed(isHO ? Children[i].Width : Children[i].Height);
                    if (sz >= 0) { resO[i] = sz; tFO += sz; }
                    else { int w = GetFlexWeight(isHO ? Children[i].Width : Children[i].Height); resO[i] = -w; tWO += w; }
                }
                int frO = Math.Max(0, availO - tFO), dO = 0, lO = -1;
                for (var i = 0; i < Children.Count; i++)
                    if (resO[i] < 0) { int w = -resO[i]; int s = tWO > 0 ? frO * w / tWO : 0; resO[i] = s; dO += s; lO = i; }
                if (lO >= 0) resO[lO] += frO - dO;

                int curO = isHO ? region.Col : region.Row;
                for (var i = 0; i < Children.Count; i++)
                {
                    int sz = Math.Max(0, resO[i]); if (sz == 0) { curO += sz; continue; }
                    Region cr = isHO ? new Region(curO, region.Row, sz, region.Height)
                                     : new Region(region.Col, curO, region.Width, sz);
                    curO += sz;
                    if (Scrollable && ScrollOffset > 0)
                        cr = Direction == Axis.Vertical ? cr with { Row = cr.Row - ScrollOffset }
                                                        : cr with { Col = cr.Col - ScrollOffset };
                    if (!Overlaps(cr, region)) continue;
                    Region cl = Clip(cr, region);
                    if (cl.Width <= 0 || cl.Height <= 0) continue;
                    if (IsComposite(Children[i]) && ctx.TryReuseWidget(Children[i], cl))
                    { ctx.RegisterWidget(Children[i], cl); }
                    else
                    { Children[i].Render(new SubRenderContext(ctx, cl));
                      if (IsComposite(Children[i])) ctx.RegisterWidget(Children[i], cl); }
                }
                return; // ← fast path exits here
            }
        }

        // ── Full path: container padding and/or at least one child has margin ────────
        int cPL = Style.HasPadding ? Style.PaddingLeft   : 0;
        int cPT = Style.HasPadding ? Style.PaddingTop    : 0;
        int cPR = Style.HasPadding ? Style.PaddingRight  : 0;
        int cPB = Style.HasPadding ? Style.PaddingBottom : 0;
        int lW  = region.Width  - cPL - cPR;
        int lH  = region.Height - cPT - cPB;
        if (lW <= 0 || lH <= 0) return;
        int lCol = region.Col + cPL, lRow = region.Row + cPT;
        var lReg = new Region(lCol, lRow, lW, lH);

        bool isH = Direction == Axis.Horizontal;
        int avail = isH ? lW : lH;
        var resolved = new int[Children.Count];
        int tF = 0, tW = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var cs = Children[i].Style;
            int mM = cs.HasMargin ? (isH ? cs.MarginLeft + cs.MarginRight : cs.MarginTop + cs.MarginBottom) : 0;
            int sz = ResolveFixed(isH ? Children[i].Width : Children[i].Height);
            if (sz >= 0) { resolved[i] = sz + mM; tF += sz + mM; }
            else { int w = GetFlexWeight(isH ? Children[i].Width : Children[i].Height); resolved[i] = -w; tW += w; }
        }
        int fr = Math.Max(0, avail - tF), di = 0, la = -1;
        for (var i = 0; i < Children.Count; i++)
            if (resolved[i] < 0) { int w = -resolved[i]; int s = tW > 0 ? fr * w / tW : 0; resolved[i] = s; di += s; la = i; }
        if (la >= 0) resolved[la] += fr - di;

        int cur = isH ? lCol : lRow, cross = isH ? lH : lW, crossO = isH ? lRow : lCol;
        for (var i = 0; i < Children.Count; i++)
        {
            int ts = Math.Max(0, resolved[i]); if (ts == 0) { cur += ts; continue; }
            var cs = Children[i].Style;
            int mS = cs.HasMargin ? (isH ? cs.MarginLeft   : cs.MarginTop)    : 0;
            int mE = cs.HasMargin ? (isH ? cs.MarginRight  : cs.MarginBottom) : 0;
            int cS = cs.HasMargin ? (isH ? cs.MarginTop    : cs.MarginLeft)   : 0;
            int cE = cs.HasMargin ? (isH ? cs.MarginBottom : cs.MarginRight)  : 0;
            Region cr = isH
                ? new Region(cur + mS, crossO + cS, Math.Max(0, ts - mS - mE), Math.Max(0, cross - cS - cE))
                : new Region(crossO + cS, cur + mS, Math.Max(0, cross - cS - cE), Math.Max(0, ts - mS - mE));
            cur += ts;
            if (Scrollable && ScrollOffset > 0)
                cr = Direction == Axis.Vertical ? cr with { Row = cr.Row - ScrollOffset }
                                                : cr with { Col = cr.Col - ScrollOffset };
            if (!Overlaps(cr, lReg)) continue;
            Region cl = Clip(cr, lReg);
            if (cl.Width <= 0 || cl.Height <= 0) continue;
            if (IsComposite(Children[i]) && ctx.TryReuseWidget(Children[i], cl))
            { ctx.RegisterWidget(Children[i], cl); }
            else
            { Children[i].Render(new SubRenderContext(ctx, cl));
              if (IsComposite(Children[i])) ctx.RegisterWidget(Children[i], cl); }
        }
    }

    // Removed helper methods — fast path and full path both inline above.

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
