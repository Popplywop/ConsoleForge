namespace ConsoleForge.Layout;

/// <summary>
/// Implemented by widgets that wrap a single body child (e.g. <c>BorderBox</c>, <c>Modal</c>).
/// Used by <see cref="Core.FocusManager"/> and <see cref="LayoutEngine"/> to
/// traverse into the body for focus collection and region allocation.
/// </summary>
public interface ISingleBodyWidget : IWidget
{
    /// <summary>The single child widget contained by this wrapper, or null if empty.</summary>
    IWidget? Body { get; }

    /// <summary>
    /// Compute the region allocated to <see cref="Body"/> given the outer region allocated
    /// to this widget. The default implementation insets by 1 on all sides (one-character
    /// border convention used by <c>BorderBox</c>) plus any additional padding set on the
    /// widget's <see cref="IWidget.Style"/>.
    /// Override for widgets whose body occupies a different sub-region (e.g. <c>Modal</c>
    /// centers its dialog box within the outer region).
    /// </summary>
    Region ComputeBodyRegion(Region outer)
    {
        // Default: 1-char border inset + optional style padding
        var s  = Style;
        int t  = 1 + (s.HasPadding ? s.PaddingTop    : 0);
        int r  = 1 + (s.HasPadding ? s.PaddingRight  : 0);
        int b  = 1 + (s.HasPadding ? s.PaddingBottom : 0);
        int l  = 1 + (s.HasPadding ? s.PaddingLeft   : 0);
        return new Region(
            outer.Col + l,
            outer.Row + t,
            Math.Max(0, outer.Width  - l - r),
            Math.Max(0, outer.Height - t - b));
    }
}