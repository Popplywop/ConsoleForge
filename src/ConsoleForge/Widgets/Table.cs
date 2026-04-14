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
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);
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

    /// <summary>Character used to separate columns. Default '│'.</summary>
    public char Separator { get; init; } = '│';

    // ── Cached render helpers (allocated once per Table instance) ────────────

    // Single-char string for the column separator — avoids Separator.ToString() per cell.
    private string? _separatorStr;
    private string SeparatorStr => _separatorStr ??= Separator.ToString();

    // Cached separator line string, valid for the last seen region width.
    private string? _cachedSepLine;
    private int     _cachedSepLineWidth = -1;

    /// <summary>Object-initializer constructor.</summary>
    public Table() { }

    /// <summary>Positional constructor for inline usage.</summary>
    public Table(
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        int selectedIndex = -1,
        Style? headerStyle = null,
        Style? rowStyle = null)
    {
        Columns = columns;
        Rows = rows;
        SelectedIndex = selectedIndex;
        if (headerStyle is not null) HeaderStyle = headerStyle.Value;
        if (rowStyle is not null) RowStyle = rowStyle.Value;
    }

    // ── Render ───────────────────────────────────────────────────────────────
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

            // Separator line — cached per region width
            if (renderRow < maxRow)
            {
                if (_cachedSepLine is null || _cachedSepLineWidth != region.Width)
                {
                    _cachedSepLine      = BuildSeparatorLine(colWidths, region.Width);
                    _cachedSepLineWidth = region.Width;
                }
                ctx.Write(region.Col, renderRow, _cachedSepLine, effectiveHeader);
                renderRow++;
            }

            // Data rows — iterate columns by index, no per-row ToList()
            for (var i = 0; i < Rows.Count && renderRow < maxRow; i++, renderRow++)
            {
                var style = i == SelectedIndex ? effectiveSelected : effectiveRow;
                RenderDataRow(ctx, region.Col, renderRow, colWidths, Rows[i], style);
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
        var separatorCount = Columns.Count - 1;
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
        var col = startCol;
        for (var i = 0; i < colWidths.Length; i++)
        {
            if (i > 0)
            {
                ctx.Write(col, row, SeparatorStr, style);
                col++;
            }

            var w = colWidths[i];
            if (w <= 0) continue;

            var text = Columns[i].Header;
            if (text.Length > w) text = text[..w];
            else if (text.Length < w) text = text.PadRight(w);

            ctx.Write(col, row, text, style);
            col += w;
        }
    }

    private void RenderDataRow(IRenderContext ctx, int startCol, int row,
        Span<int> colWidths, IReadOnlyList<string> cells, Style style)
    {
        var col = startCol;
        for (var i = 0; i < colWidths.Length; i++)
        {
            if (i > 0)
            {
                ctx.Write(col, row, SeparatorStr, style);
                col++;
            }

            var w = colWidths[i];
            if (w <= 0) continue;

            var text = i < cells.Count ? cells[i] : "";
            if (text.Length > w) text = text[..w];
            else if (text.Length < w) text = text.PadRight(w);

            // Per-column style override
            var cellStyle = (Columns[i].Style?.Inherit(style)) ?? style;
            ctx.Write(col, row, text, cellStyle);
            col += w;
        }
    }

    private string BuildSeparatorLine(Span<int> colWidths, int totalWidth)
    {
        var sb = new System.Text.StringBuilder(totalWidth);
        for (var i = 0; i < colWidths.Length; i++)
        {
            if (i > 0) sb.Append('┼');
            sb.Append('─', Math.Max(0, colWidths[i]));
        }
        // Pad or trim to exact width
        if (sb.Length > totalWidth) return sb.ToString()[..totalWidth];
        return sb.ToString().PadRight(totalWidth);
    }
}

/// <summary>
/// Dispatched when the selected row in a <see cref="Table"/> changes.
/// The model should replace its Table reference with <c>table with { SelectedIndex = msg.NewIndex }</c>.
/// </summary>
public sealed record TableSelectionChangedMsg(Table Source, int NewIndex) : IMsg;
