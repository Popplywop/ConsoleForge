using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Tests.Rendering;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="Spinner"/>.</summary>
public class SpinnerTests
{
    [Fact]
    public void WideLabel_IsCutByColumns_NotChars()
    {
        // Cut at six *chars*, "⠋ 中文中文" is ten columns: it ran four past its region.
        var root = new Container(Axis.Horizontal,
            [new Spinner(0, "中文中文中文") { Width = SizeConstraint.Fixed(6) }, new TextBlock("|")]);
        var sim = new TerminalSim(7, 1);
        sim.Apply(new Renderer().Render(root, 7, 1, Theme.Dark, ColorProfile.TrueColor).Content);
        Assert.Equal("⠋ 中文|", sim.Lines[0]);
    }
}
