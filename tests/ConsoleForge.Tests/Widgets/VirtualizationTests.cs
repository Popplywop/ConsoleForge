using ConsoleForge.Core;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>
/// Tests for virtualized scroll behaviour added to <see cref="List"/> and
/// <see cref="Table"/>: <c>ScrollOffset</c>, <c>ComputeScrollOffset</c>, and
/// viewport-clipped rendering.
/// </summary>
public class VirtualizationTests
{
    // ════════════════════════════════════════════════════════════════════════
    // List — ComputeScrollOffset
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void List_ComputeScrollOffset_CursorInView_Unchanged()
    {
        // cursor 3, viewport 5, scroll 1 → rows 1-5 visible → no change
        Assert.Equal(1, List.ComputeScrollOffset(3, 5, 1));
    }

    [Fact]
    public void List_ComputeScrollOffset_CursorAboveView_ScrollsUp()
    {
        Assert.Equal(2, List.ComputeScrollOffset(2, 5, 5));
    }

    [Fact]
    public void List_ComputeScrollOffset_CursorBelowView_ScrollsDown()
    {
        // cursor 10, viewport 5, scroll 0 → needs to show 10 → scroll = 10-5+1 = 6
        Assert.Equal(6, List.ComputeScrollOffset(10, 5, 0));
    }

    [Fact]
    public void List_ComputeScrollOffset_ZeroViewport_Unchanged()
    {
        Assert.Equal(3, List.ComputeScrollOffset(5, 0, 3));
    }

    [Fact]
    public void List_ComputeScrollOffset_CursorAtFirstRow_ScrollsToZero()
    {
        Assert.Equal(0, List.ComputeScrollOffset(0, 5, 3));
    }

    // ════════════════════════════════════════════════════════════════════════
    // List — ScrollOffset rendering
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void List_ScrollOffset_Zero_ShowsFromFirstItem()
    {
        var items = Enumerable.Range(0, 20).Select(i => $"item{i:D2}").ToArray();
        var list  = new List(items, scrollOffset: 0);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(list, width: 20, height: 5).Content);
        Assert.Contains("item00", plain);
        Assert.Contains("item04", plain);
        Assert.DoesNotContain("item05", plain);
    }

    [Fact]
    public void List_ScrollOffset_Mid_ShowsCorrectWindow()
    {
        var items = Enumerable.Range(0, 20).Select(i => $"item{i:D2}").ToArray();
        var list  = new List(items, scrollOffset: 10);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(list, width: 20, height: 5).Content);
        Assert.Contains("item10", plain);
        Assert.Contains("item14", plain);
        Assert.DoesNotContain("item09", plain);
        Assert.DoesNotContain("item15", plain);
    }

    [Fact]
    public void List_ScrollOffset_NearEnd_ClampsCorrectly()
    {
        var items = Enumerable.Range(0, 10).Select(i => $"row{i}").ToArray();
        // scroll 8, viewport 5 → only rows 8,9 exist; rows 10-12 are blank
        var list  = new List(items, scrollOffset: 8);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(list, width: 20, height: 5).Content);
        Assert.Contains("row8", plain);
        Assert.Contains("row9", plain);
    }

    [Fact]
    public void List_ScrollOffset_SelectionHighlightFollowsOffset()
    {
        // Items 0-19, selected=12, scroll=10 → item12 is at visual row 2
        var items = Enumerable.Range(0, 20).Select(i => $"item{i:D2}").ToArray();
        var list  = new List(items, selectedIndex: 12, scrollOffset: 10);
        // Render must not throw and must show item12
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(list, width: 20, height: 5).Content);
        Assert.Contains("item12", plain);
    }

    [Fact]
    public void List_ScrollOffset_SelectionOutsideViewport_NotHighlighted()
    {
        // selected=0 but scroll=10; item0 is off-screen — no crash, item0 not shown
        var items = Enumerable.Range(0, 20).Select(i => $"item{i:D2}").ToArray();
        var list  = new List(items, selectedIndex: 0, scrollOffset: 10);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(list, width: 20, height: 5).Content);
        Assert.DoesNotContain("item00", plain);
        Assert.Contains("item10", plain);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Table — ComputeScrollOffset
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Table_ComputeScrollOffset_CursorInView_Unchanged()
    {
        Assert.Equal(1, Table.ComputeScrollOffset(3, 5, 1));
    }

    [Fact]
    public void Table_ComputeScrollOffset_CursorAboveView_ScrollsUp()
    {
        Assert.Equal(2, Table.ComputeScrollOffset(2, 5, 5));
    }

    [Fact]
    public void Table_ComputeScrollOffset_CursorBelowView_ScrollsDown()
    {
        Assert.Equal(6, Table.ComputeScrollOffset(10, 5, 0));
    }

    [Fact]
    public void Table_ComputeScrollOffset_ZeroViewport_Unchanged()
    {
        Assert.Equal(3, Table.ComputeScrollOffset(5, 0, 3));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Table — ScrollOffset rendering
    // ════════════════════════════════════════════════════════════════════════

    private static IReadOnlyList<IReadOnlyList<string>> MakeRows(int count) =>
        Enumerable.Range(0, count)
            .Select(i => (IReadOnlyList<string>)new[] { $"Row{i:D3}" })
            .ToArray();

    private static readonly TableColumn[] OneCol = [new TableColumn("Name", Width: 10)];

    [Fact]
    public void Table_ScrollOffset_Zero_ShowsFromFirstDataRow()
    {
        // height 6 = 1 header + 5 data rows
        var table = new Table(OneCol, MakeRows(20), scrollOffset: 0);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(table, width: 20, height: 6).Content);
        Assert.Contains("Row000", plain);
        Assert.Contains("Row004", plain);
        Assert.DoesNotContain("Row005", plain);
    }

    [Fact]
    public void Table_ScrollOffset_Mid_ShowsCorrectWindow()
    {
        var table = new Table(OneCol, MakeRows(20), scrollOffset: 10);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(table, width: 20, height: 6).Content);
        Assert.Contains("Row010", plain);
        Assert.Contains("Row014", plain);
        Assert.DoesNotContain("Row009", plain);
        Assert.DoesNotContain("Row015", plain);
    }

    [Fact]
    public void Table_ScrollOffset_HeaderAlwaysVisible()
    {
        var table = new Table(OneCol, MakeRows(20), scrollOffset: 15);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(table, width: 20, height: 6).Content);
        Assert.Contains("Name", plain); // header always present
    }

    [Fact]
    public void Table_ScrollOffset_SelectionHighlightFollowsOffset()
    {
        var table = new Table(OneCol, MakeRows(20), selectedIndex: 12, scrollOffset: 10);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(table, width: 20, height: 6).Content);
        Assert.Contains("Row012", plain);
    }

    [Fact]
    public void Table_ScrollOffset_SelectionOutsideViewport_NoThrow()
    {
        // selected=0 but scroll=15; Row000 is off screen
        var table = new Table(OneCol, MakeRows(20), selectedIndex: 0, scrollOffset: 15);
        var ex    = Record.Exception(() => ViewDescriptor.From(table, width: 20, height: 6));
        Assert.Null(ex);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(table, width: 20, height: 6).Content);
        Assert.DoesNotContain("Row000", plain);
    }

    [Fact]
    public void Table_ScrollOffset_NearEnd_RendersRemainingRows()
    {
        var table = new Table(OneCol, MakeRows(5), scrollOffset: 3);
        // Only Row003 and Row004 remain; 3 data-row slots will be blank
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(table, width: 20, height: 6).Content);
        Assert.Contains("Row003", plain);
        Assert.Contains("Row004", plain);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Large-dataset: confirm O(viewport) not O(total)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void List_1000Items_RendersOnlyViewportRows()
    {
        var items = Enumerable.Range(0, 1000).Select(i => $"item{i:D4}").ToArray();
        var list  = new List(items, selectedIndex: 500, scrollOffset: 498);
        // viewport 5 → items 498-502 visible; item0000 and item0999 must not appear
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(list, width: 20, height: 5).Content);
        Assert.Contains("item0498", plain);
        Assert.DoesNotContain("item0000", plain);
        Assert.DoesNotContain("item0999", plain);
    }

    [Fact]
    public void Table_1000Rows_RendersOnlyViewportRows()
    {
        var rows = Enumerable.Range(0, 1000)
            .Select(i => (IReadOnlyList<string>)new[] { $"R{i:D4}" })
            .ToArray();
        var table = new Table(OneCol, rows, selectedIndex: 500, scrollOffset: 498);
        // height 6 = 1 header + 5 data rows → rows 498-502
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(table, width: 20, height: 6).Content);
        Assert.Contains("R0498", plain);
        Assert.DoesNotContain("R0000", plain);
        Assert.DoesNotContain("R0999", plain);
    }
}
