using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;   // unqualified: ConsoleForge.Tests.Styling would shadow it
using ConsoleForge.Tests.Rendering;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Layout;

/// <summary>
/// When children do not fit, <see cref="LayoutEngine"/> and <c>Container.Render</c> must
/// agree about where they end up.
/// </summary>
/// <remarks>
/// They used to disagree: the layout pass scaled oversized children back to fit while the
/// render pass placed them at full size and let the region clip. Everything below the first
/// overflowing child then drew somewhere other than where focus and hit-testing believed it
/// was — and a screen with more content than rows, which is ordinary, looked scrambled.
/// </remarks>
public class OverflowAgreementTests
{
    private const int Width = 40, Height = 24;

    /// <summary>Three children wanting 40 rows in 24, with a flex child so it clamps rather than throws.</summary>
    private static (IWidget Root, IWidget[] Children) Overflowing()
    {
        var children = new IWidget[]
        {
            new TextBlock("AAA") { Height = SizeConstraint.Fixed(20) },
            new TextBlock("BBB") { Height = SizeConstraint.Fixed(20) },
            new TextBlock("CCC") { Height = SizeConstraint.Flex(1) },
        };
        return (new Container(Axis.Vertical, children), children);
    }

    [Fact]
    public void RenderPlacesAnOverflowingChildWhereLayoutSaysItGoes()
    {
        var (root, children) = Overflowing();

        var layout = LayoutEngine.Resolve(root, Width, Height);
        int expectedRow = layout.GetRegion(children[1])!.Value.Row;

        var sim = new TerminalSim(Width, Height);
        sim.Apply(ViewDescriptor.From(root, width: Width, height: Height).Content);

        int actualRow = Array.FindIndex(sim.Lines, l => l.Contains("BBB", StringComparison.Ordinal));

        Assert.True(expectedRow == actualRow,
            $"layout put the second child on row {expectedRow}, render drew it on row {actualRow}");
    }

    [Fact]
    public void OverflowingChildrenAreScaledToFit_NotLeftToClip()
    {
        var (root, children) = Overflowing();
        var layout = LayoutEngine.Resolve(root, Width, Height);

        int total = 0;
        foreach (var child in children) total += layout.GetRegion(child)!.Value.Height;

        Assert.Equal(Height, total);
    }

    [Fact]
    public void AnUnsatisfiableLayoutThrowsFromLayout_ButRenderStillDrawsAFrame()
    {
        // No flex child and no measured child: the layout genuinely cannot be satisfied.
        var children = new IWidget[]
        {
            new TextBlock("AAA") { Height = SizeConstraint.Fixed(20) },
            new TextBlock("BBB") { Height = SizeConstraint.Fixed(20) },
        };
        var root = new Container(Axis.Vertical, children);

        Assert.Throws<LayoutConstraintException>(() => LayoutEngine.Resolve(root, Width, Height));

        // Render must not throw a second time: a frame that can still be drawn should be.
        var region = new Region(0, 0, Width, Height);
        var ctx    = new RenderContext(region, Theme.Dark, ColorProfile.TrueColor,
                                       new ResolvedLayout(new Dictionary<IWidget, Region>()));
        Assert.Null(Record.Exception(() => root.Render(ctx)));
    }
}
