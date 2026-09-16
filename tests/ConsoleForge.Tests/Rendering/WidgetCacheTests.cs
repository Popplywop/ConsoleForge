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
}
