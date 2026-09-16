using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="ImageWidget"/>.</summary>
public class ImageWidgetTests
{
    // ── Recording render context ──────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="IRenderContext"/> that records <see cref="WriteRawEscape"/>
    /// calls and cell writes. Used to verify widget dispatch without a full render pipeline.
    /// </summary>
    private sealed class RecordingContext : IRenderContext
    {
        public Region         Region       { get; }
        public Theme          Theme        => Theme.Default;
        public ColorProfile   ColorProfile => ColorProfile.TrueColor;
        public ResolvedLayout Layout       { get; }
        public CursorDescriptor? Cursor    => null;

        public readonly List<(Region Region, IRawEscapePayload Payload)> RawEscapes = new();
        public readonly List<(int Col, int Row, string Text)> Writes = new();

        public RecordingContext(int w = 20, int h = 10)
        {
            Region = new Region(0, 0, w, h);
            Layout = LayoutEngine.Resolve(new TextBlock(), w, h);
        }

        public void Write(int col, int row, string text, Style style)
            => Writes.Add((col, row, text));

        public void WriteRawEscape(Region region, IRawEscapePayload payload)
            => RawEscapes.Add((region, payload));

        public void SetCursorDescriptor(CursorDescriptor cursor) { }
    }

    // Minimal valid 1×1 transparent PNG
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    // ── Mode resolution ───────────────────────────────────────────────────────

    [Fact]
    public void Render_KittyMode_CallsWriteRawEscape()
    {
        var ctx    = new RecordingContext();
        var widget = new ImageWidget
        {
            PngData    = TinyPng,
            RenderMode = ImageRenderMode.Kitty,
        };

        widget.Render(ctx);

        Assert.Single(ctx.RawEscapes);
        Assert.Equal(ctx.Region, ctx.RawEscapes[0].Region);
        Assert.IsType<KittyPayload>(ctx.RawEscapes[0].Payload);
    }

    [Fact]
    public void Render_HalfBlockMode_WritesBlockCharacters()
    {
        var pixels = new byte[4 * 4 * 4]; // 4×4 RGBA, all zeros (transparent black)
        var ctx    = new RecordingContext(4, 2);
        var widget = new ImageWidget
        {
            RgbaData   = new RgbaImageData(pixels, 4, 4),
            RenderMode = ImageRenderMode.HalfBlock,
        };

        widget.Render(ctx);

        // Each cell gets a ▀ character; 4 columns × 2 rows = 8 writes
        Assert.Equal(8, ctx.Writes.Count);
        Assert.All(ctx.Writes, w => Assert.Equal("▀", w.Text));
    }

    [Fact]
    public void Render_NoneMode_WritesNothing()
    {
        var ctx    = new RecordingContext();
        var widget = new ImageWidget
        {
            PngData    = TinyPng,
            RenderMode = ImageRenderMode.None,
        };

        widget.Render(ctx);

        Assert.Empty(ctx.RawEscapes);
        Assert.Empty(ctx.Writes);
    }

    // ── Auto mode ─────────────────────────────────────────────────────────────

    [Fact]
    public void Render_AutoMode_KittyCapable_UsesKitty()
    {
        var ctx    = new RecordingContext();
        var widget = new ImageWidget(TinyPng, TerminalCapabilities.WithKitty);

        widget.Render(ctx);

        Assert.Single(ctx.RawEscapes);
        Assert.IsType<KittyPayload>(ctx.RawEscapes[0].Payload);
        Assert.Empty(ctx.Writes);
    }

    [Fact]
    public void Render_AutoMode_NoKittyCapability_UsesHalfBlock()
    {
        var pixels = new byte[2 * 2 * 4]; // 2×2 RGBA
        var ctx    = new RecordingContext(4, 2);
        var widget = new ImageWidget
        {
            PngData      = TinyPng,
            RgbaData     = new RgbaImageData(pixels, 2, 2),
            Capabilities = TerminalCapabilities.None,
            RenderMode   = ImageRenderMode.Auto,
        };

        widget.Render(ctx);

        Assert.Empty(ctx.RawEscapes);
        Assert.NotEmpty(ctx.Writes);
        Assert.All(ctx.Writes, w => Assert.Equal("▀", w.Text));
    }

    [Fact]
    public void Render_AutoMode_NoCapabilitiesNoRgba_NoOp()
    {
        var ctx    = new RecordingContext();
        var widget = new ImageWidget
        {
            PngData    = TinyPng,
            RenderMode = ImageRenderMode.Auto,
            // Capabilities = null, RgbaData = null → no-op
        };

        widget.Render(ctx);

        Assert.Empty(ctx.RawEscapes);
        Assert.Empty(ctx.Writes);
    }

    [Fact]
    public void Render_AutoMode_KittyCapable_NullPng_FallsToHalfBlock()
    {
        var pixels = new byte[2 * 2 * 4];
        var ctx    = new RecordingContext(4, 2);
        var widget = new ImageWidget
        {
            PngData      = null, // no PNG → Kitty unavailable despite capability
            RgbaData     = new RgbaImageData(pixels, 2, 2),
            Capabilities = TerminalCapabilities.WithKitty,
            RenderMode   = ImageRenderMode.Auto,
        };

        widget.Render(ctx);

        Assert.Empty(ctx.RawEscapes);
        Assert.NotEmpty(ctx.Writes);
    }

    // ── Guard conditions ──────────────────────────────────────────────────────

    [Fact]
    public void Render_ZeroWidthRegion_NoOp()
    {
        var layout = LayoutEngine.Resolve(new TextBlock(), 0, 10);
        // Construct a zero-width context manually
        var ctx = new RecordingContext(0, 10);
        var widget = new ImageWidget(TinyPng, TerminalCapabilities.WithKitty);

        // Must not throw
        widget.Render(ctx);
    }

    [Fact]
    public void Render_KittyMode_NullPngData_NoOp()
    {
        var ctx    = new RecordingContext();
        var widget = new ImageWidget
        {
            PngData    = null,
            RenderMode = ImageRenderMode.Kitty,
        };

        widget.Render(ctx);

        Assert.Empty(ctx.RawEscapes);
    }

    [Fact]
    public void Render_HalfBlockMode_NullRgbaData_NoOp()
    {
        var ctx    = new RecordingContext();
        var widget = new ImageWidget
        {
            RgbaData   = null,
            RenderMode = ImageRenderMode.HalfBlock,
        };

        widget.Render(ctx);

        Assert.Empty(ctx.Writes);
    }

    // ── Kitty payload content ─────────────────────────────────────────────────

    [Fact]
    public void Render_KittyPayload_EncodeContainsApcSequence()
    {
        var ctx    = new RecordingContext(10, 5);
        var widget = new ImageWidget(TinyPng, TerminalCapabilities.WithKitty);

        widget.Render(ctx);

        var (region, payload) = ctx.RawEscapes[0];
        var sequences = payload.Encode(region, ColorProfile.TrueColor).ToList();

        Assert.NotEmpty(sequences);
        // First sequence is a=t (transmit-only upload chunk)
        var first = sequences[0].Contains("\x1bPtmux;")
            ? sequences[0].Replace("\x1b\x1b", "\x1b")
            : sequences[0];
        Assert.Contains("\x1b_G", first);
        Assert.Contains("a=t",   first);
        Assert.Contains("f=100", first);
        // Last sequence is a=p (place with explicit size)
        var last = sequences[^1].Contains("\x1bPtmux;")
            ? sequences[^1].Replace("\x1b\x1b", "\x1b")
            : sequences[^1];
        Assert.Contains("a=p",              last);
        Assert.Contains($"c={region.Width}",  last);
        Assert.Contains($"r={region.Height}", last);
        Assert.EndsWith("\x1b\\", last.TrimEnd());
    }

    [Fact]
    public void Render_KittyPayload_CleanupIsNotNull()
    {
        var ctx    = new RecordingContext();
        var widget = new ImageWidget(TinyPng, TerminalCapabilities.WithKitty);

        widget.Render(ctx);

        var (region, payload) = ctx.RawEscapes[0];
        var cleanup = payload.Cleanup(region);

        Assert.NotNull(cleanup);
        Assert.Contains("a=d", cleanup);
    }

    // ── HalfBlock pixel mapping ───────────────────────────────────────────────

    [Fact]
    public void Render_HalfBlock_CellCountMatchesRegionDimensions()
    {
        var pixels = new byte[8 * 8 * 4]; // 8×8 RGBA
        var ctx    = new RecordingContext(6, 3);
        var widget = new ImageWidget
        {
            RgbaData   = new RgbaImageData(pixels, 8, 8),
            RenderMode = ImageRenderMode.HalfBlock,
        };

        widget.Render(ctx);

        // 6 cols × 3 rows = 18 cells
        Assert.Equal(18, ctx.Writes.Count);
    }
}
