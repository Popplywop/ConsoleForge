using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Layout;

/// <summary>Unit tests for <see cref="LayoutEngine"/>.</summary>
public class LayoutEngineTests
{
    // ── Fixed children ────────────────────────────────────────────────────────

    [Fact]
    public void VerticalContainer_TwoFixedChildren_GetExactSizes()
    {
        var a = new TextBlock("A") { Height = SizeConstraint.Fixed(4) };
        var b = new TextBlock("B") { Height = SizeConstraint.Fixed(6) };
        var root = new Container(Axis.Vertical, [a, b]);

        var layout = LayoutEngine.Resolve(root, 80, 24);

        Assert.Equal(4, layout.GetRegion(a)!.Value.Height);
        Assert.Equal(6, layout.GetRegion(b)!.Value.Height);
    }

    [Fact]
    public void HorizontalContainer_TwoFixedChildren_GetExactSizes()
    {
        var a = new TextBlock("A") { Width = SizeConstraint.Fixed(20) };
        var b = new TextBlock("B") { Width = SizeConstraint.Fixed(30) };
        var root = new Container(Axis.Horizontal, [a, b]);

        var layout = LayoutEngine.Resolve(root, 80, 24);

        Assert.Equal(20, layout.GetRegion(a)!.Value.Width);
        Assert.Equal(30, layout.GetRegion(b)!.Value.Width);
    }

    // ── Flex children ─────────────────────────────────────────────────────────

    [Fact]
    public void VerticalContainer_TwoEqualFlexChildren_SplitEvenly()
    {
        var a = new TextBlock("A") { Height = SizeConstraint.Flex(1) };
        var b = new TextBlock("B") { Height = SizeConstraint.Flex(1) };
        var root = new Container(Axis.Vertical, [a, b]);

        var layout = LayoutEngine.Resolve(root, 80, 20);

        Assert.Equal(10, layout.GetRegion(a)!.Value.Height);
        Assert.Equal(10, layout.GetRegion(b)!.Value.Height);
    }

    [Fact]
    public void HorizontalContainer_WeightedFlexChildren_DistributeProportionally()
    {
        // Weights 1:2 in 90px → 30 and 60
        var a = new TextBlock("A") { Width = SizeConstraint.Flex(1) };
        var b = new TextBlock("B") { Width = SizeConstraint.Flex(2) };
        var root = new Container(Axis.Horizontal, [a, b]);

        var layout = LayoutEngine.Resolve(root, 90, 24);

        Assert.Equal(30, layout.GetRegion(a)!.Value.Width);
        Assert.Equal(60, layout.GetRegion(b)!.Value.Width);
    }

    [Fact]
    public void FlexRemainder_GoesToLastFlexChild()
    {
        // 3 equal flex children in 10px → 3,3,4 (remainder to last)
        var a = new TextBlock("A") { Width = SizeConstraint.Flex(1) };
        var b = new TextBlock("B") { Width = SizeConstraint.Flex(1) };
        var c = new TextBlock("C") { Width = SizeConstraint.Flex(1) };
        var root = new Container(Axis.Horizontal, [a, b, c]);

        var layout = LayoutEngine.Resolve(root, 10, 24);

        var wa = layout.GetRegion(a)!.Value.Width;
        var wb = layout.GetRegion(b)!.Value.Width;
        var wc = layout.GetRegion(c)!.Value.Width;
        Assert.Equal(10, wa + wb + wc); // total must equal available
    }

    // ── Mixed fixed + flex ────────────────────────────────────────────────────

    [Fact]
    public void MixedFixedAndFlex_FlexGetsRemainder()
    {
        var fixed1 = new TextBlock("F") { Width = SizeConstraint.Fixed(20) };
        var flex1  = new TextBlock("X") { Width = SizeConstraint.Flex(1)  };
        var root   = new Container(Axis.Horizontal, [fixed1, flex1]);

        var layout = LayoutEngine.Resolve(root, 80, 24);

        Assert.Equal(20, layout.GetRegion(fixed1)!.Value.Width);
        Assert.Equal(60, layout.GetRegion(flex1)!.Value.Width);
    }

    [Fact]
    public void MixedFixedAndFlex_MultipleFlexShareRemainder()
    {
        var sidebar = new TextBlock("S") { Width = SizeConstraint.Fixed(20) };
        var left    = new TextBlock("L") { Width = SizeConstraint.Flex(1)  };
        var right   = new TextBlock("R") { Width = SizeConstraint.Flex(1)  };
        var root    = new Container(Axis.Horizontal, [sidebar, left, right]);

        var layout = LayoutEngine.Resolve(root, 80, 24);

        Assert.Equal(20, layout.GetRegion(sidebar)!.Value.Width);
        // 60 remaining split evenly → 30 each
        Assert.Equal(30, layout.GetRegion(left)!.Value.Width);
        Assert.Equal(30, layout.GetRegion(right)!.Value.Width);
    }

    // ── Overflow ──────────────────────────────────────────────────────────────

    [Fact]
    public void AllFixedOverflow_NoFlexChildren_ThrowsLayoutConstraintException()
    {
        var a = new TextBlock("A") { Width = SizeConstraint.Fixed(50) };
        var b = new TextBlock("B") { Width = SizeConstraint.Fixed(50) };
        var root = new Container(Axis.Horizontal, [a, b]);

        // 100px children in 80px container with no flex → must throw
        Assert.Throws<LayoutConstraintException>(
            () => LayoutEngine.Resolve(root, 80, 24));
    }

    [Fact]
    public void FixedPlusFlexOverflow_ClampsProprtionally()
    {
        // Fixed 50 + Flex 1 in 30px → total would be > 30, but flex is present → clamp not throw
        var a = new TextBlock("A") { Width = SizeConstraint.Fixed(50) };
        var b = new TextBlock("B") { Width = SizeConstraint.Flex(1)   };
        var root = new Container(Axis.Horizontal, [a, b]);

        var layout = LayoutEngine.Resolve(root, 30, 24);

        var wa = layout.GetRegion(a)!.Value.Width;
        var wb = layout.GetRegion(b)!.Value.Width;
        Assert.True(wa >= 0);
        Assert.True(wb >= 0);
        Assert.Equal(30, wa + wb); // total must not exceed available
    }

    // ── Absolute positions ────────────────────────────────────────────────────

    [Fact]
    public void VerticalContainer_ChildrenStack_CorrectRowOffsets()
    {
        var a = new TextBlock("A") { Height = SizeConstraint.Fixed(3) };
        var b = new TextBlock("B") { Height = SizeConstraint.Fixed(5) };
        var root = new Container(Axis.Vertical, [a, b]);

        var layout = LayoutEngine.Resolve(root, 80, 24);

        Assert.Equal(0, layout.GetRegion(a)!.Value.Row);
        Assert.Equal(3, layout.GetRegion(b)!.Value.Row);
    }

    [Fact]
    public void HorizontalContainer_ChildrenSideBySide_CorrectColOffsets()
    {
        var a = new TextBlock("A") { Width = SizeConstraint.Fixed(10) };
        var b = new TextBlock("B") { Width = SizeConstraint.Fixed(15) };
        var root = new Container(Axis.Horizontal, [a, b]);

        var layout = LayoutEngine.Resolve(root, 80, 24);

        Assert.Equal(0,  layout.GetRegion(a)!.Value.Col);
        Assert.Equal(10, layout.GetRegion(b)!.Value.Col);
    }

    // ── Nested containers ─────────────────────────────────────────────────────

    [Fact]
    public void NestedContainers_InnerChildrenGetCorrectRegions()
    {
        var inner = new TextBlock("I") { Width = SizeConstraint.Fixed(20) };
        var innerContainer = new Container(Axis.Horizontal, [inner]) { Width = SizeConstraint.Fixed(40) };
        var root = new Container(Axis.Horizontal, [innerContainer]);

        var layout = LayoutEngine.Resolve(root, 80, 24);

        Assert.Equal(40, layout.GetRegion(innerContainer)!.Value.Width);
        Assert.Equal(20, layout.GetRegion(inner)!.Value.Width);
        Assert.Equal(0, layout.GetRegion(inner)!.Value.Col);
    }

    // ── SizeConstraint.Min / Max ──────────────────────────────────────────────

    [Fact]
    public void MinConstraint_EnforcesMinimumFixed()
    {
        // Min(10, Fixed(5)) → effective size 10
        var a = new TextBlock("A") { Width = SizeConstraint.Min(10, SizeConstraint.Fixed(5)) };
        var b = new TextBlock("B") { Width = SizeConstraint.Flex(1) };
        var root = new Container(Axis.Horizontal, [a, b]);

        var layout = LayoutEngine.Resolve(root, 80, 24);
        Assert.Equal(10, layout.GetRegion(a)!.Value.Width);
    }

    [Fact]
    public void MaxConstraint_CapsFixedSize()
    {
        // Max(15, Fixed(30)) → effective size 15
        var a = new TextBlock("A") { Width = SizeConstraint.Max(15, SizeConstraint.Fixed(30)) };
        var b = new TextBlock("B") { Width = SizeConstraint.Flex(1) };
        var root = new Container(Axis.Horizontal, [a, b]);

        var layout = LayoutEngine.Resolve(root, 80, 24);
        Assert.Equal(15, layout.GetRegion(a)!.Value.Width);
    }

    // ── Empty container ───────────────────────────────────────────────────────

    [Fact]
    public void EmptyContainer_Resolves_WithoutError()
    {
        var root = new Container(Axis.Vertical, []);
        var ex = Record.Exception(() => LayoutEngine.Resolve(root, 80, 24));
        Assert.Null(ex);
    }

    // ── Single child ─────────────────────────────────────────────────────────

    [Fact]
    public void SingleFlexChild_GetsAllSpace()
    {
        var a = new TextBlock("A");
        var root = new Container(Axis.Vertical, [a]);

        var layout = LayoutEngine.Resolve(root, 80, 24);
        Assert.Equal(24, layout.GetRegion(a)!.Value.Height);
    }
}
