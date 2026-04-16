using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="ZStack"/>.</summary>
public class ZStackTests
{
    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_EmptyLayers_DoesNotThrow()
    {
        var zs = new ZStack([]);
        var ex = Record.Exception(() => ViewDescriptor.From(zs, width: 40, height: 10));
        Assert.Null(ex);
    }

    // ── Render: layering ─────────────────────────────────────────────────────

    [Fact]
    public void Render_SingleLayer_ShowsContent()
    {
        var zs = new ZStack([new TextBlock("BaseContent")]);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(zs, width: 40, height: 5).Content);
        Assert.Contains("BaseContent", plain);
    }

    [Fact]
    public void Render_TwoLayers_BothContentRendered()
    {
        // Layer 1: fills full region with text on row 0.
        // Layer 2: fills row 1 with different text.
        // Both content strings should appear in different rows.
        var layer1 = new Container(Axis.Vertical, [
            new TextBlock("BottomLayer"),
            new TextBlock(""),
        ]);
        var layer2 = new Container(Axis.Vertical, [
            new TextBlock(""),
            new TextBlock("TopLayer"),
        ]);
        var zs = new ZStack([layer1, layer2]);

        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(zs, width: 40, height: 5).Content);
        Assert.Contains("BottomLayer", plain);
        Assert.Contains("TopLayer",   plain);
    }

    [Fact]
    public void Render_TopLayerOverwritesBottomAtSamePosition()
    {
        // Both layers write to row 0 col 0.
        // The top layer (second) should win.
        var bottom = new TextBlock("AAAAAAAAAA");
        var top    = new TextBlock("BBBBBBBBBB");
        var zs = new ZStack([bottom, top]);

        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(zs, width: 20, height: 1).Content);
        Assert.Contains("BBBBBBBBBB", plain);
        // AAAA should have been overwritten
        Assert.DoesNotContain("AAAAAAAAAA", plain);
    }

    [Fact]
    public void Render_BottomContentVisibleAroundTopOverlay()
    {
        // Row 0: bottom layer fills "Background..."
        // Row 1+: top layer only touches rows 2-4.
        // Row 0 of the bottom layer should still be visible.
        var bottom = new Container(Axis.Vertical, [
            new TextBlock("BackgroundRow0"),
            new TextBlock("BackgroundRow1"),
            new TextBlock("BackgroundRow2"),
        ]);
        var top = new Container(Axis.Vertical, [
            new TextBlock(""),
            new TextBlock(""),
            new TextBlock("OverlayRow2"),
        ]);
        var zs = new ZStack([bottom, top]);

        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(zs, width: 40, height: 5).Content);
        Assert.Contains("BackgroundRow0", plain);
        Assert.Contains("OverlayRow2",    plain);
    }

    // ── Focus traversal ───────────────────────────────────────────────────────

    [Fact]
    public void FocusManager_CollectsFromAllLayers()
    {
        var inputA = new TextInput("a");
        var inputB = new TextInput("b");
        var zs = new ZStack([
            new Container(Axis.Vertical, [inputA]),
            new Container(Axis.Vertical, [inputB]),
        ]);

        var focusable = FocusManager.CollectFocusable(zs);
        Assert.Equal(2, focusable.Count);
        Assert.Same(inputA, focusable[0]);
        Assert.Same(inputB, focusable[1]);
    }

    [Fact]
    public void FocusManager_EmptyLayers_ReturnsEmpty()
    {
        var zs = new ZStack([new TextBlock("no inputs")]);
        var focusable = FocusManager.CollectFocusable(zs);
        Assert.Empty(focusable);
    }

    // ── ILayeredContainer ─────────────────────────────────────────────────────

    [Fact]
    public void Layers_Property_ReturnsProvidedLayers()
    {
        var a = new TextBlock("A");
        var b = new TextBlock("B");
        var zs = new ZStack([a, b]);
        Assert.Equal(2, zs.Layers.Count);
        Assert.Same(a, zs.Layers[0]);
        Assert.Same(b, zs.Layers[1]);
    }
}
