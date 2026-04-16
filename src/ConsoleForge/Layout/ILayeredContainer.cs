namespace ConsoleForge.Layout;

/// <summary>
/// Implemented by widgets that hold multiple stacked layers rendered back-to-front
/// (e.g. <c>ZStack</c>). Used by <see cref="ConsoleForge.Core.FocusManager"/> for
/// focus traversal across all layers.
/// </summary>
public interface ILayeredContainer : IWidget
{
    /// <summary>Layers in back-to-front order. The last element renders on top.</summary>
    IReadOnlyList<IWidget> Layers { get; }
}
