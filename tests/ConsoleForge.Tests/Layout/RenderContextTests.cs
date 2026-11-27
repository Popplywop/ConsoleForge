using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Tests.Rendering;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Layout;

/// <summary>
/// What <see cref="RenderContext.Write"/> puts on screen, read back through
/// <see cref="TerminalSim"/>, which moves its cursor by display width the way a
/// terminal does — so a cell that drifts, or a byte that moves the cursor, shows.
/// </summary>
public class RenderContextTests
{
    private static string[] Screen(IWidget root, int width, int height)
    {
        var sim = new TerminalSim(width, height);
        sim.Apply(new Renderer().Render(root, width, height, Theme.Dark, ColorProfile.TrueColor).Content);
        return sim.Lines;
    }

    private static string Raw(IWidget root, int width, int height) =>
        new Renderer().Render(root, width, height, Theme.Dark, ColorProfile.TrueColor).Content;

    // ── Styles shape blocks, not cells ────────────────────────────────────────

    [Theory]
    [InlineData("café")]
    [InlineData("日本")]
    [InlineData("naïve")]
    public void PaddedStyle_DoesNotPadEachNonAsciiGlyph(string text)
    {
        // The padding is the widget's to apply, once. Each non-ASCII cell used to go
        // through Style.Render, which padded it again: "café" drew as "c  a  f".
        var style = Style.Default.Padding(0, 1, 0, 1).Foreground(Color.Red);
        var line = Screen(new TextBlock(text, style), 12, 1)[0];
        Assert.Equal(" " + text, line.TrimEnd());
    }

    [Fact]
    public void WidthStyle_DoesNotWidenEachNonAsciiGlyph()
    {
        var style = Style.Default.Width(6).Foreground(Color.Red);
        Assert.Equal("é", Screen(new TextBlock("é", style), 12, 1)[0].TrimEnd());
    }

    // ── Control characters ────────────────────────────────────────────────────

    [Fact]
    public void CrLfLineEndings_KeepTheFirstLine()
    {
        // The CR reached the terminal, sent the cursor to column 0, and the blanks
        // after it erased the line.
        var lines = Screen(new TextBlock("ab\r\ncd"), 6, 2);
        Assert.Equal("ab", lines[0].TrimEnd());
        Assert.Equal("cd", lines[1].TrimEnd());
    }

    [Fact]
    public void CrLfText_MeasuresWithoutTheCr()
    {
        Assert.Equal(new Size(2, 2), new TextBlock("ab\r\ncd").Measure(20, 20));
    }

    [Fact]
    public void Tab_DrawsAsOneBlank()
    {
        Assert.Equal("a b|", Screen(new TextBlock("a\tb|"), 8, 1)[0].TrimEnd());
    }

    [Theory]
    [InlineData("a\u001b]0;pwned\u0007b")]          // OSC: set the window title
    [InlineData("a\u001b[2Jb")]                     // CSI: clear the screen
    [InlineData("中\u001b]52;c;aGk=\u0007文")]       // non-ASCII path: OSC 52 clipboard write
    [InlineData("中\u009b2J文")]                     // C1 CSI
    public void EscapeSequencesInText_NeverReachTheTerminal(string text)
    {
        // Every ESC in the frame must open a sequence the renderer wrote itself: a
        // cursor move or an SGR. Anything left after removing those came from the text.
        var raw = Raw(new TextBlock(text), 20, 1);
        var stray = System.Text.RegularExpressions.Regex.Replace(raw, @"\x1b\[[0-9;]*[mH]", "");
        Assert.DoesNotContain('\u001b', stray);
        Assert.DoesNotContain('\u009b', stray);
        Assert.DoesNotContain('\u0007', stray);
    }

    // ── Wide glyphs ───────────────────────────────────────────────────────────

    [Fact]
    public void NarrowTextOverHalfAWideGlyph_LeavesTheRestOfTheRowInPlace()
    {
        // XY lands on the right half of 中 and the left half of 文. The terminal erases
        // both glyphs; the buffer kept them, so 中 was emitted two columns wide, X went
        // out one column late, and so did everything after it.
        var under = new TextBlock("中文中文中文");
        var over = new Overflowing("XY", Offset: 1);
        Assert.Equal(" XY 中文中文", Screen(new ZStack([under, over]), 12, 1)[0]);
    }

    // ── Clipping ──────────────────────────────────────────────────────────────

    [Fact]
    public void TextPastARegionsRightEdge_StopsAtTheEdge()
    {
        var root = new Container(Axis.Horizontal,
        [
            new Overflowing("abcdefghij") { Width = SizeConstraint.Fixed(4) },
            new Container(Axis.Vertical, []),
        ]);
        Assert.Equal("abcd", Screen(root, 12, 1)[0].TrimEnd());
    }

    [Fact]
    public void TextStartingLeftOfARegion_StartsAtTheEdge()
    {
        var root = new Container(Axis.Horizontal,
        [
            new Container(Axis.Vertical, [], width: SizeConstraint.Fixed(2)),
            new Overflowing("abcdef", Offset: -2) { Width = SizeConstraint.Fixed(4) },
            new Container(Axis.Vertical, []),
        ]);
        Assert.Equal("  cdef", Screen(root, 12, 1)[0].TrimEnd());
    }

    [Fact]
    public void WideGlyphCutByTheRightEdge_DrawsABlank()
    {
        var root = new Container(Axis.Horizontal,
        [
            new Overflowing("ab中") { Width = SizeConstraint.Fixed(3) },
            new TextBlock("|"),
        ]);
        Assert.Equal("ab |", Screen(root, 6, 1)[0].TrimEnd());
    }

    [Fact]
    public void NestedRegions_ClipToTheirIntersection()
    {
        var ctx = new RenderContext(new Region(0, 0, 10, 1), Theme.Dark, ColorProfile.TrueColor, LayoutEngine.Resolve(new TextBlock(""), 10, 1));
        var outer = new SubRenderContext(ctx, new Region(2, 0, 4, 1));
        var inner = new SubRenderContext(outer, new Region(0, 0, 10, 1));
        inner.Write(0, 0, "0123456789", Style.Default);

        var sim = new TerminalSim(10, 1);
        sim.Apply(ctx.ToAnsiFrame());
        Assert.Equal("  2345", sim.Lines[0].TrimEnd());
    }

    /// <summary>Writes its text at its region's left edge plus an offset, unclipped.</summary>
    private sealed record Overflowing(string Text, int Offset = 0) : IWidget
    {
        public SizeConstraint Width { get; init; } = SizeConstraint.Flex(1);
        public SizeConstraint Height { get; init; } = SizeConstraint.Fixed(1);
        public Style Style => Style.Default;
        public void Render(IRenderContext ctx) => ctx.Write(ctx.Region.Col + Offset, ctx.Region.Row, Text, Style.Default);
    }
}
