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
    /// whose hash was present last frame is re-placed via <see cref="Place"/> instead of
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
    /// Optional: send this payload's content to the terminal without displaying it, so a
    /// later first appearance costs <see cref="Place"/> rather than <see cref="Encode"/>.
    /// Returning null — the default — means the protocol has no separate transmit step, and
    /// the payload is encoded in full when it first appears, as before.
    /// <para>
    /// Called by <see cref="Core.Cmd.Preload"/>, outside any frame, so it takes no region:
    /// what it emits must not depend on where the payload will be drawn, nor on the cursor.
    /// A payload that implements this must also override <see cref="Place"/>, since the
    /// framework will rely on <see cref="Place"/> to show what was transmitted.
    /// </para>
    /// <para>
    /// <b>Kitty:</b> the <c>a=t</c> upload chunks that <see cref="Encode"/> starts with.
    /// </para>
    /// </summary>
    /// <param name="profile">Active terminal color profile — may influence encoding.</param>
    IEnumerable<string>? Transmit(ColorProfile profile) => null;

    /// <summary>
    /// Re-position content the terminal already holds, without re-transmitting it. Called
    /// when this payload was present last frame at a <em>different</em> region — the
    /// scrolling case.
    /// <para>
    /// The framework has already emitted <see cref="Cleanup"/> for the old region, so this
    /// is not optional: returning nothing leaves the payload off the screen entirely. The
    /// default re-runs <see cref="Encode"/>, which is correct for any protocol without a
    /// cheaper way to move content; override it when one exists.
    /// </para>
    /// <para>
    /// <b>Kitty:</b> a single <c>a=p</c> naming the already-uploaded image id, so a moving
    /// image costs one short command per frame rather than its whole payload.
    /// </para>
    /// </summary>
    /// <param name="region">The region the payload occupies this frame.</param>
    /// <param name="profile">Active terminal color profile — may influence encoding.</param>
    IEnumerable<string> Place(Region region, ColorProfile profile) => Encode(region, profile);

    /// <summary>
    /// Optional per-frame renewal for a payload that has <em>not</em> moved and is already
    /// on screen. Returning null — the default — means a stationary payload costs nothing,
    /// which is the common case.
    /// <para>
    /// Implement this only when the terminal loses a placement that is left alone, e.g.
    /// tmux re-render cycles moving the outer terminal's cursor between frames. Motion is
    /// <see cref="Place"/>'s job, not this one; a protocol that renews unconditionally
    /// repaints every visible payload at the frame rate, which reads as flicker.
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
