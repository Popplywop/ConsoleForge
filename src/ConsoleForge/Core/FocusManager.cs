using ConsoleForge.Layout;

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
    /// <see cref="CollectFocusable(IWidget)"/> into a caller-owned list, which is cleared
    /// first — for the per-frame region report, which must not allocate a list per frame.
    /// </summary>
    internal static void CollectFocusable(IWidget root, List<IFocusable> into)
    {
        into.Clear();
        Collect(root, into);
    }

    /// <summary>
    /// Collect the focus key of every <see cref="IFocusable"/> in the tree rooted at
    /// <paramref name="root"/>, in depth-first, declaration order. A widget whose
    /// <see cref="IFocusable.FocusKey"/> is null is skipped — it is not a focus target.
    /// </summary>
    public static IReadOnlyList<string> CollectFocusKeys(IWidget root)
    {
        var keys = new List<string>();
        foreach (var f in CollectFocusable(root))
        {
            if (f.FocusKey is string k) keys.Add(k);
        }
        return keys;
    }

    /// <summary>
    /// Return the key after <paramref name="currentKey"/> in <paramref name="keys"/>,
    /// wrapping past the end. If <paramref name="currentKey"/> is null or not present,
    /// returns the first key. Returns null if <paramref name="keys"/> is empty.
    /// </summary>
    public static string? GetNext(string? currentKey, IReadOnlyList<string> keys)
        => Step(currentKey, keys, +1);

    /// <summary>
    /// Return the key before <paramref name="currentKey"/> in <paramref name="keys"/>,
    /// wrapping past the start. If <paramref name="currentKey"/> is null or not present,
    /// returns the last key. Returns null if <paramref name="keys"/> is empty.
    /// </summary>
    public static string? GetPrev(string? currentKey, IReadOnlyList<string> keys)
        => Step(currentKey, keys, -1);

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void Collect(IWidget widget, List<IFocusable> result)
    {
        if (widget is IFocusable f) result.Add(f);

        // Indexed rather than foreach: enumerating an IReadOnlyList through the interface
        // boxes an enumerator per container, and this walk now runs after every frame.
        if (widget is IContainer container)
        {
            var children = container.Children;
            for (int i = 0; i < children.Count; i++)
                Collect(children[i], result);
        }
        else if (widget is ILayeredContainer layered)
        {
            var layers = layered.Layers;
            for (int i = 0; i < layers.Count; i++)
                Collect(layers[i], result);
        }
        else if (widget is ISingleBodyWidget wrapper && wrapper.Body is not null)
        {
            Collect(wrapper.Body, result);
        }
    }

    private static string? Step(string? currentKey, IReadOnlyList<string> keys, int delta)
    {
        if (keys.Count == 0) return null;

        var idx = IndexOf(keys, currentKey);

        return idx < 0 ? delta > 0 ? keys[0] : keys[keys.Count - 1] : keys[(idx + delta + keys.Count) % keys.Count];
    }

    private static int IndexOf(IReadOnlyList<string> keys, string? key)
    {
        if (key is null) return -1;
        for (var i = 0; i < keys.Count; i++)
        {
            if (StringComparer.Ordinal.Equals(keys[i], key))
            {
                return i;
            }
        }
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
        IWidget root, ResolvedLayout layout, int col, int row)
    {
        var all = CollectFocusable(root);
        IFocusable? best = null;
        foreach (var f in all)
        {
            var region = layout.GetRegion(f);
            if (region is null) continue;
            var r = region.Value;
            if (col >= r.Col && col < r.Col + r.Width &&
                row >= r.Row && row < r.Row + r.Height)
                best = f; // last match wins (topmost layer)
        }
        return best;
    }
}