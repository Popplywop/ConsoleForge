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
    /// Cursor information for TextArea and TextInput widgets
    /// </summary>
    CursorDescriptor? Cursor { get; }

    /// <summary>
    /// Write a styled string at an absolute terminal position.
    /// The call is a no-op if (col, row) falls outside Region.
    /// </summary>
    void Write(int col, int row, string text, Style style);

    /// <summary>
    /// If <paramref name="widget"/> (same reference) was rendered at the same
    /// <paramref name="region"/> last frame, copy its cells from the previous buffer
    /// and return true. Caller should skip rendering that widget.
    /// A hit re-registers the widget for the current frame, so the caller must not
    /// call <see cref="RegisterWidget"/> again for it.
    /// Default implementation returns false (no caching).
    /// </summary>
    bool TryReuseWidget(IWidget widget, Region region) => false;

    /// <summary>
    /// Record that <paramref name="widget"/> was rendered at <paramref name="region"/>.
    /// Used by the render cache for next-frame reuse.
    /// Default implementation is a no-op.
    /// </summary>
    void RegisterWidget(IWidget widget, Region region) { }

    /// <summary>
    /// Set <paramref name="cursor"/> on the Rendering Context
    /// </summary>
    void SetCursorDescriptor(CursorDescriptor cursor);

    /// <summary>
    /// Register a raw escape payload for <paramref name="region"/>.
    /// All terminal cells covered by the region are sentinel-filled so the cell diff
    /// does not overwrite the image with styled spaces. The payload's sequences are
    /// emitted after the full cell-diff pass in the same frame, preceded by a
    /// cursor-move to <paramref name="region"/>'s top-left corner.
    /// Re-emission is skipped when <see cref="IRawEscapePayload.ContentHash"/> and
    /// the region both match the previous frame (hash-based deduplication).
    /// Default implementation is a no-op — test fakes and
    /// <see cref="SubRenderContext"/> override this.
    /// </summary>
    void WriteRawEscape(Region region, IRawEscapePayload payload) { }
}