using ConsoleForge.Core;

namespace ConsoleForge.Tests.Core;

public class KeyMapTests
{
    private sealed record TestMsg(string Value) : IMsg;

    // ── KeyPattern matching ───────────────────────────────────────────────────

    [Fact]
    public void KeyPattern_Of_MatchesAnyModifiers()
    {
        var p = KeyPattern.Of(ConsoleKey.Q);
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.Q, 'q')));
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.Q, 'Q', Shift: true)));
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.Q, 'q', Ctrl: true)));
        Assert.False(p.Matches(new KeyMsg(ConsoleKey.W, 'w')));
    }

    [Fact]
    public void KeyPattern_WithCtrl_MatchesOnlyCtrl()
    {
        var p = KeyPattern.WithCtrl(ConsoleKey.S);
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.S, 's', Ctrl: true)));
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.S, 's', Ctrl: true, Shift: true))); // Shift wildcard
        Assert.False(p.Matches(new KeyMsg(ConsoleKey.S, 's'))); // no Ctrl
    }

    [Fact]
    public void KeyPattern_Plain_MatchesNoModifiers()
    {
        var p = KeyPattern.Plain(ConsoleKey.A);
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.A, 'a')));
        Assert.False(p.Matches(new KeyMsg(ConsoleKey.A, 'a', Shift: true)));
        Assert.False(p.Matches(new KeyMsg(ConsoleKey.A, 'a', Ctrl: true)));
    }

    [Fact]
    public void KeyPattern_WithShift_MatchesShift()
    {
        var p = KeyPattern.WithShift(ConsoleKey.Tab);
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.Tab, '\t', Shift: true)));
        Assert.False(p.Matches(new KeyMsg(ConsoleKey.Tab, '\t')));
    }

    [Fact]
    public void KeyPattern_WithAlt_MatchesAlt()
    {
        var p = KeyPattern.WithAlt(ConsoleKey.X);
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.X, 'x', Alt: true)));
        Assert.False(p.Matches(new KeyMsg(ConsoleKey.X, 'x')));
    }

    // ── KeyPattern character matching ─────────────────────────────────────────

    [Fact]
    public void KeyPattern_OfChar_IgnoresConsoleKey()
    {
        // '?' is Shift+Oem2 on a US layout and a different physical key elsewhere.
        var p = KeyPattern.OfChar('?');
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.Oem2, '?', Shift: true)));
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.Oem7, '?')));
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.None, '?')));
        Assert.False(p.Matches(new KeyMsg(ConsoleKey.Oem2, '/')));
    }

    [Fact]
    public void KeyPattern_OfChar_IsCaseSensitive()
    {
        var upper = KeyPattern.OfChar('N');
        Assert.True(upper.Matches(new KeyMsg(ConsoleKey.N, 'N', Shift: true)));
        Assert.True(upper.Matches(new KeyMsg(ConsoleKey.N, 'N'))); // caps lock — Shift is a wildcard
        Assert.False(upper.Matches(new KeyMsg(ConsoleKey.N, 'n')));
        Assert.False(KeyPattern.OfChar('n').Matches(new KeyMsg(ConsoleKey.N, 'N', Shift: true)));
    }

    [Fact]
    public void KeyPattern_OfChar_RejectsCtrlAndAlt()
    {
        var p = KeyPattern.OfChar('/');
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.Oem2, '/')));
        Assert.False(p.Matches(new KeyMsg(ConsoleKey.Oem2, '/', Ctrl: true)));
        Assert.False(p.Matches(new KeyMsg(ConsoleKey.Oem2, '/', Alt: true)));
    }

    [Fact]
    public void KeyPattern_OfChar_DoesNotMatchKeyWithoutCharacter()
    {
        Assert.False(KeyPattern.OfChar('?').Matches(new KeyMsg(ConsoleKey.UpArrow, null)));
    }

    [Fact]
    public void KeyPattern_Default_MatchesEveryKey()
    {
        // Every field null is a wildcard, so the default pattern is a catch-all.
        var p = default(KeyPattern);
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.UpArrow, null)));
        Assert.True(p.Matches(new KeyMsg(ConsoleKey.Q, 'q', Shift: true, Alt: true, Ctrl: true)));
    }

    // ── KeyMap key handling ───────────────────────────────────────────────────

    [Fact]
    public void Handle_MatchingKey_ReturnsMsg()
    {
        var map = new KeyMap()
            .On(ConsoleKey.Q, () => new TestMsg("quit"));

        var result = map.Handle(new KeyMsg(ConsoleKey.Q, 'q'));
        var msg = Assert.IsType<TestMsg>(result);
        Assert.Equal("quit", msg.Value);
    }

    [Fact]
    public void Handle_NoMatch_ReturnsNull()
    {
        var map = new KeyMap()
            .On(ConsoleKey.Q, () => new TestMsg("quit"));

        Assert.Null(map.Handle(new KeyMsg(ConsoleKey.W, 'w')));
    }

    [Fact]
    public void Handle_FirstMatchWins()
    {
        var map = new KeyMap()
            .On(ConsoleKey.Q, () => new TestMsg("first"))
            .On(ConsoleKey.Q, () => new TestMsg("second"));

        var result = Assert.IsType<TestMsg>(map.Handle(new KeyMsg(ConsoleKey.Q, 'q')));
        Assert.Equal("first", result.Value);
    }

    [Fact]
    public void Handle_PatternOverload_MatchesModifiers()
    {
        var map = new KeyMap()
            .On(KeyPattern.WithCtrl(ConsoleKey.S), () => new TestMsg("save"))
            .On(ConsoleKey.S, () => new TestMsg("letter-s"));

        // Ctrl+S → save
        var r1 = Assert.IsType<TestMsg>(map.Handle(new KeyMsg(ConsoleKey.S, 's', Ctrl: true)));
        Assert.Equal("save", r1.Value);

        // plain S → letter-s
        var r2 = Assert.IsType<TestMsg>(map.Handle(new KeyMsg(ConsoleKey.S, 's')));
        Assert.Equal("letter-s", r2.Value);
    }

    [Fact]
    public void Handle_HandlerReceivesKeyMsg()
    {
        KeyMsg? received = null;
        var map = new KeyMap()
            .On(ConsoleKey.A, k => { received = k; return new TestMsg("a"); });

        map.Handle(new KeyMsg(ConsoleKey.A, 'a', Shift: true));
        Assert.NotNull(received);
        Assert.True(received!.Shift);
    }

    // ── Mouse bindings ────────────────────────────────────────────────────────

    [Fact]
    public void Handle_OnClick_MatchesLeftPress()
    {
        var map = new KeyMap()
            .OnClick(_ => new TestMsg("clicked"));

        var result = map.Handle(new MouseMsg(MouseButton.Left, MouseAction.Press, 5, 10));
        Assert.IsType<TestMsg>(result);
    }

    [Fact]
    public void Handle_OnClick_DoesNotMatchRelease()
    {
        var map = new KeyMap().OnClick(_ => new TestMsg("x"));
        Assert.Null(map.Handle(new MouseMsg(MouseButton.Left, MouseAction.Release, 0, 0)));
    }

    [Fact]
    public void Handle_OnClick_DoesNotMatchRightButton()
    {
        var map = new KeyMap().OnClick(_ => new TestMsg("x"));
        Assert.Null(map.Handle(new MouseMsg(MouseButton.Right, MouseAction.Press, 0, 0)));
    }

    [Fact]
    public void Handle_OnScroll_MatchesBothDirections()
    {
        var map = new KeyMap()
            .OnScroll(m => new TestMsg(m.Button == MouseButton.ScrollUp ? "up" : "down"));

        var r1 = Assert.IsType<TestMsg>(map.Handle(
            new MouseMsg(MouseButton.ScrollUp, MouseAction.Press, 0, 0)));
        Assert.Equal("up", r1.Value);

        var r2 = Assert.IsType<TestMsg>(map.Handle(
            new MouseMsg(MouseButton.ScrollDown, MouseAction.Press, 0, 0)));
        Assert.Equal("down", r2.Value);
    }

    [Fact]
    public void Handle_OnMouse_CustomPredicate()
    {
        var map = new KeyMap()
            .OnMouse(m => m.Button == MouseButton.Right && m.Action == MouseAction.Press,
                     _ => new TestMsg("right-click"));

        var result = map.Handle(new MouseMsg(MouseButton.Right, MouseAction.Press, 0, 0));
        Assert.IsType<TestMsg>(result);
        Assert.Null(map.Handle(new MouseMsg(MouseButton.Left, MouseAction.Press, 0, 0)));
    }

    [Fact]
    public void Handle_OnMouse_ButtonAndAction()
    {
        var map = new KeyMap()
            .OnMouse(MouseButton.Middle, MouseAction.Release, _ => new TestMsg("middle-release"));

        Assert.NotNull(map.Handle(new MouseMsg(MouseButton.Middle, MouseAction.Release, 0, 0)));
        Assert.Null(map.Handle(new MouseMsg(MouseButton.Middle, MouseAction.Press, 0, 0)));
    }

    // ── Handle with non-input msg ─────────────────────────────────────────────

    [Fact]
    public void Handle_NonInputMsg_ReturnsNull()
    {
        var map = new KeyMap().On(ConsoleKey.Q, () => new TestMsg("q"));
        Assert.Null(map.Handle(new WindowResizeMsg(80, 24)));
    }

    // ── Merge ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_ThisBindingsFirst_TakesPriority()
    {
        var a = new KeyMap().On(ConsoleKey.Q, () => new TestMsg("a"));
        var b = new KeyMap().On(ConsoleKey.Q, () => new TestMsg("b"));

        var merged = a.Merge(b);
        var result = Assert.IsType<TestMsg>(merged.Handle(new KeyMsg(ConsoleKey.Q, 'q')));
        Assert.Equal("a", result.Value); // a's binding comes first
    }

    [Fact]
    public void Merge_IncludesBothKeySets()
    {
        var a = new KeyMap().On(ConsoleKey.A, () => new TestMsg("a"));
        var b = new KeyMap().On(ConsoleKey.B, () => new TestMsg("b"));

        var merged = a.Merge(b);
        Assert.NotNull(merged.Handle(new KeyMsg(ConsoleKey.A, 'a')));
        Assert.NotNull(merged.Handle(new KeyMsg(ConsoleKey.B, 'b')));
    }

    [Fact]
    public void Merge_IncludesBothMouseSets()
    {
        var a = new KeyMap().OnClick(_ => new TestMsg("click"));
        var b = new KeyMap().OnScroll(_ => new TestMsg("scroll"));

        var merged = a.Merge(b);
        Assert.NotNull(merged.Handle(new MouseMsg(MouseButton.Left, MouseAction.Press, 0, 0)));
        Assert.NotNull(merged.Handle(new MouseMsg(MouseButton.ScrollUp, MouseAction.Press, 0, 0)));
    }

    [Fact]
    public void Merge_DoesNotMutateOriginals()
    {
        var a = new KeyMap().On(ConsoleKey.A, () => new TestMsg("a"));
        var b = new KeyMap().On(ConsoleKey.B, () => new TestMsg("b"));

        var merged = a.Merge(b);
        Assert.Equal(1, a.KeyBindingCount);  // a unchanged
        Assert.Equal(1, b.KeyBindingCount);  // b unchanged
        Assert.Equal(2, merged.KeyBindingCount);
    }

    // ── Counts ────────────────────────────────────────────────────────────────

    [Fact]
    public void KeyBindingCount_ReflectsRegistrations()
    {
        var map = new KeyMap()
            .On(ConsoleKey.A, () => new TestMsg("a"))
            .On(ConsoleKey.B, () => new TestMsg("b"));
        Assert.Equal(2, map.KeyBindingCount);
    }

    [Fact]
    public void MouseBindingCount_ReflectsRegistrations()
    {
        var map = new KeyMap().OnClick(_ => new TestMsg("x")).OnScroll(_ => new TestMsg("y"));
        Assert.Equal(2, map.MouseBindingCount);
    }

    [Fact]
    public void Handle_CharBinding_MatchesRegardlessOfConsoleKey()
    {
        var map = new KeyMap().On('?', () => new TestMsg("help"));
        Assert.Equal(new TestMsg("help"), map.Handle(new KeyMsg(ConsoleKey.Oem2, '?', Shift: true)));
        Assert.Equal(new TestMsg("help"), map.Handle(new KeyMsg(ConsoleKey.Oem4, '?')));
        Assert.Null(map.Handle(new KeyMsg(ConsoleKey.Oem2, '/')));
    }

    [Fact]
    public void Handle_CharBinding_ReceivesOriginalKeyMsg()
    {
        var map = new KeyMap().On('n', k => new TestMsg(k.Key.ToString()));
        Assert.Equal(new TestMsg("N"), map.Handle(new KeyMsg(ConsoleKey.N, 'n')));
    }
}
