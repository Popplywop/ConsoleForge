namespace ConsoleForge.Layout;

/// <summary>
/// Result of layout resolution: maps each widget to its allocated terminal region.
/// </summary>
public sealed class ResolvedLayout
{
    public ResolvedLayout(Dictionary<IWidget, Region> allocations)
    {
        Allocations = allocations;
    }

    /// <summary>Map from each widget to its allocated absolute terminal region.</summary>
    public Dictionary<IWidget, Region> Allocations { get; }

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
}
