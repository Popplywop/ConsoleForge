using ConsoleForge.Layout;

namespace ConsoleForge.Core;

/// <summary>
/// Works out, frame by frame, which keyed focusables need a <see cref="RegionChangedMsg"/>:
/// those that appeared, moved or resized since the last report. Keys that leave a frame are
/// forgotten, so a widget that returns is reported again.
/// </summary>
/// <remarks>
/// Not thread-safe; the caller serialises frames (App holds the render lock). Allocates
/// nothing on a frame where nothing changed.
/// </remarks>
internal sealed class RegionReporter
{
    private readonly Dictionary<string, Region> _reported = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<IFocusable> _focusables = [];
    private readonly List<string> _gone = [];

    /// <summary>
    /// Compare the frame described by <paramref name="root"/> and <paramref name="layout"/>
    /// with the last one reported, and pass a message to <paramref name="post"/> for each
    /// keyed focusable whose region is new or different. With duplicate keys, the first
    /// focusable in declaration order is the one reported.
    /// </summary>
    public void Report(IWidget root, ResolvedLayout layout, Action<RegionChangedMsg> post)
    {
        FocusManager.CollectFocusable(root, _focusables);
        _seen.Clear();

        foreach (var f in _focusables)
        {
            if (f.FocusKey is not string key) continue;
            if (!_seen.Add(key)) continue;
            if (!layout.TryGetRegion(f, out var region)) continue;

            if (_reported.TryGetValue(key, out var previous) && previous == region) continue;
            _reported[key] = region;
            post(new RegionChangedMsg(key, region));
        }

        _gone.Clear();
        foreach (var key in _reported.Keys)
            if (!_seen.Contains(key)) _gone.Add(key);
        foreach (var key in _gone)
            _reported.Remove(key);

        _focusables.Clear(); // hold no widgets past the frame
    }
}
