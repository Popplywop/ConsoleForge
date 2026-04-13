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
        else if (widget is BorderBox box && box.Body is not null)
        {
            // BorderBox is not IContainer but wraps a single body widget
            Collect(box.Body, result);
        }
    }

    private static int IndexOf(IReadOnlyList<IFocusable> all, IFocusable target)
    {
        for (var i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], target)) return i;
        return -1;
    }
}
