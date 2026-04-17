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
                    int totalFixed = 0, totalFlexWeight = 0;
                    for (var i = 0; i < children.Count; i++)
                    {
                        var constraint = isHorizontal ? children[i].Width : children[i].Height;
                        int size = ResolveFixed(constraint);
                        if (size >= 0) { resolved[i] = size; totalFixed += size; }
                        else { int w = GetFlexWeight(constraint); resolved[i] = -w; totalFlexWeight += w; }
                    }
                    int freeSpace = Math.Max(0, available - totalFixed), distributed = 0, lastFlex = -1;
                    for (var i = 0; i < children.Count; i++)
                        if (resolved[i] < 0) { int w = -resolved[i]; int s = totalFlexWeight > 0 ? freeSpace * w / totalFlexWeight : 0; resolved[i] = s; distributed += s; lastFlex = i; }
                    if (lastFlex >= 0) resolved[lastFlex] += freeSpace - distributed;

                    // Overflow clamping (unchanged)
                    int total = 0;
                    for (var i = 0; i < children.Count; i++) total += Math.Max(0, resolved[i]);
                    if (total > available && total > 0)
                    {
                        if (totalFlexWeight == 0)
                            throw new LayoutConstraintException(
                                $"Fixed children ({total}px) collectively exceed available space ({available}px) " +
                                $"in a {container.Direction} container with no flex children.");
                        int lastNonZero = -1;
                        for (var i = children.Count - 1; i >= 0; i--) if (resolved[i] > 0) { lastNonZero = i; break; }
                        int remainder = available;
                        for (var i = 0; i < children.Count; i++)
                        {
                            if (resolved[i] <= 0) { resolved[i] = 0; continue; }
                            int clamped = i != lastNonZero ? resolved[i] * available / total : remainder;
                            resolved[i] = Math.Max(0, clamped); remainder -= resolved[i];
                        }
                    }

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
            int tF2 = 0, tW2 = 0;
            for (var i = 0; i < children.Count; i++)
            {
                var cs = children[i].Style;
                int mM = isHorizontal
                    ? (cs.HasMargin ? cs.MarginLeft  + cs.MarginRight  : 0)
                    : (cs.HasMargin ? cs.MarginTop   + cs.MarginBottom : 0);
                var constraint = isHorizontal ? children[i].Width : children[i].Height;
                int size = ResolveFixed(constraint);
                if (size >= 0) { resolved2[i] = size + mM; tF2 += size + mM; }
                else { int w = GetFlexWeight(constraint); resolved2[i] = -w; tW2 += w; }
            }
            int fr2 = Math.Max(0, avail2 - tF2), di2 = 0, la2 = -1;
            for (var i = 0; i < children.Count; i++)
                if (resolved2[i] < 0) { int w = -resolved2[i]; int s = tW2 > 0 ? fr2 * w / tW2 : 0; resolved2[i] = s; di2 += s; la2 = i; }
            if (la2 >= 0) resolved2[la2] += fr2 - di2;

            // Overflow clamping for full path
            int total2 = 0;
            for (var i = 0; i < children.Count; i++) total2 += Math.Max(0, resolved2[i]);
            if (total2 > avail2 && total2 > 0)
            {
                if (tW2 == 0)
                    throw new LayoutConstraintException(
                        $"Fixed children ({total2}px) collectively exceed available space ({avail2}px) " +
                        $"in a {container.Direction} container with no flex children.");
                int lastNZ2 = -1;
                for (var i = children.Count - 1; i >= 0; i--) if (resolved2[i] > 0) { lastNZ2 = i; break; }
                int rem2 = avail2;
                for (var i = 0; i < children.Count; i++)
                {
                    if (resolved2[i] <= 0) { resolved2[i] = 0; continue; }
                    int clamped = i != lastNZ2 ? resolved2[i] * avail2 / total2 : rem2;
                    resolved2[i] = Math.Max(0, clamped); rem2 -= resolved2[i];
                }
            }

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

    /// <summary>
    /// Returns the fixed pixel size for a constraint.
    /// Returns -1 if the constraint is (or wraps) a Flex or Auto constraint
    /// (both mean "participate in flex distribution" at layout time).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int ResolveFixed(SizeConstraint constraint) =>
        constraint switch
        {
            SizeConstraint.FixedConstraint f  => f.Size,
            SizeConstraint.AutoConstraint     => -1,   // Auto = flex weight 1, same as Container.cs
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
}
