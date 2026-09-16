using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Rendering;

/// <summary>
/// The frame the renderer keeps for hit-testing, and the buffer swap that keeps it valid.
/// <para>
/// Focus handling reads the tree and layout of the frame on screen rather than calling
/// <c>View()</c> again. That only works while the on-screen layout stays readable across
/// the next resolve, which is what ping-ponging the two buffers buys. Resolving into a
/// single reused buffer would refill the layout a caller is still holding — no exception,
/// just the next frame's regions answering questions about the current one.
/// </para>
/// </summary>
public class LastFrameCacheTests
{
    private const int W = 40;
    private const int H = 12;

    [Fact]
    public void TryGetLastFrame_BeforeAnyRender_IsFalse()
    {
        var renderer = new Renderer();
        Assert.False(renderer.TryGetLastFrame(out _, out _));
    }

    [Fact]
    public void TryGetLastFrame_AfterRender_ReturnsThatTreeAndItsRegions()
    {
        var a    = new TextBlock("A") { Height = SizeConstraint.Fixed(4) };
        var root = new Container(Axis.Vertical, [a, new TextBlock("rest")]);

        var renderer = new Renderer();
        renderer.Render(root, W, H, Theme.Dark, ColorProfile.TrueColor);

        Assert.True(renderer.TryGetLastFrame(out var cachedRoot, out var cachedLayout));
        Assert.Same(root, cachedRoot);
        Assert.Equal(0, cachedLayout.GetRegion(a)!.Value.Row);
        Assert.Equal(4, cachedLayout.GetRegion(a)!.Value.Height);
    }

    [Fact]
    public void LastFrameLayout_StillDescribesItsOwnFrame_AfterTheNextRender()
    {
        // The same widget instance sits at row 0 in the first frame and row 6 in the
        // second. A layout handed out for frame one must keep saying row 0.
        var a      = new TextBlock("A") { Height = SizeConstraint.Fixed(4) };
        var first  = new Container(Axis.Vertical, [a, new TextBlock("rest")]);
        var second = new Container(Axis.Vertical,
            [new TextBlock("head") { Height = SizeConstraint.Fixed(6) }, a]);

        var renderer = new Renderer();
        renderer.Render(first, W, H, Theme.Dark, ColorProfile.TrueColor);
        Assert.True(renderer.TryGetLastFrame(out _, out var firstLayout));
        Assert.Equal(0, firstLayout.GetRegion(a)!.Value.Row);

        renderer.Render(second, W, H, Theme.Dark, ColorProfile.TrueColor);

        // Held reference: untouched by the resolve that just happened.
        Assert.Equal(0, firstLayout.GetRegion(a)!.Value.Row);

        // And the newly cached frame is the one just drawn.
        Assert.True(renderer.TryGetLastFrame(out var secondRoot, out var secondLayout));
        Assert.Same(second, secondRoot);
        Assert.Equal(6, secondLayout.GetRegion(a)!.Value.Row);
        Assert.NotSame(firstLayout, secondLayout);
    }

    [Fact]
    public void Invalidate_DropsTheCachedFrame()
    {
        // Invalidate means the screen can no longer be trusted, so neither can a hit test
        // against it. Resize takes this path before drawing at the new size.
        var renderer = new Renderer();
        renderer.Render(new TextBlock("A"), W, H, Theme.Dark, ColorProfile.TrueColor);
        Assert.True(renderer.TryGetLastFrame(out _, out _));

        renderer.Invalidate();

        Assert.False(renderer.TryGetLastFrame(out _, out _));
    }
}
