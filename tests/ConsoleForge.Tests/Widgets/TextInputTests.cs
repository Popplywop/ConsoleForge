using ConsoleForge.Core;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="TextInput"/>.</summary>
public class TextInputTests
{
    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_CursorPosition_ClampedToValueLength()
    {
        var input = new TextInput("hi", cursorPosition: 99);
        Assert.Equal(2, input.CursorPosition);
    }

    [Fact]
    public void Constructor_NegativeCursor_ClampedToZero()
    {
        var input = new TextInput("hi", cursorPosition: -5);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void Constructor_EmptyValue_CursorIsZero()
    {
        var input = new TextInput("");
        Assert.Equal(0, input.CursorPosition);
    }

    // ── Printable character input ─────────────────────────────────────────────

    [Fact]
    public void OnKeyEvent_PrintableChar_AppendsAtCursorAndAdvances()
    {
        var input = new TextInput("ab", cursorPosition: 2);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.NoName, 'c'), msg => received = msg as TextInputChangedMsg);

        Assert.NotNull(received);
        Assert.Equal("abc", received!.NewValue);
        Assert.Equal(3, received.NewCursorPosition);
    }

    [Fact]
    public void OnKeyEvent_PrintableChar_InsertsAtMiddle()
    {
        var input = new TextInput("ac", cursorPosition: 1);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.NoName, 'b'), msg => received = msg as TextInputChangedMsg);

        Assert.NotNull(received);
        Assert.Equal("abc", received!.NewValue);
        Assert.Equal(2, received.NewCursorPosition);
    }

    [Fact]
    public void OnKeyEvent_ControlChar_Ignored()
    {
        var input = new TextInput("hello", cursorPosition: 5);
        TextInputChangedMsg? received = null;
        // '\x01' is a control char (Ctrl+A)
        input.OnKeyEvent(new KeyMsg(ConsoleKey.A, '\x01', Ctrl: true), msg => received = msg as TextInputChangedMsg);
        Assert.Null(received);
    }

    // ── Backspace ─────────────────────────────────────────────────────────────

    [Fact]
    public void OnKeyEvent_Backspace_DeletesCharBeforeCursor()
    {
        var input = new TextInput("hello", cursorPosition: 5);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.Backspace, null), msg => received = msg as TextInputChangedMsg);

        Assert.NotNull(received);
        Assert.Equal("hell", received!.NewValue);
        Assert.Equal(4, received.NewCursorPosition);
    }

    [Fact]
    public void OnKeyEvent_Backspace_AtStart_DoesNothing()
    {
        var input = new TextInput("hello", cursorPosition: 0);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.Backspace, null), msg => received = msg as TextInputChangedMsg);
        Assert.Null(received);
    }

    [Fact]
    public void OnKeyEvent_Backspace_EmptyValue_DoesNothing()
    {
        var input = new TextInput("", cursorPosition: 0);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.Backspace, null), msg => received = msg as TextInputChangedMsg);
        Assert.Null(received);
    }

    [Fact]
    public void OnKeyEvent_Backspace_InMiddle_DeletesCorrectChar()
    {
        var input = new TextInput("abc", cursorPosition: 2);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.Backspace, null), msg => received = msg as TextInputChangedMsg);

        Assert.NotNull(received);
        Assert.Equal("ac", received!.NewValue);
        Assert.Equal(1, received.NewCursorPosition);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void OnKeyEvent_Delete_DeletesCharAtCursor()
    {
        var input = new TextInput("hello", cursorPosition: 0);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.Delete, null), msg => received = msg as TextInputChangedMsg);

        Assert.NotNull(received);
        Assert.Equal("ello", received!.NewValue);
        Assert.Equal(0, received.NewCursorPosition);
    }

    [Fact]
    public void OnKeyEvent_Delete_AtEnd_DoesNothing()
    {
        var input = new TextInput("hello", cursorPosition: 5);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.Delete, null), msg => received = msg as TextInputChangedMsg);
        Assert.Null(received);
    }

    // ── Cursor movement ───────────────────────────────────────────────────────

    [Fact]
    public void OnKeyEvent_LeftArrow_DecrementsCursor()
    {
        var input = new TextInput("hello", cursorPosition: 3);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.LeftArrow, null), msg => received = msg as TextInputChangedMsg);

        Assert.NotNull(received);
        Assert.Equal("hello", received!.NewValue);
        Assert.Equal(2, received.NewCursorPosition);
    }

    [Fact]
    public void OnKeyEvent_LeftArrow_AtStart_StaysAtZero()
    {
        var input = new TextInput("hello", cursorPosition: 0);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.LeftArrow, null), msg => received = msg as TextInputChangedMsg);

        Assert.NotNull(received);
        Assert.Equal(0, received!.NewCursorPosition);
    }

    [Fact]
    public void OnKeyEvent_RightArrow_IncrementsCursor()
    {
        var input = new TextInput("hello", cursorPosition: 2);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.RightArrow, null), msg => received = msg as TextInputChangedMsg);

        Assert.NotNull(received);
        Assert.Equal(3, received!.NewCursorPosition);
    }

    [Fact]
    public void OnKeyEvent_RightArrow_AtEnd_StaysAtEnd()
    {
        var input = new TextInput("hello", cursorPosition: 5);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.RightArrow, null), msg => received = msg as TextInputChangedMsg);

        Assert.NotNull(received);
        Assert.Equal(5, received!.NewCursorPosition);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_ShowsPlaceholder_WhenValueEmpty()
    {
        var input = new TextInput("", placeholder: "Type here");
        var descriptor = ViewDescriptor.From(input, width: 40, height: 1);
        Assert.Contains("Type here", descriptor.Content);
    }

    [Fact]
    public void Render_ShowsValue_WhenNotEmpty()
    {
        var input = new TextInput("hello world");
        var descriptor = ViewDescriptor.From(input, width: 40, height: 1);
        Assert.Contains("hello world", descriptor.Content);
    }

    [Fact]
    public void Render_Truncates_WhenValueExceedsWidth()
    {
        var input = new TextInput("abcdefghij"); // 10 chars
        var descriptor = ViewDescriptor.From(input, width: 5, height: 1);
        // Content must not be longer than width visible
        Assert.DoesNotContain("abcdefghij", descriptor.Content);
    }

    [Fact]
    public void Render_Focused_IncludesCursorHighlight()
    {
        var input = new TextInput("hello", cursorPosition: 0);
        input.HasFocus = true;
        // Just verify render doesn't throw and produces output
        var descriptor = ViewDescriptor.From(input, width: 20, height: 1);
        Assert.NotEmpty(descriptor.Content);
    }

    // ── Source identity in message ────────────────────────────────────────────

    [Fact]
    public void OnKeyEvent_Message_SourceIsThisInput()
    {
        var input = new TextInput("hi", cursorPosition: 2);
        TextInputChangedMsg? received = null;
        input.OnKeyEvent(new KeyMsg(ConsoleKey.NoName, 'x'), msg => received = msg as TextInputChangedMsg);

        Assert.NotNull(received);
        Assert.Same(input, received!.Source);
    }
}
