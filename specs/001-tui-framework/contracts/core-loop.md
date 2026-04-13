# Contract: Core Elm Loop Interfaces

**Feature**: specs/001-tui-framework
**Date**: 2026-04-12

This contract defines the public interface surface for the core Elm
Model-Update-View loop. Application developers implement `IModel`;
the framework owns the runtime.

---

## `IMsg` — Message Marker Interface

```csharp
namespace ConsoleForge.Core;

/// <summary>
/// Marker interface for all messages flowing through the event loop.
/// Implement this on any record or class to define a custom message type.
/// </summary>
public interface IMsg { }
```

**Contract rules**:
- Any type implementing `IMsg` is a valid message.
- Framework-internal messages (`QuitMsg`, `BatchMsg`, `SequenceMsg`,
  `KeyMsg`, `WindowResizeMsg`) are sealed and live in `ConsoleForge.Core`.
- User-defined message types MUST NOT shadow built-in types.

---

## `ICmd` — Command Type Alias

```csharp
namespace ConsoleForge.Core;

/// <summary>
/// A command: a blocking function that produces one message when complete.
/// The framework executes each non-null command on a background thread.
/// Return null (Cmd.None) to indicate no operation.
/// </summary>
public delegate IMsg ICmd();
```

**Contract rules**:
- `ICmd` runs on a thread-pool thread; it MUST NOT interact with the terminal directly.
- After the command returns its `IMsg`, the message is injected into the
  event loop. The command's goroutine/thread exits.
- Infinite-loop commands are not supported and will leak threads.

---

## `IModel` — Application Model Interface

```csharp
namespace ConsoleForge.Core;

/// <summary>
/// The root interface for all ConsoleForge application models.
/// Implement this to define your application's state and behavior.
/// </summary>
public interface IModel
{
    /// <summary>
    /// Called once at program start. Return a command to execute on startup,
    /// or null for no initial side-effect.
    /// </summary>
    ICmd? Init();

    /// <summary>
    /// Pure update function. Given the current message, return the new model
    /// state and an optional follow-up command.
    /// Convention: return a new record copy (C# 'with' expression).
    /// MUST NOT return a null IModel.
    /// </summary>
    (IModel Model, ICmd? Cmd) Update(IMsg msg);

    /// <summary>
    /// Produce the rendered view for the current model state.
    /// Called after every Update. MUST be a pure, side-effect-free function.
    /// </summary>
    ViewDescriptor View();
}
```

---

## `ViewDescriptor` — Frame Descriptor

```csharp
namespace ConsoleForge.Core;

/// <summary>
/// Immutable descriptor for a single rendered frame.
/// Produced by IModel.View() and diffed by the renderer.
/// </summary>
public readonly record struct ViewDescriptor
{
    /// <summary>Pre-rendered ANSI string for the full terminal frame.</summary>
    public required string Content { get; init; }

    /// <summary>Optional terminal window title. Null = no change.</summary>
    public string? Title { get; init; }

    /// <summary>Cursor state for this frame.</summary>
    public CursorDescriptor Cursor { get; init; }

    /// <summary>
    /// Convenience factory: render an IWidget into a ViewDescriptor
    /// at the current terminal dimensions.
    /// </summary>
    public static ViewDescriptor From(IWidget root,
        int? width = null, int? height = null,
        Theme? theme = null,
        ColorProfile colorProfile = ColorProfile.TrueColor);
}

public readonly record struct CursorDescriptor
{
    public bool Visible { get; init; } = true;
    public int Col { get; init; } = 0;
    public int Row { get; init; } = 0;
}
```

---

## `Program` — Runtime Entry Point

```csharp
namespace ConsoleForge.Core;

/// <summary>
/// The ConsoleForge runtime. Owns the event loop, renderer, and terminal.
/// </summary>
public sealed class Program
{
    /// <summary>
    /// Start the application. Blocks until the model dispatches QuitMsg
    /// or the process receives SIGINT/SIGTERM.
    /// Enters alternate screen, runs event loop, restores terminal on exit.
    /// </summary>
    /// <param name="model">Initial model instance.</param>
    /// <param name="terminal">
    ///   Terminal implementation. Defaults to AnsiTerminal (production).
    ///   Pass a VirtualTerminal for testing.
    /// </param>
    /// <param name="theme">Global theme. Defaults to Theme.Default.</param>
    /// <param name="targetFps">Render tick rate. Default 60.</param>
    public static void Run(
        IModel model,
        ITerminal? terminal = null,
        Theme? theme = null,
        int targetFps = 60);
}
```

**Contract rules**:
- `Run` is synchronous and blocking.
- Terminal state (raw mode, alternate screen, cursor) is guaranteed restored on
  both clean exit and unhandled exceptions (try/finally in runtime).
- `targetFps` is a maximum; actual fps is limited by Update throughput.

---

## Built-in Messages

```csharp
namespace ConsoleForge.Core;

/// <summary>Signals the program loop to exit cleanly.</summary>
public sealed record QuitMsg : IMsg;

/// <summary>A keyboard input event.</summary>
public sealed record KeyMsg(
    ConsoleKey Key,
    char? Character,
    bool Shift = false,
    bool Alt = false,
    bool Ctrl = false) : IMsg;

/// <summary>Terminal was resized.</summary>
public sealed record WindowResizeMsg(int Width, int Height) : IMsg;

/// <summary>Focus moved from one widget to another.</summary>
public sealed record FocusChangedMsg(IWidget? Previous, IWidget? Next) : IMsg;
```

---

## `Cmd` — Command Factory

```csharp
namespace ConsoleForge.Core;

public static class Cmd
{
    /// <summary>No-op command. Framework skips dispatch.</summary>
    public static readonly ICmd? None = null;

    /// <summary>Returns QuitMsg immediately, ending the program loop.</summary>
    public static ICmd Quit();

    /// <summary>
    /// Run all commands concurrently. Null commands filtered out.
    /// Returns null if zero cmds remain; the single cmd if one remains.
    /// </summary>
    public static ICmd? Batch(params ICmd?[] cmds);

    /// <summary>
    /// Run commands serially: each waits for the previous to complete.
    /// Null commands filtered out.
    /// </summary>
    public static ICmd? Sequence(params ICmd?[] cmds);

    /// <summary>
    /// Fire once after interval. Returns fn(timestamp) as the message.
    /// </summary>
    public static ICmd Tick(TimeSpan interval, Func<DateTimeOffset, IMsg> fn);
}
```
