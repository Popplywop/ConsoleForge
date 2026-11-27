using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using ConsoleForge.Core;

namespace ConsoleForge.Terminal;

/// <summary>
/// Production <see cref="ITerminal"/> backed by <see cref="Console"/>.
/// Handles ANSI output, raw mode (Unix/Windows), alternate screen, and resize events.
/// On Unix: raw mode via termios, resize via SIGWINCH.
/// On Windows: raw mode via kernel32 console mode, resize via background polling thread.
/// <para>
/// Input is read as raw characters from stdin and decoded by the framework, not by
/// <c>Console.ReadKey</c>, on both platforms. That is what lets it understand modified
/// keys, the Kitty keyboard protocol, bracketed paste and SGR mouse reports everywhere,
/// and keep replies the terminal sends to queries from ever reaching the model as keys.
/// </para>
/// </summary>
public sealed class AnsiTerminal : ITerminal
{
    // Kitty keyboard protocol flags pushed while in raw mode: 1 (disambiguate escape
    // codes) + 4 (report alternate keys, so Alt+Shift+1 carries '!'). Key presses without
    // Ctrl/Alt/Super still arrive as plain text, so typing is unchanged.
    private const string KittyKeyboardPush = "\x1b[>5u";
    private const string KittyKeyboardPop  = "\x1b[<u";

    // How often an idle input loop wakes to notice Dispose.
    private const int IdlePollMs = 100;

    private readonly Subject<InputEvent> _inputSubject = new();
    private readonly StringBuilder _outputBuffer = new();
    private Thread? _inputThread;
    private PosixSignalRegistration? _sigwinchRegistration;

    // Windows resize polling
    private Thread? _resizePollingThread;
    private volatile bool _stopResizePolling;

#if !WINDOWS
    private byte[]? _savedTermios;
#endif
    private bool _rawMode;
    private int _inputGeneration;

    private volatile bool _disposed;

    private bool _mouseEnabled;
    private bool _pasteEnabled;

    // The Kitty keyboard flag stack is kept per screen, so the push has to be made, and
    // undone, separately on the main and the alternate screen.
    private bool _onAlternateScreen;
    private bool _keyboardPushedMain;
    private bool _keyboardPushedAlternate;

    /// <summary>
    /// Ask the terminal for the Kitty keyboard protocol while in raw mode. On by default.
    /// Terminals that support it report Escape without a timeout, and keys such as
    /// Ctrl+Enter, Shift+Enter, Ctrl+Tab, Ctrl+Backspace and Ctrl+I unambiguously; others
    /// ignore the request. Turn it off to receive only legacy encodings.
    /// <para>
    /// Setting it means constructing the terminal yourself and passing it to
    /// <see cref="Core.App.Run"/>, which then does not dispose it — dispose it yourself
    /// (<c>using var terminal = new AnsiTerminal { KittyKeyboard = false };</c>), or raw
    /// mode and the alternate screen outlive the app.
    /// </para>
    /// </summary>
    public bool KittyKeyboard { get; init; } = true;

    /// <inheritdoc/>
    public int Width => Console.WindowWidth;
    /// <inheritdoc/>
    public int Height => Console.WindowHeight;

    /// <inheritdoc/>
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
        // Direct write: the quit path calls this after the render loop has
        // stopped, so anything left in _outputBuffer would never be flushed.
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
        _onAlternateScreen = true;
        if (_rawMode) PushKeyboardFlags();
        Console.Out.Flush();
    }

    /// <inheritdoc/>
    public void ExitAlternateScreen()
    {
        PopKeyboardFlags();
        Console.Write("\x1b[?1049l");
        _onAlternateScreen = false;
        // Raw mode already ended while on the alternate screen: the main screen's push is
        // still on its stack, and this is the first chance to undo it.
        if (!_rawMode) PopKeyboardFlags();
        Console.Out.Flush();
    }

    // ── Input mode ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public void EnterRawMode()
    {
        if (_rawMode) return;

        if (OperatingSystem.IsWindows())
        {
            WindowsConsole.TryEnableRawMode();
        }
#if !WINDOWS
        else if (Termios.GetAttr() is { } saved && Termios.MakeRaw(saved))
        {
            _savedTermios = saved;
        }
#endif
        _rawMode = true;

        PushKeyboardFlags();
        Console.Out.Flush();

        StartInputThread();
        RegisterSigwinch();
    }

    /// <inheritdoc/>
    public void ExitRawMode()
    {
        if (!_rawMode) return;

        PopKeyboardFlags();
        Console.Out.Flush();
        Interlocked.Increment(ref _inputGeneration); // retire the reader

        if (OperatingSystem.IsWindows())
        {
            WindowsConsole.TryRestoreMode();
        }
#if !WINDOWS
        else if (_savedTermios is { } saved)
        {
            Termios.Restore(saved);
            _savedTermios = null;
        }
#endif
        _rawMode = false;
    }

    private void PushKeyboardFlags()
    {
        if (!KittyKeyboard) return;
        ref bool pushed = ref (_onAlternateScreen ? ref _keyboardPushedAlternate : ref _keyboardPushedMain);
        if (pushed) return;
        Console.Write(KittyKeyboardPush);
        pushed = true;
    }

    private void PopKeyboardFlags()
    {
        ref bool pushed = ref (_onAlternateScreen ? ref _keyboardPushedAlternate : ref _keyboardPushedMain);
        if (!pushed) return;
        Console.Write(KittyKeyboardPop);
        pushed = false;
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
        if (OperatingSystem.IsWindows()) WindowsConsole.SetMouseCapture(true);
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
        if (OperatingSystem.IsWindows()) WindowsConsole.SetMouseCapture(false);
    }

    // ── Paste ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void EnableBracketedPaste()
    {
        if (_pasteEnabled) return;
        _pasteEnabled = true;
        Console.Write("\x1b[?2004h");
        Console.Out.Flush();
    }

    /// <inheritdoc/>
    public void DisableBracketedPaste()
    {
        if (!_pasteEnabled) return;
        _pasteEnabled = false;
        Console.Write("\x1b[?2004l");
        Console.Out.Flush();
    }

    // ── Input thread ─────────────────────────────────────────────────

    private void StartInputThread()
    {
        // Each raw-mode session gets its own reader; ExitRawMode retires it by moving the
        // generation on, so stdin is never read while the terminal is back in cooked mode
        // (a child process run in between would otherwise lose keystrokes to it).
        int generation = Interlocked.Increment(ref _inputGeneration);
        _inputThread = new Thread(() => ReadInputLoop(generation))
        {
            IsBackground = true,
            Name = "ConsoleForge.Input"
        };
        _inputThread.Start();
    }

    private void ReadInputLoop(int generation)
    {
        var reader = StdinReader.Open();
        if (reader is null) return; // stdin redirected or not a console: no keyboard

        var decoder = new InputDecoder();
        var chars   = new char[StdinReader.MinBufferSize];
        var events  = new List<InputEvent>();

        while (!_disposed && generation == Volatile.Read(ref _inputGeneration))
        {
            int pending = decoder.PendingTimeoutMs;
            int n = reader.Read(chars, pending > 0 ? pending : IdlePollMs);

            if (n == StdinReader.Closed) break;
            if (n == 0)
            {
                // Quiet for the whole timeout: a held ESC is the Escape key after all.
                if (pending > 0) decoder.Flush(events);
            }
            else if (n > 0)
            {
                decoder.Feed(chars.AsSpan(0, n), events);
            }

            foreach (var ev in events)
            {
                // Replies to terminal queries are the framework's business; a model must
                // never see one as keys. Graphics errors go on to App, which acts on an
                // evicted image (ENOENT); OK replies and device attributes go nowhere.
                if (ev is DeviceAttributesEvent) continue;
                if (ev is KittyGraphicsReplyEvent { Message: "OK" }) continue;
                if (_disposed) break;
                _inputSubject.OnNext(ev);
            }
            events.Clear();
        }
    }

    // ── SIGWINCH / resize polling ─────────────────────────────────────

    private void RegisterSigwinch()
    {
        if (_sigwinchRegistration is not null || _resizePollingThread is not null) return;

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
        try { DisableBracketedPaste(); } catch { /* ensure cleanup */ }
        try { ExitRawMode(); } catch { /* ensure cleanup */ }
        try { ExitAlternateScreen(); } catch { /* ensure cleanup */ }
        // After ExitAlternateScreen: ?1049l restores saved cursor state on
        // some terminals (VTE, tmux), which would re-hide a cursor shown
        // while still on the alternate screen.
        try { SetCursorVisible(true); } catch { /* ensure cleanup */ }

        _sigwinchRegistration?.Dispose();
        _inputSubject.OnCompleted();
        _inputSubject.Dispose();
    }
}