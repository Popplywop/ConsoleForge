using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>
/// Tests that <see cref="Style.Padding"/> and <see cref="Style.Margin"/> are honoured
/// by <see cref="Container"/>, <see cref="LayoutEngine"/>, <see cref="TextBlock"/>,
/// and <see cref="TextInput"/> renders.
/// </summary>
public class PaddingMarginTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Container padding — children are inset within the container's bounds
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Container_NoPadding_ChildFillsFullRegion()
    {
        var child = new TextBlock("X");
        var root  = new Container(Axis.Vertical, [child]);
        var layout = LayoutEngine.Resolve(root, 10, 1);
        var r = layout.GetRegion(child)!.Value;
        Assert.Equal(0, r.Col);
        Assert.Equal(0, r.Row);
        Assert.Equal(10, r.Width);
        Assert.Equal(1, r.Height);
    }

    [Fact]
    public void Container_Padding1_ChildInsetBy1OnAllSides()
    {
        var child = new TextBlock("X");
        var root  = new Container(Axis.Vertical, [child],
            style: Style.Default.Padding(1));
        var layout = LayoutEngine.Resolve(root, 10, 5);
        var r = layout.GetRegion(child)!.Value;
        Assert.Equal(1, r.Col);
        Assert.Equal(1, r.Row);
        Assert.Equal(8, r.Width);   // 10 - 1 - 1
        Assert.Equal(3, r.Height);  //  5 - 1 - 1
    }

    [Fact]
    public void Container_AsymmetricPadding_ChildInsetCorrectly()
    {
        // Padding(top:1, right:2, bottom:3, left:4)
        var child = new TextBlock("X");
        var root  = new Container(Axis.Vertical, [child],
            style: Style.Default.Padding(1, 2, 3, 4));
        var layout = LayoutEngine.Resolve(root, 20, 10);
        var r = layout.GetRegion(child)!.Value;
        Assert.Equal(4, r.Col);         // left pad
        Assert.Equal(1, r.Row);         // top pad
        Assert.Equal(14, r.Width);      // 20 - 4 - 2
        Assert.Equal(6,  r.Height);     // 10 - 1 - 3
    }

    [Fact]
    public void Container_Padding_ContentRenderedInInsetRegion()
    {
        var root = new Container(Axis.Vertical,
            [new TextBlock("HI")],
            style: Style.Default.Padding(1));
        var plain = TestHelpers.StripAnsi(
            ViewDescriptor.From(root, width: 10, height: 5).Content);
        Assert.Contains("HI", plain);
    }

    [Fact]
    public void Container_PaddingLargerThanRegion_DoesNotThrow()
    {
        var root = new Container(Axis.Vertical,
            [new TextBlock("X")],
            style: Style.Default.Padding(10));
        var ex = Record.Exception(() => ViewDescriptor.From(root, width: 5, height: 3));
        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Child margin — space around each child inside a container
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Container_ChildWithMargin1_ChildRegionInsetBy1()
    {
        var child = new TextBlock("X") { Style = Style.Default.Margin(1) };
        var root  = new Container(Axis.Vertical, [child]);
        var layout = LayoutEngine.Resolve(root, 10, 5);
        var r = layout.GetRegion(child)!.Value;
        Assert.Equal(1, r.Col);    // cross-axis margin
        Assert.Equal(1, r.Row);    // main-axis margin start
        Assert.Equal(8, r.Width);  // 10 - 1 - 1
        // Height = total space (5) - margin(1+1) = 3, but child is flex so gets remaining
        Assert.Equal(3, r.Height);
    }

    [Fact]
    public void Container_ChildWithMargin_RendersWithoutThrow()
    {
        var child = new TextBlock("Hello") { Style = Style.Default.Margin(1) };
        var root  = new Container(Axis.Vertical, [child]);
        var ex = Record.Exception(() => ViewDescriptor.From(root, width: 20, height: 5));
        Assert.Null(ex);
        var plain = TestHelpers.StripAnsi(
            ViewDescriptor.From(root, width: 20, height: 5).Content);
        Assert.Contains("Hello", plain);
    }

    [Fact]
    public void Container_Horizontal_ChildrenWithMargin_SpacedApart()
    {
        // Two fixed-width children with left/right margin of 1 each
        var a = new TextBlock("A")
        {
            Width = SizeConstraint.Fixed(3),
            Style = Style.Default.Margin(0, 1, 0, 1), // top=0,right=1,bottom=0,left=1
        };
        var b = new TextBlock("B")
        {
            Width = SizeConstraint.Fixed(3),
            Style = Style.Default.Margin(0, 1, 0, 1),
        };
        var root = new Container(Axis.Horizontal, [a, b]);
        var layout = LayoutEngine.Resolve(root, 80, 1);

        var rA = layout.GetRegion(a)!.Value;
        var rB = layout.GetRegion(b)!.Value;

        // A: left margin=1, content width=3 → starts at col 1
        Assert.Equal(1, rA.Col);
        Assert.Equal(3, rA.Width);

        // B: A consumed 1+3+1=5, then B left margin=1 → starts at col 6
        Assert.Equal(6, rB.Col);
        Assert.Equal(3, rB.Width);
    }

    [Fact]
    public void Container_TwoFixedChildrenWithMargin_TotalSpaceAccountedFor()
    {
        // Each child is Fixed(5) with margin(1) each side → consumes 7 per child = 14 total
        var a = new TextBlock("A") { Width = SizeConstraint.Fixed(5), Style = Style.Default.Margin(0, 1, 0, 1) };
        var b = new TextBlock("B") { Width = SizeConstraint.Fixed(5), Style = Style.Default.Margin(0, 1, 0, 1) };
        var root = new Container(Axis.Horizontal, [a, b]);
        var layout = LayoutEngine.Resolve(root, 80, 1);

        var rA = layout.GetRegion(a)!.Value;
        var rB = layout.GetRegion(b)!.Value;
        Assert.Equal(1, rA.Col);
        Assert.Equal(8, rB.Col); // 1(lm)+5(content)+1(rm) + 1(lm) = 8
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Combined container padding + child margin
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Container_PaddingAndChildMargin_Compound()
    {
        // Container padding=1, child margin=1 → child col = 2, row = 2
        var child = new TextBlock("Z")
            { Style = Style.Default.Margin(1) };
        var root = new Container(Axis.Vertical, [child],
            style: Style.Default.Padding(1));
        var layout = LayoutEngine.Resolve(root, 20, 10);
        var r = layout.GetRegion(child)!.Value;
        Assert.Equal(2, r.Col); // container padL=1 + child marginL=1
        Assert.Equal(2, r.Row); // container padT=1 + child marginT=1
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TextBlock padding — text starts at padded position
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TextBlock_NoPadding_TextAtOrigin()
    {
        var tb = new TextBlock("Hi");
        var desc = ViewDescriptor.From(tb, width: 20, height: 1);
        var plain = TestHelpers.StripAnsi(desc.Content);
        Assert.Contains("Hi", plain);
    }

    [Fact]
    public void TextBlock_WithPadding_TextStillAppears()
    {
        var tb = new TextBlock("Hi") { Style = Style.Default.Padding(1) };
        var desc = ViewDescriptor.From(tb, width: 20, height: 5);
        var plain = TestHelpers.StripAnsi(desc.Content);
        Assert.Contains("Hi", plain);
    }

    [Fact]
    public void TextBlock_PaddingLargerThanRegion_DoesNotThrow()
    {
        var tb = new TextBlock("Hi") { Style = Style.Default.Padding(5) };
        var ex = Record.Exception(() => ViewDescriptor.From(tb, width: 3, height: 2));
        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TextInput padding — text area inset within widget bounds
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TextInput_WithPadding_RendersWithoutThrow()
    {
        var ti = new TextInput("hello") { Style = Style.Default.Padding(0, 1, 0, 1) };
        var ex = Record.Exception(() => ViewDescriptor.From(ti, width: 20, height: 1));
        Assert.Null(ex);
    }

    [Fact]
    public void TextInput_PaddingLargerThanRegion_DoesNotThrow()
    {
        var ti = new TextInput("hi") { Style = Style.Default.Padding(10) };
        var ex = Record.Exception(() => ViewDescriptor.From(ti, width: 5, height: 1));
        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BorderBox padding — body inset beyond the border
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BorderBox_WithPadding_BodyRegionFurtherInset()
    {
        var body = new TextBlock("inner");
        var box  = new BorderBox("T", body,
            style: Style.Default.Border(Borders.Normal).Padding(1));
        var layout = LayoutEngine.Resolve(box, 20, 10);
        var bodyR  = layout.GetRegion(body)!.Value;
        // border=1 + padding=1 → body starts at col 2, row 2
        Assert.Equal(2, bodyR.Col);
        Assert.Equal(2, bodyR.Row);
        Assert.Equal(16, bodyR.Width);   // 20 - 2 - 2
        Assert.Equal(6,  bodyR.Height);  // 10 - 2 - 2
    }

    [Fact]
    public void BorderBox_NoPadding_BodyRegionOneBorderInset()
    {
        var body = new TextBlock("inner");
        var box  = new BorderBox("T", body);
        var layout = LayoutEngine.Resolve(box, 20, 10);
        var bodyR  = layout.GetRegion(body)!.Value;
        Assert.Equal(1,  bodyR.Col);
        Assert.Equal(1,  bodyR.Row);
        Assert.Equal(18, bodyR.Width);
        Assert.Equal(8,  bodyR.Height);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Zero padding/margin — no regression when values are zero
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Container_ExplicitZeroPadding_BehavesLikeNoPadding()
    {
        var child = new TextBlock("X");
        var withPad    = new Container(Axis.Vertical, [child], style: Style.Default.Padding(0));
        var withoutPad = new Container(Axis.Vertical, [child]);

        var r1 = LayoutEngine.Resolve(withPad,    10, 5).GetRegion(child)!.Value;
        var r2 = LayoutEngine.Resolve(withoutPad, 10, 5).GetRegion(child)!.Value;

        Assert.Equal(r2.Col,    r1.Col);
        Assert.Equal(r2.Row,    r1.Row);
        Assert.Equal(r2.Width,  r1.Width);
        Assert.Equal(r2.Height, r1.Height);
    }

    [Fact]
    public void Style_HasPadding_FalseByDefault()
    {
        Assert.False(Style.Default.HasPadding);
    }

    [Fact]
    public void Style_HasPadding_TrueAfterPadding()
    {
        Assert.True(Style.Default.Padding(1).HasPadding);
    }

    [Fact]
    public void Style_HasMargin_FalseByDefault()
    {
        Assert.False(Style.Default.HasMargin);
    }

    [Fact]
    public void Style_HasMargin_TrueAfterMargin()
    {
        Assert.True(Style.Default.Margin(1).HasMargin);
    }
}
