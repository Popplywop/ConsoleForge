using ConsoleForge.Styling;

namespace ConsoleForge.Layout;

/// <summary>
/// Represents a raw terminal escape sequence payload to be emitted at a specific region,
/// bypassing the cell-based render pipeline.
/// <para>
/// Intended for pixel graphics protocols (Kitty, Sixel) where the terminal interprets
/// binary escape blobs rather than styled character cells. Implement this interface and
/// pass an instance to <see cref="IRenderContext.WriteRawEscape"/> from within a widget's
/// <c>Render</c> method.
/// </para>
/// </summary>
public interface IRawEscapePayload
{
    /// <summary>
    /// Stable identity for this payload's visual content — equal hashes mean the terminal
    /// already holds this content and does not need it transmitted again.
    /// </summary>
    /// <remarks>
    /// The framework tracks payloads across frames by this value, not by region. A payload
    /// whose hash was present last frame is re-placed via <see cref="Refresh"/> instead of
    /// re-encoded, wherever it has moved to; one whose hash is new is sent through
    /// <see cref="Encode"/>. This is what lets a payload move every frame — a scrolling row
    /// of images — without re-uploading it.
    /// <para>
    /// Change the hash whenever the content changes, so the next frame re-encodes.
    /// </para>
    /// </remarks>
    int ContentHash { get; }

    /// <summary>
    /// Encode the payload as one or more raw terminal escape sequences positioned at
    /// <paramref name="region"/>. Multiple strings are supported for protocols that
    /// require chunked transmission (e.g. Kitty's 4096-byte base64 chunks).
    /// The framework emits a cursor-move sequence to <paramref name="region"/>'s
    /// top-left corner immediately before the first string.
    /// </summary>
    /// <param name="region">The terminal region allocated for this payload.</param>
    /// <param name="profile">Active terminal color profile — may influence encoding.</param>
    IEnumerable<string> Encode(Region region, ColorProfile profile);

    /// <summary>
    /// Optional lightweight re-placement sequence emitted every frame when
    /// <see cref="Encode"/> is skipped due to a hash match.
    /// <para>
    /// Implement this when the protocol uses cursor-based placement and the
    /// terminal may have moved its cursor between frames (e.g. tmux re-render
    /// cycles). The default no-op is correct for protocols whose placement is
    /// fully encoded in <see cref="Encode"/>.
    /// </para>
    /// <para>
    /// <b>Kitty:</b> returns a single cheap <c>a=p</c> command that re-displays
    /// the already-uploaded image at the current cursor position — no PNG data
    /// is re-transmitted.
    /// </para>
    /// </summary>
    IEnumerable<string>? Refresh(Region region, ColorProfile profile) => null;

    /// <summary>
    /// the next frame (i.e. the widget was removed or moved). Return
    /// <see langword="null"/> when no cleanup is required.
    /// Kitty implementations use this to delete the placed image by its image-id,
    /// preventing ghost images after a widget is unmounted.
    /// </summary>
    /// <param name="region">The region this payload last occupied.</param>
    string? Cleanup(Region region);
}
