using System.Collections.Concurrent;
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;
using ConsoleForge.Testing;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Rendering;

/// <summary>
/// An image the terminal evicted is uploaded again (WISHLIST 13).
/// </summary>
/// <remarks>
/// Kitty and Ghostty keep uploaded images in a storage quota and delete the oldest past it,
/// images without placements first — preloaded ones, say. The frame tracking assumed whatever
/// it uploaded was still held and re-placed it; the placement then failed. With placements
/// reporting errors (<c>q=1</c>), that failure arrives as <c>ENOENT</c>, and the renderer
/// forgets the image so the next frame sends it in full.
/// </remarks>
public class ImageEvictionTests
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x42,
    ];

    private static readonly TerminalCapabilities Kitty = new() { SupportsKittyGraphics = true, InsideTmux = false };

    private static readonly uint ImageId = (uint)KittyProtocol.CreatePayload(Png, Kitty).ContentHash;

    private const int Width = 40, Height = 10;

    private static IWidget Image() =>
        new ImageWidget(Png, Kitty) { Width = SizeConstraint.Fixed(8), Height = SizeConstraint.Fixed(Height) };

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static int Uploads(string s) => Count(s, "\x1b_Ga=t,");

    private static string Frame(RenderContext ctx, IWidget root)
    {
        ctx.Reset(new Region(0, 0, Width, Height), Theme.Dark, ColorProfile.TrueColor,
                  LayoutEngine.Resolve(root, Width, Height));
        root.Render(ctx);
        return ctx.ToAnsiFrame();
    }

    private static RenderContext NewContext(IWidget root) =>
        new(new Region(0, 0, Width, Height), Theme.Dark, ColorProfile.TrueColor, LayoutEngine.Resolve(root, Width, Height));

    // ── RenderContext ─────────────────────────────────────────────────────────

    [Fact]
    public void Forgotten_IsUploadedAgain_Once()
    {
        var root = Image();
        var ctx  = NewContext(root);

        Assert.Equal(1, Uploads(Frame(ctx, root)));
        Assert.Equal(0, Uploads(Frame(ctx, root)));   // held: nothing to send

        ctx.ForgetRawPayload((int)ImageId);
        Assert.Equal(1, Uploads(Frame(ctx, root)));   // evicted: sent in full again
        Assert.Equal(0, Uploads(Frame(ctx, root)));   // and held from then on
    }

    [Fact]
    public void ForgottenPreload_IsUploadedOnFirstAppearance()
    {
        // Preloaded images have no placement until they appear, which makes them the first
        // a terminal evicts.
        var blank = new TextBlock("");
        var ctx   = NewContext(blank);
        Frame(ctx, blank);
        Assert.NotNull(ctx.Preload(KittyProtocol.CreatePayload(Png, Kitty)));

        ctx.ForgetRawPayload((int)ImageId);
        Assert.Equal(1, Uploads(Frame(ctx, Image())));
    }

    [Fact]
    public void ForgettingAnImageNotOnScreen_CostsNothing()
    {
        var root = Image();
        var ctx  = NewContext(root);
        Frame(ctx, root);

        ctx.ForgetRawPayload(12345);
        Assert.Equal(0, Uploads(Frame(ctx, root)));
    }

    // ── Through App ───────────────────────────────────────────────────────────

    private sealed record ImageModel(ConcurrentQueue<IMsg> Seen, TaskCompletionSource Resized) : IModel
    {
        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg)
        {
            Seen.Enqueue(msg);
            if (msg is WindowResizeMsg) Resized.TrySetResult();
            return msg switch
            {
                KeyMsg { Key: ConsoleKey.Q }   => (this, Cmd.Quit()),
                KeyMsg { Key: ConsoleKey.F24 } => (this with { }, null), // a new model: one frame
                _                              => (this, null),
            };
        }

        public IWidget View() => Image();
    }

    private static int TotalUploads(VirtualTerminal t) => t.WriteHistory.Sum(Uploads);

    /// <summary>One frame after both of the inputs before it, in order.</summary>
    private static async Task NextFrame(VirtualTerminal terminal)
    {
        int baseline = terminal.FramesFlushed;
        // A key that changes the model follows whatever was queued before it into the same
        // drain of the queue, so exactly one frame comes after both.
        terminal.EnqueueKey(new KeyMsg(ConsoleKey.F24, null));
        await terminal.WaitForFrames(baseline + 1);
    }

    private static async Task Reply(VirtualTerminal terminal, string message)
    {
        terminal.EnqueueInput(new KittyGraphicsReplyEvent(ImageId, message));
        await NextFrame(terminal);
    }

    /// <summary>
    /// Start the app and wait until startup is over. The startup resize redraws the frame
    /// from scratch — and re-sends the image with it — either before or after the first
    /// timer frame, so the upload count is only stable once it has been handled.
    /// </summary>
    private static async Task<(VirtualTerminal Terminal, Task Run, ConcurrentQueue<IMsg> Seen)> Start()
    {
        var seen     = new ConcurrentQueue<IMsg>();
        var resized  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = new VirtualTerminal(Width, Height);
        var run      = App.Run(new ImageModel(seen, resized), terminal, Theme.Dark);
        await resized.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await NextFrame(terminal);
        return (terminal, run, seen);
    }

    [Fact]
    public async Task EnoentReply_ReuploadsThroughApp_AndNeverReachesTheModel()
    {
        var (terminal, run, seen) = await Start();
        int before = TotalUploads(terminal);
        Assert.True(before >= 1);

        await Reply(terminal, "ENOENT:image evicted");
        Assert.Equal(before + 1, TotalUploads(terminal));

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await run;

        Assert.All(seen, m => Assert.True(m is KeyMsg or WindowResizeMsg, $"model saw {m}"));
    }

    [Fact]
    public async Task RejectedUpload_IsNotRetried()
    {
        // The terminal refused the upload itself (q=1 reports that too); the placement's
        // ENOENT that follows must not start an upload every frame.
        var (terminal, run, _) = await Start();
        int before = TotalUploads(terminal);

        await Reply(terminal, "EBADPNG:bad png data");
        await Reply(terminal, "ENOENT:no such image");
        await Reply(terminal, "ENOENT:no such image");
        Assert.Equal(before, TotalUploads(terminal));

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await run;
    }

    [Fact]
    public async Task Recoveries_AreBounded()
    {
        var (terminal, run, _) = await Start();
        int before = TotalUploads(terminal);

        for (int i = 0; i < 6; i++)
            await Reply(terminal, "ENOENT:no such image");

        Assert.Equal(before + 3, TotalUploads(terminal)); // three recoveries, then no more

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await run;
    }
}
