using System.Reactive.Subjects;
using ConsoleForge.Core;
using ConsoleForge.Terminal;

namespace ConsoleForge.Testing;

/// <summary>
/// In-memory ITerminal for use in unit and integration tests.
/// Captures all Write() calls into a 2D char buffer.
/// Allows synthetic key events to be injected.
/// </summary>
public sealed class VirtualTerminal : ITerminal
{
    private readonly Subject<InputEvent> _input = new();
    private readonly List<string> _writeHistory = new();
    private readonly char[,] _screen;
    private bool _rawMode;
    private bool _alternateScreen;
    private bool _exitedCleanly;
    private bool _disposed;

    public VirtualTerminal(int width = 80, int height = 24)
    {
        Width = width;
        Height = height;
        _screen = new char[height, width];
        for (var r = 0; r < height; r++)
            for (var c = 0; c < width; c++)
                _screen[r, c] = ' ';
    }

    // ── ITerminal: Dimensions ─────────────────────────────────────────
    public int Width { get; private set; }
    public int Height { get; private set; }

    // ── ITerminal: Output ─────────────────────────────────────────────
    public void Write(string ansiText)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _writeHistory.Add(ansiText);
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (var r = 0; r < Height; r++)
            for (var c = 0; c < Width; c++)
                _screen[r, c] = ' ';
    }

    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // No-op for virtual terminal — writes are captured immediately.
    }

    // ── ITerminal: Cursor ─────────────────────────────────────────────
    public void SetCursorVisible(bool visible) { }
    public void SetCursorPosition(int col, int row) { }

    // ── ITerminal: Title ──────────────────────────────────────────────
    public void SetTitle(string title) { }

    // ── ITerminal: Screen mode ────────────────────────────────────────
    public void EnterAlternateScreen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _alternateScreen = true;
    }

    public void ExitAlternateScreen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _alternateScreen = false;
    }

    // ── ITerminal: Input mode ─────────────────────────────────────────
    public void EnterRawMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _rawMode = true;
    }

    public void ExitRawMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _rawMode = false;
    }

    // ── ITerminal: Input stream ───────────────────────────────────────
    public IObservable<InputEvent> Input => _input;

    // ── ITerminal: Resize events ──────────────────────────────────────
    public event EventHandler<TerminalResizedEventArgs>? Resized;

    // ── IDisposable ───────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Capture states before exiting so ExitedCleanly reflects whether
        // cleanup was needed (i.e., we were actually in those modes).
        bool wasRaw = _rawMode;
        bool wasAlt = _alternateScreen;

        if (_rawMode) ExitRawMode();
        if (_alternateScreen) ExitAlternateScreen();

        _exitedCleanly = wasRaw || wasAlt; // true if cleanup was performed
        _input.OnCompleted();
    }

    // ── Test inspection surface ───────────────────────────────────────

    /// <summary>The current rendered screen as an array of row strings.</summary>
    public string[] Lines
    {
        get
        {
            var lines = new string[Height];
            for (var r = 0; r < Height; r++)
            {
                var chars = new char[Width];
                for (var c = 0; c < Width; c++)
                    chars[c] = _screen[r, c];
                lines[r] = new string(chars);
            }
            return lines;
        }
    }

    /// <summary>True if ExitRawMode + ExitAlternateScreen were called on Dispose.</summary>
    public bool ExitedCleanly => _exitedCleanly;

    /// <summary>
    /// True if any ANSI escape sequences remain in the write history that suggest
    /// incomplete cleanup (e.g., alternate screen not exited).
    /// </summary>
    public bool HasArtifacts => _writeHistory.Any(w => w.Contains("\x1b[?1049h") && !_writeHistory.Any(w2 => w2.Contains("\x1b[?1049l")));

    /// <summary>History of all Write() calls, in order.</summary>
    public IReadOnlyList<string> WriteHistory => _writeHistory.AsReadOnly();

    // ── Test injection surface ────────────────────────────────────────

    /// <summary>Enqueue a synthetic key event to be delivered to the Input observable.</summary>
    public void EnqueueKey(KeyMsg key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.OnNext(new KeyInputEvent(key));
    }

    /// <summary>Trigger a synthetic resize event.</summary>
    public void SimulateResize(int newWidth, int newHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Width = newWidth;
        Height = newHeight;
        _input.OnNext(new ResizeInputEvent(newWidth, newHeight));
        Resized?.Invoke(this, new TerminalResizedEventArgs(newWidth, newHeight));
    }
}
