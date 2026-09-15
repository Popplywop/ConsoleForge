using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Rendering;

/// <summary>
/// What the Kitty graphics path emits when images <em>move</em> — the shape a horizontally
/// scrolling shelf of poster cards produces every frame.
/// </summary>
/// <remarks>
/// Images that hold still are covered by <c>ImageWidgetTests</c>. These characterise motion,
/// which is a different problem: <c>RenderContext</c> tracks raw-escape regions by
/// <see cref="Region"/>, and a scroll changes every region on every frame.
/// </remarks>
public class ImageMotionTests
{
    // A 1x1 PNG. Content does not matter; only the emitted control sequences do.
    private static byte[] Png(byte tag) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, tag,
    ];

    private static readonly TerminalCapabilities Kitty = new() { SupportsKittyGraphics = true };

    private const int Width = 40, Height = 10;
    private const int CardWidth = 8;

    /// <summary>A row of image cards starting at <paramref name="offset"/> columns from the left.</summary>
    private static Container Row(byte[][] images, int offset)
    {
        var cells = new List<IWidget>();
        if (offset > 0)
            cells.Add(new TextBlock("") { Width = SizeConstraint.Fixed(offset) });

        foreach (var png in images)
            cells.Add(new ImageWidget(png, Kitty)
            {
                Width = SizeConstraint.Fixed(CardWidth),
                Height = SizeConstraint.Fixed(Height),
            });

        return new Container(Axis.Horizontal, [.. cells]);
    }

    /// <summary>Render two frames into one context and return the ANSI emitted for each.</summary>
    private static (string First, string Second) TwoFrames(Container frame1, Container frame2)
    {
        var layout1 = LayoutEngine.Resolve(frame1, Width, Height);
        var region = new Region(0, 0, Width, Height);
        var ctx = new RenderContext(region, Theme.Dark, ColorProfile.TrueColor, layout1);

        frame1.Render(ctx);
        string first = ctx.ToAnsiFrame();

        var layout2 = LayoutEngine.Resolve(frame2, Width, Height);
        ctx.Reset(region, Theme.Dark, ColorProfile.TrueColor, layout2);
        frame2.Render(ctx);
        string second = ctx.ToAnsiFrame();

        return (first, second);
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    /// <summary>Number of image *uploads* — the expensive part, carrying the full base64 blob.</summary>
    private static int Uploads(string frame) => Count(frame, "\x1b_Ga=t,");

    /// <summary>Number of *placements* — cheap, positions an already-uploaded image.</summary>
    private static int Placements(string frame) => Count(frame, "\x1b_Ga=p,");

    // ── Still images: the case that already works ─────────────────────────────

    [Fact]
    public void StationaryImage_IsUploadedOnce()
    {
        var png = Png(1);
        var (first, second) = TwoFrames(Row([png], 0), Row([png], 0));

        Assert.Equal(1, Uploads(first));
        Assert.Equal(0, Uploads(second));
    }

    [Fact]
    public void StationaryImage_IsNotRePlacedRepeatedly()
    {
        // An image that has not moved needs nothing emitted. Inside tmux one refresh is
        // allowed, because tmux's re-render cycles drift the placement out of position;
        // it carries a placement id so it replaces rather than stacks. Either way a
        // second frame must never cost more than one command per image.
        var png = Png(1);
        var (_, second) = TwoFrames(Row([png], 0), Row([png], 0));

        Assert.True(Placements(second) <= 1,
            $"an unchanged image emitted {Placements(second)} placements; stacking them is " +
            "what makes a screen of artwork flicker");
    }

    [Fact]
    public void EveryPlacementCarriesAPlacementId()
    {
        // Without p= each place adds a placement instead of replacing one.
        var (first, _) = TwoFrames(Row([Png(1)], 0), Row([Png(1)], 0));

        Assert.Contains("\x1b_Ga=p,", first);
        Assert.Matches(@"\x1b_Ga=p,i=\d+,p=\d+,", first);
    }

    [Fact]
    public void DeleteTargetsOnePlacement_NotEveryCopyOfTheImage()
    {
        // The same artwork can be on screen twice — one show in two shelves. Removing one
        // copy must not blank the other, so the delete names the placement.
        byte[] a = Png(1), b = Png(2);
        var (_, second) = TwoFrames(Row([a, b], 0), Row([b], 0));

        Assert.Matches(@"\x1b_Ga=d,d=i,i=\d+,p=\d+", second);
    }

    // ── Motion: the shelf case ────────────────────────────────────────────────

    [Fact]
    public void MovedImage_IsRePlaced_NotReUploaded()
    {
        // A card that slid one column left. The image is byte-identical and the terminal
        // already holds it, so this should cost a placement, not a transmission — Kitty
        // separates transmit (a=t) from place (a=p) exactly so an image can be moved
        // without re-sending it.
        var png = Png(1);
        var (_, second) = TwoFrames(Row([png], 2), Row([png], 1));

        Assert.Equal(0, Uploads(second));
        Assert.Equal(1, Placements(second));
    }

    [Fact]
    public void ImageWhoseSlotIsTakenOver_StillHasItsOldPlacementDeleted()
    {
        // Two cards shift left by exactly one card width, so B lands on the slot A just
        // left. Matching previous regions against current ones by region alone made A's
        // vacated slot look occupied, suppressing A's delete and stranding a copy of A
        // underneath B. The delete has to be attributed to A specifically.
        byte[] a = Png(1), b = Png(2);
        uint idA = KittyProtocol.ImageIdFromBytes(a);

        var before = Row([a, b], CardWidth);  // a at col 8,  b at col 16
        var after = Row([a, b], 0);          // a at col 0,  b at col 8 — b takes a's slot

        var (_, second) = TwoFrames(before, after);

        Assert.Contains($"d=i,i={idA}", second);
    }

    [Fact]
    public void ScrollingRow_UploadsEachImageOnce_ThenOnlyRePlaces()
    {
        // The shelf case. Six frames of a three-card row scrolling one column at a time
        // used to cost three full uploads per frame — every visible poster re-transmitted
        // every frame, which no amount of card virtualisation would have rescued.
        byte[] a = Png(1), b = Png(2), c = Png(3);

        var region = new Region(0, 0, Width, Height);
        var ctx = new RenderContext(
            region, Theme.Dark, ColorProfile.TrueColor,
            LayoutEngine.Resolve(Row([a, b, c], 0), Width, Height));

        int uploadsAfterFirstFrame = 0;
        for (int offset = 0; offset < 6; offset++)
        {
            var frame = Row([a, b, c], offset);
            var frameL = LayoutEngine.Resolve(frame, Width, Height);
            ctx.Reset(region, Theme.Dark, ColorProfile.TrueColor, frameL);
            frame.Render(ctx);
            string emitted = ctx.ToAnsiFrame();

            if (offset > 0) uploadsAfterFirstFrame += Uploads(emitted);
        }

        Assert.Equal(0, uploadsAfterFirstFrame);
    }

    [Fact]
    public void ImageScrolledOffScreen_IsDeleted()
    {
        byte[] a = Png(1), b = Png(2);
        uint idA = KittyProtocol.ImageIdFromBytes(a);

        // Frame two drops the first card entirely, as a shelf does when it scrolls past.
        var (_, second) = TwoFrames(Row([a, b], 0), Row([b], 0));

        Assert.Contains($"d=i,i={idA}", second);
    }
}