using ConsoleForge.Styling;

namespace ConsoleForge.Layout;

/// <summary>
/// Passed to IWidget.Render(). Provides the allocated screen region
/// and render-time context (theme, color profile, terminal writer).
/// </summary>
public interface IRenderContext
{
    /// <summary>The allocated region for this widget (absolute terminal coordinates).</summary>
    Region Region { get; }

    /// <summary>Active theme for style inheritance.</summary>
    Theme Theme { get; }

    /// <summary>Detected terminal color capability.</summary>
    ColorProfile ColorProfile { get; }

    /// <summary>
    /// Resolved layout for the current frame. Container widgets use this
    /// to retrieve child regions without re-running layout.
    /// </summary>
    ResolvedLayout Layout { get; }

    /// <summary>
    /// Write a styled string at an absolute terminal position.
    /// The call is a no-op if (col, row) falls outside Region.
    /// </summary>
    void Write(int col, int row, string text, Style style);
}
