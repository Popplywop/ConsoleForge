using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>Defines a single column in a <see cref="Table"/> widget.</summary>
/// <param name="Header">Column header text.</param>
/// <param name="Width">
/// Column width in characters. Use 0 to distribute remaining space equally
/// among all zero-width columns.
/// </param>
/// <param name="Style">Optional per-column style for the data cells.</param>
public sealed record TableColumn(string Header, int Width = 0, Style? Style = null);

/// <summary>
/// A tabular data widget. Renders a header row followed by data rows.
/// Columns are defined by <see cref="Columns"/>; each row is a string array
/// whose elements correspond to each column in order.
/// </summary>
/// <remarks>
/// Columns with <c>Width == 0</c> share the remaining horizontal space equally.
/// Content that exceeds a column's width is truncated. No scrolling — clip to
/// the available height. For scrolling, wrap in a scrollable <see cref="Container"/>.
/// </remarks>
public sealed class Table : IWidget
{
    // ── Render-cache constants ────────────────────────────────────────────────

    // Columns beyond this threshold fall back to a rented array instead of stackalloc.
    private const int StackAllocThreshold = 64;

    // ── IWidget ─────────────────────────────────────────────────────────────
    /// <summary>Horizontal size constraint. Defaults to <see cref="SizeConstraint.Flex(int)"/> weight 1 (fill available width).</summary>
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    /// <summary>Vertical size constraint. Defaults to <see cref="SizeConstraint.Flex(int)"/> weight 1 (fill available height).</summary>
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);
    /// <summary>Base style applied to the widget. Individual row and header styles override this per-section.</summary>
    public Style Style { get; init; } = Style.Default;

    // ── Table-specific ───────────────────────────────────────────────────────
    /// <summary>Column definitions. Must not be empty.</summary>
    public IReadOnlyList<TableColumn> Columns { get; init; } = [];

    /// <summary>
    /// Data rows. Each row must have the same number of elements as <see cref="Columns"/>;
    /// extra elements are ignored, missing elements are treated as empty strings.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = [];

    /// <summary>Style applied to the header row. Inherits theme base style when unset.</summary>
    public Style HeaderStyle { get; init; } = Style.Default.Bold(true);

    /// <summary>Style applied to data rows. Inherits theme base style when unset.</summary>
    public Style RowStyle { get; init; } = Style.Default;

    /// <summary>Style applied to the currently selected row (when <see cref="SelectedIndex"/> ≥ 0).</summary>
    public Style SelectedRowStyle { get; init; } = Style.Default.Reverse(true);

    /// <summary>Zero-based index of the selected row. -1 means no selection.</summary>
    public int SelectedIndex { get; init; } = -1;

    /// <summary>
    /// Character used to separate columns. Defaults to <c>'\0'</c> (no separator).
    /// When non-zero the separator is always rendered with the base row style so it
    /// never inherits the selection highlight.
    /// </summary>
    public char Separator { get; init; } = '\0';

    /// <summary>
    /// Blank columns inserted to the left of each cell's text.
    /// Provides breathing room between the column edge and the content.
    /// Defaults to <c>1</c>, matching the Bubbles table convention.
    /// </summary>
    public int PaddingLeft { get; init; } = 1;

    /// <summary>
    /// Blank columns reserved to the right of each cell's text.
    /// Defaults to <c>1</c>, matching the Bubbles table convention.
    /// </summary>
    public int PaddingRight { get; init; } = 1;

    // ── Cached render helpers (allocated once per Table instance) ────────────

    // Single-char string for the column separator — avoids Separator.ToString() per cell.
    private string? _separatorStr;
    private string SeparatorStr => _separatorStr ??= Separator.ToString();

    /// <summary>Object-initializer constructor.</summary>
    public Table() { }

    /// <summary>Positional constructor for inline usage.</summary>
    public Table(
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        int selectedIndex = -1,
        Style? headerStyle = null,
        Style? rowStyle = null,
        int paddingLeft = 1,
        int paddingRight = 1)
    {
        Columns = columns;
        Rows = rows;
        SelectedIndex = selectedIndex;
        if (headerStyle is not null) HeaderStyle = headerStyle.Value;
        if (rowStyle is not null) RowStyle = rowStyle.Value;
        PaddingLeft  = paddingLeft;
        PaddingRight = paddingRight;
    }

    // ── Render ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Renders the header row, separator line, and data rows into <paramref name="ctx"/>'s
    /// allocated region. Rows that exceed the available height are clipped.
    /// Column widths are resolved on each render; flex columns share remaining space equally.
    /// </summary>
    /// <param name="ctx">The render context providing the target region, theme, and write methods.</param>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0 || Columns.Count == 0) return;

        var colCount = Columns.Count;
        var effectiveHeader   = HeaderStyle.Inherit(ctx.Theme.BaseStyle);
        var effectiveRow      = RowStyle.Inherit(ctx.Theme.BaseStyle);
        var effectiveSelected = SelectedRowStyle.Inherit(ctx.Theme.BaseStyle);

        int renderRow = region.Row;
        int maxRow    = region.Row + region.Height;

        // ── Resolve column widths — stack-allocated for small tables ─────────
        int[]? rentedWidths = null;
        Span<int> colWidths = colCount <= StackAllocThreshold
            ? stackalloc int[colCount]
            : (rentedWidths = System.Buffers.ArrayPool<int>.Shared.Rent(colCount)).AsSpan(0, colCount);

        try
        {
            ResolveColumnWidths(region.Width, colWidths);

            // Header row — iterate Columns directly, no ToList()
            if (renderRow < maxRow)
            {
                RenderHeaderRow(ctx, region.Col, renderRow, colWidths, effectiveHeader);
                renderRow++;
            }

            // Data rows — iterate columns by index, no per-row ToList()
            for (var i = 0; i < Rows.Count && renderRow < maxRow; i++, renderRow++)
            {
                var style = i == SelectedIndex ? effectiveSelected : effectiveRow;
                RenderDataRow(ctx, region.Col, renderRow, colWidths, Rows[i], style, effectiveRow);
            }
        }
        finally
        {
            if (rentedWidths is not null)
                System.Buffers.ArrayPool<int>.Shared.Return(rentedWidths);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void ResolveColumnWidths(int totalWidth, Span<int> colWidths)
    {
        // Only reserve space for separator characters when one is actually configured.
        var separatorCount = Separator != '\0' ? Columns.Count - 1 : 0;
        var availableWidth = totalWidth - separatorCount;

        int fixedTotal = 0;
        int flexCount  = 0;
        for (var i = 0; i < Columns.Count; i++)
        {
            var w = Columns[i].Width;
            colWidths[i] = w;
            if (w > 0) fixedTotal += w;
            else        flexCount++;
        }

        int flexWidth = flexCount > 0
            ? Math.Max(0, (availableWidth - fixedTotal) / flexCount)
            : 0;

        if (flexCount > 0)
        {
            for (var i = 0; i < colWidths.Length; i++)
                if (colWidths[i] == 0) colWidths[i] = flexWidth;
        }
    }

    private void RenderHeaderRow(IRenderContext ctx, int startCol, int row,
        Span<int> colWidths, Style style)
    {
        var col    = startCol;
        var padL   = Math.Max(0, PaddingLeft);
        var padR   = Math.Max(0, PaddingRight);
        for (var i = 0; i < colWidths.Length; i++)
        {
            if (i > 0 && Separator != '\0')
            {
                // Separator always uses the base header style — never inherits selection.
                ctx.Write(col, row, SeparatorStr, style);
                col++;
            }

            var w = colWidths[i];
            if (w <= 0) continue;

            var textW = Math.Max(0, w - padL - padR);
            var text  = Columns[i].Header;
            if (text.Length > textW) text = text[..textW];
            else if (text.Length < textW) text = text.PadRight(textW);

            // Write: left-pad · text · right-pad — fills exactly w columns.
            ctx.Write(col,          row, new string(' ', padL), style);
            ctx.Write(col + padL,   row, text,                  style);
            ctx.Write(col + padL + textW, row, new string(' ', padR), style);
            col += w;
        }
    }

    private void RenderDataRow(IRenderContext ctx, int startCol, int row,
        Span<int> colWidths, IReadOnlyList<string> cells, Style rowStyle, Style baseRowStyle)
    {
        var col  = startCol;
        var padL = Math.Max(0, PaddingLeft);
        var padR = Math.Max(0, PaddingRight);
        for (var i = 0; i < colWidths.Length; i++)
        {
            if (i > 0 && Separator != '\0')
            {
                // Separator is always rendered with the neutral base row style so it
                // never gets reverse-highlighted when the row is selected.
                ctx.Write(col, row, SeparatorStr, baseRowStyle);
                col++;
            }

            var w = colWidths[i];
            if (w <= 0) continue;

            var textW = Math.Max(0, w - padL - padR);
            var text  = i < cells.Count ? cells[i] : "";
            if (text.Length > textW) text = text[..textW];
            else if (text.Length < textW) text = text.PadRight(textW);

            // Per-column style override (applied on top of rowStyle).
            var cellStyle = (Columns[i].Style?.Inherit(rowStyle)) ?? rowStyle;

            ctx.Write(col,          row, new string(' ', padL), rowStyle);
            ctx.Write(col + padL,   row, text,                  cellStyle);
            ctx.Write(col + padL + textW, row, new string(' ', padR), rowStyle);
            col += w;
        }
    }
}

/// <summary>
/// Dispatched when the selected row in a <see cref="Table"/> changes.
/// The model should replace its Table reference with <c>table with { SelectedIndex = msg.NewIndex }</c>.
/// </summary>
public sealed record TableSelectionChangedMsg(Table Source, int NewIndex) : IMsg;
