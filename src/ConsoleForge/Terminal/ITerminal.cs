using ConsoleForge.Core;
using ConsoleForge.Styling;

namespace ConsoleForge.Terminal;

/// <summary>
/// Thin abstraction over a physical terminal.
/// Production implementation: AnsiTerminal (uses System.Console + ANSI codes).
/// Test implementation: VirtualTerminal (in-memory buffer, injectable input).
/// </summary>
public interface ITerminal : IDisposable
{
    // ── Dimensions ───────────────────────────────────────────────────
    /// <summary>Current terminal width in columns.</summary>
    int Width { get; }

    /// <summary>Current terminal height in rows.</summary>
    int Height { get; }

    // ── Output ───────────────────────────────────────────────────────
    /// <summary>
    /// Write a pre-rendered ANSI string to the terminal output buffer.
    /// Does not flush to the physical terminal until Flush() is called.
    /// </summary>
    void Write(string ansiText);

    /// <summary>Clear the internal output buffer (not the physical terminal).</summary>
    void Clear();

    /// <summary>
    /// Flush the buffered output to the physical terminal.
    /// Called by the renderer at most once per frame (≤60 fps).
    /// </summary>
    void Flush();

    // ── Cursor ───────────────────────────────────────────────────────
    /// <summary>Show or hide the terminal hardware cursor.</summary>
    void SetCursorVisible(bool visible);
    /// <summary>Move the terminal cursor to the given zero-based column and row.</summary>
    void SetCursorPosition(int col, int row);

    // ── Title ────────────────────────────────────────────────────────
    /// <summary>Set the terminal window title. No-op if not supported.</summary>
    void SetTitle(string title);

    // ── Screen mode ──────────────────────────────────────────────────
    /// <summary>Enter alternate screen buffer (saves scroll-back).</summary>
    void EnterAlternateScreen();

    /// <summary>Exit alternate screen buffer and restore previous content.</summary>
    void ExitAlternateScreen();

    // ── Input mode ───────────────────────────────────────────────────
    /// <summary>
    /// Enter raw / no-echo mode. Keyboard events are delivered via Input
    /// without waiting for newline. Ctrl+C is delivered as KeyMsg, not SIGINT.
    /// </summary>
    void EnterRawMode();

    /// <summary>Restore cooked (normal) input mode.</summary>
    void ExitRawMode();

    // ── Input stream ─────────────────────────────────────────────────
    /// <summary>
    /// Observable stream of input events (keypresses, resize events).
    /// The runtime subscribes to this; application code does not.
    /// </summary>
    IObservable<InputEvent> Input { get; }

    // ── Resize events ────────────────────────────────────────────────
    /// <summary>
    /// Raised when the terminal is resized. On Unix, triggered by SIGWINCH.
    /// The runtime re-queries Width/Height and sends WindowResizeMsg.
    /// </summary>
    event EventHandler<TerminalResizedEventArgs> Resized;
}

// ── Supporting types ─────────────────────────────────────────────────────────

/// <summary>Base discriminated union for all terminal input events.</summary>
public abstract record InputEvent;
/// <summary>Input event wrapping a keyboard keypress.</summary>
public sealed record KeyInputEvent(KeyMsg Key) : InputEvent;
/// <summary>Input event raised when the terminal is resized.</summary>
public sealed record ResizeInputEvent(int Width, int Height) : InputEvent;

/// <summary>
/// Event arguments carrying the new terminal dimensions after a resize.
/// </summary>
public sealed class TerminalResizedEventArgs(int width, int height) : EventArgs
{
    /// <summary>New terminal width in columns.</summary>
    public int Width { get; } = width;
    /// <summary>New terminal height in rows.</summary>
    public int Height { get; } = height;
}
