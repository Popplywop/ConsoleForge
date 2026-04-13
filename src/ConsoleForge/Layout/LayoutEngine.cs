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

        // Recurse into IContainer children (Container widget implemented in T040)
        if (widget is IContainer container)
            AllocateContainer(container, region, allocations);
    }

    private static void AllocateContainer(IContainer container, Region region, Dictionary<IWidget, Region> allocations)
    {
        var children = container.Children;
        if (children.Count == 0) return;

        bool isHorizontal = container.Direction == Axis.Horizontal;
        int available = isHorizontal ? region.Width : region.Height;

        // Pass 1: resolve fixed sizes — rent array from pool to avoid per-frame allocation
        var resolved = ArrayPool<int>.Shared.Rent(children.Count);
        try
        {
        int totalFixed = 0;
        int totalFlexWeight = 0;

        for (var i = 0; i < children.Count; i++)
        {
            var constraint = isHorizontal ? children[i].Width : children[i].Height;
            int size = ResolveFixed(constraint);
            if (size >= 0)
            {
                resolved[i] = size;
                totalFixed += size;
            }
            else
            {
                // Flex — store negative weight as sentinel
                int weight = GetFlexWeight(constraint);
                resolved[i] = -weight;
                totalFlexWeight += weight;
            }
        }

        // Pass 2: distribute free space to flex children
        int freeSpace = Math.Max(0, available - totalFixed);
        int distributed = 0;
        int lastFlexIndex = -1;
        for (var i = 0; i < children.Count; i++)
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

        // Remainder to last flex child to avoid off-by-one gaps
        if (lastFlexIndex >= 0)
            resolved[lastFlexIndex] += freeSpace - distributed;

        // T059/T061: handle overflow when resolved sizes exceed available space.
        // If there are NO flex children and fixed sizes overflow: throw LayoutConstraintException.
        // If there ARE flex children (or mixed): proportionally clamp all sizes.
        int total = 0;
        for (var i = 0; i < children.Count; i++) total += Math.Max(0, resolved[i]);
        if (total > available && total > 0)
        {
            if (totalFlexWeight == 0)
                throw new LayoutConstraintException(
                    $"Fixed children ({total}px) collectively exceed available space ({available}px) " +
                    $"in a {container.Direction} container with no flex children.");

            // Find the last child with a non-zero size; it absorbs rounding remainder.
            int lastNonZero = -1;
            for (var i = children.Count - 1; i >= 0; i--)
                if (resolved[i] > 0) { lastNonZero = i; break; }

            int remainder = available;
            for (var i = 0; i < children.Count; i++)
            {
                if (resolved[i] <= 0) { resolved[i] = 0; continue; }
                int clamped = i != lastNonZero
                    ? resolved[i] * available / total
                    : remainder;
                resolved[i] = Math.Max(0, clamped);
                remainder -= resolved[i];
            }
        }

        // Allocate each child's region
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
        finally
        {
            ArrayPool<int>.Shared.Return(resolved);
        }
    }

    /// <summary>
    /// Returns the fixed pixel size for a constraint.
    /// Returns -1 if the constraint is (or wraps) a Flex or Auto constraint
    /// (both mean "participate in flex distribution" at layout time).
    /// </summary>
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
