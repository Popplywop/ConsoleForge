using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;
using ConsoleForge.Testing;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Rendering;

/// <summary>
/// <see cref="Cmd.Preload"/>: an image's upload goes out when its bytes arrive, and the
/// frame it first appears in costs one placement.
/// </summary>
/// <remarks>
/// Without it the upload lands on the frame the image is meant to appear in. Under tmux the
/// cells of that frame then reach the outer terminal before the image does, and whatever
/// the image covers — a shelf's placeholder — shows for the gap.
/// </remarks>
public class ImagePreloadTests
{
    private static byte[] Png(byte tag) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, tag,
    ];

    private static readonly TerminalCapabilities Kitty =
        new() { SupportsKittyGraphics = true, InsideTmux = false };

    private static readonly TerminalCapabilities KittyInTmux =
        new() { SupportsKittyGraphics = true, InsideTmux = true };

    private const int Width = 40, Height = 10;

    private static IWidget Image(byte[] png, TerminalCapabilities caps) =>
        new ImageWidget(png, caps) { Width = SizeConstraint.Fixed(8), Height = SizeConstraint.Fixed(Height) };

    private static readonly IWidget Blank = new TextBlock("");

    /// <summary>A context that has drawn one frame of <paramref name="first"/>.</summary>
    private static RenderContext Drawn(IWidget first)
    {
        var ctx = new RenderContext(new Region(0, 0, Width, Height), Theme.Dark,
                                    ColorProfile.TrueColor, LayoutEngine.Resolve(first, Width, Height));
        first.Render(ctx);
        ctx.ToAnsiFrame();
        return ctx;
    }

    private static string Frame(RenderContext ctx, IWidget root, int width = Width)
    {
        ctx.Reset(new Region(0, 0, width, Height), Theme.Dark, ColorProfile.TrueColor,
                  LayoutEngine.Resolve(root, width, Height));
        root.Render(ctx);
        return ctx.ToAnsiFrame();
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static int Uploads(string s) => Count(s, "\x1b_Ga=t,");
    private static int Placements(string s) => Count(s, "\x1b_Ga=p,");

    // ── RenderContext ─────────────────────────────────────────────────────────

    [Fact]
    public void Preload_TransmitsWithoutPlacing()
    {
        var ctx = Drawn(Blank);
        var sent = ctx.Preload(KittyProtocol.CreatePayload(Png(1), Kitty));

        Assert.NotNull(sent);
        Assert.Equal(1, Uploads(sent));
        Assert.Equal(0, Placements(sent));
    }

    [Fact]
    public void PreloadedImage_FirstAppearance_IsPlaced_NotUploaded()
    {
        var png = Png(1);
        var ctx = Drawn(Blank);
        ctx.Preload(KittyProtocol.CreatePayload(png, Kitty));

        string frame = Frame(ctx, Image(png, Kitty));

        Assert.Equal(0, Uploads(frame));
        Assert.Equal(1, Placements(frame));
    }

    [Fact]
    public void PreloadedImage_FirstAppearance_IsPlaced_InTmuxToo()
    {
        var png = Png(1);
        var ctx = Drawn(Blank);
        string sent = ctx.Preload(KittyProtocol.CreatePayload(png, KittyInTmux))!;

        // The upload is cursor-independent and goes out between frames, so it carries no
        // cursor-move; it is still DCS-wrapped for passthrough.
        Assert.StartsWith("\x1bPtmux;", sent);
        Assert.DoesNotContain("\x1b\x1b[", sent);

        string frame = Frame(ctx, Image(png, KittyInTmux));
        Assert.Equal(0, Uploads(frame));
        Assert.Equal(1, Placements(frame));
    }

    [Fact]
    public void Preload_OfAnImageAlreadyOnScreen_SendsNothing()
    {
        var png = Png(1);
        var ctx = Drawn(Image(png, Kitty));

        Assert.Null(ctx.Preload(KittyProtocol.CreatePayload(png, Kitty)));
    }

    [Fact]
    public void Preload_Twice_SendsOnce()
    {
        var png = Png(1);
        var ctx = Drawn(Blank);

        Assert.NotNull(ctx.Preload(KittyProtocol.CreatePayload(png, Kitty)));
        Assert.Null(ctx.Preload(KittyProtocol.CreatePayload(png, Kitty)));
    }

    [Fact]
    public void Preload_IsConsumedByFirstAppearance()
    {
        // Once shown, the image is tracked frame to frame like any other. Leaving and
        // returning uploads it again, exactly as an image never preloaded does — a preload
        // is not a standing claim that the terminal holds the image forever.
        var png = Png(1);
        var ctx = Drawn(Blank);
        ctx.Preload(KittyProtocol.CreatePayload(png, Kitty));

        Frame(ctx, Image(png, Kitty));
        Frame(ctx, Blank);
        string back = Frame(ctx, Image(png, Kitty));

        Assert.Equal(1, Uploads(back));
    }

    [Fact]
    public void Preload_IsDroppedOnResize()
    {
        var png = Png(1);
        var ctx = Drawn(Blank);
        ctx.Preload(KittyProtocol.CreatePayload(png, Kitty));

        string frame = Frame(ctx, Image(png, Kitty), width: Width + 2);

        Assert.Equal(1, Uploads(frame));
    }

    [Fact]
    public void PayloadWithoutATransmitStep_IsNotHeld_AndIsEncodedOnFirstAppearance()
    {
        var payload = new EncodeOnlyPayload();
        var ctx = Drawn(Blank);

        Assert.Null(ctx.Preload(payload));

        var root = new RawWidget(payload);
        Frame(ctx, root);
        Assert.Equal(1, payload.Encodes);
        Assert.Equal(0, payload.Places);
    }

    private sealed class EncodeOnlyPayload : IRawEscapePayload
    {
        public int Encodes, Places;
        public int ContentHash => 7;
        public IEnumerable<string> Encode(Region region, ColorProfile profile) { Encodes++; return ["E"]; }
        public IEnumerable<string> Place(Region region, ColorProfile profile) { Places++; return ["P"]; }
        public string? Cleanup(Region region) => null;
    }

    private sealed record RawWidget(IRawEscapePayload Payload) : IWidget
    {
        public SizeConstraint Width => SizeConstraint.Fixed(4);
        public SizeConstraint Height => SizeConstraint.Fixed(2);
        public Style Style => Style.Default;
        public void Render(IRenderContext ctx) => ctx.WriteRawEscape(ctx.Region, Payload);
    }

    // ── App ───────────────────────────────────────────────────────────────────

    private sealed record LoadingModel(byte[]? Poster) : IModel
    {
        public static readonly byte[] Bytes = Png(9);

        public ICmd? Init() => null;

        // The shape an application has: the bytes arrive in a message, the model stores
        // them, and the same Update preloads them.
        public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
        {
            KeyMsg { Key: ConsoleKey.L } =>
                (this with { Poster = Bytes }, Cmd.Preload(KittyProtocol.CreatePayload(Bytes, Kitty))),
            KeyMsg { Key: ConsoleKey.Q } => (this, Cmd.Quit()),
            _ => (this, null),
        };

        public IWidget View() => Poster is null ? new TextBlock("loading") : Image(Poster, Kitty);
    }

    [Fact]
    public async Task App_SendsTheUploadAheadOfTheFrameThatShowsTheImage()
    {
        var terminal = new VirtualTerminal(Width, Height);
        var run = App.Run(new LoadingModel(null), terminal, Theme.Dark, targetFps: 30);

        await terminal.WaitForFrames(1);
        int baseline = terminal.FramesFlushed;
        terminal.EnqueueKey(new KeyMsg(ConsoleKey.L, 'l'));
        await terminal.WaitForFrames(baseline + 2); // the upload's flush, then the frame
        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await run;

        var writes = terminal.WriteHistory;
        int upload = -1, place = -1;
        for (int i = 0; i < writes.Count; i++)
        {
            if (upload < 0 && writes[i].Contains("\x1b_Ga=t,")) upload = i;
            if (place < 0 && writes[i].Contains("\x1b_Ga=p,")) place = i;
        }

        Assert.True(upload >= 0, "the image was never uploaded");
        Assert.True(place > upload, "the image was placed before, or with, its upload");
        Assert.Equal(1, writes.Sum(w => Uploads(w)));
    }
}
