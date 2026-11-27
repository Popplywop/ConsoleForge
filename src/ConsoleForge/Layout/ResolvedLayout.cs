using System.Runtime.CompilerServices;

namespace ConsoleForge.Layout;

/// <summary>
/// Result of layout resolution: maps each widget to its allocated terminal region.
/// </summary>
public sealed class ResolvedLayout
{
    private static readonly IEqualityComparer<IWidget> Identity = new WidgetIdentity();

    /// <summary>
    /// Create an empty layout. Widgets are keyed by reference identity, not by value,
    /// so two equal records still occupy separate entries.
    /// </summary>
    public ResolvedLayout() => Allocations = new Dictionary<IWidget, Region>(Identity);

    /// <summary>Map from each widget to its allocated absolute terminal region.</summary>
    public Dictionary<IWidget, Region> Allocations { get; }

    internal void Reset()
    {
        Allocations.Clear();
        _clips?.Clear();
    }

    // Visible part of each widget inside a scrolling container. Such a widget keeps its
    // whole region — it renders as if unclipped — but only this part of it is on screen,
    // so only this part may answer a click. Absent for everything else, which is visible
    // wherever its region is.
    private Dictionary<IWidget, Region>? _clips;

    internal void SetClip(IWidget widget, Region clip)
        => (_clips ??= new Dictionary<IWidget, Region>(Identity))[widget] = clip;

    /// <summary>
    /// The part of <paramref name="widget"/>'s region that is on screen: its region,
    /// unless a scrolling container has scrolled some or all of it out of view.
    /// </summary>
    public Region? GetVisibleRegion(IWidget widget)
    {
        if (!Allocations.TryGetValue(widget, out var region)) return null;
        return _clips is not null && _clips.TryGetValue(widget, out var clip) ? clip : region;
    }

    /// <summary>Get the region for a specific widget, or null if not found.</summary>
    public Region? GetRegion(IWidget widget) =>
        Allocations.TryGetValue(widget, out var region) ? region : null;

    /// <summary>Try-get pattern for use in Container.Render().</summary>
    public bool TryGetRegion(IWidget widget, out Region region)
    {
        if (Allocations.TryGetValue(widget, out region)) return true;
        region = default;
        return false;
    }

    /// <summary>
    /// Keys allocations by widget identity rather than value.
    /// <para>
    /// Widgets are records, so two structurally identical widgets in the same tree are
    /// equal and hash alike. Under the default comparer they collapse into one entry and
    /// the later one's region overwrites the earlier one's, leaving the first widget
    /// reporting a region it does not occupy. Rendering does not notice — containers
    /// re-solve their own children through <see cref="LayoutSolver"/> — but everything
    /// that looks a widget up by region does, starting with hit-testing.
    /// </para>
    /// </summary>
    private sealed class WidgetIdentity : IEqualityComparer<IWidget>
    {
        public bool Equals(IWidget? x, IWidget? y) => ReferenceEquals(x, y);

        public int GetHashCode(IWidget obj) => RuntimeHelpers.GetHashCode(obj);
    }
}