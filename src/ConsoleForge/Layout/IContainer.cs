namespace ConsoleForge.Layout;

/// <summary>
/// Implemented by widgets that contain and lay out children.
/// Used by <see cref="LayoutEngine"/> to recurse into child regions.
/// </summary>
public interface IContainer : IWidget
{
    /// <summary>Layout direction for children.</summary>
    Axis Direction { get; }

    /// <summary>Child widgets in declaration order.</summary>
    IReadOnlyList<IWidget> Children { get; }
}
