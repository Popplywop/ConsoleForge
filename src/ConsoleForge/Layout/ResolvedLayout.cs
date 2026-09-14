using System.Runtime.CompilerServices;

namespace ConsoleForge.Layout;

/// <summary>
/// Result of layout resolution: maps each widget to its allocated terminal region.
/// </summary>
public sealed class ResolvedLayout
{
    private static readonly IEqualityComparer<IWidget> Identity = new WidgetIdentity();

    public ResolvedLayout() => Allocations = new Dictionary<IWidget, Region>(Identity);

    /// <summary>Map from each widget to its allocated absolute terminal region.</summary>
    public Dictionary<IWidget, Region> Allocations { get; }

    internal void Reset() => Allocations.Clear();

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