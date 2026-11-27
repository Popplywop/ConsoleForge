using ConsoleForge.Core;
using ConsoleForge.Terminal;

namespace ConsoleForge.Tests;

/// <summary>
/// The decoder is pure, so every case here is a string in and events out — including the
/// timing cases, where <see cref="InputDecoder.Flush"/> stands in for "input went quiet".
/// </summary>
public class InputDecoderTests
{
    private static List<InputEvent> Decode(string input, bool flush = true)
    {
        var decoder = new InputDecoder();
        var events = new List<InputEvent>();
        decoder.Feed(input, events);
        if (flush) decoder.Flush(events);
        return events;
    }

    private static KeyMsg SingleKey(string input) =>
        Assert.IsType<KeyInputEvent>(Assert.Single(Decode(input))).Key;

    private static List<KeyMsg> Keys(string input) =>
        Decode(input).Select(e => Assert.IsType<KeyInputEvent>(e).Key).ToList();

    // ── Plain characters (same values Console.ReadKey reported on Unix) ──────────

    [Theory]
    [InlineData("a", ConsoleKey.A, 'a', false)]
    [InlineData("z", ConsoleKey.Z, 'z', false)]
    [InlineData("Q", ConsoleKey.Q, 'Q', true)]
    [InlineData("7", ConsoleKey.D7, '7', false)]
    [InlineData(" ", ConsoleKey.Spacebar, ' ', false)]
    [InlineData("\r", ConsoleKey.Enter, '\r', false)]
    [InlineData("\t", ConsoleKey.Tab, '\t', false)]
    [InlineData("/", ConsoleKey.Divide, '/', false)]
    public void PrintableAndWhitespace(string input, ConsoleKey key, char ch, bool shift)
    {
        Assert.Equal(new KeyMsg(key, ch, Shift: shift), SingleKey(input));
    }

    [Fact]
    public void Punctuation_KeepsCharacter_ForOfCharBindings()
    {
        var key = SingleKey("?");
        Assert.Equal('?', key.Character);
        Assert.True(KeyPattern.OfChar('?').Matches(key));
    }

    [Theory]
    [InlineData("\x7f")]
    [InlineData("\b")]
    public void Backspace_BothEncodings(string input)
    {
        Assert.Equal(ConsoleKey.Backspace, SingleKey(input).Key);
    }

    [Fact]
    public void CtrlLetter_CarriesControlCharacter()
    {
        Assert.Equal(new KeyMsg(ConsoleKey.A, '\x01', Ctrl: true), SingleKey("\x01"));
        Assert.Equal(new KeyMsg(ConsoleKey.W, '\x17', Ctrl: true), SingleKey("\x17"));
    }

    [Fact]
    public void NonAscii_IsDeliveredAsCharacter()
    {
        Assert.Equal('é', SingleKey("é").Character);
    }

    [Fact]
    public void AstralCharacter_ArrivesAsBothSurrogates()
    {
        var keys = Keys("😀");
        Assert.Equal("😀", string.Concat(keys.Select(k => k.Character)));
    }

    // ── Escape and its timeout ─────────────────────────────────────────────────

    [Fact]
    public void LoneEscape_WaitsForTimeout_ThenIsEscape()
    {
        var decoder = new InputDecoder();
        var events = new List<InputEvent>();

        decoder.Feed("\x1b", events);
        Assert.Empty(events);
        Assert.Equal(InputDecoder.EscapeTimeoutMs, decoder.PendingTimeoutMs);

        decoder.Flush(events);
        var key = Assert.IsType<KeyInputEvent>(Assert.Single(events)).Key;
        Assert.Equal(ConsoleKey.Escape, key.Key);
        Assert.Equal(0, decoder.PendingTimeoutMs);
    }

    [Fact]
    public void SequenceSplitAcrossReads_IsNotEscapeThenKeys()
    {
        // The defect this decoder replaces: over SSH or tmux "ESC [ A" can arrive in two
        // reads, and the old ~microsecond wait turned it into Escape, '[', 'A'.
        var decoder = new InputDecoder();
        var events = new List<InputEvent>();

        decoder.Feed("\x1b", events);
        decoder.Feed("[A", events);

        var key = Assert.IsType<KeyInputEvent>(Assert.Single(events)).Key;
        Assert.Equal(ConsoleKey.UpArrow, key.Key);
    }

    [Fact]
    public void LongerPartialSequence_WaitsLonger()
    {
        var decoder = new InputDecoder();
        decoder.Feed("\x1b[1;", new List<InputEvent>());
        Assert.Equal(InputDecoder.SequenceTimeoutMs, decoder.PendingTimeoutMs);
    }

    [Fact]
    public void DoubleEscape_IsEscapeThenPendingEscape()
    {
        var decoder = new InputDecoder();
        var events = new List<InputEvent>();
        decoder.Feed("\x1b\x1b", events);
        Assert.Equal(ConsoleKey.Escape, Assert.IsType<KeyInputEvent>(Assert.Single(events)).Key.Key);
        Assert.NotEqual(0, decoder.PendingTimeoutMs);
    }

    [Fact]
    public void AltLetter()
    {
        Assert.Equal(new KeyMsg(ConsoleKey.X, 'x', Alt: true), SingleKey("\x1bx"));
        Assert.Equal(new KeyMsg(ConsoleKey.Enter, '\r', Alt: true), SingleKey("\x1b\r"));
    }

    [Fact]
    public void AltBracket_ResolvedOnFlush()
    {
        var decoder = new InputDecoder();
        var events = new List<InputEvent>();
        decoder.Feed("\x1b[", events);
        Assert.Empty(events);

        decoder.Flush(events);
        var key = Assert.IsType<KeyInputEvent>(Assert.Single(events)).Key;
        Assert.Equal('[', key.Character);
        Assert.True(key.Alt);
    }

    [Fact]
    public void UnfinishedSequence_FlushesAsAltThenKeys()
    {
        var keys = Keys("\x1b[1;");
        Assert.Equal(['[', '1', ';'], keys.Select(k => k.Character!.Value).ToArray());
        Assert.True(keys[0].Alt);
        Assert.False(keys[1].Alt);
    }

    // ── Cursor and editing keys ────────────────────────────────────────────────

    [Theory]
    [InlineData("\x1b[A", ConsoleKey.UpArrow)]
    [InlineData("\x1b[B", ConsoleKey.DownArrow)]
    [InlineData("\x1b[C", ConsoleKey.RightArrow)]
    [InlineData("\x1b[D", ConsoleKey.LeftArrow)]
    [InlineData("\x1bOA", ConsoleKey.UpArrow)]
    [InlineData("\x1b[H", ConsoleKey.Home)]
    [InlineData("\x1bOF", ConsoleKey.End)]
    [InlineData("\x1b[2~", ConsoleKey.Insert)]
    [InlineData("\x1b[3~", ConsoleKey.Delete)]
    [InlineData("\x1b[5~", ConsoleKey.PageUp)]
    [InlineData("\x1b[6~", ConsoleKey.PageDown)]
    [InlineData("\x1bOP", ConsoleKey.F1)]
    [InlineData("\x1b[13~", ConsoleKey.F3)]
    [InlineData("\x1b[15~", ConsoleKey.F5)]
    [InlineData("\x1b[24~", ConsoleKey.F12)]
    public void NamedKeys(string input, ConsoleKey key)
    {
        var msg = SingleKey(input);
        Assert.Equal(key, msg.Key);
        Assert.False(msg.Ctrl || msg.Alt || msg.Shift);
    }

    [Theory]
    [InlineData("\x1b[1;5C", ConsoleKey.RightArrow, false, false, true)]
    [InlineData("\x1b[1;2A", ConsoleKey.UpArrow, true, false, false)]
    [InlineData("\x1b[1;3D", ConsoleKey.LeftArrow, false, true, false)]
    [InlineData("\x1b[1;6H", ConsoleKey.Home, true, false, true)]
    [InlineData("\x1b[5;5~", ConsoleKey.PageUp, false, false, true)]   // was dropped before
    [InlineData("\x1b[3;5~", ConsoleKey.Delete, false, false, true)]   // Ctrl+Delete word-delete
    [InlineData("\x1b[1;2P", ConsoleKey.F1, true, false, false)]
    [InlineData("\x1b[15;3~", ConsoleKey.F5, false, true, false)]
    public void ModifiedKeys(string input, ConsoleKey key, bool shift, bool alt, bool ctrl)
    {
        Assert.Equal(new KeyMsg(key, null, shift, alt, ctrl), SingleKey(input));
    }

    [Fact]
    public void BackTab_IsShiftTab()
    {
        Assert.Equal(new KeyMsg(ConsoleKey.Tab, '\t', Shift: true), SingleKey("\x1b[Z"));
    }

    [Fact]
    public void KeypadApplicationMode()
    {
        Assert.Equal(new KeyMsg(ConsoleKey.NumPad5, '5'), SingleKey("\x1bOu"));
        Assert.Equal(new KeyMsg(ConsoleKey.Enter, '\r'), SingleKey("\x1bOM"));
    }

    [Fact]
    public void Burst_DecodesEveryKey()
    {
        var keys = Keys("\x1b[A\x1b[Bx\x1b[1;5C");
        Assert.Equal(
            [ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.X, ConsoleKey.RightArrow],
            keys.Select(k => k.Key).ToArray());
        Assert.True(keys[3].Ctrl);
    }

    [Fact]
    public void UnrequestedReports_AreDroppedNotTyped()
    {
        Assert.Empty(Decode("\x1b[I"));      // focus in
        Assert.Empty(Decode("\x1b[?1u"));    // keyboard flags reply
        Assert.Empty(Decode("\x1b[8;24;80t"));
    }

    // ── Kitty keyboard protocol ────────────────────────────────────────────────

    [Fact]
    public void Kitty_Escape_NeedsNoTimeout()
    {
        var decoder = new InputDecoder();
        var events = new List<InputEvent>();
        decoder.Feed("\x1b[27u", events);

        Assert.Equal(ConsoleKey.Escape, Assert.IsType<KeyInputEvent>(Assert.Single(events)).Key.Key);
        Assert.Equal(0, decoder.PendingTimeoutMs);
    }

    [Fact]
    public void Kitty_CtrlLetter_MatchesLegacyEncoding()
    {
        // Same KeyMsg as the legacy 0x01 byte, so Ctrl bindings work either way.
        Assert.Equal(SingleKey("\x01"), SingleKey("\x1b[97;5u"));
        Assert.Equal(new KeyMsg(ConsoleKey.A, '\x01', Shift: true, Ctrl: true), SingleKey("\x1b[97;6u"));
    }

    [Theory]
    [InlineData("\x1b[13;2u", ConsoleKey.Enter, true, false, false)]   // Shift+Enter
    [InlineData("\x1b[13;5u", ConsoleKey.Enter, false, false, true)]   // Ctrl+Enter
    [InlineData("\x1b[9;5u", ConsoleKey.Tab, false, false, true)]      // Ctrl+Tab
    [InlineData("\x1b[127;5u", ConsoleKey.Backspace, false, false, true)]
    [InlineData("\x1b[105;5u", ConsoleKey.I, false, false, true)]      // Ctrl+I, distinct from Tab
    public void Kitty_DisambiguatedKeys(string input, ConsoleKey key, bool shift, bool alt, bool ctrl)
    {
        var msg = SingleKey(input);
        Assert.Equal(key, msg.Key);
        Assert.Equal((shift, alt, ctrl), (msg.Shift, msg.Alt, msg.Ctrl));
    }

    [Fact]
    public void Kitty_AltLetter_KeepsCharacter()
    {
        Assert.Equal(new KeyMsg(ConsoleKey.C, 'c', Alt: true), SingleKey("\x1b[99;3u"));
        // Flag 4 reports the shifted key, so Alt+Shift+a carries 'A'.
        Assert.Equal(new KeyMsg(ConsoleKey.A, 'A', Shift: true, Alt: true), SingleKey("\x1b[97:65;4u"));
        Assert.Equal(new KeyMsg(ConsoleKey.D1, '!', Shift: true, Alt: true), SingleKey("\x1b[49:33;4u"));
    }

    [Fact]
    public void Kitty_ChordsNeverInsertText()
    {
        Assert.Null(SingleKey("\x1b[47;5u").Character);  // Ctrl+/
        Assert.Null(SingleKey("\x1b[99;9u").Character);  // Super+c
    }

    [Fact]
    public void Kitty_ReleaseAndModifierOnlyKeys_AreDropped()
    {
        Assert.Empty(Decode("\x1b[97;1:3u"));   // release of 'a'
        Assert.Empty(Decode("\x1b[57441u"));    // left Shift on its own
        Assert.Empty(Decode("\x1b[57358u"));    // Caps Lock
    }

    [Fact]
    public void Kitty_FunctionalKeys()
    {
        Assert.Equal(new KeyMsg(ConsoleKey.NumPad1, '1'), SingleKey("\x1b[57400u"));
        Assert.Equal(ConsoleKey.F13, SingleKey("\x1b[57376u").Key);
        Assert.Equal(ConsoleKey.MediaPlay, SingleKey("\x1b[57428u").Key);
    }

    [Fact]
    public void ModifyOtherKeys_IsDecodedLikeKitty()
    {
        Assert.Equal(SingleKey("\x01"), SingleKey("\x1b[27;5;97~"));
    }

    // ── Mouse ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SgrMouse_PressAndRelease()
    {
        var events = Decode("\x1b[<0;10;5M\x1b[<0;10;5m");
        Assert.Collection(events,
            e => Assert.Equal(new MouseMsg(MouseButton.Left, MouseAction.Press, 9, 4), Assert.IsType<MouseInputEvent>(e).Mouse),
            e => Assert.Equal(new MouseMsg(MouseButton.Left, MouseAction.Release, 9, 4), Assert.IsType<MouseInputEvent>(e).Mouse));
    }

    [Fact]
    public void SgrMouse_WheelMotionAndModifiers()
    {
        var events = Decode("\x1b[<64;1;1M\x1b[<35;3;4M\x1b[<16;2;2M");
        var msgs = events.Select(e => Assert.IsType<MouseInputEvent>(e).Mouse).ToList();
        Assert.Equal(MouseButton.ScrollUp, msgs[0].Button);
        Assert.Equal(MouseAction.Move, msgs[1].Action);
        Assert.True(msgs[2].Ctrl);
    }

    [Fact]
    public void SgrMouse_SplitAcrossReads()
    {
        var decoder = new InputDecoder();
        var events = new List<InputEvent>();
        decoder.Feed("\x1b[<0;1", events);
        decoder.Feed("2;7M", events);
        Assert.Equal(new MouseMsg(MouseButton.Left, MouseAction.Press, 11, 6),
            Assert.IsType<MouseInputEvent>(Assert.Single(events)).Mouse);
    }

    [Fact]
    public void X10Mouse_FromTerminalsWithoutSgr()
    {
        // CSI M, then button 0 (+32), column 5 (+32), row 3 (+32).
        var msg = Assert.IsType<MouseInputEvent>(Assert.Single(Decode("\x1b[M %#"))).Mouse;
        Assert.Equal(new MouseMsg(MouseButton.Left, MouseAction.Press, 4, 2), msg);
    }

    // ── Bracketed paste ────────────────────────────────────────────────────────

    [Fact]
    public void Paste_ArrivesAsOneEvent_WithNormalisedNewlines()
    {
        var paste = Assert.IsType<PasteInputEvent>(Assert.Single(Decode("\x1b[200~hello\r\nworld\rend\x1b[201~")));
        Assert.Equal("hello\nworld\nend", paste.Text);
    }

    [Fact]
    public void Paste_KeepsEscapesAndSequencesAsText()
    {
        var paste = Assert.IsType<PasteInputEvent>(Assert.Single(Decode("\x1b[200~a\x1b[Ab\x1b[201~")));
        Assert.Equal("a\x1b[Ab", paste.Text);
    }

    [Fact]
    public void Paste_TerminatorSplitAcrossReads()
    {
        var decoder = new InputDecoder();
        var events = new List<InputEvent>();
        decoder.Feed("\x1b[200~abc\x1b[2", events);
        Assert.True(decoder.InPaste);
        Assert.Empty(events);
        // A paste is not ended by the short escape timeouts, only by a long silence.
        Assert.Equal(InputDecoder.PasteTimeoutMs, decoder.PendingTimeoutMs);

        decoder.Feed("01~q", events);
        Assert.Collection(events,
            e => Assert.Equal("abc", Assert.IsType<PasteInputEvent>(e).Text),
            e => Assert.Equal(ConsoleKey.Q, Assert.IsType<KeyInputEvent>(e).Key.Key));
    }

    [Fact]
    public void Paste_WithoutTerminator_EndsWhenInputGoesQuiet()
    {
        // A terminal that drops the end marker must not turn every later key into paste.
        var decoder = new InputDecoder();
        var events = new List<InputEvent>();
        decoder.Feed("\x1b[200~abc", events);
        decoder.Flush(events);

        Assert.Equal("abc", Assert.IsType<PasteInputEvent>(Assert.Single(events)).Text);
        Assert.False(decoder.InPaste);

        events.Clear();
        decoder.Feed("q", events);
        Assert.Equal(ConsoleKey.Q, Assert.IsType<KeyInputEvent>(Assert.Single(events)).Key.Key);
    }

    // ── Terminal replies never become keys ─────────────────────────────────────

    [Fact]
    public void CursorPositionReport_IsNotModifiedF3()
    {
        // CSI row;col R — sent whenever something asks where the cursor is.
        Assert.Empty(Decode("\x1b[24;80R"));
        // Modified F3 itself (CSI 1;mods R) still decodes.
        Assert.Equal(new KeyMsg(ConsoleKey.F3, null, Ctrl: true), SingleKey("\x1b[1;5R"));
    }


    [Fact]
    public void KittyGraphicsReply_IsNotTyped()
    {
        // Before: Alt+'_', then "Gi=31;OK" typed into whatever had focus, then Alt+'\'.
        var events = Decode("\x1b_Gi=31;OK\x1b\\q");
        Assert.Collection(events,
            e => Assert.Equal(new KittyGraphicsReplyEvent(31, "OK"), e),
            e => Assert.Equal(ConsoleKey.Q, Assert.IsType<KeyInputEvent>(e).Key.Key));
    }

    [Fact]
    public void KittyGraphicsReply_SplitAtTerminator()
    {
        var decoder = new InputDecoder();
        var events = new List<InputEvent>();
        decoder.Feed("\x1b_Gi=7,p=2;ENOENT:no such image\x1b", events);
        Assert.Empty(events);
        decoder.Feed("\\", events);
        Assert.Equal(new KittyGraphicsReplyEvent(7, "ENOENT:no such image"), Assert.Single(events));
    }

    [Fact]
    public void AltUnderscore_IsStillAKey()
    {
        var keys = Keys("\x1b_x");
        Assert.Equal('_', keys[0].Character);
        Assert.True(keys[0].Alt);
        Assert.Equal(ConsoleKey.X, keys[1].Key);

        Assert.True(SingleKey("\x1b_").Alt);
    }

    [Fact]
    public void DeviceAttributesReply()
    {
        Assert.Equal(new DeviceAttributesEvent("62;22"), Assert.Single(Decode("\x1b[?62;22c")));
    }

    [Fact]
    public void ProbeReply_GraphicsThenAttributes()
    {
        var events = Decode("\x1b_Gi=31;OK\x1b\\\x1b[?62;4;22c");
        Assert.IsType<KittyGraphicsReplyEvent>(events[0]);
        Assert.IsType<DeviceAttributesEvent>(events[1]);
        Assert.Equal(2, events.Count);
    }
}
