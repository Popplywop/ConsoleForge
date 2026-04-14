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

    public int Width => Console.WindowWidth;
    public int Height => Console.WindowHeight;

    public IObservable<InputEvent> Input => _inputSubject;
    /// <summary>Raised when the terminal window is resized (SIGWINCH on Unix).</summary>
    public event EventHandler<TerminalResizedEventArgs>? Resized;

    // ── Output ───────────────────────────────────────────────────────

    public void Write(string ansiText)
    {
        _outputBuffer.Append(ansiText);
    }

    public void Clear()
    {
        _outputBuffer.Clear();
    }

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

    public void SetTitle(string title)
    {
        _outputBuffer.Append($"\x1b]0;{title}\x07");
    }

    // ── Screen mode ──────────────────────────────────────────────────

    public void EnterAlternateScreen()
    {
        Console.Write("\x1b[?1049h");
        Console.Out.Flush();
    }

    public void ExitAlternateScreen()
    {
        Console.Write("\x1b[?1049l");
        Console.Out.Flush();
    }

    // ── Input mode ───────────────────────────────────────────────────

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
        while (!_disposed)
        {
            try
            {
                var info = Console.ReadKey(intercept: true);
                var key = new KeyMsg(
                    info.Key,
                    info.KeyChar == '\0' ? null : info.KeyChar,
                    (info.Modifiers & ConsoleModifiers.Shift) != 0,
                    (info.Modifiers & ConsoleModifiers.Alt) != 0,
                    (info.Modifiers & ConsoleModifiers.Control) != 0);
                _inputSubject.OnNext(new KeyInputEvent(key));
            }
            catch (InvalidOperationException)
            {
                // Console.In was redirected or closed — stop reading
                break;
            }
            catch (Exception)
            {
                if (_disposed) break;
                // Transient error; continue
            }
        }
    }

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

        try { ExitRawMode(); } catch { /* ensure cleanup */ }
        try { ExitAlternateScreen(); } catch { /* ensure cleanup */ }

        _sigwinchRegistration?.Dispose();
        _inputSubject.OnCompleted();
        _inputSubject.Dispose();
    }
}
