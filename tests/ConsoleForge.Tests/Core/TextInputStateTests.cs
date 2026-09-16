using ConsoleForge.Core;

namespace ConsoleForge.Tests.Core;

/// <summary>
/// <see cref="TextInputState"/> is the editing logic every input shares, so these cover
/// the rules themselves rather than any one widget's use of them.
/// </summary>
public class TextInputStateTests
{
    private static KeyMsg Key(ConsoleKey key, char? ch = null, bool ctrl = false, bool alt = false)
        => new(key, ch, Shift: false, Alt: alt, Ctrl: ctrl);

    private static KeyMsg Char(char c) => new(ConsoleKey.NoName, c);

    private static TextInputState Type(TextInputState state, string text)
    {
        foreach (var c in text) state = state.HandleKey(Char(c));
        return state;
    }

    // ── Construction and normalisation ────────────────────────────────────────

    [Fact]
    public void DefaultState_IsEmpty_WithCursorAtZero()
    {
        var s = new TextInputState();
        Assert.Equal("", s.Value);
        Assert.Equal(0, s.Cursor);
        Assert.True(s.IsEmpty);
    }

    [Fact]
    public void Cursor_DefaultsToEndOfText()
        => Assert.Equal(5, new TextInputState("hello").Cursor);

    [Theory]
    [InlineData(-4, 0)]
    [InlineData(99, 5)]
    public void Cursor_IsClampedIntoRange(int given, int expected)
        => Assert.Equal(expected, new TextInputState("hello", given).Cursor);

    [Fact]
    public void Cursor_SnapsOutOfTheMiddleOfASurrogatePair()
    {
        // "🎞" is two UTF-16 code units; index 1 is inside it.
        var s = new TextInputState("🎞x", 1);
        Assert.Equal(0, s.Cursor);
    }

    [Fact]
    public void WithExpression_RenormalisesTheCursor()
    {
        var s = new TextInputState("hello world") with { Value = "hi" };
        Assert.Equal("hi", s.Value);
        Assert.Equal(2, s.Cursor); // was 11, clamped to the new length
    }

    [Fact]
    public void NullValue_IsTreatedAsEmpty()
        => Assert.Equal("", new TextInputState(null!).Value);

    // ── Insertion ─────────────────────────────────────────────────────────────

    [Fact]
    public void TypingAppends_AndAdvancesTheCursor()
    {
        var s = Type(new TextInputState(), "abc");
        Assert.Equal("abc", s.Value);
        Assert.Equal(3, s.Cursor);
    }

    [Fact]
    public void TypingInsertsAtTheCursor_NotAtTheEnd()
    {
        var s = Type(new TextInputState("ac", 1), "b");
        Assert.Equal("abc", s.Value);
        Assert.Equal(2, s.Cursor);
    }

    [Fact]
    public void ControlCharacters_AreNotInserted()
    {
        var s = new TextInputState("ab").HandleKey(Char('\t'));
        Assert.Equal("ab", s.Value);
    }

    [Fact]
    public void AltChordedKeys_AreNotInserted()
    {
        // Alt+B is a shortcut in most shells; it must not type a "b".
        var s = new TextInputState("a").HandleKey(Key(ConsoleKey.B, 'b', alt: true));
        Assert.Equal("a", s.Value);
    }

    [Fact]
    public void Insert_PastesAWholeBlock()
    {
        var s = new TextInputState("ad", 1).Insert("bc");
        Assert.Equal("abcd", s.Value);
        Assert.Equal(3, s.Cursor);
    }

    // ── Cursor movement ───────────────────────────────────────────────────────

    [Fact]
    public void ArrowsMoveOneCharacter_AndStopAtTheEnds()
    {
        var s = new TextInputState("ab", 0);
        Assert.Equal(0, s.HandleKey(Key(ConsoleKey.LeftArrow)).Cursor);
        Assert.Equal(1, s.HandleKey(Key(ConsoleKey.RightArrow)).Cursor);

        var end = new TextInputState("ab", 2);
        Assert.Equal(2, end.HandleKey(Key(ConsoleKey.RightArrow)).Cursor);
    }

    [Fact]
    public void ArrowsStepOverAWholeEmoji_NotHalfOfIt()
    {
        var s = new TextInputState("🎞ab", 0);
        int afterRight = s.HandleKey(Key(ConsoleKey.RightArrow)).Cursor;
        Assert.Equal(2, afterRight); // past both code units

        var back = new TextInputState("🎞ab", 2).HandleKey(Key(ConsoleKey.LeftArrow));
        Assert.Equal(0, back.Cursor);
    }

    [Fact]
    public void ArrowsStepOverACombiningMark_AsOneUnit()
    {
        // "e" + U+0301 COMBINING ACUTE ACCENT renders as one glyph.
        var s = new TextInputState("e\u0301x", 0);
        Assert.Equal(2, s.HandleKey(Key(ConsoleKey.RightArrow)).Cursor);
    }

    [Fact]
    public void HomeAndEnd_JumpToTheEdges()
    {
        var s = new TextInputState("hello", 3);
        Assert.Equal(0, s.HandleKey(Key(ConsoleKey.Home)).Cursor);
        Assert.Equal(5, s.HandleKey(Key(ConsoleKey.End)).Cursor);
    }

    [Fact]
    public void CtrlAAndCtrlE_JumpToTheEdges()
    {
        var s = new TextInputState("hello", 3);
        Assert.Equal(0, s.HandleKey(Key(ConsoleKey.A, '\u0001', ctrl: true)).Cursor);
        Assert.Equal(5, s.HandleKey(Key(ConsoleKey.E, '\u0005', ctrl: true)).Cursor);
    }

    [Fact]
    public void CtrlArrows_MoveByWord()
    {
        var s = new TextInputState("one two three", 13);
        var back = s.HandleKey(Key(ConsoleKey.LeftArrow, ctrl: true));
        Assert.Equal(8, back.Cursor); // start of "three"

        var start = new TextInputState("one two three", 0);
        Assert.Equal(4, start.HandleKey(Key(ConsoleKey.RightArrow, ctrl: true)).Cursor);
    }

    // ── Deletion ──────────────────────────────────────────────────────────────

    [Fact]
    public void BackspaceRemovesTheCharacterBeforeTheCursor()
    {
        var s = new TextInputState("abc", 2).HandleKey(Key(ConsoleKey.Backspace));
        Assert.Equal("ac", s.Value);
        Assert.Equal(1, s.Cursor);
    }

    [Fact]
    public void BackspaceRemovesAWholeEmoji()
    {
        var s = new TextInputState("a🎞").HandleKey(Key(ConsoleKey.Backspace));
        Assert.Equal("a", s.Value); // not a stranded lone surrogate
    }

    [Fact]
    public void DeleteRemovesTheCharacterAfterTheCursor()
    {
        var s = new TextInputState("abc", 1).HandleKey(Key(ConsoleKey.Delete));
        Assert.Equal("ac", s.Value);
        Assert.Equal(1, s.Cursor);
    }

    [Fact]
    public void CtrlWDeletesTheWordBeforeTheCursor()
    {
        var s = new TextInputState("one two three").HandleKey(Key(ConsoleKey.W, '\u0017', ctrl: true));
        Assert.Equal("one two ", s.Value);
        Assert.Equal(8, s.Cursor);
    }

    [Fact]
    public void CtrlUDeletesToTheStart_CtrlKToTheEnd()
    {
        var s = new TextInputState("one two", 4);
        Assert.Equal("two", s.HandleKey(Key(ConsoleKey.U, '\u0015', ctrl: true)).Value);
        Assert.Equal("one ", s.HandleKey(Key(ConsoleKey.K, '\u000B', ctrl: true)).Value);
    }

    [Fact]
    public void CtrlUPutsTheCursorAtTheStart()
    {
        var s = new TextInputState("one two", 4).HandleKey(Key(ConsoleKey.U, '\u0015', ctrl: true));
        Assert.Equal(0, s.Cursor);
    }

    [Fact]
    public void Clear_EmptiesEverything()
    {
        var s = new TextInputState("hello").Clear();
        Assert.Equal("", s.Value);
        Assert.Equal(0, s.Cursor);
    }

    // ── No-ops ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsoleKey.Backspace)]
    [InlineData(ConsoleKey.LeftArrow)]
    [InlineData(ConsoleKey.Home)]
    public void KeysThatChangeNothingAtTheStart_ReturnTheSameInstance(ConsoleKey key)
    {
        var s = new TextInputState("abc", 0);
        Assert.Same(s, s.HandleKey(Key(key)));
    }

    [Fact]
    public void KillToEdge_AtThatEdge_ReturnsTheSameInstance()
    {
        var atStart = new TextInputState("abc", 0);
        Assert.Same(atStart, atStart.HandleKey(Key(ConsoleKey.U, '\u0015', ctrl: true)));

        var atEnd = new TextInputState("abc");
        Assert.Same(atEnd, atEnd.HandleKey(Key(ConsoleKey.K, '\u000B', ctrl: true)));
    }

    [Fact]
    public void UnhandledKeys_ReturnTheSameInstance()
    {
        var s = new TextInputState("abc", 1);
        Assert.Same(s, s.HandleKey(Key(ConsoleKey.F5)));
    }

    [Fact]
    public void DeleteAtTheEnd_ReturnsTheSameInstance()
    {
        var s = new TextInputState("abc");
        Assert.Same(s, s.HandleKey(Key(ConsoleKey.Delete)));
    }

    // ── Value semantics ───────────────────────────────────────────────────────

    [Fact]
    public void StatesWithTheSameTextAndCursor_AreEqual()
        => Assert.Equal(new TextInputState("abc", 1), new TextInputState("abc", 1));

    [Fact]
    public void StatesDifferingOnlyByCursor_AreNotEqual()
        => Assert.NotEqual(new TextInputState("abc", 1), new TextInputState("abc", 2));

    [Fact]
    public void EditingDoesNotMutateThePreviousState()
    {
        var before = new TextInputState("abc", 3);
        _ = before.HandleKey(Key(ConsoleKey.Backspace));
        Assert.Equal("abc", before.Value);
        Assert.Equal(3, before.Cursor);
    }
}
