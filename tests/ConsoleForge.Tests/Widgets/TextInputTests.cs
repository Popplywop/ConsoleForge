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
    public void Update_PrintableChar_AppendsAtCursorAndAdvances()
    {
        var input = new TextInput("ab", cursorPosition: 2);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.NoName, 'c'));
        var result = (TextInput)next;

        Assert.Equal("abc", result.Value);
        Assert.Equal(3, result.CursorPosition);
    }

    [Fact]
    public void Update_PrintableChar_InsertsAtMiddle()
    {
        var input = new TextInput("ac", cursorPosition: 1);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.NoName, 'b'));
        var result = (TextInput)next;

        Assert.Equal("abc", result.Value);
        Assert.Equal(2, result.CursorPosition);
    }

    [Fact]
    public void Update_ControlChar_Ignored()
    {
        var input = new TextInput("hello", cursorPosition: 5);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.A, '\x01', Ctrl: true));
        Assert.Same(input, next);
    }

    // ── Backspace ─────────────────────────────────────────────────────────────

    [Fact]
    public void Update_Backspace_DeletesCharBeforeCursor()
    {
        var input = new TextInput("hello", cursorPosition: 5);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.Backspace, null));
        var result = (TextInput)next;

        Assert.Equal("hell", result.Value);
        Assert.Equal(4, result.CursorPosition);
    }

    [Fact]
    public void Update_Backspace_AtStart_DoesNothing()
    {
        var input = new TextInput("hello", cursorPosition: 0);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.Backspace, null));
        Assert.Same(input, next);
    }

    [Fact]
    public void Update_Backspace_EmptyValue_DoesNothing()
    {
        var input = new TextInput("", cursorPosition: 0);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.Backspace, null));
        Assert.Same(input, next);
    }

    [Fact]
    public void Update_Backspace_InMiddle_DeletesCorrectChar()
    {
        var input = new TextInput("abc", cursorPosition: 2);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.Backspace, null));
        var result = (TextInput)next;

        Assert.Equal("ac", result.Value);
        Assert.Equal(1, result.CursorPosition);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_Delete_DeletesCharAtCursor()
    {
        var input = new TextInput("hello", cursorPosition: 0);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.Delete, null));
        var result = (TextInput)next;

        Assert.Equal("ello", result.Value);
        Assert.Equal(0, result.CursorPosition);
    }

    [Fact]
    public void Update_Delete_AtEnd_DoesNothing()
    {
        var input = new TextInput("hello", cursorPosition: 5);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.Delete, null));
        Assert.Same(input, next);
    }

    // ── Cursor movement ───────────────────────────────────────────────────────

    [Fact]
    public void Update_LeftArrow_DecrementsCursor()
    {
        var input = new TextInput("hello", cursorPosition: 3);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.LeftArrow, null));
        var result = (TextInput)next;

        Assert.Equal("hello", result.Value);
        Assert.Equal(2, result.CursorPosition);
    }

    [Fact]
    public void Update_LeftArrow_AtStart_StaysAtZero()
    {
        var input = new TextInput("hello", cursorPosition: 0);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.LeftArrow, null));
        var result = (TextInput)next;

        Assert.Equal(0, result.CursorPosition);
    }

    [Fact]
    public void Update_RightArrow_IncrementsCursor()
    {
        var input = new TextInput("hello", cursorPosition: 2);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.RightArrow, null));
        var result = (TextInput)next;

        Assert.Equal(3, result.CursorPosition);
    }

    [Fact]
    public void Update_RightArrow_AtEnd_StaysAtEnd()
    {
        var input = new TextInput("hello", cursorPosition: 5);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.RightArrow, null));
        var result = (TextInput)next;

        Assert.Equal(5, result.CursorPosition);
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

    // ── Return identity ───────────────────────────────────────────────────────

    [Fact]
    public void Update_ReturnsNewInstance_WhenValueChanges()
    {
        var input = new TextInput("hi", cursorPosition: 2);
        var (next, _) = input.Update(new KeyMsg(ConsoleKey.NoName, 'x'));
        var result = (TextInput)next;

        Assert.NotSame(input, result);
        Assert.Equal("hix", result.Value);
    }
}
