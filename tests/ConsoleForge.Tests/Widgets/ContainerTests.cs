using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Tests.Rendering;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>
/// A scrollable <see cref="Container"/>: children keep their own sizes, the overflow is
/// what scrolls, and layout, render and hit-testing agree on where everything is.
/// </summary>
/// <remarks>
/// Scrolling used to be decoration on an ordinary layout: children were first squeezed
/// into the viewport (or, when fixed, made layout throw), then shifted by the offset, so
/// there was little or nothing to scroll to — and a child half out of view drew its top
/// half in the space left, instead of its bottom half.
/// </remarks>
public class ContainerTests
{
    private static IWidget[] Rows(int count, SizeConstraint height) =>
        [.. Enumerable.Range(0, count).Select(i => (IWidget)new TextBlock($"row{i}") { Height = height })];

    private static string[] Screen(IWidget root, int width, int height)
    {
        var sim = new TerminalSim(width, height);
        sim.Apply(new Renderer().Render(root, width, height, Theme.Dark, ColorProfile.TrueColor).Content);
        return [.. sim.Lines.Select(l => l.TrimEnd())];
    }

    private static Container Scroller(IWidget[] children, int offset) =>
        new(Axis.Vertical, children, scrollable: true) { ScrollOffset = offset };

    [Fact]
    public void FixedChildrenOverflowing_ScrollInsteadOfThrowing()
    {
        var root = Scroller(Rows(10, SizeConstraint.Fixed(1)), offset: 3);

        Assert.Null(Record.Exception(() => LayoutEngine.Resolve(root, 10, 4)));
        Assert.Equal(["row3", "row4", "row5", "row6"], Screen(root, 10, 4));
    }

    [Fact]
    public void AutoChildren_KeepTheirContentSize()
    {
        var root = Scroller(Rows(10, SizeConstraint.Auto), offset: 5);
        Assert.Equal(["row5", "row6", "row7", "row8"], Screen(root, 10, 4));
    }

    [Fact]
    public void AutoChild_IsMeasuredWithoutTheViewportLimit()
    {
        var tall = new TextBlock("a\nb\nc\nd\ne\nf") { Height = SizeConstraint.Auto };
        var root = Scroller([tall, new TextBlock("after") { Height = SizeConstraint.Fixed(1) }], offset: 4);
        Assert.Equal(["e", "f", "after"], Screen(root, 10, 3));
    }

    [Fact]
    public void ChildHalfOutOfView_DrawsItsVisiblePart()
    {
        var block = new TextBlock("line0\nline1\nline2") { Height = SizeConstraint.Fixed(3) };
        var root = Scroller([block, new TextBlock("next") { Height = SizeConstraint.Fixed(1) }], offset: 1);
        Assert.Equal(["line1", "line2", "next"], Screen(root, 10, 3));
    }

    [Fact]
    public void ScrollOffset_PastTheEnd_StopsAtTheLastChild()
    {
        var children = Rows(10, SizeConstraint.Fixed(1));
        var root = Scroller(children, offset: 100);

        Assert.Equal(["row6", "row7", "row8", "row9"], Screen(root, 10, 4));
        Assert.Equal(3, LayoutEngine.Resolve(root, 10, 4).GetRegion(children[9])!.Value.Row);
    }

    [Fact]
    public void NegativeScrollOffset_IsTheTop()
        => Assert.Equal(["row0", "row1"], Screen(Scroller(Rows(5, SizeConstraint.Fixed(1)), offset: -3), 10, 2));

    [Fact]
    public void LayoutAndRender_AgreeOnScrolledRegions()
    {
        var children = Rows(10, SizeConstraint.Fixed(1));
        var root = new Container(Axis.Vertical,
        [
            new TextBlock("head") { Height = SizeConstraint.Fixed(1) },
            Scroller(children, offset: 2),
        ]);
        var layout = LayoutEngine.Resolve(root, 10, 4);
        var screen = Screen(root, 10, 4);

        for (int i = 0; i < children.Length; i++)
        {
            int row = layout.GetRegion(children[i])!.Value.Row;
            if (row >= 1 && row < 4) Assert.Equal($"row{i}", screen[row]);
        }
    }

    [Fact]
    public void PaddedScroller_ClipsAtItsPadding()
    {
        var root = new Container(Axis.Vertical, Rows(10, SizeConstraint.Fixed(1)),
            style: Style.Default.Padding(1, 0, 1, 0), scrollable: true) { ScrollOffset = 1 };
        Assert.Equal(["", "row1", "row2", ""], Screen(root, 10, 4));
    }

    [Fact]
    public void FlexChildren_ShareWhatTheViewportHasLeft()
    {
        var flex = new TextBlock("flex") { Height = SizeConstraint.Flex(1) };
        var root = Scroller([new TextBlock("fixed") { Height = SizeConstraint.Fixed(1) }, flex], offset: 0);
        Assert.Equal(3, LayoutEngine.Resolve(root, 10, 4).GetRegion(flex)!.Value.Height);
    }

    [Fact]
    public void ClickOutsideTheViewport_DoesNotReachAScrolledOutChild()
    {
        // "hidden" is scrolled up out of the scroller, so its region now lies over the
        // input above it. Last match wins in hit-testing, so it used to take the click.
        var above  = new TextInput { FocusKey = "above", Height = SizeConstraint.Fixed(1) };
        var hidden = new TextInput { FocusKey = "hidden", Height = SizeConstraint.Fixed(1) };
        var shown  = new TextInput { FocusKey = "shown", Height = SizeConstraint.Fixed(1) };
        var root = new Container(Axis.Vertical,
        [
            above,
            new Container(Axis.Vertical, [hidden, shown], height: SizeConstraint.Fixed(1), scrollable: true) { ScrollOffset = 1 },
        ]);
        var layout = LayoutEngine.Resolve(root, 10, 2);

        Assert.Equal(0, layout.GetRegion(hidden)!.Value.Row);
        Assert.Same(above, FocusManager.FindFocusableAt(root, layout, col: 1, row: 0));
        Assert.Same(shown, FocusManager.FindFocusableAt(root, layout, col: 1, row: 1));
    }

    // ── Reading the layout instead of solving again ───────────────────────────

    [Fact]
    public void SameInstanceTwice_RendersInBothPlaces()
    {
        // The layout keys regions by instance, so it holds only one of these two; the
        // container must not take its children's places from it.
        var shared = new TextBlock("dup") { Height = SizeConstraint.Fixed(1) };
        var root = new Container(Axis.Vertical, [shared, new TextBlock("mid") { Height = SizeConstraint.Fixed(1) }, shared]);
        Assert.Equal(["dup", "mid", "dup"], Screen(root, 6, 3));
    }

    [Fact]
    public void ContainerRenderedOutsideTheLayoutPass_StillPlacesItsChildren()
    {
        // A container the layout never saw (built inside another widget's Render) has no
        // regions to read back and must solve for itself.
        var inner = new Container(Axis.Vertical, [new TextBlock("a") { Height = SizeConstraint.Fixed(1) }, new TextBlock("b") { Height = SizeConstraint.Fixed(1) }]);
        var host = new Hosting(inner);
        Assert.Equal(["a", "b"], Screen(host, 4, 2));
    }

    private sealed record Hosting(IWidget Inner) : IWidget
    {
        public SizeConstraint Width => SizeConstraint.Flex(1);
        public SizeConstraint Height => SizeConstraint.Flex(1);
        public Style Style => Style.Default;
        public void Render(IRenderContext ctx) => Inner.Render(new SubRenderContext(ctx, ctx.Region));
    }

    [Fact]
    public void AFrame_MeasuresEachAutoChildOnlyDuringLayout()
    {
        // Container.Render re-ran the solver, measuring every Auto child a second time
        // per frame — and once per level of Auto nesting on top of that.
        var probe = new CountingWidget();
        IWidget root = new Container(Axis.Vertical,
            [new Container(Axis.Horizontal, [probe, new TextBlock("x")], height: SizeConstraint.Auto)]);

        LayoutEngine.Resolve(root, 20, 5);
        int layoutOnly = probe.Calls;
        probe.Calls = 0;

        new Renderer().Render(root, 20, 5, Theme.Dark, ColorProfile.TrueColor);
        Assert.Equal(layoutOnly, probe.Calls);
    }

    private sealed class CountingWidget : IWidget, IMeasurable
    {
        public int Calls;
        public SizeConstraint Width => SizeConstraint.Auto;
        public SizeConstraint Height => SizeConstraint.Auto;
        public Style Style => Style.Default;
        public void Render(IRenderContext ctx) { }
        public Size Measure(int availableWidth, int availableHeight) { Calls++; return new Size(3, 1); }
    }
}
