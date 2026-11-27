namespace ConsoleForge.Layout;

/// <summary>
/// A layer that hides what is beneath it from the pointer, not just from view. Hit-testing
/// stops at it: a click it covers reaches nothing in the layers under it.
/// </summary>
internal interface IPointerOpaque
{
    /// <summary>
    /// Whether the cell at <paramref name="col"/>, <paramref name="row"/> is covered, given
    /// the widget's allocated <paramref name="region"/>.
    /// </summary>
    bool BlocksPointerAt(Region region, int col, int row);
}
