using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;

namespace ConsoleForge.Widgets;

/// <summary>
/// Renders an image inside its allocated terminal region.
/// <para>
/// Two render modes are available:
/// <list type="bullet">
///   <item>
///     <term>Kitty</term>
///     <description>
///       Uses the Kitty terminal graphics protocol for true pixel rendering.
///       Requires <see cref="PngData"/> or <see cref="RgbaData"/> and a
///       <see cref="Capabilities"/> instance with
///       <see cref="TerminalCapabilities.SupportsKittyGraphics"/> set. PNG is sent as
///       is (<c>f=100</c>); pixels are sent zlib-compressed (<c>f=32</c>), which is how
///       any other format — JPEG included — reaches the terminal: decode it first.
///     </description>
///   </item>
///   <item>
///     <term>HalfBlock</term>
///     <description>
///       Renders using Unicode half-block characters (<c>▀</c>) with 24-bit
///       foreground/background colours — two pixel rows per terminal cell.
///       Requires <see cref="RgbaData"/>.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// Set <see cref="RenderMode"/> to <see cref="ImageRenderMode.Auto"/> (default) to let
/// the widget choose: Kitty when <see cref="Capabilities"/> reports support and either
/// source is present (PNG preferred, being smaller on the wire); HalfBlock when
/// <see cref="RgbaData"/> is present; otherwise no-op. One <see cref="RgbaData"/> therefore
/// serves both a Kitty terminal and every other one.
/// </para>
/// </summary>
public sealed class ImageWidget : IWidget
{
    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>Object-initializer constructor.</summary>
    public ImageWidget() { }

    /// <summary>
    /// Positional constructor for Kitty / Auto rendering.
    /// </summary>
    /// <param name="pngData">
    /// Raw PNG bytes. The terminal handles decoding and scaling. Must be PNG — see
    /// <see cref="PngData"/>.
    /// </param>
    /// <param name="capabilities">
    /// Detected terminal capabilities. When null, <see cref="ImageRenderMode.Auto"/>
    /// will fall back to HalfBlock (or no-op if <see cref="RgbaData"/> is also absent).
    /// </param>
    public ImageWidget(byte[] pngData, TerminalCapabilities? capabilities = null)
    {
        PngData      = pngData;
        Capabilities = capabilities;
    }

    /// <summary>
    /// Positional constructor for HalfBlock-only rendering (pre-decoded RGBA).
    /// </summary>
    /// <param name="rgbaData">Pre-decoded RGBA pixel data.</param>
    public ImageWidget(RgbaImageData rgbaData)
    {
        RgbaData = rgbaData;
    }

    /// <summary>
    /// Positional constructor for decoded pixels on any terminal: Kitty (<c>f=32</c>)
    /// where <paramref name="capabilities"/> reports support, half-blocks elsewhere.
    /// The way to show a JPEG or any other format the Kitty protocol cannot declare.
    /// </summary>
    /// <param name="rgbaData">Decoded RGBA pixel data.</param>
    /// <param name="capabilities">Detected terminal capabilities; null means half-blocks.</param>
    public ImageWidget(RgbaImageData rgbaData, TerminalCapabilities? capabilities)
    {
        RgbaData     = rgbaData;
        Capabilities = capabilities;
    }

    // ── Properties ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);

    /// <inheritdoc/>
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);

    /// <inheritdoc/>
    public Style Style { get; init; } = Style.Default;

    /// <summary>
    /// Raw PNG bytes used for Kitty rendering and (in Auto mode) as the primary
    /// source. The terminal reads image dimensions from the PNG header; no pixel
    /// pre-processing is required by the application.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Set to bytes that are not PNG. Sent as <c>f=100</c> they would declare a format
    /// the data contradicts, which kitty and Ghostty answer by drawing nothing. Decode
    /// other formats and use <see cref="RgbaData"/>.
    /// </exception>
    public byte[]? PngData
    {
        get => _pngData;
        init
        {
            if (value is not null) KittyProtocol.ThrowIfNotPng(value, nameof(PngData));
            _pngData = value;
        }
    }

    private readonly byte[]? _pngData;

    /// <summary>
    /// Pre-decoded RGBA pixel data. Drawn with half-blocks when the terminal has no Kitty
    /// graphics, and sent as zlib-compressed pixels (<c>f=32</c>) when it does and
    /// <see cref="PngData"/> is absent.
    /// </summary>
    public RgbaImageData? RgbaData { get; init; }

    /// <summary>
    /// Rendering strategy. Defaults to <see cref="ImageRenderMode.Auto"/>.
    /// </summary>
    public ImageRenderMode RenderMode { get; init; } = ImageRenderMode.Auto;

    /// <summary>
    /// Terminal capabilities detected at startup. Used by
    /// <see cref="ImageRenderMode.Auto"/> to decide between Kitty and HalfBlock.
    /// Inject via <see cref="TerminalCapabilities.Detect()"/> at application start.
    /// </summary>
    public TerminalCapabilities? Capabilities { get; init; }

    // ── IWidget ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        switch (ResolveMode())
        {
            case ImageRenderMode.Kitty:
                RenderKitty(ctx);
                break;
            case ImageRenderMode.HalfBlock:
                RenderHalfBlock(ctx);
                break;
            // None / unresolvable Auto → no-op
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Resolve <see cref="ImageRenderMode.Auto"/> to a concrete mode.</summary>
    private ImageRenderMode ResolveMode() => RenderMode switch
    {
        ImageRenderMode.Kitty     => ImageRenderMode.Kitty,
        ImageRenderMode.HalfBlock => ImageRenderMode.HalfBlock,
        ImageRenderMode.None      => ImageRenderMode.None,
        // Auto: prefer Kitty when supported and there is anything to send it
        _ => (Capabilities?.SupportsKittyGraphics == true && (PngData is not null || RgbaData.HasValue))
                ? ImageRenderMode.Kitty
                : (RgbaData.HasValue
                    ? ImageRenderMode.HalfBlock
                    : ImageRenderMode.None),
    };

    private void RenderKitty(IRenderContext ctx)
    {
        // PNG first: the terminal decodes it, and it is far smaller than raw pixels even
        // compressed. Pixels otherwise — the path for every format the protocol lacks.
        KittyPayload? payload =
            PngData is not null      ? KittyProtocol.CreatePayload(PngData, Capabilities) :
            RgbaData is { } rgba     ? KittyProtocol.CreatePayload(rgba, Capabilities) :
            null;
        if (payload is not null) ctx.WriteRawEscape(ctx.Region, payload);
    }

    private void RenderHalfBlock(IRenderContext ctx)
    {
        if (!RgbaData.HasValue) return;
        var data   = RgbaData.Value;
        var region = ctx.Region;

        for (int r = 0; r < region.Height; r++)
        {
            for (int c = 0; c < region.Width; c++)
            {
                // Each terminal cell covers two source-image pixel rows via ▀:
                //   foreground = top pixel row, background = bottom pixel row.
                // Scale from terminal cell coordinates to source pixel coordinates
                // using integer nearest-neighbour to avoid floating-point per cell.
                int topPy = r * 2       * data.Height / (region.Height * 2);
                int botPy = (r * 2 + 1) * data.Height / (region.Height * 2);
                int px    = c           * data.Width  / region.Width;

                var (tr, tg, tb, _) = data.GetPixel(px, topPy);
                var (br, bg, bb, _) = data.GetPixel(px, botPy);

                var style = Style.Default
                    .Foreground(new TrueColor(tr, tg, tb))
                    .Background(new TrueColor(br, bg, bb));

                ctx.Write(region.Col + c, region.Row + r, "▀", style);
            }
        }
    }
}

/// <summary>Rendering strategy for <see cref="ImageWidget"/>.</summary>
public enum ImageRenderMode
{
    /// <summary>
    /// Automatically select the best available mode: Kitty when
    /// <see cref="TerminalCapabilities.SupportsKittyGraphics"/> is true and
    /// <see cref="ImageWidget.PngData"/> or <see cref="ImageWidget.RgbaData"/> is present;
    /// otherwise HalfBlock when <see cref="ImageWidget.RgbaData"/> is present; otherwise no-op.
    /// </summary>
    Auto,

    /// <summary>
    /// Use the Kitty terminal graphics protocol. Requires
    /// <see cref="ImageWidget.PngData"/> or <see cref="ImageWidget.RgbaData"/> and a
    /// capable terminal. Falls back to no-op if both are absent.
    /// </summary>
    Kitty,

    /// <summary>
    /// Use Unicode half-block characters with 24-bit colour.
    /// Requires <see cref="ImageWidget.RgbaData"/>. Falls back to no-op if absent.
    /// </summary>
    HalfBlock,

    /// <summary>Render nothing. Reserves layout space without drawing pixels.</summary>
    None,
}
