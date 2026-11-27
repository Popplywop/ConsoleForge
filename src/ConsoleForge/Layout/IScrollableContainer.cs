namespace ConsoleForge.Layout;

/// <summary>
/// A container that can scroll its children along <see cref="IContainer.Direction"/>.
/// Layout gives such children their own sizes even when they overflow, offsets them by
/// <see cref="ScrollOffset"/>, and records what of each is still visible.
/// </summary>
internal interface IScrollableContainer : IContainer
{
    /// <summary>Whether the container scrolls; when false it lays out like any other.</summary>
    bool Scrollable { get; }

    /// <summary>Cells scrolled past along the axis, before clamping to the content.</summary>
    int ScrollOffset { get; }
}
