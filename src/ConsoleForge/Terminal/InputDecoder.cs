using System.Text;
using ConsoleForge.Core;

namespace ConsoleForge.Terminal;

/// <summary>
/// Turns the characters a terminal sends on stdin into <see cref="InputEvent"/>s.
/// </summary>
/// <remarks>
/// <para>
/// Pure: no I/O and no clock. The reader feeds characters as they arrive with
/// <see cref="Feed"/>, and calls <see cref="Flush"/> once input has been quiet for
/// <see cref="PendingTimeoutMs"/>. That split is what makes the decoder testable with
/// plain strings, and it is the only place time enters: a lone <c>ESC</c> is the Escape
/// key or the first byte of a sequence, and only the absence of a next byte decides.
/// </para>
/// <para>
/// Understands legacy xterm/VT keys (with xterm modifier parameters), SS3 keys, SGR and
/// X10 mouse reports, bracketed paste, the Kitty keyboard protocol (<c>CSI … u</c>,
/// flags 1 and 4) and xterm's <c>modifyOtherKeys</c> form (<c>CSI 27 ; m ; code ~</c>).
/// Replies the terminal sends to queries — Kitty graphics responses and primary device
/// attributes — come out as their own events, so they can never be mistaken for typing.
/// </para>
/// <para>
/// Single characters map to <see cref="KeyMsg"/> the way .NET's <c>Console.ReadKey</c>
/// maps them on Unix, so models written against the old input path see the same values.
/// </para>
/// </remarks>
internal sealed class InputDecoder
{
    private const char Esc = '\x1b';

    /// <summary>
    /// How long a lone <c>ESC</c> (or <c>ESC</c> plus one character) waits for the rest of a
    /// sequence before it is taken as the Escape key (or Alt plus that character).
    /// </summary>
    internal const int EscapeTimeoutMs = 50;

    /// <summary>
    /// How long a sequence that is already three or more characters long waits for its
    /// final byte. Nobody types that fast, so this is a terminal reply split across reads
    /// (an SSH packet boundary, say) and is worth waiting for: giving up early is exactly
    /// how a reply ends up delivered as keystrokes.
    /// </summary>
    internal const int SequenceTimeoutMs = 500;

    /// <summary>
    /// How long a bracketed paste may go quiet before it is ended without its terminator.
    /// A paste arrives as one burst; this only matters if the terminal drops the end
    /// marker, which would otherwise turn every key after it into paste text, forever.
    /// </summary>
    internal const int PasteTimeoutMs = 3000;

    // Guards against a stream that opens a sequence and never closes it.
    private const int MaxCsiLength = 128;
    private const int MaxApcLength = 16 * 1024;

    private const string PasteEnd = "\x1b[201~";

    // Kitty / xterm modifier bits (the transmitted value minus one).
    private const int ModShift = 1, ModAlt = 2, ModCtrl = 4, ModSuper = 8, ModHyper = 16, ModMeta = 32;

    private readonly StringBuilder _buffer = new();
    private StringBuilder? _paste;

    /// <summary>
    /// Milliseconds the reader should wait for more input before calling <see cref="Flush"/>,
    /// or 0 when nothing is waiting on a timeout.
    /// </summary>
    public int PendingTimeoutMs
    {
        get
        {
            // Inside a paste nothing is ambiguous — the body is text until the terminator —
            // but a terminator that never comes must not hold input hostage.
            if (_paste is not null) return PasteTimeoutMs;
            if (_buffer.Length == 0) return 0;
            return _buffer.Length <= 2 ? EscapeTimeoutMs : SequenceTimeoutMs;
        }
    }

    /// <summary>True while a bracketed paste has started and not yet ended.</summary>
    public bool InPaste => _paste is not null;

    /// <summary>
    /// Decode <paramref name="input"/>, appending complete events to <paramref name="output"/>.
    /// A trailing incomplete sequence is held until more input or <see cref="Flush"/>.
    /// </summary>
    public void Feed(ReadOnlySpan<char> input, List<InputEvent> output)
    {
        _buffer.Append(input);
        Drain(output, final: false);
    }

    /// <summary>
    /// Input has gone quiet: resolve whatever is held. A lone <c>ESC</c> becomes Escape, and
    /// a sequence that never finished becomes Alt plus its second character, with the rest
    /// decoded as ordinary keys. A bracketed paste whose terminator never arrived is ended
    /// with the text received so far.
    /// </summary>
    public void Flush(List<InputEvent> output)
    {
        if (_paste is not null)
        {
            _paste.Append(_buffer);
            _buffer.Clear();
            output.Add(new PasteInputEvent(NormaliseNewlines(_paste.ToString())));
            _paste = null;
            return;
        }

        Drain(output, final: true);
    }

    private void Drain(List<InputEvent> output, bool final)
    {
        if (_buffer.Length == 0) return;
        string s = _buffer.ToString();
        int i = 0;

        while (i < s.Length)
        {
            if (_paste is not null)
            {
                int end = s.IndexOf(PasteEnd, i, StringComparison.Ordinal);
                if (end < 0)
                {
                    // Keep back a tail that could be the start of the terminator.
                    int keep = PartialSuffix(s, i, PasteEnd);
                    _paste.Append(s, i, s.Length - i - keep);
                    i = s.Length - keep;
                    break;
                }

                _paste.Append(s, i, end - i);
                output.Add(new PasteInputEvent(NormaliseNewlines(_paste.ToString())));
                _paste = null;
                i = end + PasteEnd.Length;
                continue;
            }

            int consumed = DecodeOne(s, i, final, output);
            if (consumed == 0) break; // incomplete: wait for more input or a flush
            i += consumed;
        }

        _buffer.Remove(0, i);
    }

    /// <summary>Decode one unit at <paramref name="i"/>; returns characters consumed, 0 if incomplete.</summary>
    private int DecodeOne(string s, int i, bool final, List<InputEvent> output)
    {
        char c = s[i];
        if (c != Esc)
        {
            output.Add(Key(SingleChar(c)));
            return 1;
        }

        if (i + 1 >= s.Length)
        {
            if (!final) return 0;
            output.Add(Key(new KeyMsg(ConsoleKey.Escape, Esc)));
            return 1;
        }

        char next = s[i + 1];
        switch (next)
        {
            case '[': return DecodeCsi(s, i, final, output);
            case 'O': return DecodeSs3(s, i, final, output);
            case '_': return DecodeApc(s, i, final, output);
            case Esc:
                // ESC ESC: the first is a key on its own; the second starts the next unit.
                output.Add(Key(new KeyMsg(ConsoleKey.Escape, Esc)));
                return 1;
        }

        if (char.IsSurrogate(next))
        {
            output.Add(Key(new KeyMsg(ConsoleKey.Escape, Esc)));
            return 1;
        }

        // ESC + key is how terminals send Alt + key.
        output.Add(Key(SingleChar(next) with { Alt = true }));
        return 2;
    }

    // ── CSI ───────────────────────────────────────────────────────────────────

    private int DecodeCsi(string s, int i, bool final, List<InputEvent> output)
    {
        int j = i + 2;

        // Legacy X10 mouse report: CSI M Cb Cx Cy, three raw bytes offset by 32. Only sent
        // by terminals that honour ?1000 but not the SGR encoding (?1006) asked for as well.
        if (j < s.Length && s[j] == 'M')
        {
            if (j + 3 < s.Length)
            {
                int cb = s[j + 1] - 32;
                // X10 reports every release as button 3, without saying which was released.
                var mouse = Mouse(cb, s[j + 2] - 32, s[j + 3] - 32, isPress: (cb & 3) != 3);
                if (mouse is not null) output.Add(mouse);
                return j + 4 - i;
            }
            if (!final) return 0;
            return AltFallback('[', output);
        }

        while (j < s.Length)
        {
            char ch = s[j];
            if (ch >= '\x40' && ch <= '\x7e') break;            // final byte
            if (ch < '\x20' || ch > '\x3f' || j - i > MaxCsiLength)
                return AltFallback('[', output);               // not a CSI after all
            j++;
        }

        if (j >= s.Length)
            return final ? AltFallback('[', output) : 0;

        HandleCsi(s.Substring(i + 2, j - (i + 2)), s[j], output);
        return j - i + 1;
    }

    private void HandleCsi(string body, char fin, List<InputEvent> output)
    {
        char marker = body.Length > 0 && body[0] is '<' or '=' or '>' or '?' ? body[0] : '\0';
        string p = marker == '\0' ? body : body[1..];

        // Intermediate bytes (0x20–0x2F) mark sequences no key produces.
        foreach (char ch in p)
            if (ch < '0') return;

        switch (marker)
        {
            case '<':
                if (fin is 'M' or 'm') AddSgrMouse(p, fin == 'M', output);
                return;
            case '?':
                if (fin == 'c') output.Add(new DeviceAttributesEvent(p));
                return; // 'u' is the Kitty keyboard flags reply: nothing to deliver
            case '\0':
                break;
            default:
                return;
        }

        var fields = p.Split(';');
        switch (fin)
        {
            case 'u':
                DecodeKittyKey(fields, output);
                return;
            case '~':
                DecodeTilde(fields, output);
                return;
            case 'A' or 'B' or 'C' or 'D' or 'H' or 'F' or 'E' or 'P' or 'Q' or 'R' or 'S' or 'Z':
                DecodeLetterKey(fin, fields, output);
                return;
            // 'I' / 'O' are focus reports, 't' window reports: not requested, dropped.
        }
    }

    private static void DecodeLetterKey(char fin, string[] fields, List<InputEvent> output)
    {
        // A cursor position report (CSI row ; col R) shares its final byte with modified
        // F3 (CSI 1 ; mods R). Anything that reads the cursor position — Console.CursorLeft
        // in app code, say — makes the terminal send one, and decoded as a key it would be
        // Ctrl+Alt+Shift+F3 or worse. A key report always has 1 in the first field.
        if (fin == 'R' && fields.Length >= 2 && fields[0] is not ("" or "1")) return;

        var (mods, evt) = Modifiers(fields, 1);
        if (evt == 3) return; // release

        ConsoleKey key = fin switch
        {
            'A' => ConsoleKey.UpArrow,
            'B' => ConsoleKey.DownArrow,
            'C' => ConsoleKey.RightArrow,
            'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home,
            'F' => ConsoleKey.End,
            'E' => ConsoleKey.Clear,     // keypad 5 with Num Lock off
            'P' => ConsoleKey.F1,
            'Q' => ConsoleKey.F2,
            'R' => ConsoleKey.F3,
            'S' => ConsoleKey.F4,
            _   => ConsoleKey.Tab,       // 'Z': back-tab
        };

        if (fin == 'Z')
        {
            mods |= ModShift;
            output.Add(Key(WithMods(ConsoleKey.Tab, '\t', mods)));
            return;
        }

        output.Add(Key(WithMods(key, null, mods)));
    }

    private void DecodeTilde(string[] fields, List<InputEvent> output)
    {
        if (!int.TryParse(FirstSubfield(fields[0]), out int code)) return;

        if (code == 200) { _paste = new StringBuilder(); return; }
        if (code == 201) return; // stray end-of-paste

        // xterm modifyOtherKeys: CSI 27 ; modifiers ; codepoint ~
        if (code == 27 && fields.Length >= 3)
        {
            if (!int.TryParse(fields[2], out int cp)) return;
            var (m, _) = Modifiers(fields, 1);
            AddCodepointKey(cp, shifted: null, m, text: null, output);
            return;
        }

        var (mods, evt) = Modifiers(fields, 1);
        if (evt == 3) return;

        ConsoleKey? key = code switch
        {
            1 or 7 => ConsoleKey.Home,
            2  => ConsoleKey.Insert,
            3  => ConsoleKey.Delete,
            4 or 8 => ConsoleKey.End,
            5  => ConsoleKey.PageUp,
            6  => ConsoleKey.PageDown,
            11 => ConsoleKey.F1,
            12 => ConsoleKey.F2,
            13 => ConsoleKey.F3,
            14 => ConsoleKey.F4,
            15 => ConsoleKey.F5,
            17 => ConsoleKey.F6,
            18 => ConsoleKey.F7,
            19 => ConsoleKey.F8,
            20 => ConsoleKey.F9,
            21 => ConsoleKey.F10,
            23 => ConsoleKey.F11,
            24 => ConsoleKey.F12,
            25 => ConsoleKey.F13,
            26 => ConsoleKey.F14,
            28 => ConsoleKey.F15,
            29 => ConsoleKey.F16,
            31 => ConsoleKey.F17,
            32 => ConsoleKey.F18,
            33 => ConsoleKey.F19,
            34 => ConsoleKey.F20,
            _  => null,
        };

        if (key is { } k) output.Add(Key(WithMods(k, null, mods)));
    }

    // ── Kitty keyboard protocol ───────────────────────────────────────────────

    /// <summary>
    /// <c>CSI code[:shifted[:base]] [; modifiers[:event]] [; text] u</c>.
    /// </summary>
    private static void DecodeKittyKey(string[] fields, List<InputEvent> output)
    {
        var codes = fields[0].Split(':');
        if (!int.TryParse(codes[0], out int code)) return;
        int? shifted = codes.Length > 1 && int.TryParse(codes[1], out int sh) ? sh : null;

        var (mods, evt) = Modifiers(fields, 1);
        if (evt == 3) return; // release: KeyMsg has no notion of it

        string? text = null;
        if (fields.Length > 2 && fields[2].Length > 0)
        {
            var sb = new StringBuilder();
            foreach (var part in fields[2].Split(':'))
                if (int.TryParse(part, out int cp) && IsScalar(cp))
                    sb.Append(char.ConvertFromUtf32(cp));
            text = sb.Length > 0 ? sb.ToString() : null;
        }

        AddCodepointKey(code, shifted, mods, text, output);
    }

    private static void AddCodepointKey(int code, int? shifted, int mods, string? text, List<InputEvent> output)
    {
        // Keys with fixed legacy meanings.
        switch (code)
        {
            case 9:   output.Add(Key(WithMods(ConsoleKey.Tab, '\t', mods)));       return;
            case 13:  output.Add(Key(WithMods(ConsoleKey.Enter, '\r', mods)));     return;
            case 27:  output.Add(Key(WithMods(ConsoleKey.Escape, Esc, mods)));     return;
            case 8 or 127:
                output.Add(Key(WithMods(ConsoleKey.Backspace, '\b', mods)));       return;
        }

        if (FunctionalKey(code) is { } fn)
        {
            if (fn.Key is { } fk) output.Add(Key(WithMods(fk, fn.Char, mods)));
            return; // modifier-only keys and unmapped functional keys are dropped
        }

        if (code < 32 || !IsScalar(code) || (code >= 0xE000 && code <= 0xF8FF)) return;

        string glyph = char.ConvertFromUtf32(code);
        char baseChar = glyph[0];
        ConsoleKey key = glyph.Length == 1 ? SingleChar(char.ToLowerInvariant(baseChar)).Key : default;

        bool ctrl  = (mods & ModCtrl) != 0;
        bool shift = (mods & ModShift) != 0;
        bool superish = (mods & (ModSuper | ModHyper)) != 0;

        if (text is not null)
        {
            foreach (char ch in text)
                output.Add(Key(WithMods(key, ch, mods)));
            return;
        }

        char? character;
        if (superish)
            character = null;  // a Super/Hyper chord is a shortcut, never text
        else if (ctrl)
            // Match the legacy encoding: Ctrl+letter carries its control character.
            character = baseChar is >= 'a' and <= 'z' ? (char)(baseChar - 'a' + 1) : null;
        else if (shift && shifted is { } s && IsScalar(s) && s <= 0xFFFF)
            character = (char)s;
        else if (shift && char.IsLetter(baseChar))
            character = char.ToUpperInvariant(baseChar);
        else if (glyph.Length == 2)
        {
            // Astral character with no modifiers that suppress text: deliver both halves,
            // which is how the legacy path delivers it too.
            output.Add(Key(WithMods(default, glyph[0], mods)));
            output.Add(Key(WithMods(default, glyph[1], mods)));
            return;
        }
        else
            character = baseChar;

        output.Add(Key(WithMods(key, character, mods)));
    }

    /// <summary>Kitty's Private Use Area key codes. Null when the code is not one of them.</summary>
    private static (ConsoleKey? Key, char? Char)? FunctionalKey(int code)
    {
        if (code is >= 57399 and <= 57408)
            return (ConsoleKey.NumPad0 + (code - 57399), (char)('0' + (code - 57399)));
        if (code is >= 57376 and <= 57387)
            return (ConsoleKey.F13 + (code - 57376), null);

        return code switch
        {
            57409 => (ConsoleKey.Decimal, '.'),
            57410 => (ConsoleKey.Divide, '/'),
            57411 => (ConsoleKey.Multiply, '*'),
            57412 => (ConsoleKey.Subtract, '-'),
            57413 => (ConsoleKey.Add, '+'),
            57414 => (ConsoleKey.Enter, '\r'),
            57415 => (default(ConsoleKey), '='),
            57416 => (ConsoleKey.Separator, ','),
            57417 => (ConsoleKey.LeftArrow, null),
            57418 => (ConsoleKey.RightArrow, null),
            57419 => (ConsoleKey.UpArrow, null),
            57420 => (ConsoleKey.DownArrow, null),
            57421 => (ConsoleKey.PageUp, null),
            57422 => (ConsoleKey.PageDown, null),
            57423 => (ConsoleKey.Home, null),
            57424 => (ConsoleKey.End, null),
            57425 => (ConsoleKey.Insert, null),
            57426 => (ConsoleKey.Delete, null),
            57427 => (ConsoleKey.Clear, null),
            57361 => (ConsoleKey.PrintScreen, null),
            57362 => (ConsoleKey.Pause, null),
            57363 => (ConsoleKey.Applications, null),
            57428 or 57430 => (ConsoleKey.MediaPlay, null),
            57432 => (ConsoleKey.MediaStop, null),
            57435 => (ConsoleKey.MediaNext, null),
            57436 => (ConsoleKey.MediaPrevious, null),
            57438 => (ConsoleKey.VolumeDown, null),
            57439 => (ConsoleKey.VolumeUp, null),
            57440 => (ConsoleKey.VolumeMute, null),
            // Lock keys, modifier keys, F25+ and the remaining media keys have no
            // ConsoleKey; recognise them so they are dropped rather than typed.
            >= 57344 and <= 57454 => (null, null),
            _ => null,
        };
    }

    // ── SS3 ───────────────────────────────────────────────────────────────────

    private static int DecodeSs3(string s, int i, bool final, List<InputEvent> output)
    {
        if (i + 2 >= s.Length)
            return final ? AltFallback('O', output) : 0;

        (ConsoleKey Key, char? Char)? k = s[i + 2] switch
        {
            'A' => (ConsoleKey.UpArrow, null),
            'B' => (ConsoleKey.DownArrow, null),
            'C' => (ConsoleKey.RightArrow, null),
            'D' => (ConsoleKey.LeftArrow, null),
            'H' => (ConsoleKey.Home, null),
            'F' => (ConsoleKey.End, null),
            'E' => (ConsoleKey.Clear, null),
            'P' => (ConsoleKey.F1, null),
            'Q' => (ConsoleKey.F2, null),
            'R' => (ConsoleKey.F3, null),
            'S' => (ConsoleKey.F4, null),
            // Keypad in application mode.
            'M' => (ConsoleKey.Enter, '\r'),
            'j' => (ConsoleKey.Multiply, '*'),
            'k' => (ConsoleKey.Add, '+'),
            'l' => (ConsoleKey.Separator, ','),
            'm' => (ConsoleKey.Subtract, '-'),
            'n' => (ConsoleKey.Decimal, '.'),
            'o' => (ConsoleKey.Divide, '/'),
            'X' => (default(ConsoleKey), '='),
            >= 'p' and <= 'y' => (ConsoleKey.NumPad0 + (s[i + 2] - 'p'), (char)('0' + (s[i + 2] - 'p'))),
            _ => null,
        };

        if (k is not { } key)
            return AltFallback('O', output); // Alt+Shift+O, then decode what followed

        output.Add(Key(new KeyMsg(key.Key, key.Char)));
        return 3;
    }

    // ── APC (Kitty graphics replies) ──────────────────────────────────────────

    private static int DecodeApc(string s, int i, bool final, List<InputEvent> output)
    {
        if (i + 2 >= s.Length)
            return final ? AltFallback('_', output) : 0;

        // Only graphics replies are expected; anything else is the user pressing Alt+_.
        if (s[i + 2] != 'G')
            return AltFallback('_', output);

        for (int j = i + 3; j < s.Length; j++)
        {
            if (j - i > MaxApcLength) return AltFallback('_', output);

            char ch = s[j];
            if (ch == '\a')
            {
                AddGraphicsReply(s.Substring(i + 3, j - (i + 3)), output);
                return j + 1 - i;
            }
            if (ch == Esc)
            {
                if (j + 1 >= s.Length) break; // ST split across reads
                if (s[j + 1] != '\\') return AltFallback('_', output);
                AddGraphicsReply(s.Substring(i + 3, j - (i + 3)), output);
                return j + 2 - i;
            }
        }

        return final ? AltFallback('_', output) : 0;
    }

    private static void AddGraphicsReply(string content, List<InputEvent> output)
    {
        // "i=31,p=4;OK" — control keys, then the message after the first ';'.
        int semi = content.IndexOf(';');
        string keys    = semi < 0 ? content : content[..semi];
        string message = semi < 0 ? ""      : content[(semi + 1)..];

        uint? imageId = null;
        foreach (var kv in keys.Split(','))
            if (kv.StartsWith("i=", StringComparison.Ordinal) && uint.TryParse(kv.AsSpan(2), out uint id))
                imageId = id;

        output.Add(new KittyGraphicsReplyEvent(imageId, message));
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────

    private static void AddSgrMouse(string p, bool isPress, List<InputEvent> output)
    {
        // "Cb;Cx;Cy" — button code, then 1-based column and row.
        var parts = p.Split(';');
        if (parts.Length != 3) return;
        if (!int.TryParse(parts[0], out int cb)) return;
        if (!int.TryParse(parts[1], out int cx)) return;
        if (!int.TryParse(parts[2], out int cy)) return;

        var ev = Mouse(cb, cx, cy, isPress);
        if (ev is not null) output.Add(ev);
    }

    private static MouseInputEvent? Mouse(int cb, int cx, int cy, bool isPress)
    {
        if (cx < 1 || cy < 1) return null;

        bool shift  = (cb & 4)  != 0;
        bool alt    = (cb & 8)  != 0;
        bool ctrl   = (cb & 16) != 0;
        bool motion = (cb & 32) != 0;
        int  btn    = cb & ~(4 | 8 | 16 | 32);

        var button = btn switch
        {
            0  => MouseButton.Left,
            1  => MouseButton.Middle,
            2  => MouseButton.Right,
            64 => MouseButton.ScrollUp,
            65 => MouseButton.ScrollDown,
            _  => MouseButton.None,
        };

        var action = btn is 64 or 65
            ? MouseAction.Press                  // a wheel notch is always a press
            : motion ? MouseAction.Move
            : isPress ? MouseAction.Press
            : MouseAction.Release;

        // Terminal coordinates are 1-based; MouseMsg is 0-based.
        return new MouseInputEvent(new MouseMsg(button, action, cx - 1, cy - 1, shift, alt, ctrl));
    }

    // ── Single characters ─────────────────────────────────────────────────────

    /// <summary>
    /// Map one character to a key, matching what <c>Console.ReadKey</c> reports for the
    /// same byte on Unix: letters and digits get their <see cref="ConsoleKey"/>, control
    /// characters 1–26 are Ctrl+letter, and punctuation keeps a default key with the
    /// character — bind it with <see cref="KeyPattern.OfChar"/>.
    /// </summary>
    internal static KeyMsg SingleChar(char c) => c switch
    {
        '\b'   => new KeyMsg(ConsoleKey.Backspace, '\b'),
        '\x7f' => new KeyMsg(ConsoleKey.Backspace, '\b'),
        '\t'   => new KeyMsg(ConsoleKey.Tab, '\t'),
        '\r' or '\n' => new KeyMsg(ConsoleKey.Enter, c),
        ' '    => new KeyMsg(ConsoleKey.Spacebar, ' '),
        Esc    => new KeyMsg(ConsoleKey.Escape, Esc),
        '*'    => new KeyMsg(ConsoleKey.Multiply, c),
        '/'    => new KeyMsg(ConsoleKey.Divide, c),
        '-'    => new KeyMsg(ConsoleKey.Subtract, c),
        '+'    => new KeyMsg(ConsoleKey.Add, c),
        ','    => new KeyMsg(ConsoleKey.OemComma, c),
        '.'    => new KeyMsg(ConsoleKey.OemPeriod, c),
        >= 'a' and <= 'z' => new KeyMsg(ConsoleKey.A + (c - 'a'), c),
        >= 'A' and <= 'Z' => new KeyMsg(ConsoleKey.A + (c - 'A'), c, Shift: true),
        >= '0' and <= '9' => new KeyMsg(ConsoleKey.D0 + (c - '0'), c),
        // Ctrl+A … Ctrl+Z, minus the four that are Backspace, Tab, Enter above.
        >= '\x01' and <= '\x1a' => new KeyMsg(ConsoleKey.A + (c - 1), c, Ctrl: true),
        // Ctrl+4 … Ctrl+7 (and Ctrl+\ ] ^ _ on most layouts).
        >= '\x1c' and <= '\x1f' => new KeyMsg(ConsoleKey.D4 + (c - '\x1c'), null, Ctrl: true),
        '\0'   => new KeyMsg(ConsoleKey.D2, null, Ctrl: true), // Ctrl+Space / Ctrl+@
        _      => new KeyMsg(default, c),
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static KeyInputEvent Key(KeyMsg msg) => new(msg);

    private static KeyMsg WithMods(ConsoleKey key, char? ch, int mods) => new(
        key, ch,
        Shift: (mods & ModShift) != 0,
        Alt:   (mods & (ModAlt | ModMeta)) != 0,
        Ctrl:  (mods & ModCtrl) != 0);

    /// <summary>Emit Alt + <paramref name="c"/> for an <c>ESC c</c> that turned out not to open a sequence.</summary>
    private static int AltFallback(char c, List<InputEvent> output)
    {
        output.Add(Key(SingleChar(c) with { Alt = true }));
        return 2;
    }

    /// <summary>Modifier bits and event type from field <paramref name="index"/> ("5" or "5:3").</summary>
    private static (int Mods, int Event) Modifiers(string[] fields, int index)
    {
        if (index >= fields.Length || fields[index].Length == 0) return (0, 1);
        var parts = fields[index].Split(':');
        int mods = int.TryParse(parts[0], out int v) && v >= 1 ? v - 1 : 0;
        int evt  = parts.Length > 1 && int.TryParse(parts[1], out int e) ? e : 1;
        return (mods, evt);
    }

    private static string FirstSubfield(string field)
    {
        int colon = field.IndexOf(':');
        return colon < 0 ? field : field[..colon];
    }

    private static bool IsScalar(int cp) => cp is >= 0 and <= 0x10FFFF && !(cp is >= 0xD800 and <= 0xDFFF);

    /// <summary>Length of the longest suffix of <c>s[start..]</c> that is a proper prefix of <paramref name="token"/>.</summary>
    private static int PartialSuffix(string s, int start, string token)
    {
        int max = Math.Min(token.Length - 1, s.Length - start);
        for (int len = max; len > 0; len--)
            if (string.CompareOrdinal(s, s.Length - len, token, 0, len) == 0)
                return len;
        return 0;
    }

    /// <summary>Terminals send pasted line breaks as CR; models expect <c>\n</c>.</summary>
    private static string NormaliseNewlines(string text) =>
        text.Contains('\r') ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;
}

/// <summary>A reply to a Kitty graphics command: <c>ESC _ G i=&lt;id&gt; ; &lt;message&gt; ESC \</c>.</summary>
/// <param name="ImageId">The <c>i=</c> key of the reply, when present.</param>
/// <param name="Message"><c>OK</c>, or an error such as <c>ENOENT:…</c>.</param>
internal sealed record KittyGraphicsReplyEvent(uint? ImageId, string Message) : InputEvent;

/// <summary>A primary device attributes reply (<c>CSI ? … c</c>), the answer to <c>CSI c</c>.</summary>
/// <param name="Attributes">The parameter string, e.g. <c>62;22</c>.</param>
internal sealed record DeviceAttributesEvent(string Attributes) : InputEvent;
