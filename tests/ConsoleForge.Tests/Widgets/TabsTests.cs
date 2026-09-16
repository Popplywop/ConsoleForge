using ConsoleForge.Core;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="Tabs"/>.</summary>
public class TabsTests
{
    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ActiveIndex_ClampedToRange()
    {
        var tabs = new Tabs(["A", "B", "C"], activeIndex: 99);
        Assert.Equal(2, tabs.ActiveIndex);
    }

    [Fact]
    public void Constructor_EmptyLabels_ActiveIndexIsZero()
    {
        var tabs = new Tabs([]);
        Assert.Equal(0, tabs.ActiveIndex);
    }

    // ── Key handling ──────────────────────────────────────────────────────────

    [Fact]
    public void Update_RightArrow_AdvancesTab()
    {
        var tabs = new Tabs(["A", "B", "C"], activeIndex: 0);
        var (next, _) = tabs.Update(new KeyMsg(ConsoleKey.RightArrow, null));
        var result = (Tabs)next;

        Assert.Equal(1, result.ActiveIndex);
    }

    [Fact]
    public void Update_RightArrow_AtLast_WrapsToFirst()
    {
        var tabs = new Tabs(["A", "B", "C"], activeIndex: 2);
        var (next, _) = tabs.Update(new KeyMsg(ConsoleKey.RightArrow, null));
        var result = (Tabs)next;

        Assert.Equal(0, result.ActiveIndex);
    }

    [Fact]
    public void Update_LeftArrow_RetreatsTab()
    {
        var tabs = new Tabs(["A", "B", "C"], activeIndex: 2);
        var (next, _) = tabs.Update(new KeyMsg(ConsoleKey.LeftArrow, null));
        var result = (Tabs)next;

        Assert.Equal(1, result.ActiveIndex);
    }

    [Fact]
    public void Update_LeftArrow_AtFirst_WrapsToLast()
    {
        var tabs = new Tabs(["A", "B", "C"], activeIndex: 0);
        var (next, _) = tabs.Update(new KeyMsg(ConsoleKey.LeftArrow, null));
        var result = (Tabs)next;

        Assert.Equal(2, result.ActiveIndex);
    }

    [Fact]
    public void Update_NumberKey_JumpsToTab()
    {
        var tabs = new Tabs(["A", "B", "C"], activeIndex: 0);
        var (next, _) = tabs.Update(new KeyMsg(ConsoleKey.D3, '3'));
        var result = (Tabs)next;

        Assert.Equal(2, result.ActiveIndex); // '3' → index 2
    }

    [Fact]
    public void Update_NumberKey_OutOfRange_ClampsToLastTab()
    {
        var tabs = new Tabs(["A", "B"], activeIndex: 0); // only 2 tabs
        var (next, _) = tabs.Update(new KeyMsg(ConsoleKey.D9, '9'));
        var result = (Tabs)next;

        Assert.Equal(1, result.ActiveIndex); // '9' → clamped to last index
    }

    // ── Render ────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_AllLabelsAppearInTabBar()
    {
        var tabs = new Tabs(["Overview", "Logs", "Settings"]);
        var descriptor = ViewDescriptor.From(tabs, width: 60, height: 10);
        var plain = TestHelpers.StripAnsi(descriptor.Content);

        Assert.Contains("Overview", plain);
        Assert.Contains("Logs",     plain);
        Assert.Contains("Settings", plain);
    }

    [Fact]
    public void Render_SeparatorAppearsBeweenTabs()
    {
        var tabs = new Tabs(["A", "B", "C"]) { Separator = '│' };
        var descriptor = ViewDescriptor.From(tabs, width: 40, height: 5);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.Contains("│", plain);
    }

    [Fact]
    public void Render_NoSeparator_NoBarChar()
    {
        var tabs = new Tabs(["A", "B"]) { Separator = '\0' };
        var descriptor = ViewDescriptor.From(tabs, width: 20, height: 5);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.DoesNotContain("│", plain);
    }

    [Fact]
    public void Render_BodyContentAppearsInBodyArea()
    {
        var body = new TextBlock("BodyContent");
        var tabs = new Tabs(["Tab1", "Tab2"], activeIndex: 0, body: body);
        var descriptor = ViewDescriptor.From(tabs, width: 40, height: 5);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.Contains("BodyContent", plain);
    }

    [Fact]
    public void Render_NullBody_OnlyTabBarDrawn_NoThrow()
    {
        var tabs = new Tabs(["A", "B"]);
        var ex = Record.Exception(() => ViewDescriptor.From(tabs, width: 30, height: 3));
        Assert.Null(ex);
    }

    [Fact]
    public void Render_SingleRow_OnlyTabBar_NoBody()
    {
        var body = new TextBlock("ShouldNotAppear");
        var tabs = new Tabs(["X"], activeIndex: 0, body: body);

        // Height=1 means no room for body
        var descriptor = ViewDescriptor.From(tabs, width: 20, height: 1);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.DoesNotContain("ShouldNotAppear", plain);
    }

    [Fact]
    public void Render_EmptyLabels_DoesNotThrow()
    {
        var tabs = new Tabs([]);
        var ex = Record.Exception(() => ViewDescriptor.From(tabs, width: 30, height: 5));
        Assert.Null(ex);
    }

    [Fact]
    public void Render_BodyInsideBorderBox_RendersCorrectly()
    {
        // Verify Tabs composed inside a BorderBox doesn't blow up
        var tabs = new Tabs(["One", "Two"], body: new TextBlock("content"));
        var box = new BorderBox(title: "Panel", body: tabs);
        var ex = Record.Exception(() => ViewDescriptor.From(box, width: 40, height: 10));
        Assert.Null(ex);
    }
}