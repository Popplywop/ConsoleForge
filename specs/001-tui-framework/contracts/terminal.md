# Contract: Terminal Abstraction Interface

**Feature**: specs/001-tui-framework
**Date**: 2026-04-12

---

## `ITerminal` — Terminal Abstraction

```csharp
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
    void SetCursorVisible(bool visible);
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

public abstract record InputEvent;
public sealed record KeyInputEvent(KeyMsg Key) : InputEvent;
public sealed record ResizeInputEvent(int Width, int Height) : InputEvent;

public sealed class TerminalResizedEventArgs(int width, int height) : EventArgs
{
    public int Width { get; } = width;
    public int Height { get; } = height;
}
```

---

## `AnsiTerminal` — Production Implementation

```csharp
namespace ConsoleForge.Terminal;

/// <summary>
/// Production ITerminal backed by System.Console and ANSI escape sequences.
/// Unix: uses Termios P/Invoke for raw mode and PosixSignalRegistration
///       for SIGWINCH resize detection.
/// Windows: uses SetConsoleMode Win32 API (v2 — deferred from v1).
/// </summary>
public sealed class AnsiTerminal : ITerminal
{
    public static AnsiTerminal Create();  // detect ColorProfile, open stdout writer
    // implements all ITerminal members
    public void Dispose();  // guaranteed: ExitRawMode + ExitAlternateScreen
}
```

---

## `VirtualTerminal` — Test Implementation

```csharp
namespace ConsoleForge.Testing;

/// <summary>
/// In-memory ITerminal for use in unit and integration tests.
/// Captures all Write() calls into a 2D char buffer.
/// Allows synthetic key events to be injected.
/// </summary>
public sealed class VirtualTerminal : ITerminal
{
    public VirtualTerminal(int width = 80, int height = 24);

    // ── Test inspection surface ──────────────────────────────────────

    /// <summary>The current rendered screen as an array of row strings.</summary>
    public string[] Lines { get; }

    /// <summary>True if ExitRawMode + ExitAlternateScreen were called on Dispose.</summary>
    public bool ExitedCleanly { get; }

    /// <summary>
    /// True if any ANSI sequences remain in the output buffer after the last
    /// Flush (indicates incomplete cleanup).
    /// </summary>
    public bool HasArtifacts { get; }

    /// <summary>History of all Write() calls, in order.</summary>
    public IReadOnlyList<string> WriteHistory { get; }

    // ── Test injection surface ───────────────────────────────────────

    /// <summary>
    /// Enqueue a synthetic key event to be delivered to the Input observable.
    /// </summary>
    public void EnqueueKey(KeyMsg key);

    /// <summary>Trigger a synthetic resize event.</summary>
    public void SimulateResize(int newWidth, int newHeight);
}
```
