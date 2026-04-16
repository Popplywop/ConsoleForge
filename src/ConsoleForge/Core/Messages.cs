using ConsoleForge.Layout;

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

/// <summary>
/// Focus moved to the widget at <see cref="Index"/> in the depth-first focusable list.
/// Models use this to set <c>HasFocus = true</c> on the correct widget instance.
/// </summary>
public sealed record FocusIndexChangedMsg(int Index) : IMsg;

/// <summary>Internal: aggregates results from Cmd.Batch concurrent execution.</summary>
public sealed record BatchMsg(IMsg[] Messages) : IMsg;

/// <summary>Internal: aggregates results from Cmd.Sequence serial execution.</summary>
public sealed record SequenceMsg(IMsg[] Messages) : IMsg;

/// <summary>Triggers a re-render without changing model state (e.g., theme swap).</summary>
public sealed record RedrawMsg : IMsg;

/// <summary>Dispatched by <c>List</c> when the user presses Enter on a selected item.</summary>
/// <param name="Index">Zero-based index of the selected item.</param>
/// <param name="Item">The item value (string for built-in <c>List</c>).</param>
public sealed record ListItemSelectedMsg(int Index, object Item) : IMsg;

/// <summary>
/// Dispatched to change the active theme at runtime.
/// <see cref="Program"/> intercepts this message and calls
/// <see cref="Program.SetTheme"/> before forwarding to the model.
/// The model should update any theme-tracking state in its own Update handler.
/// </summary>
public sealed record ThemeChangedMsg(Styling.Theme NewTheme) : IMsg;

/// <summary>
/// Dispatched when a command throws an unhandled exception.
/// Models may handle this to display error state; unhandled it is silently dropped.
/// </summary>
/// <param name="Exception">The exception thrown by the command.</param>
/// <param name="Source">Optional label identifying which command failed (for logging).</param>
public sealed record CmdErrorMsg(Exception Exception, string? Source = null) : IMsg;

// ── Mouse ────────────────────────────────────────────────────────────────

/// <summary>Which mouse button was involved in an event.</summary>
public enum MouseButton
{
    /// <summary>Primary (left) button.</summary>
    Left,
    /// <summary>Middle button or scroll-wheel click.</summary>
    Middle,
    /// <summary>Secondary (right) button.</summary>
    Right,
    /// <summary>Scroll wheel rotated upward (away from user).</summary>
    ScrollUp,
    /// <summary>Scroll wheel rotated downward (toward user).</summary>
    ScrollDown,
    /// <summary>No button — motion-only event.</summary>
    None,
}

/// <summary>The type of mouse action represented by a <see cref="MouseMsg"/>.</summary>
public enum MouseAction
{
    /// <summary>Button pressed down.</summary>
    Press,
    /// <summary>Button released.</summary>
    Release,
    /// <summary>Cursor moved (button may or may not be held).</summary>
    Move,
}

/// <summary>
/// A mouse input event. Zero-based <see cref="Col"/> and <see cref="Row"/> give the
/// terminal cell under the pointer when the event occurred.
/// </summary>
public sealed record MouseMsg(
    MouseButton Button,
    MouseAction Action,
    int Col,
    int Row,
    bool Shift = false,
    bool Alt   = false,
    bool Ctrl  = false) : IMsg;