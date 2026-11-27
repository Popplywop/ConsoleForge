namespace ConsoleForge.Core;

/// <summary>
/// A key binding together with the help text that describes it, so the keys an application
/// handles and the keys its help bar lists come from one value and cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, like everything else the model holds. Whether a binding is active is part of
/// its value: derive the enabled form from model state with <c>with { Enabled = … }</c>
/// rather than toggling one in place. A disabled binding matches no key and is left out of
/// help.
/// </para>
/// <code>
/// static readonly KeyBinding Quit = new("q", "quit", KeyPattern.OfChar('q'), KeyPattern.WithCtrl(ConsoleKey.C));
/// static readonly KeyBinding Save = new("ctrl+s", "save", KeyPattern.WithCtrl(ConsoleKey.S));
///
/// // One map per state: Save only does anything — and only shows — when there is something to save.
/// KeyMap Keys => new KeyMap()
///     .On(Quit, () => new QuitMsg())
///     .On(Save with { Enabled = IsDirty }, () => new SaveMsg());
///
/// // In Update:  if (Keys.Handle(msg) is { } action) msg = action;
/// // In View:    new HelpBar(Keys.Bindings)
/// </code>
/// </remarks>
/// <param name="Patterns">The keys that trigger the binding. Any one of them matches.</param>
/// <param name="HelpKey">How the keys are shown in help, e.g. <c>"↑/k"</c> or <c>"ctrl+s"</c>.</param>
/// <param name="HelpDescription">What the binding does, e.g. <c>"move up"</c>.</param>
public sealed record KeyBinding(IReadOnlyList<KeyPattern> Patterns, string HelpKey, string HelpDescription)
{
    /// <summary>
    /// Create a binding from help text and the patterns that trigger it.
    /// </summary>
    /// <param name="helpKey">How the keys are shown in help.</param>
    /// <param name="helpDescription">What the binding does.</param>
    /// <param name="patterns">The keys that trigger the binding. Any one of them matches.</param>
    public KeyBinding(string helpKey, string helpDescription, params KeyPattern[] patterns)
        : this(patterns, helpKey, helpDescription) { }

    /// <summary>
    /// Whether the binding is active. A disabled binding matches nothing, is skipped by
    /// <see cref="KeyMap"/>, and is hidden from help. Defaults to true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>True when the binding is enabled and any of its patterns matches <paramref name="msg"/>.</summary>
    public bool Matches(KeyMsg msg)
    {
        if (!Enabled) return false;
        for (int i = 0; i < Patterns.Count; i++)
            if (Patterns[i].Matches(msg)) return true;
        return false;
    }
}
