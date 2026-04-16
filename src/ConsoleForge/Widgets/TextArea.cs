using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A multi-line text input widget.
/// Cursor position and scroll state live in the model (all properties are
/// <c>{ get; init; }</c>) — use the <see cref="TextAreaChangedMsg"/> returned from
/// <see cref="OnKeyEvent"/> to derive the next widget state via <c>with</c>.
/// </summary>
/// <remarks>
/// <para><b>Scroll</b> — The widget renders lines
/// <c>[ScrollRow, ScrollRow + visibleHeight)</c>. Update <see cref="ScrollRow"/> in your
/// model's Update handler; use <see cref="ComputeScrollRow"/> as a helper.</para>
/// <para><b>Line endings</b> — All text is stored as a list of strings (one per logical
/// line). The widget does not produce or consume <c>\n</c> in its messages.</para>
/// </remarks>
public sealed class TextArea : IFocusable
{
    // ── IFocusable ───────────────────────────────────────────────────────────
    /// <inheritdoc/>
    public bool HasFocus { get; set; }

    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
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
    /// Update via <see cref="ComputeScrollRow"/> when handling <see cref="TextAreaChangedMsg"/>.
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
        Lines     = lines is { Count: > 0 } ? lines : (IReadOnlyList<string>)[""];
        CursorRow = Math.Clamp(cursorRow, 0, Lines.Count - 1);
        CursorCol = Math.Clamp(cursorCol, 0, Lines[CursorRow].Length);
        ScrollRow = Math.Max(0, scrollRow);
        MaxLines  = maxLines;
        if (style is not null) Style = style.Value;
    }

    // ── Key handling ─────────────────────────────────────────────────────────

    /// <summary>
    /// Process a key event and dispatch a <see cref="TextAreaChangedMsg"/> with the
    /// new document state. The model should replace this widget instance with one
    /// constructed from the message fields using <c>with</c> expressions.
    /// </summary>
    public void OnKeyEvent(KeyMsg key, Action<IMsg> dispatch)
    {
        var mutableLines = Lines.ToList();
        var row = CursorRow;
        var col = CursorCol;

        switch (key.Key)
        {
            // ── Navigation ─────────────────────────────────────────────────
            case ConsoleKey.LeftArrow:
                if (col > 0)
                    col--;
                else if (row > 0)
                {
                    row--;
                    col = mutableLines[row].Length;
                }
                break;

            case ConsoleKey.RightArrow:
                if (col < mutableLines[row].Length)
                    col++;
                else if (row < mutableLines.Count - 1)
                {
                    row++;
                    col = 0;
                }
                break;

            case ConsoleKey.UpArrow:
                if (row > 0)
                {
                    row--;
                    col = Math.Min(col, mutableLines[row].Length);
                }
                break;

            case ConsoleKey.DownArrow:
                if (row < mutableLines.Count - 1)
                {
                    row++;
                    col = Math.Min(col, mutableLines[row].Length);
                }
                break;

            case ConsoleKey.Home:
                col = 0;
                break;

            case ConsoleKey.End:
                col = mutableLines[row].Length;
                break;

            case ConsoleKey.PageUp:
                row = Math.Max(0, row - 10);
                col = Math.Min(col, mutableLines[row].Length);
                break;

            case ConsoleKey.PageDown:
                row = Math.Min(mutableLines.Count - 1, row + 10);
                col = Math.Min(col, mutableLines[row].Length);
                break;

            // ── Editing ────────────────────────────────────────────────────
            case ConsoleKey.Enter:
            {
                // No-op when MaxLines limit reached
                if (MaxLines > 0 && mutableLines.Count >= MaxLines) break;

                var tail = mutableLines[row][col..];
                mutableLines[row] = mutableLines[row][..col];
                mutableLines.Insert(row + 1, tail);
                row++;
                col = 0;
                break;
            }

            case ConsoleKey.Backspace:
                if (col > 0)
                {
                    mutableLines[row] = mutableLines[row][..(col - 1)] + mutableLines[row][col..];
                    col--;
                }
                else if (row > 0)
                {
                    // Join current line onto end of previous
                    var prevLen = mutableLines[row - 1].Length;
                    mutableLines[row - 1] += mutableLines[row];
                    mutableLines.RemoveAt(row);
                    row--;
                    col = prevLen;
                }
                break;

            case ConsoleKey.Delete:
                if (col < mutableLines[row].Length)
                {
                    mutableLines[row] = mutableLines[row][..col] + mutableLines[row][(col + 1)..];
                }
                else if (row < mutableLines.Count - 1)
                {
                    // Join next line onto end of current
                    mutableLines[row] += mutableLines[row + 1];
                    mutableLines.RemoveAt(row + 1);
                }
                break;

            default:
            {
                // Printable characters
                if (key.Character is char c && !char.IsControl(c))
                {
                    mutableLines[row] = mutableLines[row][..col] + c + mutableLines[row][col..];
                    col++;
                }
                else return; // Unhandled key — dispatch nothing
                break;
            }
        }

        // Clamp to valid range after all edits
        row = Math.Clamp(row, 0, mutableLines.Count - 1);
        col = Math.Clamp(col, 0, mutableLines[row].Length);

        dispatch(new TextAreaChangedMsg(this, mutableLines.AsReadOnly(), row, col));
    }

    // ── Scroll helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compute a new <see cref="ScrollRow"/> that keeps <paramref name="cursorRow"/>
    /// within the visible viewport.
    /// Call this from your model's Update handler when handling
    /// <see cref="TextAreaChangedMsg"/> and <see cref="ConsoleForge.Core.WindowResizeMsg"/>.
    /// </summary>
    /// <param name="cursorRow">The cursor row after the edit.</param>
    /// <param name="viewportHeight">Number of visible rows in the TextArea's region.</param>
    /// <param name="currentScrollRow">Current scroll offset.</param>
    /// <returns>Adjusted scroll row ensuring cursor is visible.</returns>
    public static int ComputeScrollRow(int cursorRow, int viewportHeight, int currentScrollRow)
    {
        if (viewportHeight <= 0) return currentScrollRow;

        if (cursorRow < currentScrollRow)
            return cursorRow;

        if (cursorRow >= currentScrollRow + viewportHeight)
            return cursorRow - viewportHeight + 1;

        return currentScrollRow;
    }

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
        var cursorStyle    = effectiveStyle.Reverse(true);
        var fill           = new string(' ', region.Width);

        for (var rowOffset = 0; rowOffset < region.Height; rowOffset++)
        {
            var lineIdx = ScrollRow + rowOffset;
            var absRow  = region.Row + rowOffset;

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
                var cursorChar = CursorCol < line.Length
                    ? line[CursorCol].ToString()
                    : " ";
                ctx.Write(region.Col + cursorScreenCol, absRow, cursorChar, cursorStyle);
            }
        }
    }
}

/// <summary>
/// Dispatched when a <see cref="TextArea"/> document or cursor position changes.
/// The model should create a new TextArea via:
/// <code>
/// textArea with {
///     Lines     = msg.NewLines,
///     CursorRow = msg.NewCursorRow,
///     CursorCol = msg.NewCursorCol,
///     ScrollRow = TextArea.ComputeScrollRow(msg.NewCursorRow, viewportHeight, textArea.ScrollRow)
/// }
/// </code>
/// </summary>
public sealed record TextAreaChangedMsg(
    TextArea Source,
    IReadOnlyList<string> NewLines,
    int NewCursorRow,
    int NewCursorCol) : IMsg;
