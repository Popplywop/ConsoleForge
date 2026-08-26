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
    /// Stable hash derived from this payload's visual content.
    /// The framework skips re-emitting sequences when this value matches the previous
    /// frame's value for the same <see cref="Region"/>. Change the hash whenever the
    /// image data changes so the framework re-emits on the next frame.
    /// </summary>
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
