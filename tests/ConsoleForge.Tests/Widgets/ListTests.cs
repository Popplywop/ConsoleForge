using ConsoleForge.Core;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="List"/>.</summary>
public class ListTests
{
    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SelectedIndex_ClampedToLastItem()
    {
        var list = new List(["a", "b", "c"], selectedIndex: 99);
        Assert.Equal(2, list.SelectedIndex);
    }

    [Fact]
    public void Constructor_NegativeSelectedIndex_ClampedToZero()
    {
        var list = new List(["a", "b"], selectedIndex: -1);
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void Constructor_EmptyItems_SelectedIndexIsZero()
    {
        var list = new List([]);
        Assert.Equal(0, list.SelectedIndex);
    }

    // ── DownArrow ─────────────────────────────────────────────────────────────

    [Fact]
    public void OnKeyEvent_DownArrow_IncrementsSelection()
    {
        var list = new List(["a", "b", "c"], selectedIndex: 0);
        ListSelectionChangedMsg? received = null;
        list.OnKeyEvent(new KeyMsg(ConsoleKey.DownArrow, null), msg => received = msg as ListSelectionChangedMsg);

        Assert.NotNull(received);
        Assert.Equal(1, received!.NewIndex);
    }

    [Fact]
    public void OnKeyEvent_DownArrow_AtLastItem_DoesNotExceedBound()
    {
        var list = new List(["a", "b", "c"], selectedIndex: 2);
        ListSelectionChangedMsg? received = null;
        list.OnKeyEvent(new KeyMsg(ConsoleKey.DownArrow, null), msg => received = msg as ListSelectionChangedMsg);

        Assert.NotNull(received);
        Assert.Equal(2, received!.NewIndex); // stays at 2
    }

    // ── UpArrow ───────────────────────────────────────────────────────────────

    [Fact]
    public void OnKeyEvent_UpArrow_DecrementsSelection()
    {
        var list = new List(["a", "b", "c"], selectedIndex: 2);
        ListSelectionChangedMsg? received = null;
        list.OnKeyEvent(new KeyMsg(ConsoleKey.UpArrow, null), msg => received = msg as ListSelectionChangedMsg);

        Assert.NotNull(received);
        Assert.Equal(1, received!.NewIndex);
    }

    [Fact]
    public void OnKeyEvent_UpArrow_AtFirstItem_StaysAtZero()
    {
        var list = new List(["a", "b", "c"], selectedIndex: 0);
        ListSelectionChangedMsg? received = null;
        list.OnKeyEvent(new KeyMsg(ConsoleKey.UpArrow, null), msg => received = msg as ListSelectionChangedMsg);

        Assert.NotNull(received);
        Assert.Equal(0, received!.NewIndex);
    }

    // ── Enter ─────────────────────────────────────────────────────────────────

    [Fact]
    public void OnKeyEvent_Enter_DispatchesListItemSelectedMsg()
    {
        var list = new List(["alpha", "beta", "gamma"], selectedIndex: 1);
        ListItemSelectedMsg? received = null;
        list.OnKeyEvent(new KeyMsg(ConsoleKey.Enter, null), msg => received = msg as ListItemSelectedMsg);

        Assert.NotNull(received);
        Assert.Equal(1, received!.Index);
        Assert.Equal("beta", received.Item);
    }

    [Fact]
    public void OnKeyEvent_Enter_EmptyList_DoesNothing()
    {
        var list = new List([]);
        ListItemSelectedMsg? received = null;
        list.OnKeyEvent(new KeyMsg(ConsoleKey.Enter, null), msg => received = msg as ListItemSelectedMsg);
        Assert.Null(received);
    }

    // ── Unhandled keys ────────────────────────────────────────────────────────

    [Fact]
    public void OnKeyEvent_OtherKey_DispatchesNothing()
    {
        var list = new List(["a", "b"], selectedIndex: 0);
        IMsg? received = null;
        list.OnKeyEvent(new KeyMsg(ConsoleKey.Escape, null), msg => received = msg);
        Assert.Null(received);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_ShowsAllItems()
    {
        var list = new List(["apple", "banana", "cherry"]);
        var descriptor = ViewDescriptor.From(list, width: 30, height: 10);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.Contains("apple",  plain);
        Assert.Contains("banana", plain);
        Assert.Contains("cherry", plain);
    }

    [Fact]
    public void Render_EmptyList_DoesNotThrow()
    {
        var list = new List([]);
        var ex = Record.Exception(() => ViewDescriptor.From(list, width: 30, height: 10));
        Assert.Null(ex);
    }

    [Fact]
    public void Render_ItemsClippedToHeight()
    {
        // 10 items in a 3-row region → only first 3 should appear
        var items = Enumerable.Range(1, 10).Select(i => $"item{i}").ToArray();
        var list = new List(items);
        var descriptor = ViewDescriptor.From(list, width: 20, height: 3);

        // item4+ must not appear
        Assert.DoesNotContain("item4",  descriptor.Content);
        Assert.DoesNotContain("item10", descriptor.Content);
    }

    [Fact]
    public void Render_SelectedItem_AppearsInOutput()
    {
        var list = new List(["first", "second", "third"], selectedIndex: 1);
        var descriptor = ViewDescriptor.From(list, width: 20, height: 10);
        Assert.Contains("second", TestHelpers.StripAnsi(descriptor.Content));
    }

    // ── Source identity in message ────────────────────────────────────────────

    [Fact]
    public void OnKeyEvent_SelectionMsg_SourceIsThisList()
    {
        var list = new List(["a", "b"], selectedIndex: 0);
        ListSelectionChangedMsg? received = null;
        list.OnKeyEvent(new KeyMsg(ConsoleKey.DownArrow, null), msg => received = msg as ListSelectionChangedMsg);

        Assert.NotNull(received);
        Assert.Same(list, received!.Source);
    }
}
