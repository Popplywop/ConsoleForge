using System.Buffers;
using ConsoleForge.Styling;

namespace ConsoleForge.Layout;

/// <summary>Axis for container layout direction.</summary>
public enum Axis { Horizontal, Vertical }

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
    public static ResolvedLayout Resolve(IWidget root, int terminalWidth, int terminalHeight)
    {
        var allocations = new Dictionary<IWidget, Region>();
        var rootRegion = new Region(0, 0, terminalWidth, terminalHeight);
        Allocate(root, rootRegion, allocations);
        return new ResolvedLayout(allocations);
    }

    private static void Allocate(IWidget widget, Region region, Dictionary<IWidget, Region> allocations)
    {
        allocations[widget] = region;

        if (widget is IContainer container)
            AllocateContainer(container, region, allocations);
        else if (widget is ILayeredContainer layered)
        {
            // Each layer shares the same outer region.
            foreach (var layer in layered.Layers)
                Allocate(layer, region, allocations);
        }
        else if (widget is ISingleBodyWidget wrapper && wrapper.Body is not null)
        {
            // Recurse into the body using the widget's declared body region.
            var bodyRegion = wrapper.ComputeBodyRegion(region);
            Allocate(wrapper.Body, bodyRegion, allocations);
        }
    }

    private static void AllocateContainer(IContainer container, Region region, Dictionary<IWidget, Region> allocations)
    {
        var children = container.Children;
        if (children.Count == 0) return;

        bool isHorizontal = container.Direction == Axis.Horizontal;

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
                var resolved  = ArrayPool<int>.Shared.Rent(children.Count);
                try
                {
                    LayoutSolver.ResolveSizes(
                        children, container.Direction, available,
                        crossAvailable: isHorizontal ? region.Height : region.Width,
                        includeMargins: false, clampOverflow: true,
                        resolved.AsSpan(0, children.Count));

                    int cursor = isHorizontal ? region.Col : region.Row;
                    for (var i = 0; i < children.Count; i++)
                    {
                        int size = Math.Max(0, resolved[i]);
                        Region childRegion = isHorizontal
                            ? new Region(cursor, region.Row, size, region.Height)
                            : new Region(region.Col, cursor, region.Width, size);
                        cursor += size;
                        Allocate(children[i], childRegion, allocations);
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
            cPadT = cw.Style.PaddingTop;    cPadR = cw.Style.PaddingRight;
            cPadB = cw.Style.PaddingBottom; cPadL = cw.Style.PaddingLeft;
        }
        var layout = new Region(
            region.Col + cPadL, region.Row + cPadT,
            Math.Max(0, region.Width  - cPadL - cPadR),
            Math.Max(0, region.Height - cPadT - cPadB));

        int avail2 = isHorizontal ? layout.Width : layout.Height;
        var resolved2 = ArrayPool<int>.Shared.Rent(children.Count);
        try
        {
            LayoutSolver.ResolveSizes(
                children, container.Direction, avail2,
                crossAvailable: isHorizontal ? layout.Height : layout.Width,
                includeMargins: true, clampOverflow: true,
                resolved2.AsSpan(0, children.Count));

            int cursor2 = isHorizontal ? layout.Col : layout.Row;
            int cross2   = isHorizontal ? layout.Height : layout.Width;
            for (var i = 0; i < children.Count; i++)
            {
                int ts = Math.Max(0, resolved2[i]);
                var cs = children[i].Style;
                int mS = isHorizontal ? (cs.HasMargin ? cs.MarginLeft   : 0) : (cs.HasMargin ? cs.MarginTop    : 0);
                int mE = isHorizontal ? (cs.HasMargin ? cs.MarginRight  : 0) : (cs.HasMargin ? cs.MarginBottom : 0);
                int cS = isHorizontal ? (cs.HasMargin ? cs.MarginTop    : 0) : (cs.HasMargin ? cs.MarginLeft   : 0);
                int cE = isHorizontal ? (cs.HasMargin ? cs.MarginBottom : 0) : (cs.HasMargin ? cs.MarginRight  : 0);
                int cm = Math.Max(0, ts - mS - mE), cc = Math.Max(0, cross2 - cS - cE);
                int xS = (isHorizontal ? layout.Row : layout.Col) + cS;
                Region childRegion = isHorizontal
                    ? new Region(cursor2 + mS, xS, cm, cc)
                    : new Region(xS, cursor2 + mS, cc, cm);
                cursor2 += ts;
                Allocate(children[i], childRegion, allocations);
            }
        }
        finally { ArrayPool<int>.Shared.Return(resolved2); }
    }

}
