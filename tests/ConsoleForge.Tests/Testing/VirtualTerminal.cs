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
    private int _cursorRow;
    private int _cursorCol;
    private bool _rawMode;
    private bool _alternateScreen;
    private bool _exitedCleanly;
    private bool _disposed;

    /// <summary>
    /// Initialises a virtual terminal with the given dimensions.
    /// The screen buffer is filled with spaces.
    /// </summary>
    /// <param name="width">Terminal width in columns. Default 80.</param>
    /// <param name="height">Terminal height in rows. Default 24.</param>
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
    /// <summary>Terminal width in columns. Updated by <see cref="SimulateResize"/>.</summary>
    public int Width { get; private set; }
    /// <summary>Terminal height in rows. Updated by <see cref="SimulateResize"/>.</summary>
    public int Height { get; private set; }

    // ── ITerminal: Output ─────────────────────────────────────────────
    /// <summary>
    /// Appends <paramref name="ansiText"/> to the write history and applies it to the
    /// internal screen buffer via the ANSI decoder.
    /// </summary>
    /// <param name="ansiText">Raw ANSI text (may contain escape sequences).</param>
    public void Write(string ansiText)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _writeHistory.Add(ansiText);
        ApplyAnsiToScreen(ansiText);
    }

    /// <summary>Clears the screen buffer, filling every cell with a space character.</summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (var r = 0; r < Height; r++)
            for (var c = 0; c < Width; c++)
                _screen[r, c] = ' ';
    }

    /// <summary>No-op for the virtual terminal — writes are applied immediately in <see cref="Write"/>.</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // No-op for virtual terminal — writes are captured immediately.
    }

    // ── ITerminal: Cursor ─────────────────────────────────────────────
    /// <summary>No-op — cursor visibility is not tracked by the virtual terminal.</summary>
    public void SetCursorVisible(bool visible) { }

    /// <summary>Updates the internal cursor position used by the ANSI decoder for subsequent writes.</summary>
    /// <param name="col">Zero-based column.</param>
    /// <param name="row">Zero-based row.</param>
    public void SetCursorPosition(int col, int row)
    {
        _cursorRow = row;
        _cursorCol = col;
    }

    // ── ITerminal: Title ──────────────────────────────────────────────
    /// <summary>No-op — window title changes are not tracked by the virtual terminal.</summary>
    public void SetTitle(string title) { }

    // ── ITerminal: Screen mode ────────────────────────────────────────
    /// <summary>Records that the alternate screen has been entered. Checked by <see cref="ExitedCleanly"/>.</summary>
    public void EnterAlternateScreen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _alternateScreen = true;
    }

    /// <summary>Records that the alternate screen has been exited. Checked by <see cref="ExitedCleanly"/>.</summary>
    public void ExitAlternateScreen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _alternateScreen = false;
    }

    // ── ITerminal: Input mode ─────────────────────────────────────────
    /// <summary>Records that raw mode has been entered. Checked by <see cref="ExitedCleanly"/>.</summary>
    public void EnterRawMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _rawMode = true;
    }

    /// <summary>Records that raw mode has been exited. Checked by <see cref="ExitedCleanly"/>.</summary>
    public void ExitRawMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _rawMode = false;
    }

    /// <summary>No-op — mouse mode is not simulated by the virtual terminal.</summary>
    public void EnableMouse(MouseMode mode = ConsoleForge.Terminal.MouseMode.ButtonEvents) { }

    /// <summary>No-op.</summary>
    public void DisableMouse() { }

    // ── ITerminal: Input stream ───────────────────────────────────────
    /// <summary>Observable stream of input events. Inject events via <see cref="EnqueueKey"/> or <see cref="SimulateResize"/>.</summary>
    public IObservable<InputEvent> Input => _input;

    // ── ITerminal: Resize events ──────────────────────────────────────
    /// <summary>Raised when <see cref="SimulateResize"/> is called.</summary>
    public event EventHandler<TerminalResizedEventArgs>? Resized;

    // ── IDisposable ───────────────────────────────────────────────────
    /// <summary>
    /// Exits raw mode and alternate screen if active, then completes the input observable.
    /// Sets <see cref="ExitedCleanly"/> to <see langword="true"/> if either mode was active.
    /// Safe to call multiple times.
    /// </summary>
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

    /// <summary>
    /// Returns a string containing the full rendered screen content (all rows joined with newlines).
    /// Useful for simple assertions: <c>Assert.Contains("Hello", terminal.ScreenContent)</c>.
    /// </summary>
    public string ScreenContent => string.Join("\n", Lines);

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

    /// <summary>Enqueue a synthetic mouse event to be delivered to the Input observable.</summary>
    public void EnqueueMouse(MouseMsg mouse)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.OnNext(new MouseInputEvent(mouse));
    }

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

    // ── ANSI decoder ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parse <paramref name="ansi"/> and write printable characters into <see cref="_screen"/>,
    /// respecting cursor-movement sequences (<c>ESC[row;colH</c>) and erase-line sequences.
    /// All other escape sequences are consumed without writing.
    /// </summary>
    private void ApplyAnsiToScreen(string ansi)
    {
        int i = 0;
        while (i < ansi.Length)
        {
            if (ansi[i] == '\x1b' && i + 1 < ansi.Length && ansi[i + 1] == '[')
            {
                // CSI sequence: ESC [ <params> <final>
                int j = i + 2;
                while (j < ansi.Length && (ansi[j] < 0x40 || ansi[j] > 0x7E)) j++;

                if (j < ansi.Length)
                {
                    var final = ansi[j];
                    var param = ansi[(i + 2)..j];

                    switch (final)
                    {
                        case 'H': // cursor position ESC[row;colH or ESC[H
                        {
                            var parts = param.Split(';');
                            if (parts.Length == 2 &&
                                int.TryParse(parts[0], out int r) &&
                                int.TryParse(parts[1], out int c))
                            {
                                _cursorRow = r - 1;
                                _cursorCol = c - 1;
                            }
                            else
                            {
                                _cursorRow = 0;
                                _cursorCol = 0;
                            }
                            break;
                        }
                        case 'K': // erase in line: ESC[K or ESC[0K = erase to end; ESC[2K = erase whole line
                        {
                            if (param is "2")
                            {
                                for (var c = 0; c < Width; c++)
                                    if (_cursorRow >= 0 && _cursorRow < Height)
                                        _screen[_cursorRow, c] = ' ';
                            }
                            else // 0 or empty = erase to end of line
                            {
                                for (var c = _cursorCol; c < Width; c++)
                                    if (_cursorRow >= 0 && _cursorRow < Height && c >= 0)
                                        _screen[_cursorRow, c] = ' ';
                            }
                            break;
                        }
                        case 'J': // erase in display: ESC[2J = clear screen
                        {
                            if (param is "2" or "")
                            {
                                for (var r = 0; r < Height; r++)
                                    for (var c = 0; c < Width; c++)
                                        _screen[r, c] = ' ';
                            }
                            break;
                        }
                        // All other sequences (color, bold, etc.) — skip
                    }
                    i = j + 1;
                }
                else { i++; }
            }
            else if (ansi[i] == '\x1b')
            {
                // Non-CSI escape — skip until end of sequence
                int j = i + 1;
                while (j < ansi.Length && ansi[j] < '@') j++;
                i = j + 1;
            }
            else
            {
                var ch = ansi[i];
                if (ch == '\n')
                {
                    _cursorRow++;
                    _cursorCol = 0;
                }
                else if (ch == '\r')
                {
                    _cursorCol = 0;
                }
                else
                {
                    if (_cursorRow >= 0 && _cursorRow < Height &&
                        _cursorCol >= 0 && _cursorCol < Width)
                        _screen[_cursorRow, _cursorCol] = ch;
                    _cursorCol++;
                }
                i++;
            }
        }
    }
}