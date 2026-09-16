namespace ConsoleForge.Core;

/// <summary>
/// Selection and scroll as a pure value: a count, an index, an offset, a viewport,
/// and one function from a <see cref="KeyMsg"/> to the next state.
/// </summary>
/// <remarks>
/// <para>
/// The rules — clamping a selection to the list, keeping it inside the visible window,
/// refusing to scroll past the last useful row — are written once here instead of in
/// every model that owns a list. <c>List</c> delegates to it, so the widget and the
/// reducer cannot drift.
/// </para>
/// <code>
/// sealed record Model(ListState Rows) : IModel
/// {
///     public (IModel, ICmd?) Update(IMsg msg) => msg switch
///     {
///         WindowResizeMsg r => (this with { Rows = Rows.WithViewport(r.Height - Chrome) }, null),
///         KeyMsg k          => (this with { Rows = Rows.HandleKey(k) }, null),
///         _                 => (this, null),
///     };
/// }
/// </code>
/// <para>
/// The state does <em>not</em> hold the items. It holds their <see cref="Count"/>, so it
/// serves a string array, a filtered view, or virtualised pages equally — the model keeps
/// the data and indexes it with <see cref="SelectedIndex"/>, or slices it with
/// <see cref="VisibleRange"/>.
/// </para>
/// <para>
/// <see cref="ViewportHeight"/> is the number of visible rows, which is a layout result
/// rather than model state — a widget only learns its region at render time. Set it from
/// whatever the application knows (a resize message, a fixed pane height) and leave it 0
/// when nothing knows it yet: scroll is then left exactly as the caller set it, and paging
/// does nothing, because a page has no size.
/// </para>
/// <para>Bindings handled by <see cref="HandleKey"/>:</para>
/// <list type="table">
///   <item><term>Up / Down</term><description>move one row</description></item>
///   <item><term>PageUp / PageDown</term><description>move one viewport, or nothing when the viewport is unknown</description></item>
///   <item><term>Home / End</term><description>first, last</description></item>
/// </list>
/// <para>
/// Enter is not handled: what a selection <em>means</em> belongs to the model, the same
/// way <see cref="TextInputState"/> emits nothing of its own.
/// </para>
/// </remarks>
public sealed record ListState
{
    private readonly int _count;
    private readonly int _selectedIndex;
    private readonly int _scrollOffset;
    private readonly int _viewportHeight;

    /// <summary>
    /// Number of items the selection ranges over. Never negative. Changing it pulls
    /// <see cref="SelectedIndex"/> and <see cref="ScrollOffset"/> back into range.
    /// </summary>
    public int Count
    {
        get => _count;
        init
        {
            (_count, _selectedIndex, _scrollOffset, _viewportHeight)
                = Normalize(value, _selectedIndex, _scrollOffset, _viewportHeight);
        }
    }

    /// <summary>
    /// Highlighted row, always within <c>[0, Count - 1]</c>. Reads 0 for an empty list;
    /// check <see cref="IsEmpty"/> before using it to index.
    /// </summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        init
        {
            (_count, _selectedIndex, _scrollOffset, _viewportHeight)
                = Normalize(_count, value, _scrollOffset, _viewportHeight);
        }
    }

    /// <summary>
    /// Index of the first visible row. Maintained automatically whenever the viewport is
    /// known, so the selection cannot scroll out of sight.
    /// </summary>
    public int ScrollOffset
    {
        get => _scrollOffset;
        init
        {
            (_count, _selectedIndex, _scrollOffset, _viewportHeight)
                = Normalize(_count, _selectedIndex, value, _viewportHeight);
        }
    }

    /// <summary>
    /// Number of rows visible at once. 0 means not yet known — scroll is then left alone
    /// and paging is a no-op. Setting it re-scrolls to bring the selection into view.
    /// </summary>
    public int ViewportHeight
    {
        get => _viewportHeight;
        init
        {
            (_count, _selectedIndex, _scrollOffset, _viewportHeight)
                = Normalize(_count, _selectedIndex, _scrollOffset, value);
        }
    }

    /// <summary>An empty list: nothing to select, nothing to scroll.</summary>
    public ListState() { }

    /// <summary>
    /// A list of <paramref name="count"/> items. Every argument is reconciled against the
    /// others, so no combination produces a broken state.
    /// </summary>
    /// <param name="count">Number of items. Negative is treated as empty.</param>
    /// <param name="selectedIndex">Highlighted row; clamped into range.</param>
    /// <param name="scrollOffset">First visible row; clamped, then adjusted to show the selection.</param>
    /// <param name="viewportHeight">Visible rows; 0 when unknown.</param>
    public ListState(int count, int selectedIndex = 0, int scrollOffset = 0, int viewportHeight = 0)
    {
        (_count, _selectedIndex, _scrollOffset, _viewportHeight)
            = Normalize(count, selectedIndex, scrollOffset, viewportHeight);
    }

    /// <summary>True when there is nothing to select.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>
    /// The slice a view should draw: where to start in the caller's items, and how many
    /// to take. <c>Length</c> is the whole remainder when the viewport is unknown.
    /// </summary>
    public (int Offset, int Length) VisibleRange
    {
        get
        {
            int remaining = _count - _scrollOffset;
            int length = _viewportHeight > 0 ? Math.Min(_viewportHeight, remaining) : remaining;
            return (_scrollOffset, Math.Max(0, length));
        }
    }

    // ── The reducer ───────────────────────────────────────────────────────────

    /// <summary>
    /// Apply one key press. Returns this same instance when the key is not a navigation
    /// key or the move would change nothing, so a caller can compare by reference to
    /// detect "nothing happened".
    /// </summary>
    /// <param name="key">The key event to interpret.</param>
    public ListState HandleKey(KeyMsg key)
    {
        if (key is null) return this;

        return key.Key switch
        {
            ConsoleKey.UpArrow => MoveUp(),
            ConsoleKey.DownArrow => MoveDown(),
            // A page is the viewport. With no viewport it has no size, and moving a
            // single row instead would be a quiet lie about what the key did.
            ConsoleKey.PageUp => _viewportHeight > 0 ? MoveUp(_viewportHeight) : this,
            ConsoleKey.PageDown => _viewportHeight > 0 ? MoveDown(_viewportHeight) : this,
            ConsoleKey.Home => MoveTo(0),
            ConsoleKey.End => MoveTo(_count - 1),
            _ => this,
        };
    }

    // ── Explicit moves ────────────────────────────────────────────────────────

    /// <summary>Move the selection towards the start, stopping at the first row.</summary>
    /// <param name="by">Rows to move. Negative is treated as 0.</param>
    public ListState MoveUp(int by = 1) => MoveTo(_selectedIndex - Math.Max(0, by));

    /// <summary>Move the selection towards the end, stopping at the last row.</summary>
    /// <param name="by">Rows to move. Negative is treated as 0.</param>
    public ListState MoveDown(int by = 1) => MoveTo(_selectedIndex + Math.Max(0, by));

    /// <summary>
    /// Select <paramref name="index"/>, clamped into range, scrolling it into view when
    /// the viewport is known.
    /// </summary>
    public ListState MoveTo(int index)
    {
        var n = Normalize(_count, index, _scrollOffset, _viewportHeight);
        return n.Selected == _selectedIndex && n.Scroll == _scrollOffset
            ? this
            : new ListState(_count, n.Selected, n.Scroll, _viewportHeight);
    }

    /// <summary>
    /// The list gained or lost items. Selection and scroll are pulled back into range;
    /// a selection past the new end lands on the last row.
    /// </summary>
    public ListState WithCount(int count)
    {
        int c = Math.Max(0, count);
        return c == _count ? this : this with { Count = c };
    }

    /// <summary>
    /// The visible row count changed — a resize, or a layout that finally knows its
    /// region. Re-scrolls to keep the selection visible.
    /// </summary>
    public ListState WithViewport(int rows)
    {
        int v = Math.Max(0, rows);
        return v == _viewportHeight ? this : this with { ViewportHeight = v };
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reconciles all four fields at once. Every constructor and every init accessor
    /// funnels through here, so no combination of them can produce a state that violates
    /// the invariants — a selection outside the list, an offset that hides the selection,
    /// or an offset past the last useful row.
    /// </summary>
    private static (int Count, int Selected, int Scroll, int Viewport) Normalize(
        int count, int selected, int scroll, int viewport)
    {
        // The two inputs nothing else can be derived without.
        count = Math.Max(0, count);
        viewport = Math.Max(0, viewport);

        // An empty list has no selectable row. SelectedIndex reads 0 there rather than
        // -1, so arithmetic on it stays safe; IsEmpty is the guard before indexing.
        selected = count == 0 ? 0 : Math.Clamp(selected, 0, count - 1);

        // Scroll ceiling. With a viewport, the last useful offset is count - viewport:
        // going further only buys blank rows under the final item. Without one, all we
        // can say is that the offset has to name a real item.
        int maxScroll = viewport > 0
            ? Math.Max(0, count - viewport)
            : Math.Max(0, count - 1);
        scroll = Math.Clamp(scroll, 0, maxScroll);

        // This cannot re-break the ceiling above: selected is at most count - 1, so the
        // largest offset it can ask for is count - viewport.
        scroll = EnsureVisible(selected, scroll, viewport);

        return (count, selected, scroll, viewport);
    }

    /// <summary>
    /// Slide <paramref name="scroll"/> the minimum distance that brings
    /// <paramref name="selected"/> inside the window. Only expressible with a viewport;
    /// with none the caller's offset stands.
    /// </summary>
    internal static int EnsureVisible(int selected, int scroll, int viewport)
    {
        if (viewport <= 0) return scroll;
        if (selected < scroll) return selected;                      // window below it — pull up
        if (selected >= scroll + viewport) return selected - viewport + 1;  // window above it — pull down
        return scroll;
    }
}