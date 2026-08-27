using System.Globalization;

namespace ConsoleForge.Core;

/// <summary>
/// Single-line text editing as a pure value: a string, a cursor, and one function
/// from a <see cref="KeyMsg"/> to the next state.
/// </summary>
/// <remarks>
/// <para>
/// The editing rules — insert, delete, cursor movement, word jumps — are written once
/// here instead of in every model that owns an input. Bubble Tea solves the same problem
/// by making the input a stateful sub-program with its own update loop; that state would
/// live outside the model, so ConsoleForge takes the Elm-native route: the state is part
/// of your model, and <see cref="HandleKey"/> is a pure function over it.
/// </para>
/// <code>
/// sealed record Model(TextInputState Filter) : IModel
/// {
///     public (IModel, ICmd?) Update(IMsg msg) => msg switch
///     {
///         KeyMsg k => (this with { Filter = Filter.HandleKey(k) }, null),
///         _        => (this, null),
///     };
///
///     public IWidget View() => new TextInput(Filter.Value, cursorPosition: Filter.Cursor);
/// }
/// </code>
/// <para>
/// <see cref="Cursor"/> is an index into <see cref="Value"/> in UTF-16 code units, kept on
/// a grapheme cluster boundary: an emoji or an accented letter moves and deletes as one
/// unit, never leaving half a surrogate pair behind. It is not a column — a wide glyph
/// occupies two columns but one cursor step. Out-of-range or mid-cluster values are
/// normalised on construction, so <c>with</c> expressions cannot produce a broken state.
/// </para>
/// <para>Bindings handled by <see cref="HandleKey"/>:</para>
/// <list type="table">
///   <item><term>Left / Right</term><description>move one grapheme cluster</description></item>
///   <item><term>Ctrl+Left / Ctrl+Right</term><description>move one word</description></item>
///   <item><term>Home / Ctrl+A, End / Ctrl+E</term><description>start, end</description></item>
///   <item><term>Backspace, Delete</term><description>delete the cluster before / after the cursor</description></item>
///   <item><term>Ctrl+Backspace / Ctrl+W, Ctrl+Delete</term><description>delete the word before / after the cursor</description></item>
///   <item><term>Ctrl+U, Ctrl+K</term><description>delete to the start / end of the line</description></item>
///   <item><term>any printable character</term><description>insert at the cursor</description></item>
/// </list>
/// </remarks>
public sealed record TextInputState
{
    private readonly string _value  = "";
    private readonly int    _cursor;

    /// <summary>The edited text. Never null; setting null stores an empty string.</summary>
    public string Value
    {
        get => _value;
        init
        {
            _value  = value ?? "";
            // Re-normalise: the cursor that came with the copy may be past the end of
            // this text, or inside one of its clusters.
            _cursor = Normalize(_value, _cursor);
        }
    }

    /// <summary>
    /// Insertion point as an index into <see cref="Value"/>, always on a grapheme cluster
    /// boundary and within <c>[0, Value.Length]</c>.
    /// </summary>
    public int Cursor
    {
        get => _cursor;
        init => _cursor = Normalize(_value, value);
    }

    /// <summary>An empty input with the cursor at the start.</summary>
    public TextInputState() { }

    /// <summary>
    /// An input holding <paramref name="value"/>, with the cursor at
    /// <paramref name="cursor"/> — clamped into range and snapped back to a cluster
    /// boundary. Defaults to the end of the text, where typing continues.
    /// </summary>
    /// <param name="value">Initial text. Null is treated as empty.</param>
    /// <param name="cursor">Initial cursor index; defaults to the end of <paramref name="value"/>.</param>
    public TextInputState(string value, int? cursor = null)
    {
        _value  = value ?? "";
        _cursor = Normalize(_value, cursor ?? _value.Length);
    }

    /// <summary>True when there is no text to edit.</summary>
    public bool IsEmpty => _value.Length == 0;

    // ── The reducer ───────────────────────────────────────────────────────────

    /// <summary>
    /// Apply one key press. Returns the next state, or this same instance when the key
    /// is not an editing key or the edit would be a no-op (Backspace at the start, for
    /// instance), so a model can compare by reference to detect "nothing happened".
    /// </summary>
    /// <param name="key">The key event to interpret.</param>
    public TextInputState HandleKey(KeyMsg key)
    {
        if (key is null) return this;

        // Ctrl-chorded editing first: these arrive with a control character in
        // Character, which the printable-insert case below must not see.
        if (key.Ctrl)
        {
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:  return WithCursor(PrevWord(_value, _cursor));
                case ConsoleKey.RightArrow: return WithCursor(NextWord(_value, _cursor));
                case ConsoleKey.A:          return WithCursor(0);
                case ConsoleKey.E:          return WithCursor(_value.Length);
                case ConsoleKey.U:          return _cursor == 0 ? this : Replace(0, _cursor, "", 0);
                case ConsoleKey.K:          return _cursor == _value.Length ? this : Replace(_cursor, _value.Length, "", _cursor);
                case ConsoleKey.W:
                case ConsoleKey.Backspace:  return DeleteWordBackward();
                case ConsoleKey.Delete:     return DeleteWordForward();
            }
        }

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:  return WithCursor(PrevBoundary(_value, _cursor));
            case ConsoleKey.RightArrow: return WithCursor(NextBoundary(_value, _cursor));
            case ConsoleKey.Home:       return WithCursor(0);
            case ConsoleKey.End:        return WithCursor(_value.Length);

            case ConsoleKey.Backspace:
            {
                int start = PrevBoundary(_value, _cursor);
                return start == _cursor ? this : Replace(start, _cursor, "", start);
            }

            case ConsoleKey.Delete:
            {
                int end = NextBoundary(_value, _cursor);
                return end == _cursor ? this : Replace(_cursor, end, "", _cursor);
            }
        }

        // Printable input. Alt-chorded keys are shortcuts, not text.
        if (!key.Alt && key.Character is char c && !char.IsControl(c))
            return Insert(c.ToString());

        return this;
    }

    // ── Explicit edits ────────────────────────────────────────────────────────

    /// <summary>
    /// Insert <paramref name="text"/> at the cursor and place the cursor after it.
    /// Use this for paste, where the text arrives as a block rather than as key events.
    /// </summary>
    /// <param name="text">Text to insert. Null or empty leaves the state unchanged.</param>
    public TextInputState Insert(string text) =>
        string.IsNullOrEmpty(text)
            ? this
            : Replace(_cursor, _cursor, text, _cursor + text.Length);

    /// <summary>Delete the word before the cursor, along with any whitespace run before it.</summary>
    public TextInputState DeleteWordBackward()
    {
        int start = PrevWord(_value, _cursor);
        return start == _cursor ? this : Replace(start, _cursor, "", start);
    }

    /// <summary>Delete from the cursor to the start of the next word.</summary>
    public TextInputState DeleteWordForward()
    {
        int end = NextWord(_value, _cursor);
        return end == _cursor ? this : Replace(_cursor, end, "", _cursor);
    }

    /// <summary>Empty the text and reset the cursor.</summary>
    public TextInputState Clear() => IsEmpty && _cursor == 0 ? this : new TextInputState();

    /// <inheritdoc/>
    public override string ToString() => _value;

    // ── Internals ─────────────────────────────────────────────────────────────

    private TextInputState WithCursor(int cursor) =>
        cursor == _cursor ? this : new TextInputState(_value, cursor);

    /// <summary>Splice <paramref name="text"/> over <c>[start, end)</c> and land the cursor at <paramref name="cursor"/>.</summary>
    private TextInputState Replace(int start, int end, string text, int cursor) =>
        new(string.Concat(_value.AsSpan(0, start), text, _value.AsSpan(end)), cursor);

    /// <summary>Clamp into range, then snap back to the start of the cluster the index landed in.</summary>
    private static int Normalize(string value, int cursor)
    {
        if (cursor <= 0) return 0;
        if (cursor >= value.Length) return value.Length;

        int pos = 0;
        while (pos < cursor)
        {
            int next = NextBoundary(value, pos);
            if (next > cursor) return pos;  // the index fell inside this cluster
            pos = next;
        }
        return pos;
    }

    private static int NextBoundary(string value, int index) =>
        index >= value.Length
            ? value.Length
            : index + StringInfo.GetNextTextElementLength(value.AsSpan(index));

    /// <summary>
    /// Start of the cluster ending at <paramref name="index"/>. There is no
    /// walk-backwards API for text elements, so this walks forward from the start —
    /// single-line inputs are short enough for that to be free.
    /// </summary>
    private static int PrevBoundary(string value, int index)
    {
        if (index <= 0) return 0;

        int pos = 0;
        while (true)
        {
            int next = NextBoundary(value, pos);
            if (next >= index) return pos;
            pos = next;
        }
    }

    // Word scanning runs over chars rather than clusters: whitespace never appears
    // inside a cluster in single-line text, so any position it stops at is a boundary.
    private static int PrevWord(string value, int index)
    {
        int p = Math.Clamp(index, 0, value.Length);
        while (p > 0 && char.IsWhiteSpace(value[p - 1])) p--;
        while (p > 0 && !char.IsWhiteSpace(value[p - 1])) p--;
        return p;
    }

    private static int NextWord(string value, int index)
    {
        int p = Math.Clamp(index, 0, value.Length);
        while (p < value.Length && !char.IsWhiteSpace(value[p])) p++;
        while (p < value.Length && char.IsWhiteSpace(value[p])) p++;
        return p;
    }
}
