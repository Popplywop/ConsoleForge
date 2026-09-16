using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Layout;

/// <summary>
/// <see cref="SizeConstraint.Auto"/> sizes to content for widgets that implement
/// <see cref="IMeasurable"/>, and keeps its older flex-weight-1 behaviour for those that
/// don't. These cover both halves, plus the interaction with Min/Max and overflow.
/// </summary>
public class MeasureTests
{
    /// <summary>A widget that cannot report a content size, to pin the fallback.</summary>
    private sealed record Opaque : IWidget
    {
        public Style Style { get; init; } = Style.Default;
        public SizeConstraint Width  { get; init; } = SizeConstraint.Auto;
        public SizeConstraint Height { get; init; } = SizeConstraint.Auto;
        public void Render(IRenderContext ctx) { }
    }

    // ── TextBlock ─────────────────────────────────────────────────────────────

    [Fact]
    public void TextBlock_MeasuresOneRowPerWrappedLine()
    {
        var block = new TextBlock("hello");
        Assert.Equal(new Size(5, 1), block.Measure(40, 10));
    }

    [Fact]
    public void TextBlock_WrapsWithinTheOfferedWidth()
    {
        // 10 characters at width 4 wraps to 3 lines.
        var block = new TextBlock("aaaabbbbcc");
        var size  = block.Measure(4, 10);
        Assert.Equal(4, size.Width);
        Assert.Equal(3, size.Height);
    }

    [Fact]
    public void TextBlock_CountsExplicitNewlines()
        => Assert.Equal(3, new TextBlock("a\nb\nc").Measure(40, 10).Height);

    [Fact]
    public void TextBlock_EmptyText_IsStillOneRow()
    {
        // Render wraps "" to a single empty line, so a blank TextBlock is a one-row
        // spacer. Measure has to agree or the spacer silently collapses.
        Assert.Equal(1, new TextBlock("").Measure(40, 10).Height);
    }

    [Fact]
    public void TextBlock_NeverAsksForMoreThanOffered()
    {
        var size = new TextBlock("a\nb\nc\nd\ne").Measure(40, 2);
        Assert.Equal(2, size.Height);
    }

    [Fact]
    public void TextBlock_IncludesItsOwnPadding()
    {
        var block = new TextBlock("hi") { Style = Style.Default.Padding(1) };
        var size  = block.Measure(40, 10);
        Assert.Equal(4, size.Width);  // 2 text + 1 left + 1 right
        Assert.Equal(3, size.Height); // 1 line + 1 top  + 1 bottom
    }

    // ── Spinner ───────────────────────────────────────────────────────────────

    [Fact]
    public void Spinner_MeasuresFramePlusLabel()
    {
        var spinner = new Spinner(0, "Loading", Spinner.AsciiFrames);
        // "-" + " " + "Loading"
        Assert.Equal(new Size(9, 1), spinner.Measure(40, 10));
    }

    [Fact]
    public void Spinner_WithoutLabel_IsJustTheFrame()
        => Assert.Equal(new Size(1, 1), new Spinner(0, null, Spinner.AsciiFrames).Measure(40, 10));

    // ── Composites ────────────────────────────────────────────────────────────

    [Fact]
    public void Container_SumsAlongTheAxis_AndMaxesAcrossIt()
    {
        var container = new Container(Axis.Vertical, [
            new TextBlock("one"),
            new TextBlock("a longer line"),
        ]);

        var size = container.Measure(80, 24);
        Assert.Equal(13, size.Width);  // the longer child
        Assert.Equal(2,  size.Height); // one row each
    }

    [Fact]
    public void Container_Horizontal_SumsWidths()
    {
        var container = new Container(Axis.Horizontal, [
            new TextBlock("abc"),
            new TextBlock("de"),
        ]);
        Assert.Equal(5, container.Measure(80, 24).Width);
    }

    [Fact]
    public void Container_IncludesPaddingAndChildMargins()
    {
        var container = new Container(Axis.Vertical,
            [new TextBlock("x") { Style = Style.Default.Margin(1) }],
            style: Style.Default.Padding(2));

        var size = container.Measure(80, 24);
        // 1 line + margin 1 top and bottom + padding 2 top and bottom
        Assert.Equal(1 + 2 + 4, size.Height);
    }

    [Fact]
    public void Container_FlexChild_ContributesNothingAlongTheAxis()
    {
        // Flex means "fill the leftovers", which is not a content size. Documented on
        // Container.Measure, because it makes an all-flex Auto container collapse.
        var container = new Container(Axis.Vertical, [
            new TextBlock("x") { Height = SizeConstraint.Flex(1) },
        ]);
        Assert.Equal(0, container.Measure(80, 24).Height);
    }

    [Fact]
    public void BorderBox_AddsItsBorderToTheBodySize()
    {
        var box = new BorderBox("", new TextBlock("hi"));
        var size = box.Measure(80, 24);
        Assert.Equal(4, size.Width);  // 2 text + 1 border each side
        Assert.Equal(3, size.Height); // 1 line + 1 border each side
    }

    [Fact]
    public void BorderBox_IsAtLeastAsWideAsItsTitle()
    {
        // RenderTitle needs Width - 4, so a short body must not clip the title.
        var box = new BorderBox("A Long Title", new TextBlock("hi"));
        Assert.Equal(16, box.Measure(80, 24).Width); // 12 + 4
    }

    [Fact]
    public void ZStack_TakesTheLargestLayer()
    {
        var stack = new ZStack([
            new TextBlock("short"),
            new TextBlock("a much longer layer"),
        ]);
        Assert.Equal(19, stack.Measure(80, 24).Width);
    }

    // ── Constraint interaction ────────────────────────────────────────────────

    [Fact]
    public void MaxWrappingAuto_CapsTheContentSize()
    {
        var block = new TextBlock("a\nb\nc\nd\ne")
        {
            Height = SizeConstraint.Max(3, SizeConstraint.Auto),
        };
        var root   = new Container(Axis.Vertical, [block]);
        var layout = LayoutEngine.Resolve(root, 80, 24);

        Assert.Equal(3, layout.GetRegion(block)!.Value.Height);
    }

    [Fact]
    public void MinWrappingAuto_FloorsTheContentSize()
    {
        var block = new TextBlock("one line")
        {
            Height = SizeConstraint.Min(4, SizeConstraint.Auto),
        };
        var root   = new Container(Axis.Vertical, [block]);
        var layout = LayoutEngine.Resolve(root, 80, 24);

        Assert.Equal(4, layout.GetRegion(block)!.Value.Height);
    }

    // ── Fallback for widgets that cannot measure ──────────────────────────────

    [Fact]
    public void AutoOnANonMeasurableWidget_StillBehavesAsFlex()
    {
        var opaque = new Opaque();
        var root   = new Container(Axis.Vertical, [opaque]);
        var layout = LayoutEngine.Resolve(root, 80, 24);

        // Unchanged from before IMeasurable existed: it takes the whole container.
        Assert.Equal(24, layout.GetRegion(opaque)!.Value.Height);
    }

    [Fact]
    public void AutoAndFlexSiblings_AutoTakesItsContent_FlexTakesTheRest()
    {
        var header = new TextBlock("title");
        var body   = new TextBlock("body") { Height = SizeConstraint.Flex(1) };
        var root   = new Container(Axis.Vertical, [header, body]);
        var layout = LayoutEngine.Resolve(root, 80, 24);

        Assert.Equal(1,  layout.GetRegion(header)!.Value.Height);
        Assert.Equal(23, layout.GetRegion(body)!.Value.Height);
    }

    // ── Overflow ──────────────────────────────────────────────────────────────

    [Fact]
    public void MoreMeasuredContentThanFits_ScalesBack_DoesNotThrow()
    {
        // Ten one-line blocks in four rows. Auto children resolve like fixed ones, and
        // fixed children that cannot fit normally throw — but running out of room for
        // content is ordinary, so this has to clamp instead.
        var children = Enumerable.Range(0, 10)
            .Select(i => (IWidget)new TextBlock($"line {i}"))
            .ToArray();
        var root = new Container(Axis.Vertical, children);

        var ex = Record.Exception(() => LayoutEngine.Resolve(root, 80, 4));
        Assert.Null(ex);
    }

    [Fact]
    public void ImpossibleFixedLayout_StillThrows()
    {
        // No Auto involved, so this is a layout that genuinely cannot be satisfied.
        var children = new IWidget[]
        {
            new TextBlock("a") { Height = SizeConstraint.Fixed(10) },
            new TextBlock("b") { Height = SizeConstraint.Fixed(10) },
        };
        var root = new Container(Axis.Vertical, children);

        Assert.Throws<LayoutConstraintException>(() => LayoutEngine.Resolve(root, 80, 4));
    }

    // ── Layout and render agree ───────────────────────────────────────────────

    [Fact]
    public void RenderPlacesAutoChildrenWhereLayoutSaysTheyGo()
    {
        // Container.Render resolves sizes independently of LayoutEngine; both go through
        // LayoutSolver so both measure. If only one did, the text would land on the wrong row.
        var root = new Container(Axis.Vertical, [
            new TextBlock("first"),
            new TextBlock("second"),
            new TextBlock("third") { Height = SizeConstraint.Flex(1) },
        ]);

        var plain = TestHelpers.StripAnsi(
            ViewDescriptor.From(root, width: 20, height: 6).Content);

        Assert.Contains("first",  plain);
        Assert.Contains("second", plain);
        Assert.Contains("third",  plain);
    }
}
