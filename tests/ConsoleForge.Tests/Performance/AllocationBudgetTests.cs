using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Performance;

/// <summary>
/// Ceilings on what a frame is allowed to allocate and emit.
/// </summary>
/// <remarks>
/// <para>
/// Tier 1 of the performance policy in <c>AGENTS.md</c>. Every performance defect found in
/// 0.4.0 had an exact signature that a benchmark spent ten minutes approximating: the widget
/// cache serving one widget per frame, the diff re-emitting untouched cells, a measure pass
/// allocating lines that Render immediately rebuilt. These assert those signatures directly,
/// in milliseconds, in CI.
/// </para>
/// <para>
/// Budgets are ceilings with headroom, not the measured value — they exist to catch a
/// regression of the kind that has actually happened here (5x to 20x), not to fail on a
/// hundred bytes of drift. Tighten them only alongside a real improvement.
/// </para>
/// </remarks>
public class AllocationBudgetTests
{
    /// <summary>
    /// Bytes allocated per call, once the JIT has settled. Averaging over iterations keeps
    /// tiering and one-off statics out of the number.
    /// </summary>
    private static long AllocatedPerCall(Action action, int warmup = 20, int iterations = 50)
    {
        for (int i = 0; i < warmup; i++) action();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    private static void AssertUnder(long budget, long actual, string what)
        => Assert.True(actual <= budget,
            $"{what}: {actual} bytes per frame, budget {budget}. " +
            "If this is a deliberate trade, raise the budget in the same commit and say why.");

    private static RenderContext Primed(IWidget root, int width = 80, int height = 24)
    {
        var layout = LayoutEngine.Resolve(root, width, height);
        var region = layout.GetRegion(root) ?? new Region(0, 0, width, height);
        var ctx    = new RenderContext(region, Theme.Dark, ColorProfile.TrueColor, layout);
        root.Render(ctx);
        ctx.ToAnsiFrame();
        return ctx;
    }

    private static IWidget TwentyTextBlocks() =>
        new Container(Axis.Vertical, [.. Enumerable.Range(0, 20)
            .Select(i => (IWidget)new TextBlock($"Item {i:D2}"))]);

    private static IWidget TwelveBorderedRows() =>
        new Container(Axis.Vertical, [.. Enumerable.Range(0, 12)
            .Select(i => (IWidget)new BorderBox($"Row {i:D2}", new TextBlock($"stable {i}")))]);

    // ── Steady-state frames ───────────────────────────────────────────────────

    [Fact]
    public void SteadyStateFrame_OfTextBlocks_StaysWithinBudget()
    {
        // Every TextBlock here is Auto on both axes, so this is the measure pass at its
        // worst. Measuring by building the wrapped lines cost 6,400 bytes a frame on this
        // exact tree; TextUtils.MeasureWrapped exists because of it.
        var root = TwentyTextBlocks();
        var ctx  = Primed(root);

        long perFrame = AllocatedPerCall(() =>
        {
            ctx.Reset(ctx.Region, Theme.Dark, ColorProfile.TrueColor, ctx.Layout);
            root.Render(ctx);
            ctx.ToAnsiFrame();
        });

        AssertUnder(16_000, perFrame, "20 Auto TextBlocks, nothing changed");
    }

    [Fact]
    public void SteadyStateFrame_OfReusableComposites_StaysWithinBudget()
    {
        // Guards the widget render cache. When RegisterWidget consumed the previous frame's
        // map, only the first row was reusable and this tree cost ~131 KB a frame instead
        // of ~6 KB — with pixel-identical output, so nothing else noticed.
        var root = TwelveBorderedRows();
        var ctx  = Primed(root);

        long perFrame = AllocatedPerCall(() =>
        {
            ctx.Reset(ctx.Region, Theme.Dark, ColorProfile.TrueColor, ctx.Layout);
            root.Render(ctx);
            ctx.ToAnsiFrame();
        });

        AssertUnder(20_000, perFrame, "12 reusable BorderBox rows, nothing changed");
    }

    [Fact]
    public void Layout_OfAutoChildren_DoesNotAllocatePerMeasure()
    {
        // Resolving a layout allocates its Dictionary; the measure pass on top of it must
        // not add per-child allocations.
        var root = TwentyTextBlocks();

        long withAuto = AllocatedPerCall(() => LayoutEngine.Resolve(root, 80, 24));

        var flexRoot = new Container(Axis.Vertical, [.. Enumerable.Range(0, 20)
            .Select(i => (IWidget)new TextBlock($"Item {i:D2}") { Height = SizeConstraint.Flex(1) })]);
        long withFlex = AllocatedPerCall(() => LayoutEngine.Resolve(flexRoot, 80, 24));

        Assert.True(withAuto <= withFlex + 512,
            $"measuring Auto children allocated {withAuto - withFlex} bytes more than " +
            $"resolving flex ones ({withAuto} vs {withFlex})");
    }

    // ── The diff actually diffs ───────────────────────────────────────────────

    [Fact]
    public void UnchangedFrame_EmitsFarLessThanAFullRepaint()
    {
        // The diff used to skip its comparison for cells holding null — most of the screen —
        // and re-emit them, so an "incremental" frame was within 0.2% of a full repaint.
        var root = TwentyTextBlocks();

        var layout = LayoutEngine.Resolve(root, 80, 24);
        var region = layout.GetRegion(root)!.Value;
        var ctx    = new RenderContext(region, Theme.Dark, ColorProfile.TrueColor, layout);

        root.Render(ctx);
        string fullRepaint = ctx.ToAnsiFrame();

        ctx.Reset(region, Theme.Dark, ColorProfile.TrueColor, layout);
        root.Render(ctx);
        string unchanged = ctx.ToAnsiFrame();

        Assert.True(unchanged.Length < fullRepaint.Length / 10,
            $"an unchanged frame emitted {unchanged.Length} characters against " +
            $"{fullRepaint.Length} for a full repaint — the diff is not diffing");
    }

    [Fact]
    public void ChangingOneRow_EmitsOnlyThatRow()
    {
        var rows = Enumerable.Range(0, 20)
            .Select(i => (IWidget)new TextBlock($"Item {i:D2}"))
            .ToArray();

        var layout = LayoutEngine.Resolve(new Container(Axis.Vertical, rows), 80, 24);
        var region = new Region(0, 0, 80, 24);
        var ctx    = new RenderContext(region, Theme.Dark, ColorProfile.TrueColor, layout);

        IWidget first = new Container(Axis.Vertical, rows);
        first.Render(ctx);
        string fullRepaint = ctx.ToAnsiFrame();

        rows[7] = new TextBlock("Item XX");
        IWidget second = new Container(Axis.Vertical, rows);
        var layout2 = LayoutEngine.Resolve(second, 80, 24);

        ctx.Reset(region, Theme.Dark, ColorProfile.TrueColor, layout2);
        second.Render(ctx);
        string oneRowChanged = ctx.ToAnsiFrame();

        // Only the cells that differ are emitted — "Item 07" and "Item XX" share a prefix,
        // so the diff writes two characters, not the row and certainly not the screen.
        Assert.Contains("X", oneRowChanged);
        Assert.DoesNotContain("Item 03", oneRowChanged);
        Assert.DoesNotContain("Item 19", oneRowChanged);
        Assert.True(oneRowChanged.Length < fullRepaint.Length / 5,
            $"changing one row of twenty emitted {oneRowChanged.Length} characters " +
            $"against {fullRepaint.Length} for a full repaint");
    }

    // ── Layout reuse ──────────────────────────────────────────────────────────

    [Fact]
    public void ResolveInto_AReusedLayout_DoesNotAllocate()
    {
        // Resolving allocated a fresh Dictionary every frame — 2,616 bytes on this tree,
        // 16% of a changed frame. Allocate() itself allocates nothing (AllocateContainer
        // rents its int arrays), so clearing and refilling costs nothing once the
        // dictionary has reached its steady-state capacity.
        var root = TwentyTextBlocks();
        var buf  = new ResolvedLayout();

        long perCall = AllocatedPerCall(() => LayoutEngine.ResolveInto(buf, root, 80, 24));

        AssertUnder(256, perCall, "resolving into a reused layout");
    }
}
