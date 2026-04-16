namespace ConsoleForge.Core;

/// <summary>
/// Pattern for matching keyboard events. Null modifier fields act as wildcards
/// (match regardless of that modifier's state).
/// </summary>
/// <example>
/// <code>
/// KeyPattern.Of(ConsoleKey.Q)              // Q, any modifiers
/// KeyPattern.WithCtrl(ConsoleKey.S)        // Ctrl+S
/// new KeyPattern(ConsoleKey.Tab, Shift: true)  // Shift+Tab
/// </code>
/// </example>
public readonly record struct KeyPattern(
    ConsoleKey Key,
    bool? Shift = null,
    bool? Alt   = null,
    bool? Ctrl  = null)
{
    /// <summary>Returns true if <paramref name="msg"/> matches this pattern.</summary>
    public bool Matches(KeyMsg msg) =>
        Key == msg.Key &&
        (Shift is null || Shift.Value == msg.Shift) &&
        (Alt   is null || Alt.Value   == msg.Alt) &&
        (Ctrl  is null || Ctrl.Value  == msg.Ctrl);

    // ── Convenience factories ─────────────────────────────────────────────

    /// <summary>Match the key with any modifier combination.</summary>
    public static KeyPattern Of(ConsoleKey key) => new(key);

    /// <summary>Match Ctrl + key (Shift and Alt are wildcards).</summary>
    public static KeyPattern WithCtrl(ConsoleKey key) => new(key, Ctrl: true);

    /// <summary>Match Alt + key (Shift and Ctrl are wildcards).</summary>
    public static KeyPattern WithAlt(ConsoleKey key) => new(key, Alt: true);

    /// <summary>Match Shift + key (Alt and Ctrl are wildcards).</summary>
    public static KeyPattern WithShift(ConsoleKey key) => new(key, Shift: true);

    /// <summary>Match the key with no modifiers pressed.</summary>
    public static KeyPattern Plain(ConsoleKey key) => new(key, Shift: false, Alt: false, Ctrl: false);
}
