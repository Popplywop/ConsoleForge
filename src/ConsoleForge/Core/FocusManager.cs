using ConsoleForge.Layout;
using ConsoleForge.Widgets;

namespace ConsoleForge.Core;

/// <summary>
/// Stateless helper that traverses a widget tree depth-first and finds
/// all <see cref="IFocusable"/> instances in declaration order.
/// </summary>
public static class FocusManager
{
    /// <summary>
    /// Collect all <see cref="IFocusable"/> widgets from the tree rooted at
    /// <paramref name="root"/> in depth-first, declaration order.
    /// </summary>
    public static IReadOnlyList<IFocusable> CollectFocusable(IWidget root)
    {
        var result = new List<IFocusable>();
        Collect(root, result);
        return result;
    }

    /// <summary>
    /// Return the next focusable after <paramref name="current"/> (wrapping).
    /// If <paramref name="current"/> is null or not found, returns the first item.
    /// Returns null if <paramref name="all"/> is empty.
    /// </summary>
    public static IFocusable? GetNext(IFocusable? current, IReadOnlyList<IFocusable> all)
    {
        if (all.Count == 0) return null;
        if (current is null) return all[0];
        var idx = IndexOf(all, current);
        return idx < 0 ? all[0] : all[(idx + 1) % all.Count];
    }

    /// <summary>
    /// Return the previous focusable before <paramref name="current"/> (wrapping).
    /// If <paramref name="current"/> is null or not found, returns the last item.
    /// Returns null if <paramref name="all"/> is empty.
    /// </summary>
    public static IFocusable? GetPrev(IFocusable? current, IReadOnlyList<IFocusable> all)
    {
        if (all.Count == 0) return null;
        if (current is null) return all[all.Count - 1];
        var idx = IndexOf(all, current);
        return idx < 0 ? all[all.Count - 1] : all[(idx - 1 + all.Count) % all.Count];
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void Collect(IWidget widget, List<IFocusable> result)
    {
        if (widget is IFocusable f) result.Add(f);

        if (widget is IContainer container)
        {
            foreach (var child in container.Children)
                Collect(child, result);
        }
        else if (widget is ILayeredContainer layered)
        {
            foreach (var layer in layered.Layers)
                Collect(layer, result);
        }
        else if (widget is ISingleBodyWidget wrapper && wrapper.Body is not null)
        {
            Collect(wrapper.Body, result);
        }
    }

    private static int IndexOf(IReadOnlyList<IFocusable> all, IFocusable target)
    {
        for (var i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], target)) return i;
        return -1;
    }

    /// <summary>
    /// Returns the <see cref="IFocusable"/> whose allocated region in
    /// <paramref name="layout"/> contains the point (<paramref name="col"/>,
    /// <paramref name="row"/>), or null if none.
    /// When multiple widgets overlap (e.g. inside a ZStack), the last one
    /// in depth-first order wins (topmost layer).
    /// </summary>
    public static IFocusable? FindFocusableAt(
        IWidget root, ConsoleForge.Layout.ResolvedLayout layout, int col, int row)
    {
        var all = CollectFocusable(root);
        IFocusable? best = null;
        foreach (var f in all)
        {
            var region = layout.GetRegion((ConsoleForge.Layout.IWidget)f);
            if (region is null) continue;
            var r = region.Value;
            if (col >= r.Col && col < r.Col + r.Width &&
                row >= r.Row && row < r.Row + r.Height)
                best = f; // last match wins (topmost layer)
        }
        return best;
    }
}
