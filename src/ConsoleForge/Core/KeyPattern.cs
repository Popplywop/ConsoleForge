namespace ConsoleForge.Core;

/// <summary>
/// Pattern for matching keyboard events. Every field is optional, and null means
/// "wildcard" — that field is not consulted. A pattern matches a <see cref="KeyMsg"/>
/// when all of its non-null fields agree with it.
/// <para>
/// There are two ways to name a key. <see cref="Of(ConsoleKey)"/> matches the
/// <see cref="ConsoleKey"/>, which is right for keys that produce no character —
/// arrows, Enter, Tab, function keys. <see cref="OfChar(char)"/> matches the character
/// the terminal actually produced, which is right for printable bindings because it is
/// independent of keyboard layout: <c>?</c> sits on a different physical key on a German
/// or French layout, but it is still <c>'?'</c>.
/// </para>
/// <para>
/// A pattern with no field set — <c>default(KeyPattern)</c> — matches <em>every</em> key
/// event. That is usable as a catch-all at the end of a <see cref="KeyMap"/>, but because
/// this is a struct, a <c>default</c> reached by accident will silently swallow all input.
/// </para>
/// </summary>
/// <example>
/// <code>
/// KeyPattern.Of(ConsoleKey.Enter)              // Enter, any modifiers
/// KeyPattern.OfChar('?')                       // '?' on any keyboard layout
/// KeyPattern.OfChar('N')                       // capital N only — case-sensitive
/// KeyPattern.WithCtrl(ConsoleKey.S)            // Ctrl+S
/// new KeyPattern(ConsoleKey.Tab, Shift: true)  // Shift+Tab
/// </code>
/// </example>
public readonly record struct KeyPattern(
    ConsoleKey? Key = null,
    bool? Shift = null,
    bool? Alt = null,
    bool? Ctrl = null,
    char? Character = null)
{
    /// <summary>
    /// Returns true if <paramref name="msg"/> matches this pattern. Null fields are
    /// skipped, so a pattern constrains only what it names.
    /// </summary>
    public bool Matches(KeyMsg msg) =>
        (Key is null || Key == msg.Key) &&
        (Character is null || Character == msg.Character) &&
        (Shift is null || Shift == msg.Shift) &&
        (Alt is null || Alt == msg.Alt) &&
        (Ctrl is null || Ctrl == msg.Ctrl);

    // ── Convenience factories ─────────────────────────────────────────────

    /// <summary>Match the key with any modifier combination.</summary>
    public static KeyPattern Of(ConsoleKey key) => new(key);

    /// <summary>
    /// Match a printable character, whatever key produced it — the layout-independent
    /// way to bind <c>?</c>, <c>/</c>, or a case-sensitive letter.
    /// <para>
    /// Ctrl and Alt must be absent: Ctrl+letter arrives as a control character rather
    /// than the letter, and Alt+key is a distinct binding that would otherwise match
    /// here, since the Alt path preserves <see cref="KeyMsg.Character"/>. Shift is
    /// deliberately left a wildcard — it has already been consumed producing the glyph,
    /// so requiring it would re-introduce the layout assumption this factory removes.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Control keys carry characters too (Enter is <c>'\r'</c>, Tab <c>'\t'</c>, Space
    /// <c>' '</c>), so prefer <see cref="Of(ConsoleKey)"/> for those.
    /// </remarks>
    public static KeyPattern OfChar(char c) => new(Character: c, Ctrl: false, Alt: false);

    /// <summary>Match Ctrl + key (Shift and Alt are wildcards).</summary>
    public static KeyPattern WithCtrl(ConsoleKey key) => new(key, Ctrl: true);

    /// <summary>Match Alt + key (Shift and Ctrl are wildcards).</summary>
    public static KeyPattern WithAlt(ConsoleKey key) => new(key, Alt: true);

    /// <summary>Match Shift + key (Alt and Ctrl are wildcards).</summary>
    public static KeyPattern WithShift(ConsoleKey key) => new(key, Shift: true);

    /// <summary>Match the key with no modifiers pressed.</summary>
    public static KeyPattern Plain(ConsoleKey key) => new(key, Shift: false, Alt: false, Ctrl: false);
}
