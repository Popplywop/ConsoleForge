using ConsoleForge.Core;

namespace ConsoleForge.Tests.Core;

public class TextAreaStateTests
{
    private static KeyMsg Key(ConsoleKey k, char? ch = null, bool ctrl = false) => new(k, ch, Ctrl: ctrl);
    private static KeyMsg Char(char c) => new(ConsoleKey.NoName, c);

    // ── Invariants ────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyDocument_IsOneEmptyLine()
    {
        var s = new TextAreaState();
        Assert.True(s.IsEmpty);
        Assert.Single(s.Lines);
        Assert.Equal("", s.CurrentLine);
    }

    [Fact]
    public void NullOrEmptyLines_BecomeOneEmptyLine()
    {
        Assert.Single(new TextAreaState(null).Lines);
        Assert.Single(new TextAreaState([]).Lines);
    }

    [Fact]
    public void CursorClampsIntoTheDocument()
    {
        var s = new TextAreaState(["ab", "cd"], cursorRow: 9, cursorCol: 9);
        Assert.Equal(1, s.CursorRow);
        Assert.Equal(2, s.CursorCol);
    }

    [Fact]
    public void ReplacingLines_RenormalisesTheCursor()
    {
        var s = new TextAreaState(["aaaa", "bbbb", "cccc"], cursorRow: 2, cursorCol: 4);
        var shorter = s with { Lines = new[] { "x" } };

        Assert.Equal(0, shorter.CursorRow);
        Assert.Equal(1, shorter.CursorCol);
    }

    // ── Grapheme clusters, the bug the widget had ─────────────────────────────

    [Fact]
    public void CursorMovesOverAnEmojiAsOneUnit()
    {
        // "🙂" is a surrogate pair — two UTF-16 units, one cluster. The old widget
        // stepped by char and could land between the halves.
        var s = new TextAreaState(["a🙂b"], cursorRow: 0, cursorCol: 1);
        var right = s.HandleKey(Key(ConsoleKey.RightArrow));

        Assert.Equal(3, right.CursorCol); // skipped the whole pair, not half of it
    }

    [Fact]
    public void BackspaceDeletesAWholeCluster()
    {
        var s = new TextAreaState(["a🙂"], cursorRow: 0, cursorCol: 3);
        var back = s.HandleKey(Key(ConsoleKey.Backspace));

        Assert.Equal("a", back.CurrentLine);
    }

    [Fact]
    public void ACursorLandingMidClusterIsSnappedBack()
    {
        var s = new TextAreaState(["a🙂b"], cursorRow: 0, cursorCol: 2); // inside the pair
        Assert.Equal(1, s.CursorCol);
    }

    // ── Single-line editing is TextInputState's ───────────────────────────────

    [Fact]
    public void PrintableCharactersInsertAtTheCursor()
    {
        var s = new TextAreaState(["ac"], cursorRow: 0, cursorCol: 1).HandleKey(Char('b'));
        Assert.Equal("abc", s.CurrentLine);
        Assert.Equal(2, s.CursorCol);
    }

    [Fact]
    public void WordJumpsAndKillsComeForFree()
    {
        // The widget never had these; delegating picked them up, as TextInput did.
        var s = new TextAreaState(["one two three"], cursorRow: 0, cursorCol: 13);

        Assert.Equal(8, s.HandleKey(Key(ConsoleKey.LeftArrow, ctrl: true)).CursorCol);
        Assert.Equal("one two ", s.HandleKey(Key(ConsoleKey.W, ctrl: true)).CurrentLine);
        Assert.Equal("", s.HandleKey(Key(ConsoleKey.U, ctrl: true)).CurrentLine);
    }

    // ── Crossing lines is this type's own ─────────────────────────────────────

    [Fact]
    public void LeftAtColumnZero_GoesToTheEndOfThePreviousLine()
    {
        var s = new TextAreaState(["abc", "def"], cursorRow: 1, cursorCol: 0);
        var moved = s.HandleKey(Key(ConsoleKey.LeftArrow));

        Assert.Equal(0, moved.CursorRow);
        Assert.Equal(3, moved.CursorCol);
    }

    [Fact]
    public void RightAtEndOfLine_GoesToTheStartOfTheNext()
    {
        var s = new TextAreaState(["abc", "def"], cursorRow: 0, cursorCol: 3);
        var moved = s.HandleKey(Key(ConsoleKey.RightArrow));

        Assert.Equal(1, moved.CursorRow);
        Assert.Equal(0, moved.CursorCol);
    }

    [Fact]
    public void UpAndDownKeepTheColumnWhereTheLineAllows()
    {
        var s = new TextAreaState(["longer line", "short", "longer line"], cursorRow: 0, cursorCol: 11);

        var down = s.HandleKey(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, down.CursorRow);
        Assert.Equal(5, down.CursorCol); // clipped to the short line
    }

    [Fact]
    public void AtTheDocumentEdges_UpAndDownDoNothing()
    {
        var top = new TextAreaState(["a", "b"], cursorRow: 0);
        var bottom = new TextAreaState(["a", "b"], cursorRow: 1);

        Assert.Same(top, top.HandleKey(Key(ConsoleKey.UpArrow)));
        Assert.Same(bottom, bottom.HandleKey(Key(ConsoleKey.DownArrow)));
    }

    [Fact]
    public void EnterSplitsTheLineAtTheCursor()
    {
        var s = new TextAreaState(["abcd"], cursorRow: 0, cursorCol: 2).HandleKey(Key(ConsoleKey.Enter, '\r'));

        Assert.Equal(new[] { "ab", "cd" }, s.Lines);
        Assert.Equal(1, s.CursorRow);
        Assert.Equal(0, s.CursorCol);
    }

    [Fact]
    public void BackspaceAtColumnZero_JoinsOntoThePreviousLine()
    {
        var s = new TextAreaState(["ab", "cd"], cursorRow: 1, cursorCol: 0).HandleKey(Key(ConsoleKey.Backspace));

        Assert.Equal(new[] { "abcd" }, s.Lines);
        Assert.Equal(0, s.CursorRow);
        Assert.Equal(2, s.CursorCol); // at the seam
    }

    [Fact]
    public void DeleteAtEndOfLine_PullsTheNextLineUp()
    {
        var s = new TextAreaState(["ab", "cd"], cursorRow: 0, cursorCol: 2).HandleKey(Key(ConsoleKey.Delete));

        Assert.Equal(new[] { "abcd" }, s.Lines);
        Assert.Equal(2, s.CursorCol);
    }

    [Fact]
    public void JoiningAtTheDocumentEdges_DoesNothing()
    {
        var first = new TextAreaState(["ab"], cursorRow: 0, cursorCol: 0);
        var last = new TextAreaState(["ab"], cursorRow: 0, cursorCol: 2);

        Assert.Same(first, first.HandleKey(Key(ConsoleKey.Backspace)));
        Assert.Same(last, last.HandleKey(Key(ConsoleKey.Delete)));
    }

    // ── MaxLines ──────────────────────────────────────────────────────────────

    [Fact]
    public void AtMaxLines_EnterDoesNothing()
    {
        var s = new TextAreaState(["a", "b"], cursorRow: 1, cursorCol: 1, maxLines: 2);
        Assert.Same(s, s.HandleKey(Key(ConsoleKey.Enter, '\r')));
    }

    // ── Paste ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Insert_SplitsOnNewlines()
    {
        var s = new TextAreaState(["ad"], cursorRow: 0, cursorCol: 1).Insert("b\nc");

        Assert.Equal(new[] { "ab", "cd" }, s.Lines);
        Assert.Equal(1, s.CursorRow);
        Assert.Equal(1, s.CursorCol);
    }

    [Fact]
    public void Insert_NormalisesCarriageReturns()
    {
        Assert.Equal(new[] { "a", "b" }, new TextAreaState([""]).Insert("a\r\nb").Lines);
        Assert.Equal(new[] { "a", "b" }, new TextAreaState([""]).Insert("a\rb").Lines);
    }

    [Fact]
    public void Insert_WithoutNewlines_StaysOnOneLine()
    {
        var s = new TextAreaState(["ac"], cursorRow: 0, cursorCol: 1).Insert("b");
        Assert.Equal(new[] { "abc" }, s.Lines);
    }

    // ── No-op signalling ──────────────────────────────────────────────────────

    [Fact]
    public void AKeyThatChangesNothing_ReturnsTheSameInstance()
    {
        var s = new TextAreaState(["ab"], cursorRow: 0, cursorCol: 0);
        Assert.Same(s, s.HandleKey(Key(ConsoleKey.F5)));
        Assert.Same(s, s.HandleKey(null!));
        Assert.Same(s, s.Insert(""));
    }

    [Fact]
    public void Text_JoinsTheLines()
    {
        Assert.Equal("a\nb", new TextAreaState(["a", "b"]).Text());
        Assert.Equal("a|b", new TextAreaState(["a", "b"]).Text("|"));
    }
}
