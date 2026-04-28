using ConsoleForge.Core;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="TextArea"/>.</summary>
public class TextAreaTests
{
    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullLines_DefaultsToSingleEmptyLine()
    {
        var ta = new TextArea();
        Assert.Single(ta.Lines);
        Assert.Equal("", ta.Lines[0]);
    }

    [Fact]
    public void Constructor_CursorRow_Clamped()
    {
        var ta = new TextArea(["a", "b"], cursorRow: 99);
        Assert.Equal(1, ta.CursorRow);
    }

    [Fact]
    public void Constructor_CursorCol_Clamped()
    {
        var ta = new TextArea(["hello"], cursorCol: 99);
        Assert.Equal(5, ta.CursorCol);
    }

    // ── Printable character input ─────────────────────────────────────────────

    [Fact]
    public void Update_PrintableChar_InsertsAtCursor()
    {
        var ta = new TextArea(["hello"], cursorRow: 0, cursorCol: 5);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.NoName, '!'));
        var result = (TextArea)next;

        Assert.Equal("hello!", result.Lines[0]);
        Assert.Equal(6, result.CursorCol);
    }

    [Fact]
    public void Update_PrintableChar_InsertsInMiddle()
    {
        var ta = new TextArea(["ac"], cursorRow: 0, cursorCol: 1);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.NoName, 'b'));
        var result = (TextArea)next;

        Assert.Equal("abc", result.Lines[0]);
        Assert.Equal(2, result.CursorCol);
    }

    [Fact]
    public void Update_ControlChar_NoDispatch()
    {
        var ta = new TextArea(["text"], cursorRow: 0, cursorCol: 4);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.A, '\x01', Ctrl: true));
        Assert.Same(ta, next);
    }

    // ── Backspace ─────────────────────────────────────────────────────────────

    [Fact]
    public void Update_Backspace_DeletesCharBeforeCursor()
    {
        var ta = new TextArea(["hello"], cursorCol: 5);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.Backspace, null));
        var result = (TextArea)next;

        Assert.Equal("hell", result.Lines[0]);
        Assert.Equal(4, result.CursorCol);
    }

    [Fact]
    public void Update_Backspace_AtLineStart_JoinsWithPrevLine()
    {
        var ta = new TextArea(["first", "second"], cursorRow: 1, cursorCol: 0);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.Backspace, null));
        var result = (TextArea)next;

        Assert.Single(result.Lines);
        Assert.Equal("firstsecond", result.Lines[0]);
        Assert.Equal(0, result.CursorRow);
        Assert.Equal(5, result.CursorCol); // cursor at join point
    }

    [Fact]
    public void Update_Backspace_AtFirstLineStart_NoChange()
    {
        var ta = new TextArea(["hello"], cursorRow: 0, cursorCol: 0);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.Backspace, null));
        var result = (TextArea)next;

        Assert.Equal("hello", result.Lines[0]);
        Assert.Equal(0, result.CursorRow);
        Assert.Equal(0, result.CursorCol);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_Delete_DeletesCharAtCursor()
    {
        var ta = new TextArea(["hello"], cursorCol: 0);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.Delete, null));
        var result = (TextArea)next;

        Assert.Equal("ello", result.Lines[0]);
        Assert.Equal(0, result.CursorCol);
    }

    [Fact]
    public void Update_Delete_AtLineEnd_JoinsNextLine()
    {
        var ta = new TextArea(["first", "second"], cursorRow: 0, cursorCol: 5);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.Delete, null));
        var result = (TextArea)next;

        Assert.Single(result.Lines);
        Assert.Equal("firstsecond", result.Lines[0]);
        Assert.Equal(0, result.CursorRow);
        Assert.Equal(5, result.CursorCol);
    }

    [Fact]
    public void Update_Delete_AtLastLineEnd_NoChange()
    {
        var ta = new TextArea(["only"], cursorRow: 0, cursorCol: 4);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.Delete, null));
        var result = (TextArea)next;

        Assert.Single(result.Lines);
        Assert.Equal("only", result.Lines[0]);
    }

    // ── Enter ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_Enter_SplitsLineAtCursor()
    {
        var ta = new TextArea(["hello world"], cursorRow: 0, cursorCol: 5);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.Enter, null));
        var result = (TextArea)next;

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal("hello", result.Lines[0]);
        Assert.Equal(" world", result.Lines[1]);
        Assert.Equal(1, result.CursorRow);
        Assert.Equal(0, result.CursorCol);
    }

    [Fact]
    public void Update_Enter_AtLineEnd_AddsEmptyLine()
    {
        var ta = new TextArea(["hello"], cursorRow: 0, cursorCol: 5);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.Enter, null));
        var result = (TextArea)next;

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal("hello", result.Lines[0]);
        Assert.Equal("", result.Lines[1]);
    }

    [Fact]
    public void Update_Enter_MaxLinesReached_NoOp()
    {
        var ta = new TextArea(["a", "b"], cursorRow: 0, cursorCol: 0, maxLines: 2);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.Enter, null));
        var result = (TextArea)next;

        Assert.Equal(2, result.Lines.Count); // count unchanged
    }

    // ── Cursor navigation ─────────────────────────────────────────────────────

    [Fact]
    public void Update_Left_DecrementsCursorCol()
    {
        var ta = new TextArea(["hello"], cursorRow: 0, cursorCol: 3);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.LeftArrow, null));
        var result = (TextArea)next;

        Assert.Equal(2, result.CursorCol);
    }

    [Fact]
    public void Update_Left_AtLineStart_MovesToPrevLineEnd()
    {
        var ta = new TextArea(["abc", "def"], cursorRow: 1, cursorCol: 0);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.LeftArrow, null));
        var result = (TextArea)next;

        Assert.Equal(0, result.CursorRow);
        Assert.Equal(3, result.CursorCol); // end of "abc"
    }

    [Fact]
    public void Update_Right_IncrementsCursorCol()
    {
        var ta = new TextArea(["hello"], cursorRow: 0, cursorCol: 2);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.RightArrow, null));
        var result = (TextArea)next;

        Assert.Equal(3, result.CursorCol);
    }

    [Fact]
    public void Update_Right_AtLineEnd_MovesToNextLineStart()
    {
        var ta = new TextArea(["abc", "def"], cursorRow: 0, cursorCol: 3);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.RightArrow, null));
        var result = (TextArea)next;

        Assert.Equal(1, result.CursorRow);
        Assert.Equal(0, result.CursorCol);
    }

    [Fact]
    public void Update_Up_MovesCursorRowUp()
    {
        var ta = new TextArea(["abc", "def"], cursorRow: 1, cursorCol: 2);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.UpArrow, null));
        var result = (TextArea)next;

        Assert.Equal(0, result.CursorRow);
        Assert.Equal(2, result.CursorCol);
    }

    [Fact]
    public void Update_Up_AtFirstRow_StaysAtRow0()
    {
        var ta = new TextArea(["hello"], cursorRow: 0, cursorCol: 3);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.UpArrow, null));
        var result = (TextArea)next;

        Assert.Equal(0, result.CursorRow);
    }

    [Fact]
    public void Update_Down_MovesCursorRowDown()
    {
        var ta = new TextArea(["abc", "def"], cursorRow: 0, cursorCol: 1);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.DownArrow, null));
        var result = (TextArea)next;

        Assert.Equal(1, result.CursorRow);
    }

    [Fact]
    public void Update_Down_AtLastRow_StaysAtLastRow()
    {
        var ta = new TextArea(["abc", "def"], cursorRow: 1, cursorCol: 0);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.DownArrow, null));
        var result = (TextArea)next;

        Assert.Equal(1, result.CursorRow);
    }

    [Fact]
    public void Update_Up_ClampsColToShortPrevLine()
    {
        // prev line "ab" (len 2), cursor on longer line at col 5
        var ta = new TextArea(["ab", "hello"], cursorRow: 1, cursorCol: 5);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.UpArrow, null));
        var result = (TextArea)next;

        Assert.Equal(0, result.CursorRow);
        Assert.Equal(2, result.CursorCol); // clamped to "ab".Length
    }

    [Fact]
    public void Update_Home_MovesCursorToLineStart()
    {
        var ta = new TextArea(["hello"], cursorCol: 4);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.Home, null));
        var result = (TextArea)next;

        Assert.Equal(0, result.CursorCol);
    }

    [Fact]
    public void Update_End_MovesCursorToLineEnd()
    {
        var ta = new TextArea(["hello"], cursorCol: 0);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.End, null));
        var result = (TextArea)next;

        Assert.Equal(5, result.CursorCol);
    }

    // ── ComputeScrollRow ──────────────────────────────────────────────────────

    [Fact]
    public void ComputeScrollRow_CursorAboveViewport_ScrollsUp()
    {
        // cursor at row 2, viewport height 5, current scroll 5 → should scroll to 2
        var result = TextArea.ComputeScrollRow(cursorRow: 2, viewportHeight: 5, currentScrollRow: 5);
        Assert.Equal(2, result);
    }

    [Fact]
    public void ComputeScrollRow_CursorBelowViewport_ScrollsDown()
    {
        // cursor at row 10, viewport height 5, current scroll 0 → should scroll to 6
        var result = TextArea.ComputeScrollRow(cursorRow: 10, viewportHeight: 5, currentScrollRow: 0);
        Assert.Equal(6, result);
    }

    [Fact]
    public void ComputeScrollRow_CursorInViewport_Unchanged()
    {
        // cursor at row 3, viewport height 5, current scroll 1 → rows 1-5 visible → no change
        var result = TextArea.ComputeScrollRow(cursorRow: 3, viewportHeight: 5, currentScrollRow: 1);
        Assert.Equal(1, result);
    }

    [Fact]
    public void ComputeScrollRow_ZeroViewport_Unchanged()
    {
        var result = TextArea.ComputeScrollRow(cursorRow: 5, viewportHeight: 0, currentScrollRow: 3);
        Assert.Equal(3, result);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_ShowsAllVisibleLines()
    {
        var ta = new TextArea(["line one", "line two", "line three"]);
        var descriptor = ViewDescriptor.From(ta, width: 20, height: 5);
        var plain = TestHelpers.StripAnsi(descriptor.Content);

        Assert.Contains("line one",   plain);
        Assert.Contains("line two",   plain);
        Assert.Contains("line three", plain);
    }

    [Fact]
    public void Render_ScrollRow_SkipsEarlierLines()
    {
        var ta = new TextArea(["alpha", "beta", "gamma"], scrollRow: 1);
        var descriptor = ViewDescriptor.From(ta, width: 20, height: 3);
        var plain = TestHelpers.StripAnsi(descriptor.Content);

        Assert.DoesNotContain("alpha", plain);
        Assert.Contains("beta",  plain);
        Assert.Contains("gamma", plain);
    }

    [Fact]
    public void Render_LongLine_Truncated()
    {
        var longLine = new string('x', 100);
        var ta = new TextArea([longLine]);
        var descriptor = ViewDescriptor.From(ta, width: 20, height: 1);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.True(plain.Trim().Length <= 20);
    }

    [Fact]
    public void Render_Focused_DoesNotThrow()
    {
        var ta = new TextArea(["hello"], cursorRow: 0, cursorCol: 2);
        ta.HasFocus = true;
        var ex = Record.Exception(() => ViewDescriptor.From(ta, width: 20, height: 5));
        Assert.Null(ex);
    }

    [Fact]
    public void Render_EmptyDocument_DoesNotThrow()
    {
        var ta = new TextArea();
        var ex = Record.Exception(() => ViewDescriptor.From(ta, width: 20, height: 5));
        Assert.Null(ex);
    }

    // ── Return identity ───────────────────────────────────────────────────────

    [Fact]
    public void Update_ReturnsNewInstance_WhenValueChanges()
    {
        var ta = new TextArea(["hi"], cursorCol: 2);
        var (next, _) = ta.Update(new KeyMsg(ConsoleKey.NoName, '!'));
        var result = (TextArea)next;

        Assert.NotSame(ta, result);
        Assert.Equal("hi!", result.Lines[0]);
    }
}
