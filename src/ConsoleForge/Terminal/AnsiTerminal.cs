using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using ConsoleForge.Core;

namespace ConsoleForge.Terminal;

/// <summary>
/// Production <see cref="ITerminal"/> backed by <see cref="System.Console"/>.
/// Handles ANSI output, raw mode (Unix/Windows), alternate screen, and resize events.
/// On Unix: raw mode via termios, resize via SIGWINCH.
/// On Windows: raw mode via kernel32 console mode, resize via background polling thread.
/// </summary>
public sealed class AnsiTerminal : ITerminal
{
    private readonly Subject<InputEvent> _inputSubject = new();
    private readonly StringBuilder _outputBuffer = new();
    private Thread? _inputThread;
    private PosixSignalRegistration? _sigwinchRegistration;

    // Windows resize polling
    private Thread? _resizePollingThread;
    private volatile bool _stopResizePolling;

#if !WINDOWS
    private Termios.Termios_t _savedTermios;
    private bool _rawMode;
#endif

    private bool _disposed;

    private bool _mouseEnabled;

    /// <inheritdoc/>
    public int Width => Console.WindowWidth;
    /// <inheritdoc/>
    public int Height => Console.WindowHeight;

    public IObservable<InputEvent> Input => _inputSubject;
    /// <summary>Raised when the terminal window is resized (SIGWINCH on Unix).</summary>
    public event EventHandler<TerminalResizedEventArgs>? Resized;

    // ── Output ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Write(string ansiText)
    {
        _outputBuffer.Append(ansiText);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _outputBuffer.Clear();
    }

    /// <inheritdoc/>
    public void Flush()
    {
        if (_outputBuffer.Length == 0) return;
        Console.Write(_outputBuffer);
        Console.Out.Flush();
        _outputBuffer.Clear();
    }

    // ── Cursor ───────────────────────────────────────────────────────

    /// <summary>Shows or hides the hardware cursor by writing directly to the terminal (bypasses the render buffer).</summary>
    public void SetCursorVisible(bool visible)
    {
        Console.Write(visible ? "\x1b[?25h" : "\x1b[?25l");
        Console.Out.Flush();
    }

    /// <summary>Appends an ANSI cursor-position escape sequence (converts 0-based col/row to 1-based ANSI).</summary>
    public void SetCursorPosition(int col, int row)
    {
        // ANSI cursor position is 1-indexed
        _outputBuffer.Append($"\x1b[{row + 1};{col + 1}H");
    }

    // ── Title ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void SetTitle(string title)
    {
        _outputBuffer.Append($"\x1b]0;{title}\x07");
    }

    // ── Screen mode ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public void EnterAlternateScreen()
    {
        Console.Write("\x1b[?1049h");
        Console.Out.Flush();
    }

    /// <inheritdoc/>
    public void ExitAlternateScreen()
    {
        Console.Write("\x1b[?1049l");
        Console.Out.Flush();
    }

    // ── Input mode ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public void EnterRawMode()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsConsole.TryEnableRawMode();
        }
#if !WINDOWS
        else
        {
            if (_rawMode) return;
            if (Termios.GetAttr(out _savedTermios))
            {
                var raw = _savedTermios;
                if (Termios.MakeRaw(ref raw))
                    _rawMode = true;
            }
        }
#endif
        StartInputThread();
        RegisterSigwinch();
    }

    /// <inheritdoc/>
    public void ExitRawMode()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsConsole.TryRestoreMode();
        }
#if !WINDOWS
        else
        {
            if (!_rawMode) return;
            Termios.Restore(ref _savedTermios);
            _rawMode = false;
        }
#endif
    }

    // ── Mouse ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void EnableMouse(MouseMode mode = MouseMode.ButtonEvents)
    {
        if (_mouseEnabled) return;
        _mouseEnabled = true;
        // SGR extended mouse (1006) gives unlimited col/row range.
        // Basic tracking (1000) is the compatibility fallback.
        var seq = mode == MouseMode.AllMotion
            ? "\x1b[?1003h\x1b[?1006h"   // all-motion + SGR
            : "\x1b[?1000h\x1b[?1006h";  // button-events + SGR
        Console.Write(seq);
        Console.Out.Flush();
    }

    /// <inheritdoc/>
    public void DisableMouse()
    {
        if (!_mouseEnabled) return;
        _mouseEnabled = false;
        Console.Write("\x1b[?1003l\x1b[?1000l\x1b[?1006l");
        Console.Out.Flush();
    }

    // ── Input thread ─────────────────────────────────────────────────

    private void StartInputThread()
    {
        _inputThread = new Thread(ReadInputLoop)
        {
            IsBackground = true,
            Name = "ConsoleForge.Input"
        };
        _inputThread.Start();
    }

    private void ReadInputLoop()
    {
        // Use Console.ReadKey() exclusively on ALL platforms.
        // Console.In.Read() and Console.ReadKey() use separate internal buffers
        // on .NET/Unix — mixing them causes stdin bytes to be silently dropped.
        while (!_disposed)
        {
            ConsoleKeyInfo info;
            try
            {
                info = Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                // Console.In was redirected or closed — stop reading
                break;
            }
            catch (Exception)
            {
                if (_disposed) break;
                Thread.Sleep(1);
                continue;
            }

            InputEvent? ev = info.Key == ConsoleKey.Escape
                ? ParseEscapeViaReadKey()
                : new KeyInputEvent(new KeyMsg(
                    info.Key,
                    info.KeyChar == '\0' ? null : info.KeyChar,
                    (info.Modifiers & ConsoleModifiers.Shift)   != 0,
                    (info.Modifiers & ConsoleModifiers.Alt)     != 0,
                    (info.Modifiers & ConsoleModifiers.Control) != 0));

            if (ev is not null)
                _inputSubject.OnNext(ev);
        }
    }

    /// <summary>
    /// Called after Console.ReadKey() returned Escape.
    /// Uses Console.KeyAvailable to detect whether a CSI / SS3 / mouse sequence
    /// follows, then reads continuation bytes with further Console.ReadKey() calls.
    /// Both paths use the same Console.ReadKey() buffer — no stream mixing.
    /// </summary>
    private InputEvent? ParseEscapeViaReadKey()
    {
        // Escape sequences from the terminal arrive as a burst; check immediately.
        bool hasMore = false;
        for (int i = 0; i < 20 && !_disposed; i++)
        {
            try { if (Console.KeyAvailable) { hasMore = true; break; } }
            catch { break; }
            Thread.SpinWait(200);
        }

        if (!hasMore)
            return new KeyInputEvent(new KeyMsg(ConsoleKey.Escape, '\x1b'));

        ConsoleKeyInfo next;
        try   { next = Console.ReadKey(intercept: true); }
        catch { return new KeyInputEvent(new KeyMsg(ConsoleKey.Escape, '\x1b')); }

        return next.KeyChar switch
        {
            '[' => ParseCsiViaReadKey(),
            'O' => ParseSs3ViaReadKey(),
            _   => AltKey(MakeKeyEvent(next)),
        };
    }

    /// <summary>Parse a CSI sequence reading continuation bytes via Console.ReadKey().</summary>
    private InputEvent? ParseCsiViaReadKey()
    {
        var sb = new System.Text.StringBuilder(16);
        while (!_disposed)
        {
            ConsoleKeyInfo c;
            try   { c = Console.ReadKey(intercept: true); }
            catch { return null; }

            char ch = c.KeyChar;
            if (ch == '\0') return null; // non-printable without char mapping — abort

            sb.Append(ch);
            if (ch >= '@' && ch <= '~') break; // final byte (0x40–0x7E)
            if (sb.Length > 48) return null;   // safety limit
        }

        if (sb.Length == 0) return null;
        var seq   = sb.ToString();
        var final = seq[^1];
        var param = seq[..^1];

        // ── SGR mouse: ESC [ < Cb ; Cx ; Cy M/m ────────────────────────────
        if (param.StartsWith('<') && (final == 'M' || final == 'm'))
            return ParseSgrMouse(param[1..], final == 'M');

        // ── Modifier-bearing: ESC [ 1 ; n X ──────────────────────────────
        if (param.Contains(';'))
        {
            var parts   = param.Split(';');
            int modBits = parts.Length >= 2 && int.TryParse(parts[^1], out var m) ? m - 1 : 0;
            bool shift  = (modBits & 1) != 0;
            bool alt    = (modBits & 2) != 0;
            bool ctrl   = (modBits & 4) != 0;

            return final switch
            {
                'A' => Key(ConsoleKey.UpArrow,    null, shift, alt, ctrl),
                'B' => Key(ConsoleKey.DownArrow,  null, shift, alt, ctrl),
                'C' => Key(ConsoleKey.RightArrow, null, shift, alt, ctrl),
                'D' => Key(ConsoleKey.LeftArrow,  null, shift, alt, ctrl),
                'H' => Key(ConsoleKey.Home,       null, shift, alt, ctrl),
                'F' => Key(ConsoleKey.End,        null, shift, alt, ctrl),
                _   => null,
            };
        }

        // ── Simple sequences ──────────────────────────────────────────────────
        return (param, final) switch
        {
            ("" or "1", 'A') => Key(ConsoleKey.UpArrow),
            ("" or "1", 'B') => Key(ConsoleKey.DownArrow),
            ("" or "1", 'C') => Key(ConsoleKey.RightArrow),
            ("" or "1", 'D') => Key(ConsoleKey.LeftArrow),
            ("" or "1", 'H') => Key(ConsoleKey.Home),
            ("" or "1", 'F') => Key(ConsoleKey.End),
            ("1",  '~') => Key(ConsoleKey.Home),
            ("2",  '~') => Key(ConsoleKey.Insert),
            ("3",  '~') => Key(ConsoleKey.Delete),
            ("4",  '~') => Key(ConsoleKey.End),
            ("5",  '~') => Key(ConsoleKey.PageUp),
            ("6",  '~') => Key(ConsoleKey.PageDown),
            ("7",  '~') => Key(ConsoleKey.Home),
            ("8",  '~') => Key(ConsoleKey.End),
            ("11", '~') => Key(ConsoleKey.F1),
            ("12", '~') => Key(ConsoleKey.F2),
            ("13", '~') => Key(ConsoleKey.F3),
            ("14", '~') => Key(ConsoleKey.F4),
            ("15", '~') => Key(ConsoleKey.F5),
            ("17", '~') => Key(ConsoleKey.F6),
            ("18", '~') => Key(ConsoleKey.F7),
            ("19", '~') => Key(ConsoleKey.F8),
            ("20", '~') => Key(ConsoleKey.F9),
            ("21", '~') => Key(ConsoleKey.F10),
            ("23", '~') => Key(ConsoleKey.F11),
            ("24", '~') => Key(ConsoleKey.F12),
            ("Z",  _  ) => Key(ConsoleKey.Tab, '\t', shift: true), // Shift+Tab
            _ => null,
        };
    }

    /// <summary>Parse an SS3 (ESC O) sequence via Console.ReadKey().</summary>
    private InputEvent? ParseSs3ViaReadKey()
    {
        ConsoleKeyInfo c;
        try   { c = Console.ReadKey(intercept: true); }
        catch { return null; }

        return c.KeyChar switch
        {
            'A' => Key(ConsoleKey.UpArrow),
            'B' => Key(ConsoleKey.DownArrow),
            'C' => Key(ConsoleKey.RightArrow),
            'D' => Key(ConsoleKey.LeftArrow),
            'H' => Key(ConsoleKey.Home),
            'F' => Key(ConsoleKey.End),
            'P' => Key(ConsoleKey.F1),
            'Q' => Key(ConsoleKey.F2),
            'R' => Key(ConsoleKey.F3),
            'S' => Key(ConsoleKey.F4),
            _   => null,
        };
    }

    /// <summary>Parse an SGR mouse sequence (after "ESC [ &lt;") into a MouseInputEvent.</summary>
    private static InputEvent? ParseSgrMouse(string param, bool isPress)
    {
        // Format: "Cb;Cx;Cy" where Cb=button, Cx=1-based col, Cy=1-based row
        var parts = param.Split(';');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], out var cb)) return null;
        if (!int.TryParse(parts[1], out var cx)) return null;
        if (!int.TryParse(parts[2], out var cy)) return null;

        // Modifier bits in the button code
        bool shift = (cb & 4)  != 0;
        bool alt   = (cb & 8)  != 0;
        bool ctrl  = (cb & 16) != 0;
        int  btn   = cb & ~(4 | 8 | 16 | 32); // strip modifier + motion bits
        bool motion = (cb & 32) != 0;

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
            ? MouseAction.Press                             // scroll wheel = always press
            : motion ? MouseAction.Move
            : isPress ? MouseAction.Press
            : MouseAction.Release;

        // Convert 1-based terminal coords to 0-based
        return new MouseInputEvent(new MouseMsg(button, action, cx - 1, cy - 1, shift, alt, ctrl));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static KeyInputEvent Key(
        ConsoleKey k, char? ch = null,
        bool shift = false, bool alt = false, bool ctrl = false)
        => new(new KeyMsg(k, ch, shift, alt, ctrl));

    private static InputEvent? AltKey(InputEvent? ev)
        => ev is KeyInputEvent ki ? new KeyInputEvent(ki.Key with { Alt = true }) : ev;

    private static KeyInputEvent MakeKeyEvent(ConsoleKeyInfo info)
        => new(new KeyMsg(
            info.Key,
            info.KeyChar == '\0' ? null : info.KeyChar,
            (info.Modifiers & ConsoleModifiers.Shift)   != 0,
            (info.Modifiers & ConsoleModifiers.Alt)     != 0,
            (info.Modifiers & ConsoleModifiers.Control) != 0));

    // ── SIGWINCH / resize polling ─────────────────────────────────────

    private void RegisterSigwinch()
    {
        if (OperatingSystem.IsWindows())
        {
            StartResizePolling();
            return;
        }

        try
        {
            _sigwinchRegistration = PosixSignalRegistration.Create(
                PosixSignal.SIGWINCH,
                _ =>
                {
                    var w = Console.WindowWidth;
                    var h = Console.WindowHeight;
                    _inputSubject.OnNext(new ResizeInputEvent(w, h));
                    Resized?.Invoke(this, new TerminalResizedEventArgs(w, h));
                });
        }
        catch
        {
            // Not available on all platforms — ignore
        }
    }

    /// <summary>
    /// Windows fallback: polls <see cref="Console.WindowWidth"/> /
    /// <see cref="Console.WindowHeight"/> every 100 ms and fires resize events on change.
    /// </summary>
    private void StartResizePolling()
    {
        var lastW = Console.WindowWidth;
        var lastH = Console.WindowHeight;

        _resizePollingThread = new Thread(() =>
        {
            while (!_stopResizePolling && !_disposed)
            {
                Thread.Sleep(100);
                try
                {
                    var w = Console.WindowWidth;
                    var h = Console.WindowHeight;
                    if (w != lastW || h != lastH)
                    {
                        lastW = w;
                        lastH = h;
                        _inputSubject.OnNext(new ResizeInputEvent(w, h));
                        Resized?.Invoke(this, new TerminalResizedEventArgs(w, h));
                    }
                }
                catch
                {
                    // Console may be unavailable during shutdown — stop quietly
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "ConsoleForge.ResizePoller"
        };
        _resizePollingThread.Start();
    }

    // ── IDisposable ──────────────────────────────────────────────────

    /// <summary>
    /// Restores raw mode and alternate screen, completes the input observable, and
    /// disposes the SIGWINCH registration. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stopResizePolling = true;

        try { DisableMouse(); } catch { /* ensure cleanup */ }
        try { ExitRawMode(); } catch { /* ensure cleanup */ }
        try { ExitAlternateScreen(); } catch { /* ensure cleanup */ }

        _sigwinchRegistration?.Dispose();
        _inputSubject.OnCompleted();
        _inputSubject.Dispose();
    }
}
