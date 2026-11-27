using System.IO.Compression;
using System.Runtime.CompilerServices;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Terminal;

/// <summary>
/// Encoding helpers for the Kitty terminal graphics protocol.
/// <para>
/// Images are transmitted base64-encoded inside APC sequences (<c>ESC _ G … ESC \</c>),
/// chunked to ≤4096 base64 chars each, in one of the two forms the protocol defines for
/// compressed data:
/// <list type="bullet">
///   <item><c>f=100</c> — PNG bytes, which the terminal decodes and reads the size from.</item>
///   <item><c>f=32,s=…,v=…,o=z</c> — RGBA pixels, zlib-compressed (RFC 1950), for any
///   image the application decoded itself.</item>
/// </list>
/// The protocol has no JPEG format: <c>f</c> is 24, 32 or 100. Terminals that decode only
/// PNG under <c>f=100</c> — kitty and Ghostty among them — draw nothing for JPEG bytes
/// declared as PNG, and with <c>q=2</c> they say nothing either; only lenient ones such
/// as WezTerm sniff the real format. So PNG bytes are checked, and anything else goes in
/// as pixels.
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
    /// True when <paramref name="data"/> starts with the eight-byte PNG signature
    /// (<c>89 50 4E 47 0D 0A 1A 0A</c>).
    /// </summary>
    public static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[..8].SequenceEqual(PngSignature);

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// The per-image work that does not depend on where the image is drawn: its Kitty id,
    /// its base64 transmission text, and the control keys that declare its format.
    /// </summary>
    internal sealed record EncodedImage(uint ImageId, string Base64, string Format);

    // Widgets are rebuilt every frame, so a payload is constructed every frame for
    // the same picture. Hashing and base64-encoding a poster costs tens of thousands
    // of operations and a ~55 KB string each time, all of it to produce a value the
    // frame diff then uses to decide nothing changed. Keyed on the array's identity
    // and held weakly, so the entry dies with the image it describes.
    private static readonly ConditionalWeakTable<byte[], EncodedImage> EncodedCache = new();

    // RGBA pixels are compressed before encoding, which costs more than the hash: the
    // cache matters even more here. Keyed on the pixel array, like PNG bytes.
    private static readonly ConditionalWeakTable<byte[], RgbaEntry> EncodedRgbaCache = new();

    private sealed record RgbaEntry(int Width, int Height, EncodedImage Encoded);

    internal static EncodedImage GetEncoded(byte[] pngBytes) =>
        EncodedCache.GetValue(
            pngBytes,
            static bytes => new EncodedImage(ImageIdFromBytes(bytes), Convert.ToBase64String(bytes), "f=100"));

    internal static EncodedImage GetEncoded(RgbaImageData rgba)
    {
        if (EncodedRgbaCache.TryGetValue(rgba.Pixels, out var cached) &&
            cached.Width == rgba.Width && cached.Height == rgba.Height)
            return cached.Encoded;

        var encoded = EncodeRgba(rgba, $"f=32,s={rgba.Width},v={rgba.Height},o=z");
        // A pixel array reused under different dimensions replaces its entry: the cache
        // must always describe the array as it is currently declared.
        EncodedRgbaCache.AddOrUpdate(rgba.Pixels, new RgbaEntry(rgba.Width, rgba.Height, encoded));
        return encoded;
    }

    private static EncodedImage EncodeRgba(RgbaImageData rgba, string format)
    {
        var pixels = rgba.Pixels.AsSpan(0, rgba.Width * rgba.Height * 4);

        // The id covers the dimensions too: the same bytes as 2x8 and as 4x4 are different
        // pictures.
        uint id = ImageIdFromBytes(pixels);
        id = (id ^ (uint)rgba.Width) * 16777619u;
        id = (id ^ (uint)rgba.Height) * 16777619u;
        if (id == 0) id = 1;

        // Raw RGBA is four bytes a pixel against a few tenths of a byte for a photo's PNG
        // or JPEG; zlib (o=z, the only compression the protocol defines) wins most of that
        // back before base64 adds its third.
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(pixels);

        return new EncodedImage(id, Convert.ToBase64String(compressed.GetBuffer(), 0, (int)compressed.Length), format);
    }

    /// <summary>
    /// Create a <see cref="KittyPayload"/> wrapping <paramref name="pngBytes"/>.
    /// Pass <paramref name="capabilities"/> so the payload can apply the correct
    /// pane-offset when building DCS cursor-move sequences for tmux.
    /// <para>
    /// Cheap to call every frame: the encoding of <paramref name="pngBytes"/> is
    /// cached against the array instance and reused.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="pngBytes"/> is not a PNG (<see cref="IsPng"/>). The payload would
    /// declare <c>f=100</c> for data that contradicts it; decode the image and use the
    /// <see cref="RgbaImageData"/> overload instead.
    /// </exception>
    public static KittyPayload CreatePayload(byte[] pngBytes, TerminalCapabilities? capabilities = null)
    {
        ThrowIfNotPng(pngBytes, nameof(pngBytes));
        return new(GetEncoded(pngBytes), capabilities);
    }

    /// <summary>
    /// Create a <see cref="KittyPayload"/> that transmits decoded pixels as
    /// zlib-compressed RGBA (<c>f=32,o=z</c>) — the path for any format the protocol
    /// cannot declare, JPEG included: decode it, hand over the pixels.
    /// <para>
    /// Cheap to call every frame: the compressed encoding is cached against the
    /// <see cref="RgbaImageData.Pixels"/> array instance and reused.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The dimensions are not positive, or <see cref="RgbaImageData.Pixels"/> holds fewer
    /// than <c>Width × Height × 4</c> bytes.
    /// </exception>
    public static KittyPayload CreatePayload(RgbaImageData rgba, TerminalCapabilities? capabilities = null)
    {
        ThrowIfInvalid(rgba, nameof(rgba));
        return new(GetEncoded(rgba), capabilities);
    }

    internal static void ThrowIfNotPng(byte[] bytes, string paramName)
    {
        ArgumentNullException.ThrowIfNull(bytes, paramName);
        if (!IsPng(bytes))
            throw new ArgumentException(
                "Not PNG data: it does not start with the PNG signature" +
                (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF ? " (it is JPEG)" : "") +
                ". The Kitty protocol has no JPEG format, and kitty and Ghostty draw nothing for " +
                "other data declared as PNG. Decode the image and pass the pixels as RgbaImageData " +
                "(sent as f=32), or obtain a real PNG.",
                paramName);
    }

    internal static void ThrowIfInvalid(RgbaImageData rgba, string paramName)
    {
        if (rgba.Pixels is null)
            throw new ArgumentException("RgbaImageData.Pixels is null.", paramName);
        if (rgba.Width <= 0 || rgba.Height <= 0)
            throw new ArgumentException($"RgbaImageData is {rgba.Width}x{rgba.Height}; both dimensions must be positive.", paramName);
        if ((long)rgba.Width * rgba.Height * 4 > rgba.Pixels.Length)
            throw new ArgumentException(
                $"RgbaImageData declares {rgba.Width}x{rgba.Height} (needs {(long)rgba.Width * rgba.Height * 4} bytes) but Pixels holds {rgba.Pixels.Length}.",
                paramName);
    }

    /// <summary>The image id <see cref="BuildDetectProbe"/> queries with, and its reply carries.</summary>
    internal const uint ProbeImageId = 31;

    /// <summary>
    /// Build a Kitty capability-query probe sequence: a query action (<c>a=q</c>) for a 1x1
    /// RGB image, which a supporting terminal answers without storing or displaying it.
    /// <para>
    /// Send it followed by a primary device attributes request (<c>ESC [ c</c>), which every
    /// terminal answers: a reply to the query that arrives before the attributes means the
    /// protocol is supported, and attributes alone mean it is not — no timeout needed to
    /// decide. <see cref="TerminalCapabilities.Detect()"/> does exactly this.
    /// </para>
    /// </summary>
    public static string BuildDetectProbe() =>
        // f=24 with s=1,v=1 is three bytes of pixel data: "AAAA" is their base64. No q key:
        // the OK reply is the answer being asked for, so it must not be suppressed.
        $"\x1b_Gi={ProbeImageId},s=1,v=1,a=q,t=d,f=24;AAAA\x1b\\";

    /// <summary>
    /// Returns true if <paramref name="response"/> contains a reply to the probe from
    /// <see cref="BuildDetectProbe"/>. Any reply counts, error or <c>OK</c>: only a
    /// terminal that implements the protocol answers a graphics command at all.
    /// </summary>
    public static bool ParseDetectResponse(string response) =>
        response.Contains($"_Gi={ProbeImageId};", StringComparison.Ordinal);
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

    private readonly string _format;        // control keys declaring the data: f=100, or f=32,s,v,o=z

    internal KittyPayload(KittyProtocol.EncodedImage encoded, TerminalCapabilities? capabilities = null)
    {
        _imageId       = encoded.ImageId;
        _base64        = encoded.Base64;
        _format        = encoded.Format;
        // Declared capabilities win when supplied, so a caller states the mode rather than
        // inheriting whatever terminal the process happens to be running in. Only the
        // no-capabilities path falls back to probing the environment.
        _insideTmux    = capabilities?.InsideTmux ?? DetectTmux();
        _paneRowOffset = capabilities?.TmuxPaneRowOffset ?? 0;
        _paneColOffset = capabilities?.TmuxPaneColOffset ?? 0;
    }

    /// <summary>
    /// Last-resort tmux probe for a payload built without <see cref="TerminalCapabilities"/>.
    /// Prefer <see cref="TerminalCapabilities.InsideTmux"/>, which <c>Detect()</c> fills from
    /// the same variables but lets a caller — a test especially — override it.
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

    /// <summary>
    /// Identifies one placement of this image, derived from where it sits.
    /// </summary>
    /// <remarks>
    /// Kitty's place command <em>adds</em> a placement unless it is given an id that
    /// already exists, in which case it replaces it. Without one, renewing a placement
    /// every frame stacks a new copy each time and the terminal recomposites the pile —
    /// which is what a screen full of artwork shows as flicker. Deriving the id from the
    /// region also lets one image appear twice on screen and be deleted independently.
    /// </remarks>
    private static uint PlacementId(Region region) =>
        (uint)(((region.Row + 1) * 4096) + region.Col + 1);

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
        // Upload (a=t), then place (a=p). The cursor-move goes with the place command, not
        // the upload: upload is cursor-independent, and emitting the move atomically with
        // a=p guarantees the position even when tmux render cycles move the outer cursor
        // between upload chunks. Outside tmux the cursor-move was already emitted by
        // RenderContext.ToAnsiFrame immediately before this call.
        foreach (var chunk in Transmit(profile))
            yield return chunk;
        yield return PlaceSequence(region);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The <c>a=t</c> upload, chunked to <see cref="KittyProtocol.MaxChunkBase64Chars"/>.
    /// Transmit-only: nothing is displayed until a <c>a=p</c> names this image id. Inside
    /// tmux each chunk is DCS-wrapped so <c>allow-passthrough</c> forwards it.
    /// </remarks>
    public IEnumerable<string> Transmit(ColorProfile profile)
    {
        const int chunkSize = KittyProtocol.MaxChunkBase64Chars;
        int total  = _base64.Length;
        int chunks = Math.Max(1, (total + chunkSize - 1) / chunkSize);

        for (int i = 0; i < chunks; i++)
        {
            int start   = i * chunkSize;
            int len     = Math.Min(chunkSize, total - start);
            bool isLast = i == chunks - 1;
            int more    = isLast ? 0 : 1;
            string data = len > 0 ? _base64.Substring(start, len) : string.Empty;

            string apc = i == 0
                // First chunk: the format keys, q=2 (suppress response), a=t (transmit only)
                ? $"\x1b_Ga=t,{_format},i={_imageId},q=2,m={more};{data}\x1b\\"
                // Continuation chunks: only m= and q= are allowed. q=2 here too, since a
                // terminal may take quietness from the chunk it replies after — the last.
                : $"\x1b_Gm={more},q=2;{data}\x1b\\";

            yield return _insideTmux ? WrapForTmux(apc) : apc;
        }
    }

    /// <summary>
    /// The <c>a=p</c> command that displays the already-uploaded image at
    /// <paramref name="region"/>, carrying the region-derived placement id so it replaces
    /// any placement already there rather than stacking another copy on it.
    /// </summary>
    private string PlaceSequence(Region region)
    {
        string apc = $"\x1b_Ga=p,i={_imageId},p={PlacementId(region)},q=2," +
                     $"c={region.Width},r={region.Height}\x1b\\";
        return _insideTmux ? BuildTmuxSequence(region, apc) : apc;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Moving an image costs one <c>a=p</c>, not a re-upload: Kitty separates transmit
    /// (<c>a=t</c>) from place (<c>a=p</c>) precisely so already-held image data can be
    /// repositioned. The framework has already deleted the placement at the old region,
    /// and the placement id here is derived from the new one.
    /// </remarks>
    public IEnumerable<string> Place(Region region, ColorProfile profile)
        => Enumerable.Repeat(PlaceSequence(region), 1);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Renews a <em>stationary</em> placement with a cheap <c>a=p</c>. This exists for
    /// tmux: its re-render cycles move the outer terminal's cursor between frames, so a
    /// placement that is not renewed drifts out of position.
    /// </para>
    /// <para>
    /// Outside tmux it returns null, deliberately. Nothing moves an image that stayed put,
    /// and <c>a=p</c> creates an <em>additional</em> placement rather than updating the
    /// existing one — so renewing every frame made the terminal redraw every visible image
    /// at the frame rate, which reads as flicker on a screen full of artwork. An image that
    /// actually moved goes through <see cref="Place"/> instead, in or out of tmux.
    /// </para>
    /// </remarks>
    public IEnumerable<string>? Refresh(Region region, ColorProfile profile)
        => _insideTmux ? Enumerable.Repeat(PlaceSequence(region), 1) : null;

    /// <inheritdoc/>
    /// <remarks>
    /// Deletes the one placement at <paramref name="region"/>, not every placement of the
    /// image: the same artwork can legitimately be on screen twice — a show appearing in
    /// two shelves — and removing one must not blank the other. The image data itself stays
    /// in the terminal, so putting it back costs a placement rather than an upload.
    /// <para>
    /// <c>q=2</c>, like every other command here. Whether a terminal answers a delete
    /// varies — Ghostty never answers a successful one — but any answer arrives on stdin,
    /// once per moved or removed image, so the framework asks for none.
    /// </para>
    /// </remarks>
    public string? Cleanup(Region region)
    {
        string apc = $"\x1b_Ga=d,d=i,i={_imageId},p={PlacementId(region)},q=2\x1b\\";
        return _insideTmux ? WrapForTmux(apc) : apc;
    }
}
