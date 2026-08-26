using System.Runtime.CompilerServices;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Terminal;

/// <summary>
/// Encoding helpers for the Kitty terminal graphics protocol.
/// <para>
/// Images are transmitted as base64-encoded PNG data inside APC sequences
/// (<c>ESC _ G … ESC \</c>), chunked to ≤4096 base64 chars each.
/// Format: <c>f=100</c> (PNG — terminal reads dimensions from the header).
/// </para>
/// <para>
/// <b>Multiplexer note (tmux):</b> With <c>allow-passthrough on</c>, tmux forwards
/// APC sequences directly to the outer terminal. However, CSI cursor-move sequences
/// are handled by tmux internally and may be flushed to the outer terminal at a
/// different time, causing the image to appear at the wrong position. The fix is to
/// embed the cursor-positioning escape <em>inside</em> the DCS passthrough block
/// alongside the first APC chunk — WezTerm (or any outer terminal) then receives
/// cursor-move + image as one atomic unit and positions correctly.
/// </para>
/// </summary>
public static class KittyProtocol
{
    /// <summary>Maximum base64 characters per APC chunk (Kitty spec limit).</summary>
    public const int MaxChunkBase64Chars = 4096;

    /// <summary>
    /// Derive a stable non-zero image-id from PNG bytes using FNV-1a.
    /// Kitty image IDs are uint32, non-zero. Deterministic — same bytes → same ID,
    /// so no static counter (and no mutable state) is needed.
    /// </summary>
    public static uint ImageIdFromBytes(ReadOnlySpan<byte> data)
    {
        uint hash = 2166136261u;
        foreach (byte b in data)
            hash = (hash ^ b) * 16777619u;
        return hash == 0u ? 1u : hash;
    }

    /// <summary>
    /// The per-image work that does not depend on where the image is drawn:
    /// its Kitty id and its base64 transmission text.
    /// </summary>
    internal sealed record EncodedPng(uint ImageId, string Base64);

    // Widgets are rebuilt every frame, so a payload is constructed every frame for
    // the same picture. Hashing and base64-encoding a poster costs tens of thousands
    // of operations and a ~55 KB string each time, all of it to produce a value the
    // frame diff then uses to decide nothing changed. Keyed on the array's identity
    // and held weakly, so the entry dies with the image it describes.
    private static readonly ConditionalWeakTable<byte[], EncodedPng> EncodedCache = new();

    internal static EncodedPng GetEncoded(byte[] pngBytes) =>
        EncodedCache.GetValue(
            pngBytes,
            static bytes => new EncodedPng(ImageIdFromBytes(bytes), Convert.ToBase64String(bytes)));

    /// <summary>
    /// Create a <see cref="KittyPayload"/> wrapping <paramref name="pngBytes"/>.
    /// Pass <paramref name="capabilities"/> so the payload can apply the correct
    /// pane-offset when building DCS cursor-move sequences for tmux.
    /// <para>
    /// Cheap to call every frame: the encoding of <paramref name="pngBytes"/> is
    /// cached against the array instance and reused.
    /// </para>
    /// </summary>
    public static KittyPayload CreatePayload(byte[] pngBytes, TerminalCapabilities? capabilities = null) => new(pngBytes, capabilities);

    /// <summary>
    /// Build a Kitty capability-query probe sequence.
    /// Send this to the terminal before entering raw mode, then read the response
    /// and pass it to <see cref="ParseDetectResponse"/>.
    /// </summary>
    public static string BuildDetectProbe()
    {
        const string Pixel1x1Png =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        return $"\x1b_Gi=31,s=1,v=1,a=q,t=d,f=32,q=1;{Pixel1x1Png}\x1b\\";
    }

    /// <summary>Returns true if <paramref name="response"/> is a Kitty OK reply to the probe.</summary>
    public static bool ParseDetectResponse(string response) =>
        response.Contains("_Gi=31;OK") || response.Contains("_Gi=31;");
}

/// <summary>
/// <see cref="IRawEscapePayload"/> implementation for the Kitty terminal graphics protocol.
/// </summary>
public sealed class KittyPayload : IRawEscapePayload
{
    private readonly uint   _imageId;
    private readonly string _base64;
    private readonly bool   _insideTmux;
    private readonly int    _paneRowOffset; // terminal rows above pane top (status bars)
    private readonly int    _paneColOffset; // terminal cols left of pane (split panes)

    internal KittyPayload(byte[] pngBytes, TerminalCapabilities? capabilities = null)
    {
        var encoded    = KittyProtocol.GetEncoded(pngBytes);
        _imageId       = encoded.ImageId;
        _base64        = encoded.Base64;
        _insideTmux    = DetectTmux();
        _paneRowOffset = capabilities?.TmuxPaneRowOffset ?? 0;
        _paneColOffset = capabilities?.TmuxPaneColOffset ?? 0;
    }

    /// <summary>
    /// True when running inside a tmux session — triggers DCS passthrough wrapping.
    /// Detected once at construction; environment variables don't change mid-session.
    /// </summary>
    private static bool DetectTmux() =>
        Environment.GetEnvironmentVariable("TMUX") is not null ||
        (Environment.GetEnvironmentVariable("TERM") ?? "").StartsWith("tmux", StringComparison.Ordinal);

    // Builds the DCS-wrapped cursor-move + APC for tmux, applying the pane
    // offsets so WezTerm positions in absolute terminal coordinates rather than
    // pane-relative coordinates.
    private string BuildTmuxSequence(Region region, string apc)
    {
        int absRow = region.Row + 1 + _paneRowOffset; // +1: pane-0-based → ANSI 1-based
        int absCol = region.Col + 1 + _paneColOffset;
        string cursorMove = $"\x1b[{absRow};{absCol}H";
        return WrapForTmux(cursorMove + apc);
    }

    /// <summary>
    /// Wrap <paramref name="sequence"/> in a DCS passthrough block so that
    /// tmux forwards the contents verbatim to the outer terminal.
    /// Every ESC byte in the inner sequence is doubled per the DCS spec.
    /// </summary>
    private static string WrapForTmux(string sequence) =>
        $"\x1bPtmux;{sequence.Replace("\x1b", "\x1b\x1b")}\x1b\\";

    /// <inheritdoc/>
    public int ContentHash => (int)_imageId;

    /// <inheritdoc/>
    /// <remarks>
    /// When inside tmux the first chunk includes an embedded cursor-move so that the
    /// outer terminal (WezTerm, etc.) receives cursor-position + image as one atomic
    /// DCS passthrough. This is necessary because tmux handles CSI cursor-moves
    /// internally and flushes them to the outer terminal independently of APC
    /// passthroughs, causing a position race if they are sent separately.
    /// Outside tmux sequences are emitted raw; the cursor-move is emitted by
    /// <see cref="ConsoleForge.Layout.RenderContext"/> immediately before calling
    /// <c>Encode</c>, so no extra positioning is needed here.
    /// </remarks>
    public IEnumerable<string> Encode(Region region, ColorProfile profile)
    {
        const int chunkSize = KittyProtocol.MaxChunkBase64Chars;
        int total  = _base64.Length;
        int chunks = Math.Max(1, (total + chunkSize - 1) / chunkSize);

        // ── Step 1: upload chunks (a=t = transmit only, no display) ────────────────
        // No cursor-move needed here — upload is cursor-independent.
        // For tmux we still DCS-wrap so allow-passthrough forwards the data.
        for (int i = 0; i < chunks; i++)
        {
            int start   = i * chunkSize;
            int len     = Math.Min(chunkSize, total - start);
            bool isLast = i == chunks - 1;
            int more    = isLast ? 0 : 1;
            string data = len > 0 ? _base64.Substring(start, len) : string.Empty;

            string apc = i == 0
                // First chunk: f=100 (PNG), q=2 (suppress response), a=t (transmit only)
                ? $"\x1b_Ga=t,f=100,i={_imageId},q=2,m={more};{data}\x1b\\"
                // Continuation chunks: only m= and data
                : $"\x1b_Gm={more};{data}\x1b\\";

            yield return _insideTmux ? WrapForTmux(apc) : apc;
        }

        // ── Step 2: place (a=p) — cursor-move collocated here, not in upload ──
        // By emitting cursor-move atomically with the place command we guarantee
        // correct position even when tmux render cycles move the outer cursor
        // between upload chunks. For non-tmux the cursor-move was already emitted
        // by RenderContext.ToAnsiFrame immediately before this call.
        string placeApc = $"\x1b_Ga=p,i={_imageId},q=2,c={region.Width},r={region.Height}\x1b\\";
        yield return _insideTmux ? BuildTmuxSequence(region, placeApc) : placeApc;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Emits a cheap <c>a=p</c> command that re-places the already-uploaded
    /// image at the current cursor position. Called every frame when
    /// <see cref="Encode"/> is hash-skipped. Fixes positioning drift caused
    /// by tmux re-render cycles moving WezTerm's cursor between frames.
    /// </remarks>
    public IEnumerable<string>? Refresh(Region region, ColorProfile profile)
    {
        string apc = $"\x1b_Ga=p,i={_imageId},q=2,c={region.Width},r={region.Height}\x1b\\";
        if (_insideTmux)
            return Enumerable.Repeat(BuildTmuxSequence(region, apc), 1);
        return Enumerable.Repeat(apc, 1);
    }

    /// <inheritdoc/>
    public string? Cleanup(Region region)
    {
        string apc = $"\x1b_Ga=d,d=i,i={_imageId}\x1b\\";
        return _insideTmux ? WrapForTmux(apc) : apc;
    }
}
