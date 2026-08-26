using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Rendering;

/// <summary>
/// Differential tests for the double-buffered renderer.
/// <para>
/// Invariant: applying the incremental frames for views A then B to a terminal
/// must leave exactly the screen a from-scratch full redraw of B would produce.
/// Any divergence is a redraw artifact — stale glyphs left on screen.
/// </para>
/// </summary>
public class IncrementalRedrawTests
{
    private const int W = 60;
    private const int H = 12;

    /// <summary>Render <paramref name="views"/> in order through one renderer, returning the final screen.</summary>
    private static string RenderSequence(IEnumerable<IWidget> views, int width = W, int height = H)
    {
        var renderer = new Renderer();
        var sim = new TerminalSim(width, height);
        foreach (var view in views)
        {
            var frame = renderer.Render(view, width, height, Theme.Dark, ColorProfile.TrueColor);
            sim.Apply(frame.Content);
        }
        return sim.Screen;
    }

    /// <summary>The screen a full, from-scratch redraw of <paramref name="view"/> produces.</summary>
    private static string RenderFresh(IWidget view, int width = W, int height = H) =>
        RenderSequence([view], width, height);

    private static void AssertNoArtifacts(IWidget first, IWidget second, int width = W, int height = H)
    {
        var incremental = RenderSequence([first, second], width, height);
        var expected    = RenderFresh(second, width, height);
        Assert.Equal(expected, incremental);
    }

    [Fact]
    public void SecondFrame_IsADiff_NotAFullRepaint()
    {
        // Guards the tests below: if the renderer ever fell back to repainting every
        // cell, they would pass trivially and stop detecting stale-cell bugs.
        var renderer = new Renderer();
        var first  = renderer.Render(new TextBlock("hello"), W, H, Theme.Dark, ColorProfile.TrueColor);
        var second = renderer.Render(new TextBlock("world"), W, H, Theme.Dark, ColorProfile.TrueColor);

        Assert.True(second.Content.Length < first.Content.Length / 4,
            $"expected a small diff, got {second.Content.Length} vs {first.Content.Length} chars");
    }

    // ── Repro: PlexTui show/episode navigation ────────────────────────────────
    // Panel titles carry emoji (2 columns wide). Navigating in and back out
    // swaps one wide-titled box for another.

    private static IWidget PlexPage(string title, string[] items, IColor accent) =>
        new Container(Axis.Vertical, [
            new Container(Axis.Horizontal, [
                new BorderBox(title,
                    new List(items, 0,
                        selectedItemStyle: Style.Default.Background(accent).Foreground(Color.Black)),
                    style: Style.Default.BorderForeground(accent)
                ) { Width = SizeConstraint.Flex(2) },
                new BorderBox("Show",
                    new TextBlock("Some Show"),
                    style: Style.Default.Border(Borders.Rounded)
                ) { Width = SizeConstraint.Flex(1) },
            ]) { Height = SizeConstraint.Flex(1) },
            new TextBlock(" Navigate   Enter Select   Esc Back") { Height = SizeConstraint.Fixed(1) },
        ]);

    [Fact]
    public void NavigatingIntoEpisodesAndBack_LeavesNoArtifacts()
    {
        var seasons = PlexPage("\U0001F4FA  Some Show  —  Seasons",
            ["Season 1", "Season 2", "Season 3"], Color.FromHex("#FFB86C"));
        var episodes = PlexPage("\U0001F39E   Some Show  ·  Season 1",
            ["S01E01  Pilot", "S01E02  Second"], Color.FromHex("#50FA7B"));

        AssertNoArtifacts(seasons, episodes);
        AssertNoArtifacts(episodes, seasons);
    }

    // ── Minimal wide-glyph cases ──────────────────────────────────────────────

    [Fact]
    public void WideGlyphReplacedByNarrowText_LeavesNoArtifacts()
    {
        AssertNoArtifacts(
            new TextBlock("\U0001F4FA ok"),
            new TextBlock("abcdef"));
    }

    [Fact]
    public void NarrowTextReplacedByWideGlyph_LeavesNoArtifacts()
    {
        AssertNoArtifacts(
            new TextBlock("abcdef"),
            new TextBlock("\U0001F4FA ok"));
    }

    [Fact]
    public void WideGlyphShiftedOneColumn_LeavesNoArtifacts()
    {
        AssertNoArtifacts(
            new TextBlock("\U0001F4FAxx"),
            new TextBlock("a\U0001F4FAxx"));
    }

    [Fact]
    public void TrailingTextShrinks_LeavesNoArtifacts()
    {
        AssertNoArtifacts(
            new TextBlock("a long line of text"),
            new TextBlock("short"));
    }

    // ── Theme switching ───────────────────────────────────────────────────────

    [Fact]
    public void SwitchingTheme_RepaintsUntouchedBackgroundCells()
    {
        // Cells no widget writes to render as the theme's default cell. Switching
        // themes changes that cell, so every one of them must be repainted even
        // though the widget content did not change.
        var view     = new TextBlock("hello");
        var renderer = new Renderer();

        renderer.Render(view, W, H, Theme.Dark, ColorProfile.TrueColor);
        var switched = renderer.Render(view, W, H, Theme.Light, ColorProfile.TrueColor);

        var fresh = new Renderer()
            .Render(view, W, H, Theme.Light, ColorProfile.TrueColor);

        Assert.Equal(fresh.Content, switched.Content);
    }

    [Fact]
    public void RepeatingTheSameThemeByValue_StillDiffs()
    {
        // An equal-but-distinct Theme instance must not be mistaken for a change,
        // or every frame degenerates into a full repaint.
        var renderer = new Renderer();
        var first  = renderer.Render(new TextBlock("hello"), W, H, new Theme { Name = "t" }, ColorProfile.TrueColor);
        var second = renderer.Render(new TextBlock("world"), W, H, new Theme { Name = "t" }, ColorProfile.TrueColor);

        Assert.True(second.Content.Length < first.Content.Length / 4,
            $"expected a small diff, got {second.Content.Length} vs {first.Content.Length} chars");
    }
}
