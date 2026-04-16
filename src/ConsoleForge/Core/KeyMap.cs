namespace ConsoleForge.Core;

/// <summary>
/// Declarative input binding map. Resolves <see cref="KeyMsg"/> and <see cref="MouseMsg"/>
/// to application <see cref="IMsg"/> values without <c>switch</c> statements.
/// <para>
/// Usage:
/// <code>
/// static readonly KeyMap Nav = new KeyMap()
///     .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
///     .On(ConsoleKey.DownArrow, () => new NavDownMsg())
///     .On(ConsoleKey.Escape,    () => new QuitMsg())
///     .OnScroll(m => m.Button == MouseButton.ScrollUp
///         ? new NavUpMsg() : new NavDownMsg());
///
/// // In Update:
/// if (Nav.Handle(msg) is { } result)
///     return ProcessAction(result);
/// </code>
/// </para>
/// </summary>
/// <remarks>
/// <para>Bindings are evaluated in registration order; the first match wins.</para>
/// <para>The map is mutable during construction (fluent builder) but should be
/// stored as a <c>static readonly</c> field once built. It is thread-safe for reads.</para>
/// </remarks>
public sealed class KeyMap
{
    private readonly List<(KeyPattern Pattern, Func<KeyMsg, IMsg> Handler)> _keys = [];
    private readonly List<(Func<MouseMsg, bool> Predicate, Func<MouseMsg, IMsg> Handler)> _mouse = [];

    // ── Key bindings ──────────────────────────────────────────────────────────

    /// <summary>Bind a key (any modifiers) to a message factory.</summary>
    public KeyMap On(ConsoleKey key, Func<IMsg> handler)
    {
        _keys.Add((KeyPattern.Of(key), _ => handler()));
        return this;
    }

    /// <summary>Bind a key (any modifiers) to a message factory that receives the original <see cref="KeyMsg"/>.</summary>
    public KeyMap On(ConsoleKey key, Func<KeyMsg, IMsg> handler)
    {
        _keys.Add((KeyPattern.Of(key), handler));
        return this;
    }

    /// <summary>Bind a <see cref="KeyPattern"/> to a message factory.</summary>
    public KeyMap On(KeyPattern pattern, Func<IMsg> handler)
    {
        _keys.Add((pattern, _ => handler()));
        return this;
    }

    /// <summary>Bind a <see cref="KeyPattern"/> to a message factory that receives the original <see cref="KeyMsg"/>.</summary>
    public KeyMap On(KeyPattern pattern, Func<KeyMsg, IMsg> handler)
    {
        _keys.Add((pattern, handler));
        return this;
    }

    // ── Mouse bindings ────────────────────────────────────────────────────────

    /// <summary>Bind left-click press events.</summary>
    public KeyMap OnClick(Func<MouseMsg, IMsg> handler)
    {
        _mouse.Add((m => m.Button == MouseButton.Left && m.Action == MouseAction.Press, handler));
        return this;
    }

    /// <summary>Bind scroll-wheel events (both up and down).</summary>
    public KeyMap OnScroll(Func<MouseMsg, IMsg> handler)
    {
        _mouse.Add((m => m.Button is MouseButton.ScrollUp or MouseButton.ScrollDown, handler));
        return this;
    }

    /// <summary>Bind a specific mouse button + action combination.</summary>
    public KeyMap OnMouse(MouseButton button, MouseAction action, Func<MouseMsg, IMsg> handler)
    {
        _mouse.Add((m => m.Button == button && m.Action == action, handler));
        return this;
    }

    /// <summary>Bind any mouse event matching <paramref name="predicate"/>.</summary>
    public KeyMap OnMouse(Func<MouseMsg, bool> predicate, Func<MouseMsg, IMsg> handler)
    {
        _mouse.Add((predicate, handler));
        return this;
    }

    // ── Resolution ────────────────────────────────────────────────────────────

    /// <summary>
    /// Try to resolve an input message to an application message.
    /// Returns <see langword="null"/> if no binding matches.
    /// Handles both <see cref="KeyMsg"/> and <see cref="MouseMsg"/>.
    /// </summary>
    public IMsg? Handle(IMsg msg) => msg switch
    {
        KeyMsg key   => HandleKey(key),
        MouseMsg mouse => HandleMouse(mouse),
        _            => null,
    };

    /// <summary>Try to resolve a keyboard message. Returns null if no binding matches.</summary>
    public IMsg? HandleKey(KeyMsg key)
    {
        for (int i = 0; i < _keys.Count; i++)
            if (_keys[i].Pattern.Matches(key))
                return _keys[i].Handler(key);
        return null;
    }

    /// <summary>Try to resolve a mouse message. Returns null if no binding matches.</summary>
    public IMsg? HandleMouse(MouseMsg mouse)
    {
        for (int i = 0; i < _mouse.Count; i++)
            if (_mouse[i].Predicate(mouse))
                return _mouse[i].Handler(mouse);
        return null;
    }

    // ── Composition ───────────────────────────────────────────────────────────

    /// <summary>
    /// Create a new <see cref="KeyMap"/> containing all bindings from this map
    /// followed by all bindings from <paramref name="other"/>.
    /// This map's bindings take priority (evaluated first).
    /// </summary>
    public KeyMap Merge(KeyMap other)
    {
        var merged = new KeyMap();
        merged._keys.AddRange(_keys);
        merged._keys.AddRange(other._keys);
        merged._mouse.AddRange(_mouse);
        merged._mouse.AddRange(other._mouse);
        return merged;
    }

    /// <summary>Number of key bindings registered.</summary>
    public int KeyBindingCount => _keys.Count;

    /// <summary>Number of mouse bindings registered.</summary>
    public int MouseBindingCount => _mouse.Count;
}
