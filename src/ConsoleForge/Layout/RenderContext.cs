using System.Text;
using ConsoleForge.Styling;

namespace ConsoleForge.Layout;

/// <summary>
/// Concrete implementation of <see cref="IRenderContext"/> backed by a
/// cell buffer where each cell stores a pre-rendered ANSI string for one character.
/// <para>
/// Supports double-buffering: <see cref="ToAnsiFrame"/> diffs the current cell buffer
/// against the previous frame and emits only changed cells, then swaps the buffers.
/// On the first frame (no previous buffer) all cells are emitted.
/// </para>
/// <para>
/// Call <see cref="Reset"/> before each frame to clear the current buffer and update
/// the region. The previous buffer is untouched until <see cref="ToAnsiFrame"/> swaps.
/// </para>
/// </summary>
public sealed class RenderContext : IRenderContext
{
    // Sentinel stored in the cell immediately to the right of any wide (2-column) glyph.
    // Using a dedicated reference-identical object lets ToAnsiFrame detect wide-char
    // right-halves with ReferenceEquals — no string comparison, no ambiguity with a
    // styled space that happens to contain a literal " ".
    internal static readonly string WideCharSpacer = new(' ', 1);

    // Double buffer: _cells = current frame being written; _prev = last emitted frame.
    private string[] _cells;
    private string[]? _prev; // null = no previous frame (first render)
    private int _prevWidth;
    private int _prevHeight;

    // Widget render cache: flat arrays for widget→region map from previous frame.
    // Used by Container.Render to skip re-rendering unchanged model-stored widgets.
    // Flat arrays are cheaper than Dictionary for typical widget counts (<100).
    private IWidget?[]? _prevWidgets;
    private Region[]?   _prevRegions;
    private int         _prevWidgetCount;
    // Lazy-allocated on first RegisterWidget call — leaf widgets (TextBlock,
    // TextInput, ProgressBar, etc.) never call Register so they pay nothing.
    private IWidget[]?  _curWidgets;
    private Region[]?   _curRegions;
    private int         _curWidgetCount;

    public Region         Region       { get; private set; }
    /// <summary>Active theme for style inheritance. Updated by <see cref="Reset"/>.</summary>
    public Theme          Theme        { get; private set; }
    public ColorProfile   ColorProfile { get; private set; }
    public ResolvedLayout Layout       { get; private set; }

    /// <summary>
    /// Initialises a fresh render context for a single full-redraw frame.
    /// </summary>
    /// <param name="region">The terminal region this context covers.</param>
    /// <param name="theme">Active visual theme.</param>
    /// <param name="colorProfile">ANSI color output profile.</param>
    /// <param name="layout">Pre-resolved widget-to-region layout map.</param>
    public RenderContext(Region region, Theme theme, ColorProfile colorProfile, ResolvedLayout layout)
    {
        Region       = region;
        Theme        = theme;
        ColorProfile = colorProfile;
        Layout       = layout;
        _cells = new string[region.Width * region.Height];
    }

    /// <summary>
    /// Prepare this context for a new frame. Clears the current cell buffer
    /// (so stale content from last frame is not present) and updates Region/Layout/Theme.
    /// The previous frame buffer is preserved for diffing in <see cref="ToAnsiFrame"/>.
    /// If terminal dimensions changed, the previous buffer is discarded (forces full redraw).
    /// </summary>
    public void Reset(Region region, Theme theme, ColorProfile colorProfile, ResolvedLayout layout)
    {
        bool sizeChanged  = region.Width != Region.Width || region.Height != Region.Height;
        bool themeChanged = !ReferenceEquals(theme, Theme);

        Region       = region;
        Theme        = theme;
        ColorProfile = colorProfile;
        Layout       = layout;

        if (sizeChanged)
        {
            _cells = new string[region.Width * region.Height];
            _prev  = null;
            _prevWidgets = null;
            _prevWidgetCount = 0;
        }
        else
        {
            Array.Clear(_cells, 0, _cells.Length);
        }

        // Swap widget maps: current → previous.
        _prevWidgets     = _curWidgets;   // may be null if no composites were rendered
        _prevRegions     = _curRegions;
        _prevWidgetCount = _curWidgetCount;
        _curWidgetCount  = 0;
        _curWidgets      = null;          // will be lazy-allocated by next RegisterWidget
        _curRegions      = null;

        // Invalidate widget cache AFTER the swap so TryReuseWidget cannot
        // serve stale cells from the old theme. Setting null here means the
        // now-swapped _prevWidgets is discarded; all widgets render fresh.
        if (themeChanged)
        {
            _prevWidgets     = null;
            _prevWidgetCount = 0;
        }
    }

    /// <summary>
    /// Write a styled string at absolute terminal position (col, row).
    /// Clips text that extends beyond or starts before the Region bounds.
    /// Each visible character is stored as a pre-rendered styled cell.
    /// Uses Rune-based enumeration to avoid StringInfo allocations per character.
    /// ASCII fast path skips grapheme cluster logic entirely.
    /// </summary>
    public void Write(int col, int row, string text, Style style)
    {
        if (row < Region.Row || row >= Region.Row + Region.Height) return;
        if (col >= Region.Col + Region.Width) return;
        if (text.Length == 0) return;

        int cellRow = row - Region.Row;
        int cellOffset = 0; // column offset within the string

        // ASCII fast path: no surrogate pairs, no wide chars — tight inner loop
        if (IsAscii(text))
        {
            for (int i = 0; i < text.Length; i++)
            {
                int cellCol = col - Region.Col + cellOffset;
                if (cellCol < 0) { cellOffset++; continue; }
                if (cellCol >= Region.Width) break;

                int idx = cellRow * Region.Width + cellCol;
                _cells[idx] = style.RenderChar(text[i], ColorProfile);
                cellOffset++;
            }
            return;
        }

        // General path: enumerate runes (handles multi-codepoint graphemes via scalar fallback)
        foreach (Rune rune in text.EnumerateRunes())
        {
            string element = rune.ToString();
            int cellCol = col - Region.Col + cellOffset;
            int width = RuneDisplayWidth(rune);

            // Wide character straddles the left edge: write a space in the first visible cell
            if (cellCol == -1 && width == 2)
            {
                _cells[cellRow * Region.Width] = style.RenderChar(' ', ColorProfile);
                cellOffset += width;
                continue;
            }

            if (cellCol < 0) { cellOffset += width; continue; }
            if (cellCol >= Region.Width) break;

            int idx = cellRow * Region.Width + cellCol;
            _cells[idx] = style.Render(element, ColorProfile);

            if (width == 2 && cellCol + 1 < Region.Width)
                _cells[idx + 1] = WideCharSpacer; // sentinel: right half of wide glyph

            cellOffset += width;
        }
    }

    /// <summary>
    /// Returns true if every character in the string is ASCII (< 128).
    /// These strings have no surrogate pairs and no wide Unicode characters.
    /// </summary>
    private static bool IsAscii(string text)
    {
        foreach (char c in text)
            if (c >= 128) return false;
        return true;
    }

    /// <summary>
    /// Return the terminal display width of a Rune. Delegates to <see cref="TextUtils.RuneDisplayWidth"/>.
    /// </summary>
    private static int RuneDisplayWidth(Rune rune) => TextUtils.RuneDisplayWidth(rune);

    /// <summary>
    /// Create a sub-context restricted to a sub-region of this context.
    /// Writes to the sub-context are forwarded to this context with adjusted coordinates.
    /// </summary>
    public SubRenderContext CreateSub(Region subRegion) => new(this, subRegion);

    // ── Widget render cache ────────────────────────────────────────────────────

    /// <summary>
    /// Record that <paramref name="widget"/> was rendered at <paramref name="region"/> this frame.
    /// Called by <see cref="Widgets.Container"/> after rendering each child.
    /// </summary>
    public void RegisterWidget(IWidget widget, Region region)
    {
        // Lazy-allocate on first use. Reuse the prev arrays as the new cur
        // buffer when available — avoids a fresh allocation every frame.
        if (_curWidgets is null)
        {
            if (_prevWidgets is not null && _prevWidgets.Length >= 32)
            {
                _curWidgets  = _prevWidgets!;
                _curRegions  = _prevRegions!;
                _prevWidgets = null;   // prevent double-use as both cur and prev
                _prevRegions = null;
            }
            else
            {
                _curWidgets = new IWidget[32];
                _curRegions = new Region[32];
            }
        }
        else if (_curWidgetCount >= _curWidgets.Length)
        {
            int newLen = _curWidgets.Length * 2;
            Array.Resize(ref _curWidgets, newLen);
            Array.Resize(ref _curRegions, newLen);
        }

        _curWidgets[_curWidgetCount]  = widget;
        _curRegions![_curWidgetCount] = region;
        _curWidgetCount++;
    }

    /// <summary>
    /// If <paramref name="widget"/> (same object reference) was rendered at exactly
    /// <paramref name="region"/> last frame AND the previous cell buffer exists,
    /// copy those cells into the current buffer and return <see langword="true"/>
    /// (caller should skip rendering). Otherwise return <see langword="false"/>.
    /// </summary>
    public bool TryReuseWidget(IWidget widget, Region region)
    {
        if (_prev is null || _prevWidgets is null) return false;

        // Linear scan — typical widget count < 100, faster than Dictionary for small N.
        for (int i = 0; i < _prevWidgetCount; i++)
        {
            if (!ReferenceEquals(_prevWidgets[i], widget)) continue;
            if (_prevRegions![i] != region) continue;

            // Match! Copy cells from previous buffer.
            int w = Region.Width;
            int rStart = region.Row - Region.Row;
            int rEnd   = rStart + region.Height;
            int cStart = region.Col - Region.Col;
            int cEnd   = Math.Min(cStart + region.Width, w);
            for (int r = rStart; r < rEnd; r++)
            {
                if (r < 0 || r >= Region.Height) continue;
                for (int c = cStart; c < cEnd; c++)
                {
                    int idx = r * w + c;
                    if ((uint)idx < (uint)_cells.Length)
                        _cells[idx] = _prev[idx];
                }
            }

            RegisterWidget(widget, region);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Produce a minimal ANSI frame string by diffing current cell buffer against
    /// the previous frame. Only emits cells that changed. On first frame (no previous
    /// buffer) emits all cells.
    /// Pre-sizes StringBuilder to avoid repeated reallocations.
    /// After emitting, swaps current ↔ previous buffers.
    /// </summary>
    public string ToAnsiFrame()
    {
        bool fullRedraw = _prev is null
            || _prevWidth  != Region.Width
            || _prevHeight != Region.Height;

        // Pre-compute the themed default cell: a space rendered with the theme's base style.
        // Any cell not written to by a widget will show this instead of a raw unstyled space,
        // giving the entire terminal a uniform background colour.
        var defaultCell = Theme.BaseStyle.RenderChar(' ', ColorProfile);

        int capacity = fullRedraw
            ? 6 + Region.Height * (10 + Region.Width * 6)
            : Region.Width * Region.Height;

        var sb = new StringBuilder(capacity);

        int w = Region.Width;
        int h = Region.Height;

        int lastEmittedRow = -1;
        int lastEmittedCol = -1;

        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int idx = r * w + c;
                var cell = _cells[idx];

                // Wide-char spacer: the bare " " string written into _cells[idx+1] by the
                // Write() method to mark the right half of a 2-column glyph. The terminal
                // positions the glyph's second column automatically when it renders the
                // wide character at idx; we must NOT emit a cursor-move + space here
                // because after a wide char the hardware cursor is at c+2, not c+1.
                // Emitting without a move would land the space one column too far right
                // and leave the old content at c+1 as a ghost. Skip entirely.
                if (ReferenceEquals(cell, WideCharSpacer))
                    continue;

                string cellContent = cell is { Length: > 0 } ? cell : defaultCell;

                // Skip unchanged cells (diff against previous frame)
                if (!fullRedraw && _prev![idx] is { } prevCell)
                {
                    string prevContent = prevCell is { Length: > 0 } ? prevCell : defaultCell;
                    if (cellContent == prevContent) continue;
                }

                // Only emit cursor move if position is not the next expected column
                bool needsMove = (r != lastEmittedRow) || (c != lastEmittedCol);
                if (needsMove)
                {
                    sb.Append("\x1b[");
                    sb.Append(Region.Row + r + 1);
                    sb.Append(';');
                    sb.Append(Region.Col + c + 1);
                    sb.Append('H');
                }

                sb.Append(cellContent);
                lastEmittedRow = r;

                // After emitting a wide glyph the hardware cursor is 2 columns ahead.
                // Detect: the very next cell holds the WideCharSpacer sentinel.
                bool isWide = (c + 1 < w) && ReferenceEquals(_cells[idx + 1], WideCharSpacer);
                lastEmittedCol = c + (isWide ? 2 : 1);
            }
        }

        // Swap buffers: current → previous; old previous → current (reused, cleared by Reset next frame)
        var oldPrev = _prev;
        _prev       = _cells;
        _prevWidth  = w;
        _prevHeight = h;
        // Reuse the old prev array as next frame's current buffer (Reset() will clear it)
        _cells = oldPrev ?? new string[w * h];

        return sb.ToString();
    }
}