using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Tests.Rendering;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="ProgressBar"/>.</summary>
public class ProgressBarTests
{
    private static string Line(IWidget bar, int width)
    {
        var root = new Container(Axis.Horizontal, [bar, new TextBlock("|")]);
        var sim = new TerminalSim(width + 1, 1);
        sim.Apply(new Renderer().Render(root, width + 1, 1, Theme.Dark, ColorProfile.TrueColor).Content);
        return sim.Lines[0];
    }

    [Fact]
    public void TooNarrowForTheLabel_DrawsTheBarAlone()
    {
        // The label was written past the region regardless: " 50%" beside a one-cell bar.
        Assert.Equal("██░|", Line(new ProgressBar(50) { Width = SizeConstraint.Fixed(3) }, 3));
    }

    [Fact]
    public void WideEnough_KeepsTheLabel()
        => Assert.Equal("██░░  50%|", Line(new ProgressBar(50) { Width = SizeConstraint.Fixed(9) }, 9));
}
