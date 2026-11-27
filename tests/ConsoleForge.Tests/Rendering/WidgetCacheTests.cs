using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Rendering;

/// <summary>
/// <see cref="RenderContext.TryReuseWidget"/> lets a container skip re-rendering a
/// composite child that is the same instance in the same place as last frame.
///
/// A defeated cache produces identical pixels, so nothing else in the suite notices;
/// these tests assert the reuse itself, not the output.
/// </summary>
public class WidgetCacheTests
{
    private const int Width = 80, Height = 24;

    private static (RenderContext Ctx, IWidget Root, IWidget[] Rows, ResolvedLayout Layout)
        PrimedFrame(int rowCount)
    {
        var rows = new IWidget[rowCount];
        for (int i = 0; i < rowCount; i++)
            rows[i] = new BorderBox($"Row {i}", new TextBlock($"body {i}"));

        IWidget root = new Container(Axis.Vertical, rows);
        var layout   = LayoutEngine.Resolve(root, Width, Height);
        var region   = layout.GetRegion(root) ?? new Region(0, 0, Width, Height);
        var ctx      = new RenderContext(region, Theme.Dark, ColorProfile.TrueColor, layout);

        root.Render(ctx);
        ctx.ToAnsiFrame(); // populates the previous cell buffer and the widget map

        return (ctx, root, rows, layout);
    }

    [Fact]
    public void EveryUnchangedComposite_IsReusable_NotJustTheFirst()
    {
        var (ctx, root, rows, layout) = PrimedFrame(rowCount: 6);

        var region = layout.GetRegion(root)!.Value;
        ctx.Reset(region, Theme.Dark, ColorProfile.TrueColor, layout);

        // Stand in for Container.Render: ask for each child in the order it renders.
        // Registering the first child used to consume the previous frame's map, so
        // every child after it missed and re-rendered.
        for (int i = 0; i < rows.Length; i++)
        {
            var childRegion = layout.GetRegion(rows[i])!.Value;
            Assert.True(ctx.TryReuseWidget(rows[i], childRegion),
                $"row {i} was not reusable — the cache stopped serving after row 0");
        }
    }

    [Fact]
    public void RebuiltChild_IsNotReused_ButItsSiblingsAre()
    {
        var (ctx, root, rows, layout) = PrimedFrame(rowCount: 4);

        // A moving selection rebuilds one row; the others keep their identity.
        var replaced = new BorderBox("Row 2", new TextBlock("selected"));

        var region = layout.GetRegion(root)!.Value;
        ctx.Reset(region, Theme.Dark, ColorProfile.TrueColor, layout);

        Assert.True(ctx.TryReuseWidget(rows[0], layout.GetRegion(rows[0])!.Value));
        Assert.True(ctx.TryReuseWidget(rows[1], layout.GetRegion(rows[1])!.Value));
        Assert.False(ctx.TryReuseWidget(replaced, layout.GetRegion(rows[2])!.Value));
        Assert.True(ctx.TryReuseWidget(rows[3], layout.GetRegion(rows[3])!.Value));
    }

    [Fact]
    public void ReusedCells_MatchAFreshRender()
    {
        var (ctx, root, _, layout) = PrimedFrame(rowCount: 6);
        var region = layout.GetRegion(root)!.Value;

        // Frame two, served largely from the cache.
        ctx.Reset(region, Theme.Dark, ColorProfile.TrueColor, layout);
        root.Render(ctx);
        var cached = ctx.ToAnsiFrame();

        // The same frame with no cache at all: identical content means reuse is honest.
        var fresh = new RenderContext(region, Theme.Dark, ColorProfile.TrueColor, layout);
        root.Render(fresh);
        fresh.ToAnsiFrame();
        fresh.Reset(region, Theme.Dark, ColorProfile.TrueColor, layout);
        root.Render(fresh);
        var uncached = fresh.ToAnsiFrame();

        Assert.Equal(uncached, cached);
    }

    [Fact]
    public void ThemeChange_DiscardsTheCache()
    {
        var (ctx, root, rows, layout) = PrimedFrame(rowCount: 4);
        var region = layout.GetRegion(root)!.Value;

        ctx.Reset(region, Theme.Light, ColorProfile.TrueColor, layout);

        // Cells cached under the old theme carry the old colours; serving them would
        // strand the previous palette on screen.
        Assert.False(ctx.TryReuseWidget(rows[0], layout.GetRegion(rows[0])!.Value));
    }

    [Fact]
    public void Resize_DiscardsTheCache()
    {
        var (ctx, root, rows, layout) = PrimedFrame(rowCount: 4);

        var smaller = LayoutEngine.Resolve(root, Width, Height - 4);
        var region  = smaller.GetRegion(root) ?? new Region(0, 0, Width, Height - 4);
        ctx.Reset(region, Theme.Dark, ColorProfile.TrueColor, smaller);

        Assert.False(ctx.TryReuseWidget(rows[0], smaller.GetRegion(rows[0])!.Value));
    }

    // ── What a hit must carry besides cells ───────────────────────────────────
    // A cache hit used to copy the widget's whole region out of the previous buffer,
    // which also held whatever was drawn over it, and skipped the side channels the
    // widget's render had fed: the cursor and the raw payload list.

    private static IWidget Panel(params IWidget[] children) =>
        new Container(Axis.Vertical, [new Container(Axis.Vertical, children)]);

    [Fact]
    public void ClosingAnOverlay_RepaintsTheCachedWidgetBeneathIt()
    {
        var main = Panel(new TextBlock(string.Join("\n", Enumerable.Repeat(new string('a', 20), 6))));
        var modal = new Modal { Title = "M", Body = new TextBlock("MODAL"), DialogWidth = 10, DialogHeight = 4 };

        var renderer = new Renderer();
        var sim = new TerminalSim(20, 6);
        sim.Apply(renderer.Render(new ZStack([main, modal]), 20, 6, Theme.Dark, ColorProfile.TrueColor).Content);
        sim.Apply(renderer.Render(new ZStack([main]), 20, 6, Theme.Dark, ColorProfile.TrueColor).Content);

        var fresh = new TerminalSim(20, 6);
        fresh.Apply(new Renderer().Render(new ZStack([main]), 20, 6, Theme.Dark, ColorProfile.TrueColor).Content);
        Assert.Equal(fresh.Screen, sim.Screen);
        Assert.DoesNotContain("MODAL", sim.Screen);
    }

    [Fact]
    public void WidgetDrawnOver_IsNotServedFromCache_UntilItIsCleanAgain()
    {
        var inner = new Container(Axis.Vertical, [new TextBlock("under")]);
        IWidget root = new ZStack([new Container(Axis.Vertical, [inner]), new TextBlock("OVER")]);
        IWidget bare = new ZStack([new Container(Axis.Vertical, [inner])]);

        var layout = LayoutEngine.Resolve(root, 20, 2);
        var ctx = new RenderContext(new Region(0, 0, 20, 2), Theme.Dark, ColorProfile.TrueColor, layout);
        root.Render(ctx);
        ctx.ToAnsiFrame();

        var region = layout.GetRegion(inner)!.Value;
        ctx.Reset(new Region(0, 0, 20, 2), Theme.Dark, ColorProfile.TrueColor, LayoutEngine.Resolve(bare, 20, 2));
        Assert.False(ctx.TryReuseWidget(inner, region)); // "OVER" covered it last frame
        inner.Render(new SubRenderContext(ctx, region));
        ctx.RegisterWidget(inner, region);
        ctx.ToAnsiFrame();

        ctx.Reset(new Region(0, 0, 20, 2), Theme.Dark, ColorProfile.TrueColor, LayoutEngine.Resolve(bare, 20, 2));
        Assert.True(ctx.TryReuseWidget(inner, region)); // nothing covered it this time
    }

    [Fact]
    public void CacheHit_KeepsTheCursorItsWidgetPlaced()
    {
        var input = new TextInput("hi", cursorPosition: 2) { HasFocus = true, Width = SizeConstraint.Fixed(10) };
        var panel = Panel(input);
        var renderer = new Renderer();

        var first = renderer.Render(new Container(Axis.Vertical, [panel, new TextBlock("one")]), 20, 4, Theme.Dark, ColorProfile.TrueColor);
        var second = renderer.Render(new Container(Axis.Vertical, [panel, new TextBlock("two")]), 20, 4, Theme.Dark, ColorProfile.TrueColor);

        Assert.True(first.Cursor.Visible);
        Assert.Equal(first.Cursor, second.Cursor);
    }

    [Fact]
    public void CacheHit_KeepsTheImagesItsWidgetPlaced()
    {
        var payload = new CountingPayload();
        var panel = Panel(new RawWidget(payload));
        var renderer = new Renderer();

        renderer.Render(new Container(Axis.Vertical, [panel, new TextBlock("one")]), 20, 6, Theme.Dark, ColorProfile.TrueColor);
        var second = renderer.Render(new Container(Axis.Vertical, [panel, new TextBlock("two")]), 20, 6, Theme.Dark, ColorProfile.TrueColor);
        Assert.Equal(0, payload.Cleanups); // a hit used to drop the payload, so the frame deleted it

        // Still tracked after the hit: removing the panel now deletes the image.
        renderer.Render(new TextBlock("gone"), 20, 6, Theme.Dark, ColorProfile.TrueColor);
        Assert.Equal(1, payload.Cleanups);
        Assert.Equal(1, payload.Encodes);
    }

    private sealed class CountingPayload : IRawEscapePayload
    {
        public int Encodes, Cleanups;
        public int ContentHash => 11;
        public IEnumerable<string> Encode(Region region, ColorProfile profile) { Encodes++; return ["E"]; }
        public string? Cleanup(Region region) { Cleanups++; return "C"; }
    }

    private sealed record RawWidget(IRawEscapePayload Payload) : IWidget
    {
        public SizeConstraint Width => SizeConstraint.Fixed(4);
        public SizeConstraint Height => SizeConstraint.Fixed(2);
        public Style Style => Style.Default;
        public void Render(IRenderContext ctx) => ctx.WriteRawEscape(ctx.Region, Payload);
    }
}
