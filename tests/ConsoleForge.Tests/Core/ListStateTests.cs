using ConsoleForge.Core;

namespace ConsoleForge.Tests.Core;

public class ListStateTests
{
    // ── Invariants ────────────────────────────────────────────────────────────

    [Fact]
    public void Selection_ClampsIntoRange()
    {
        Assert.Equal(4, new ListState(5, selectedIndex: 99).SelectedIndex);
        Assert.Equal(0, new ListState(5, selectedIndex: -3).SelectedIndex);
    }

    [Fact]
    public void EmptyList_SelectsZeroAndReportsEmpty()
    {
        var s = new ListState(0, selectedIndex: 7, scrollOffset: 7);
        Assert.True(s.IsEmpty);
        Assert.Equal(0, s.SelectedIndex);
        Assert.Equal(0, s.ScrollOffset);
    }

    [Fact]
    public void ShrinkingTheCount_PullsSelectionAndScrollBack()
    {
        var s = new ListState(100, selectedIndex: 90, viewportHeight: 10);
        var smaller = s.WithCount(5);

        Assert.Equal(4, smaller.SelectedIndex);
        Assert.Equal(0, smaller.ScrollOffset); // 5 items, 10 rows — nothing to scroll
    }

    [Fact]
    public void With_IsOrderIndependent()
    {
        // Whichever member is named re-reconciles the whole tuple, so neither ordering
        // can leave a selection outside the list.
        var s = new ListState(100, selectedIndex: 50);

        var a = s with { SelectedIndex = 40, Count = 3 };
        var b = s with { Count = 3, SelectedIndex = 40 };

        Assert.Equal(2, a.SelectedIndex);
        Assert.Equal(2, b.SelectedIndex);
    }

    [Fact]
    public void ScrollNeverStrandsBlankRowsBelowTheLastItem()
    {
        // 20 items in a 10-row window: the furthest useful offset is 10, because past it
        // the window is only blank rows under the final item.
        var s = new ListState(20, selectedIndex: 19, scrollOffset: 99, viewportHeight: 10);
        Assert.Equal(10, s.ScrollOffset);
    }

    [Fact]
    public void AScrollThatWouldHideTheSelection_IsOverridden()
    {
        // Scroll is not an independent knob. Asking for an offset that puts the selection
        // off-screen loses to the selection, in either direction.
        Assert.Equal(0, new ListState(100, selectedIndex: 0, scrollOffset: 30, viewportHeight: 10).ScrollOffset);
        Assert.Equal(21, new ListState(100, selectedIndex: 30, scrollOffset: 0, viewportHeight: 10).ScrollOffset);
    }

    // ── Scroll follows selection ──────────────────────────────────────────────

    [Fact]
    public void MovingBelowTheWindow_ScrollsDownByTheMinimum()
    {
        var s = new ListState(100, selectedIndex: 0, viewportHeight: 10);
        var moved = s.MoveTo(12);

        Assert.Equal(12, moved.SelectedIndex);
        Assert.Equal(3, moved.ScrollOffset); // 12 - 10 + 1
    }

    [Fact]
    public void MovingAboveTheWindow_ScrollsUpToTheSelection()
    {
        var s = new ListState(100, selectedIndex: 50, viewportHeight: 10);
        var moved = s.MoveTo(20);

        Assert.Equal(20, moved.ScrollOffset);
    }

    [Fact]
    public void SettingTheViewport_BringsTheSelectionIntoView()
    {
        // Selection set while the viewport was unknown, then a layout reports one.
        var s = new ListState(100, selectedIndex: 80);
        Assert.Equal(0, s.ScrollOffset); // no viewport — scroll left alone

        var sized = s.WithViewport(10);
        Assert.Equal(71, sized.ScrollOffset);
    }

    [Fact]
    public void WithoutAViewport_ScrollIsLeftAlone()
    {
        var s = new ListState(100, selectedIndex: 0, scrollOffset: 40);
        Assert.Equal(40, s.ScrollOffset);
    }

    // ── Moves ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MovesClampRatherThanWrap()
    {
        var s = new ListState(3);
        Assert.Equal(0, s.MoveUp().SelectedIndex);
        Assert.Equal(2, s.MoveTo(2).MoveDown().SelectedIndex);
    }

    [Fact]
    public void AMoveThatChangesNothing_ReturnsTheSameInstance()
    {
        var s = new ListState(3);
        Assert.Same(s, s.MoveUp());
        Assert.Same(s, s.MoveTo(0));
        Assert.Same(s, s.WithCount(3));
        Assert.Same(s, s.WithViewport(0));
    }

    // ── Keys ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HandleKey_ArrowsMoveOneRow()
    {
        var s = new ListState(10, selectedIndex: 5);
        Assert.Equal(4, s.HandleKey(new KeyMsg(ConsoleKey.UpArrow, null)).SelectedIndex);
        Assert.Equal(6, s.HandleKey(new KeyMsg(ConsoleKey.DownArrow, null)).SelectedIndex);
    }

    [Fact]
    public void HandleKey_HomeAndEnd()
    {
        var s = new ListState(10, selectedIndex: 5);
        Assert.Equal(0, s.HandleKey(new KeyMsg(ConsoleKey.Home, null)).SelectedIndex);
        Assert.Equal(9, s.HandleKey(new KeyMsg(ConsoleKey.End, null)).SelectedIndex);
    }

    [Fact]
    public void HandleKey_PagingMovesOneViewport()
    {
        var s = new ListState(100, selectedIndex: 0, viewportHeight: 10);
        var down = s.HandleKey(new KeyMsg(ConsoleKey.PageDown, null));
        Assert.Equal(10, down.SelectedIndex);
        Assert.Equal(0, down.HandleKey(new KeyMsg(ConsoleKey.PageUp, null)).SelectedIndex);
    }

    [Fact]
    public void HandleKey_PagingDoesNothingWithoutAViewport()
    {
        // A page has no size here, and moving a single row would misreport the key.
        var s = new ListState(100, selectedIndex: 50);
        Assert.Same(s, s.HandleKey(new KeyMsg(ConsoleKey.PageDown, null)));
        Assert.Same(s, s.HandleKey(new KeyMsg(ConsoleKey.PageUp, null)));
    }

    [Fact]
    public void HandleKey_IgnoresKeysItDoesNotOwn()
    {
        var s = new ListState(10, selectedIndex: 5);
        Assert.Same(s, s.HandleKey(new KeyMsg(ConsoleKey.Enter, '\r')));
        Assert.Same(s, s.HandleKey(new KeyMsg(ConsoleKey.A, 'a')));
        Assert.Same(s, s.HandleKey(null!));
    }

    // ── VisibleRange ──────────────────────────────────────────────────────────

    [Fact]
    public void VisibleRange_IsTheSliceAViewShouldDraw()
    {
        var s = new ListState(100, selectedIndex: 30, scrollOffset: 30, viewportHeight: 10);
        Assert.Equal((30, 10), s.VisibleRange);
    }

    [Fact]
    public void VisibleRange_StopsAtTheLastItem()
    {
        var s = new ListState(12, selectedIndex: 11, viewportHeight: 10);
        var (offset, length) = s.VisibleRange;
        Assert.Equal(2, offset);
        Assert.Equal(10, length);
        Assert.Equal(12, offset + length); // never runs past the end
    }

    [Fact]
    public void VisibleRange_WithoutAViewport_IsTheRemainder()
    {
        var s = new ListState(10, selectedIndex: 0, scrollOffset: 4);
        Assert.Equal((4, 6), s.VisibleRange);
    }
}
