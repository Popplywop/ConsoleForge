using System.Buffers;

namespace ConsoleForge.Layout;

/// <summary>Axis for container layout direction.</summary>
public enum Axis
{
    /// <summary>Children are laid out left to right.</summary>
    Horizontal,

    /// <summary>Children are laid out top to bottom.</summary>
    Vertical
}

/// <summary>
/// Two-pass layout algorithm that resolves widget size constraints
/// into absolute terminal regions.
/// Pass 1: assign fixed sizes, compute free space.
/// Pass 2: distribute free space proportionally to flex weights.
/// </summary>
public static class LayoutEngine
{
    /// <summary>
    /// Resolve <paramref name="root"/> into a <see cref="ResolvedLayout"/>
    /// filling the given terminal dimensions.
    /// </summary>
    public static ResolvedLayout Resolve(IWidget root, int w, int h)
        => ResolveInto(new ResolvedLayout(), root, w, h);

    /// <summary>
    /// Resolve <paramref name="root"/> into <paramref name="target"/>, reusing its
    /// allocation dictionary instead of allocating a fresh <see cref="ResolvedLayout"/>.
    /// The render loop calls this every frame to keep layout off the allocation path;
    /// <paramref name="target"/> is cleared first, so any regions it held are discarded.
    /// </summary>
    /// <param name="target">Layout to clear and fill. Returned for convenience.</param>
    /// <param name="root">Root of the widget tree to resolve.</param>
    /// <param name="w">Available width in terminal columns.</param>
    /// <param name="h">Available height in terminal rows.</param>
    public static ResolvedLayout ResolveInto(ResolvedLayout target, IWidget root, int w, int h)
    {
        target.Reset();
        Allocate(root, new Region(0, 0, w, h), target, clip: null);
        return target;
    }

    // `clip` is the visible area when `widget` is inside a scrolling container, which
    // may have scrolled part of it out of view; null everywhere else.
    private static void Allocate(IWidget widget, Region region, ResolvedLayout target, Region? clip)
    {
        if (!target.Allocations.TryAdd(widget, region))
        {
            target.Allocations[widget] = region;
            target.HasDuplicates = true;
        }
        if (clip is { } c) target.SetClip(widget, Intersect(region, c));

        if (widget is IContainer container)
            AllocateContainer(container, region, target, clip);
        else if (widget is ILayeredContainer layered)
        {
            // Each layer shares the same outer region. Indexed: enumerating the interface
            // would box an enumerator per layered widget per frame.
            var layers = layered.Layers;
            for (int i = 0; i < layers.Count; i++)
                Allocate(layers[i], region, target, clip);
        }
        else if (widget is ISingleBodyWidget wrapper && wrapper.Body is not null)
        {
            // Recurse into the body using the widget's declared body region.
            var bodyRegion = wrapper.ComputeBodyRegion(region);
            Allocate(wrapper.Body, bodyRegion, target, clip);
        }
    }

    private static Region Intersect(Region a, Region b)
    {
        int col = Math.Max(a.Col, b.Col), row = Math.Max(a.Row, b.Row);
        int right = Math.Min(a.Col + a.Width, b.Col + b.Width);
        int bottom = Math.Min(a.Row + a.Height, b.Row + b.Height);
        return new Region(col, row, Math.Max(0, right - col), Math.Max(0, bottom - row));
    }

    private static void AllocateContainer(IContainer container, Region region, ResolvedLayout target, Region? clip)
    {
        var children = container.Children;
        if (children.Count == 0) return;

        bool isHorizontal = container.Direction == Axis.Horizontal;
        bool scrollable   = container is IScrollableContainer { Scrollable: true };
        int scrollOffset  = scrollable ? ((IScrollableContainer)container).ScrollOffset : 0;

        // ── Fast-path gate: no padding on container, no margins on children ────────────
        bool hasPad = container is IWidget cwp && cwp.Style.HasPadding;
        if (!hasPad)
        {
            bool anyMargin = false;
            for (int k = 0; k < children.Count; k++)
                if (children[k].Style.HasMargin) { anyMargin = true; break; }

            if (!anyMargin)
            {
                // ── ORIGINAL TIGHT LOOP ─────────────────────────────────────
                int available = isHorizontal ? region.Width : region.Height;
                var resolved = ArrayPool<int>.Shared.Rent(children.Count);
                try
                {
                    var sizes = resolved.AsSpan(0, children.Count);
                    LayoutSolver.ResolveSizes(
                        children, container.Direction, available,
                        crossAvailable: isHorizontal ? region.Height : region.Width,
                        includeMargins: false, throwWhenImpossible: true,
                        sizes, scrollable);

                    var childClip = scrollable ? Intersect(region, clip ?? region) : clip;
                    int cursor = (isHorizontal ? region.Col : region.Row)
                               - (scrollable ? LayoutSolver.ClampScroll(scrollOffset, sizes, available) : 0);
                    for (var i = 0; i < children.Count; i++)
                    {
                        int size = Math.Max(0, resolved[i]);
                        Region childRegion = isHorizontal
                            ? new Region(cursor, region.Row, size, region.Height)
                            : new Region(region.Col, cursor, region.Width, size);
                        cursor += size;
                        Allocate(children[i], childRegion, target, childClip);
                    }
                }
                finally { ArrayPool<int>.Shared.Return(resolved); }
                return; // ← fast path exits here
            }
        }

        // ── Full path: container has padding and/or at least one child has margin ──────
        int cPadT = 0, cPadR = 0, cPadB = 0, cPadL = 0;
        if (hasPad && container is IWidget cw)
        {
            cPadT = cw.Style.PaddingTop; cPadR = cw.Style.PaddingRight;
            cPadB = cw.Style.PaddingBottom; cPadL = cw.Style.PaddingLeft;
        }
        var layout = new Region(
            region.Col + cPadL, region.Row + cPadT,
            Math.Max(0, region.Width - cPadL - cPadR),
            Math.Max(0, region.Height - cPadT - cPadB));

        int avail2 = isHorizontal ? layout.Width : layout.Height;
        var resolved2 = ArrayPool<int>.Shared.Rent(children.Count);
        try
        {
            var sizes2 = resolved2.AsSpan(0, children.Count);
            LayoutSolver.ResolveSizes(
                children, container.Direction, avail2,
                crossAvailable: isHorizontal ? layout.Height : layout.Width,
                includeMargins: true, throwWhenImpossible: true,
                sizes2, scrollable);

            var childClip2 = scrollable ? Intersect(layout, clip ?? layout) : clip;
            int cursor2 = (isHorizontal ? layout.Col : layout.Row)
                        - (scrollable ? LayoutSolver.ClampScroll(scrollOffset, sizes2, avail2) : 0);
            int cross2 = isHorizontal ? layout.Height : layout.Width;
            for (var i = 0; i < children.Count; i++)
            {
                int ts = Math.Max(0, resolved2[i]);
                var cs = children[i].Style;
                int mS = isHorizontal ? (cs.HasMargin ? cs.MarginLeft : 0) : (cs.HasMargin ? cs.MarginTop : 0);
                int mE = isHorizontal ? (cs.HasMargin ? cs.MarginRight : 0) : (cs.HasMargin ? cs.MarginBottom : 0);
                int cS = isHorizontal ? (cs.HasMargin ? cs.MarginTop : 0) : (cs.HasMargin ? cs.MarginLeft : 0);
                int cE = isHorizontal ? (cs.HasMargin ? cs.MarginBottom : 0) : (cs.HasMargin ? cs.MarginRight : 0);
                int cm = Math.Max(0, ts - mS - mE), cc = Math.Max(0, cross2 - cS - cE);
                int xS = (isHorizontal ? layout.Row : layout.Col) + cS;
                Region childRegion = isHorizontal
                    ? new Region(cursor2 + mS, xS, cm, cc)
                    : new Region(xS, cursor2 + mS, cc, cm);
                cursor2 += ts;
                Allocate(children[i], childRegion, target, childClip2);
            }
        }
        finally { ArrayPool<int>.Shared.Return(resolved2); }
    }
}