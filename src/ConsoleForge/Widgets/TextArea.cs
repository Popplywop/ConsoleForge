using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A multi-line text input widget.
/// Cursor position and scroll state live in the model (all properties are
/// <c>{ get; init; }</c>) — <see cref="Update"/> returns the next widget rather than
/// mutating this one, so the model stores what it returns.
/// </summary>
/// <remarks>
/// <para><b>Scroll</b> — The widget renders lines
/// <c>[ScrollRow, ScrollRow + visibleHeight)</c>. Update <see cref="ScrollRow"/> in your
/// model's Update handler; use <see cref="ComputeScrollRow"/> as a helper.</para>
/// <para><b>Line endings</b> — All text is stored as a list of strings (one per logical
/// line). The widget does not produce or consume <c>\n</c> in its messages.</para>
/// </remarks>
public sealed record TextArea : IFocusable
{
    // ── IFocusable ───────────────────────────────────────────────────────────
    /// <inheritdoc/>
    public bool HasFocus { get; init; }

    /// <inheritdoc/>
    public string? FocusKey { get; init; }

    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);

    // ── TextArea-specific ────────────────────────────────────────────────────
    /// <summary>Visual style for the text content. Inherits theme base style when unset.</summary>
    public Style Style { get; init; } = Style.Default;

    /// <summary>Lines of text. Never null; empty list = empty document.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [""];

    /// <summary>Zero-based row of the cursor within <see cref="Lines"/>.</summary>
    public int CursorRow { get; init; }

    /// <summary>Zero-based column of the cursor within the current line.</summary>
    public int CursorCol { get; init; }

    /// <summary>
    /// First line index rendered. Used for vertical scrolling.
    /// The widget cannot maintain this — it has no viewport until render time — so a model
    /// updates it with <see cref="ComputeScrollRow"/> after storing the widget
    /// <see cref="Update"/> returned.
    /// </summary>
    public int ScrollRow { get; init; }

    /// <summary>
    /// Maximum number of lines allowed. 0 = unlimited.
    /// When at the limit, Enter is a no-op.
    /// </summary>
    public int MaxLines { get; init; }

    // ── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Object-initializer constructor; all properties default.</summary>
    public TextArea() { }

    /// <summary>Positional constructor for inline usage.</summary>
    /// <param name="lines">Initial line content. Null or empty → single empty line.</param>
    /// <param name="cursorRow">Initial cursor row (clamped).</param>
    /// <param name="cursorCol">Initial cursor column (clamped).</param>
    /// <param name="scrollRow">Initial vertical scroll offset.</param>
    /// <param name="maxLines">Max line count (0 = unlimited).</param>
    /// <param name="style">Optional visual style override.</param>
    public TextArea(
        IReadOnlyList<string>? lines = null,
        int cursorRow = 0,
        int cursorCol = 0,
        int scrollRow = 0,
        int maxLines = 0,
        Style? style = null)
    {
        Lines = lines is { Count: > 0 } ? lines : [""];
        CursorRow = Math.Clamp(cursorRow, 0, Lines.Count - 1);
        CursorCol = Math.Clamp(cursorCol, 0, Lines[CursorRow].Length);
        ScrollRow = Math.Max(0, scrollRow);
        MaxLines = maxLines;
        if (style is not null) Style = style.Value;
    }

    // ── Key handling ─────────────────────────────────────────────────────────

    /// <summary>
    /// Process a key event and return the edited widget. The model stores what this
    /// returns; nothing is dispatched.
    /// </summary>
    /// <remarks>
    /// The editing rules live in <see cref="TextAreaState"/>, which in turn defers
    /// single-line editing to <see cref="TextInputState"/>. A model that keeps its own
    /// <see cref="TextAreaState"/> and one that stores the widget behave identically.
    /// <para>
    /// <see cref="ScrollRow"/> is untouched here: the widget has no viewport until render
    /// time, so a model updates it with <see cref="ComputeScrollRow"/> after storing the
    /// result.
    /// </para>
    /// </remarks>
    public (IFocusable Next, ICmd? Cmd) Update(KeyMsg key)
    {
        var before = new TextAreaState(Lines, CursorRow, CursorCol, MaxLines);
        var after = before.HandleKey(key);
        if (ReferenceEquals(after, before)) return (this, null);

        return (this with
        {
            Lines = after.Lines,
            CursorRow = after.CursorRow,
            CursorCol = after.CursorCol,
        }, null);
    }

    // ── Scroll helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compute a new <see cref="ScrollRow"/> that keeps <paramref name="cursorRow"/>
    /// within the visible viewport.
    /// Call this from your model's Update handler after storing the widget
    /// <see cref="Update"/> returned, and on <see cref="WindowResizeMsg"/>.
    /// </summary>
    /// <param name="cursorRow">The cursor row after the edit.</param>
    /// <param name="viewportHeight">Number of visible rows in the TextArea's region.</param>
    /// <param name="currentScrollRow">Current scroll offset.</param>
    /// <returns>Adjusted scroll row ensuring cursor is visible.</returns>
    /// <remarks>
    /// Shares its arithmetic with <see cref="ListState"/>, which keeps a viewport of its
    /// own and so can hold the offset consistent rather than recomputing it on demand.
    /// </remarks>
    public static int ComputeScrollRow(int cursorRow, int viewportHeight, int currentScrollRow)
        => ListState.EnsureVisible(cursorRow, currentScrollRow, viewportHeight);

    // ── Render ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders visible lines into the allocated region.
    /// Lines outside <c>[ScrollRow, ScrollRow + height)</c> are not drawn.
    /// When focused, the cursor position is highlighted with reverse-video.
    /// </summary>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        var effectiveStyle = Style.Inherit(ctx.Theme.BaseStyle);
        var fill = new string(' ', region.Width);

        for (var rowOffset = 0; rowOffset < region.Height; rowOffset++)
        {
            var lineIdx = ScrollRow + rowOffset;
            var absRow = region.Row + rowOffset;

            // Fill row background first
            ctx.Write(region.Col, absRow, fill, effectiveStyle);

            if (lineIdx >= Lines.Count) continue; // past end of document — blank row

            var line = Lines[lineIdx];

            // Clip / truncate to visible width (visual-width-aware)
            var visible = TextUtils.TruncateToWidth(line, region.Width);
            if (visible.Length > 0)
                ctx.Write(region.Col, absRow, visible, effectiveStyle);

            // Draw cursor when focused and this is the cursor row
            if (HasFocus && lineIdx == CursorRow)
            {
                var cursorScreenCol = Math.Min(CursorCol, region.Width - 1);
                ctx.SetCursorDescriptor(new(true, cursorScreenCol, absRow));
            }
        }
    }
}

/// <summary>
/// Was dispatched when a <see cref="TextArea"/> document or cursor position changed, back
/// when widgets emitted messages through a callback. Nothing raises it now —
/// <see cref="TextArea.Update"/> returns the edited widget directly.
/// </summary>
[Obsolete("Unused since widgets stopped emitting messages. TextArea.Update returns the next widget; store that instead.")]
public sealed record TextAreaChangedMsg(
    TextArea Source,
    IReadOnlyList<string> NewLines,
    int NewCursorRow,
    int NewCursorCol) : IMsg;