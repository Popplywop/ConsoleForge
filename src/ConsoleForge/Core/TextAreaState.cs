namespace ConsoleForge.Core;

/// <summary>
/// Multi-line text editing as a pure value: a list of lines, a cursor row and column,
/// and one function from a <see cref="KeyMsg"/> to the next state.
/// </summary>
/// <remarks>
/// <para>
/// Intra-line editing is <see cref="TextInputState"/>'s, not a second copy of it: for any
/// key that stays on one line, this builds a <see cref="TextInputState"/> over the current
/// line, hands it the key, and splices the result back. Grapheme clusters, word jumps and
/// kill-to-edge therefore behave identically in both, and an emoji moves as one unit here
/// too. What this owns is the part that crosses lines — Up and Down, Enter splitting a
/// line, Backspace at column 0 joining to the previous line, Delete at end-of-line pulling
/// the next one up.
/// </para>
/// <code>
/// sealed record Model(TextAreaState Body) : IModel
/// {
///     public (IModel, ICmd?) Update(IMsg msg) => msg switch
///     {
///         KeyMsg k => (this with { Body = Body.HandleKey(k) }, null),
///         _        => (this, null),
///     };
/// }
/// </code>
/// <para>
/// <see cref="CursorCol"/> is an index into the current line in UTF-16 code units, kept on
/// a grapheme cluster boundary, and <see cref="CursorRow"/> is always a real line. Both are
/// normalised on construction and on every <c>with</c>, so no copy lands out of range or
/// mid-cluster. There is always at least one line: an empty document is one empty line.
/// </para>
/// <para>
/// Scrolling is not here. It needs a viewport height, which is a layout result — see
/// <see cref="ListState"/>, which carries one, or <c>TextArea.ComputeScrollRow</c>.
/// </para>
/// </remarks>
public sealed record TextAreaState
{
    private readonly IReadOnlyList<string> _lines = [""];
    private readonly int _cursorRow;
    private readonly int _cursorCol;

    /// <summary>
    /// The document, one string per logical line. Never null and never empty — an empty
    /// document is a single empty line. Contains no newline characters.
    /// </summary>
    public IReadOnlyList<string> Lines
    {
        get => _lines;
        init
        {
            (_lines, _cursorRow, _cursorCol) = Normalize(value, _cursorRow, _cursorCol);
        }
    }

    /// <summary>Cursor line, always within <c>[0, Lines.Count - 1]</c>.</summary>
    public int CursorRow
    {
        get => _cursorRow;
        init
        {
            (_lines, _cursorRow, _cursorCol) = Normalize(_lines, value, _cursorCol);
        }
    }

    /// <summary>
    /// Cursor column as an index into <c>Lines[CursorRow]</c>, on a grapheme cluster
    /// boundary and within <c>[0, line.Length]</c>.
    /// </summary>
    public int CursorCol
    {
        get => _cursorCol;
        init
        {
            (_lines, _cursorRow, _cursorCol) = Normalize(_lines, _cursorRow, value);
        }
    }

    /// <summary>
    /// Maximum number of lines. 0 means unlimited. At the limit, Enter does nothing.
    /// </summary>
    public int MaxLines { get; init; }

    /// <summary>An empty document: one empty line, cursor at the start.</summary>
    public TextAreaState() { }

    /// <summary>
    /// A document holding <paramref name="lines"/>, with the cursor clamped into range and
    /// snapped to a cluster boundary.
    /// </summary>
    /// <param name="lines">Initial lines. Null or empty becomes a single empty line.</param>
    /// <param name="cursorRow">Initial cursor line.</param>
    /// <param name="cursorCol">Initial cursor column.</param>
    /// <param name="maxLines">Line limit; 0 is unlimited.</param>
    public TextAreaState(
        IReadOnlyList<string>? lines,
        int cursorRow = 0,
        int cursorCol = 0,
        int maxLines = 0)
    {
        (_lines, _cursorRow, _cursorCol) = Normalize(lines, cursorRow, cursorCol);
        MaxLines = Math.Max(0, maxLines);
    }

    /// <summary>True when the document is a single empty line.</summary>
    public bool IsEmpty => _lines.Count == 1 && _lines[0].Length == 0;

    /// <summary>The line the cursor is on.</summary>
    public string CurrentLine => _lines[_cursorRow];

    /// <summary>The document as one string, lines joined with <paramref name="newline"/>.</summary>
    /// <param name="newline">Separator to join with. Defaults to a line feed.</param>
    public string Text(string newline = "\n") => string.Join(newline, _lines);

    // ── The reducer ───────────────────────────────────────────────────────────

    /// <summary>
    /// Apply one key press. Returns this same instance when the key is not an editing key
    /// or the edit would be a no-op, so a caller can compare by reference to detect
    /// "nothing happened".
    /// </summary>
    /// <param name="key">The key event to interpret.</param>
    public TextAreaState HandleKey(KeyMsg key)
    {
        if (key is null) return this;

        // Line-crossing keys first: TextInputState would treat each of these as a no-op
        // at the edge of its single line, which is exactly where the multi-line meaning
        // lives. Everything else falls through to it.
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                return _cursorRow == 0 ? this : MoveToRow(_cursorRow - 1);

            case ConsoleKey.DownArrow:
                return _cursorRow >= _lines.Count - 1 ? this : MoveToRow(_cursorRow + 1);

            case ConsoleKey.Enter:
                return SplitLine();

            case ConsoleKey.LeftArrow when _cursorCol == 0 && !key.Ctrl:
                // Off the left edge: end of the previous line.
                return _cursorRow == 0
                    ? this
                    : new TextAreaState(_lines, _cursorRow - 1, _lines[_cursorRow - 1].Length, MaxLines);

            case ConsoleKey.RightArrow when _cursorCol == CurrentLine.Length && !key.Ctrl:
                // Off the right edge: start of the next line.
                return _cursorRow >= _lines.Count - 1
                    ? this
                    : new TextAreaState(_lines, _cursorRow + 1, 0, MaxLines);

            case ConsoleKey.Backspace when _cursorCol == 0:
                return JoinWithPrevious();

            case ConsoleKey.Delete when _cursorCol == CurrentLine.Length:
                return JoinWithNext();
        }

        // Everything else is editing within one line, which TextInputState already does.
        var before = new TextInputState(CurrentLine, _cursorCol);
        var after = before.HandleKey(key);
        if (ReferenceEquals(after, before)) return this;

        return new TextAreaState(ReplaceLine(_cursorRow, after.Value), _cursorRow, after.Cursor, MaxLines);
    }

    // ── Explicit edits ────────────────────────────────────────────────────────

    /// <summary>
    /// Split the current line at the cursor, moving the tail onto a new line below and
    /// putting the cursor at its start. A no-op at <see cref="MaxLines"/>.
    /// </summary>
    public TextAreaState SplitLine()
    {
        if (MaxLines > 0 && _lines.Count >= MaxLines) return this;

        var line = CurrentLine;
        var next = new List<string>(_lines.Count + 1);
        for (int i = 0; i < _lines.Count; i++)
        {
            if (i != _cursorRow) { next.Add(_lines[i]); continue; }
            next.Add(line[.._cursorCol]);
            next.Add(line[_cursorCol..]);
        }
        return new TextAreaState(next, _cursorRow + 1, 0, MaxLines);
    }

    /// <summary>
    /// Append the current line to the previous one and remove it, leaving the cursor at
    /// the join. A no-op on the first line.
    /// </summary>
    public TextAreaState JoinWithPrevious()
    {
        if (_cursorRow == 0) return this;

        int landing = _lines[_cursorRow - 1].Length;
        var next = new List<string>(_lines);
        next[_cursorRow - 1] += next[_cursorRow];
        next.RemoveAt(_cursorRow);
        return new TextAreaState(next, _cursorRow - 1, landing, MaxLines);
    }

    /// <summary>
    /// Append the next line to the current one and remove it, leaving the cursor where it
    /// was. A no-op on the last line.
    /// </summary>
    public TextAreaState JoinWithNext()
    {
        if (_cursorRow >= _lines.Count - 1) return this;

        var next = new List<string>(_lines);
        next[_cursorRow] += next[_cursorRow + 1];
        next.RemoveAt(_cursorRow + 1);
        return new TextAreaState(next, _cursorRow, _cursorCol, MaxLines);
    }

    /// <summary>
    /// Insert <paramref name="text"/> at the cursor, splitting into new lines on every
    /// line feed. Use this for paste, where text arrives as a block rather than as keys.
    /// </summary>
    /// <param name="text">Text to insert. Null or empty leaves the state unchanged.</param>
    public TextAreaState Insert(string text)
    {
        if (string.IsNullOrEmpty(text)) return this;

        var parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (parts.Length == 1)
        {
            var edited = new TextInputState(CurrentLine, _cursorCol).Insert(text);
            return new TextAreaState(ReplaceLine(_cursorRow, edited.Value), _cursorRow, edited.Cursor, MaxLines);
        }

        var line = CurrentLine;
        string head = line[.._cursorCol], tail = line[_cursorCol..];

        var next = new List<string>(_lines.Count + parts.Length);
        for (int i = 0; i < _cursorRow; i++) next.Add(_lines[i]);
        next.Add(head + parts[0]);
        for (int i = 1; i < parts.Length - 1; i++) next.Add(parts[i]);
        next.Add(parts[^1] + tail);
        for (int i = _cursorRow + 1; i < _lines.Count; i++) next.Add(_lines[i]);

        // MaxLines caps a paste rather than rejecting it: the document keeps what fits.
        if (MaxLines > 0 && next.Count > MaxLines) next.RemoveRange(MaxLines, next.Count - MaxLines);

        return new TextAreaState(next, _cursorRow + parts.Length - 1, parts[^1].Length, MaxLines);
    }

    /// <summary>Empty the document and reset the cursor.</summary>
    public TextAreaState Clear() =>
        IsEmpty && _cursorRow == 0 && _cursorCol == 0 ? this : new TextAreaState(null, 0, 0, MaxLines);

    /// <inheritdoc/>
    public override string ToString() => Text();

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Move to <paramref name="row"/>, keeping the column where it can sit on that line.
    /// The column is re-snapped to a cluster boundary by the constructor, so landing
    /// inside an emoji on a shorter line is not possible.
    /// </summary>
    private TextAreaState MoveToRow(int row) =>
        new(_lines, row, Math.Min(_cursorCol, _lines[row].Length), MaxLines);

    private List<string> ReplaceLine(int row, string text)
    {
        var next = new List<string>(_lines);
        next[row] = text;
        return next;
    }

    /// <summary>
    /// Reconciles the document and the cursor together. Every constructor and every init
    /// accessor funnels through here, so no combination of them can produce a cursor off
    /// the end of the document, past the end of its line, or inside a grapheme cluster.
    /// </summary>
    private static (IReadOnlyList<string> Lines, int Row, int Col) Normalize(
        IReadOnlyList<string>? lines, int row, int col)
    {
        // A document always has a line to put the cursor on.
        IReadOnlyList<string> safe = lines is { Count: > 0 } ? lines : [""];

        // A null entry would crash the first indexer that touched it; treat it as empty.
        for (int i = 0; i < safe.Count; i++)
        {
            if (safe[i] is not null) continue;
            var patched = new List<string>(safe);
            for (int j = 0; j < patched.Count; j++) patched[j] ??= "";
            safe = patched;
            break;
        }

        row = Math.Clamp(row, 0, safe.Count - 1);

        // Reuse TextInputState's cluster-boundary snapping rather than repeating it: the
        // column means the same thing there, on the line it belongs to.
        col = new TextInputState(safe[row], col).Cursor;

        return (safe, row, col);
    }
}
