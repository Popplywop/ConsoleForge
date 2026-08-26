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

    // Sentinel written into every cell covered by a WriteRawEscape region.
    // ToAnsiFrame skips these cells entirely — the pixel-graphics protocol paints
    // over them visually; emitting a styled space would corrupt the image.
    // Using a distinct reference-identical object ensures ReferenceEquals detection
    // with zero string comparison cost.
    internal static readonly string RawRegionSpacer = new(' ', 1);

    // Double buffer: _cells = current frame being written; _prev = last emitted frame.
    private string[] _cells;
    private string[]? _prev; // null = no previous frame (first render)
    private int _prevWidth;
    private int _prevHeight;

    // Raw escape region side-channel: payloads registered via WriteRawEscape this frame
    // and the previous frame. Used by ToAnsiFrame for emit, hash-skip, and cleanup.
    private readonly record struct RawEntry(Region Region, IRawEscapePayload Payload, int Hash);
    private List<RawEntry>? _rawRegions;     // current frame
    private List<RawEntry>? _prevRawRegions; // previous frame (cleanup + hash-skip)

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

    public CursorDescriptor? Cursor    { get; private set; }

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
        bool themeChanged = !ReferenceEquals(theme, Theme) && !theme.Equals(Theme);
        bool styleChanged = themeChanged || colorProfile != ColorProfile;

        Region       = region;
        Theme        = theme;
        ColorProfile = colorProfile;
        Layout       = layout;
        Cursor       = null;

        // Swap raw region lists before clearing cell buffer.
        _prevRawRegions = _rawRegions;
        _rawRegions     = null;

        if (sizeChanged)
        {
            _cells = new string[region.Width * region.Height];
            _prev  = null;
            _prevWidgets     = null;
            _prevWidgetCount = 0;
            // Force raw regions to re-emit after resize — placed images are
            // cleared by the terminal when the viewport changes.
            _prevRawRegions = null;
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

        // Invalidate the caches AFTER the swap so TryReuseWidget cannot serve stale
        // cells from the old theme. Setting null here means the now-swapped
        // _prevWidgets is discarded; all widgets render fresh.
        //
        // The previous cell buffer goes too, forcing a full redraw. Both the widget
        // cells and the themed default cell behind them are re-rendered under the new
        // theme, and cells no widget writes to differ only in that default — without
        // this the diff would match them against the new default and skip repainting,
        // leaving the old background on screen.
        if (styleChanged)
        {
            _prevWidgets     = null;
            _prevWidgetCount = 0;
            _prev            = null;
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

        // General path: enumerate runes, coalescing zero-width runes (combining marks,
        // ZWJ, variation selectors) onto the preceding character. A grapheme cluster
        // occupies exactly one cell, which is how the terminal draws it — giving a
        // combining mark its own cell would shift the rest of the line one column
        // right of where the terminal actually puts it, and the frame diff would
        // never notice the drift.
        string clusterText = string.Empty;
        int clusterIdx     = -1; // cell holding the current cluster, -1 when none
        int clusterCol     = -1; // that cell's column within the region
        int clusterWidth   = 0;

        foreach (Rune rune in text.EnumerateRunes())
        {
            int width = RuneDisplayWidth(rune);

            if (width == 0)
            {
                if (clusterIdx < 0) continue; // no base character to attach to

                // U+FE0F selects emoji presentation, widening the base from 1 to 2.
                if (rune.Value == TextUtils.VariationSelector16 && clusterWidth == 1)
                {
                    clusterWidth = 2;
                    cellOffset++;
                    if (clusterCol + 1 < Region.Width)
                        _cells[clusterIdx + 1] = WideCharSpacer;
                }

                clusterText = clusterText + rune.ToString();
                _cells[clusterIdx] = style.Render(clusterText, ColorProfile);
                continue;
            }

            int cellCol = col - Region.Col + cellOffset;
            clusterIdx = -1;

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
            clusterText = rune.ToString();
            _cells[idx] = style.Render(clusterText, ColorProfile);

            clusterIdx   = idx;
            clusterCol   = cellCol;
            clusterWidth = width;

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

                // Raw region sentinel: the cell is visually covered by a pixel-graphics
                // image. Skip entirely — do not emit a space or try to diff it.
                if (ReferenceEquals(cell, RawRegionSpacer))
                    continue;

                string cellContent = cell is { Length: > 0 } ? cell : defaultCell;

                // Skip unchanged cells (diff against previous frame).
                if (!fullRedraw)
                {
                    var prevCell = _prev![idx];

                    // A sentinel means the terminal owns that cell — the right half of a
                    // wide glyph, or pixels painted by a raw escape. What it displays is
                    // not derivable from the buffer, so never assume a match: repaint.
                    bool prevIsUnknown = ReferenceEquals(prevCell, WideCharSpacer)
                                      || ReferenceEquals(prevCell, RawRegionSpacer);

                    if (!prevIsUnknown)
                    {
                        // A null entry means no widget wrote there, which renders as the
                        // themed default cell — exactly what an empty current cell renders
                        // as. Treating null as "unknown" instead would re-emit every
                        // untouched background cell on every frame.
                        string prevContent = prevCell is { Length: > 0 } ? prevCell : defaultCell;
                        if (cellContent == prevContent) continue;
                    }
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

        // ── Raw escape regions ─────────────────────────────────────────────────────
        // 1. Emit cleanup for regions present last frame but absent this frame.
        if (_prevRawRegions is not null)
        {
            foreach (var prev in _prevRawRegions)
            {
                if (!IsRawRegionInCurrentFrame(prev.Region))
                {
                    var cleanup = prev.Payload.Cleanup(prev.Region);
                    if (cleanup is not null) sb.Append(cleanup);
                }
            }
        }

        // 2. Emit current raw regions.
        //    - Hash miss  → full Encode() (upload + place).
        //    - Hash hit   → Refresh() only (cheap re-placement, no re-upload).
        if (_rawRegions is not null)
        {
            foreach (var entry in _rawRegions)
            {
                bool skip = ShouldSkipRawEmit(entry);

                // Cursor-move to region top-left (used by both paths below).
                // For DCS-passthrough payloads (Kitty/tmux) the payload
                // embeds its own cursor-move inside the DCS block; this
                // outer move is harmless there but required for non-tmux paths.
                void EmitCursorMove()
                {
                    sb.Append("\x1b[");
                    sb.Append(entry.Region.Row + 1);
                    sb.Append(';');
                    sb.Append(entry.Region.Col + 1);
                    sb.Append('H');
                }

                if (!skip)
                {
                    EmitCursorMove();
                    foreach (var seq in entry.Payload.Encode(entry.Region, ColorProfile))
                        sb.Append(seq);
                }
                else
                {
                    var refresh = entry.Payload.Refresh(entry.Region, ColorProfile);
                    if (refresh is not null)
                    {
                        EmitCursorMove();
                        foreach (var seq in refresh)
                            sb.Append(seq);
                    }
                }
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

    public void SetCursorDescriptor(CursorDescriptor cursor)
        => Cursor = cursor;

    /// <summary>
    /// Register a raw escape payload for <paramref name="region"/>.
    /// Cells covered by the region are filled with <see cref="RawRegionSpacer"/> sentinels
    /// so <see cref="ToAnsiFrame"/> skips them during the cell diff pass.
    /// The payload's sequences are emitted after the cell diff in the same frame.
    /// </summary>
    public void WriteRawEscape(Region region, IRawEscapePayload payload)
    {
        (_rawRegions ??= new()).Add(new RawEntry(region, payload, payload.ContentHash));
        SentinelFillRegion(region);
    }

    // ── Raw region helpers ───────────────────────────────────────────────────────

    /// <summary>Fill every cell in <paramref name="region"/> with <see cref="RawRegionSpacer"/>.</summary>
    private void SentinelFillRegion(Region region)
    {
        int w      = Region.Width;
        int rStart = region.Row - Region.Row;
        int rEnd   = rStart + region.Height;
        int cStart = region.Col - Region.Col;
        int cEnd   = cStart + region.Width;

        for (int r = rStart; r < rEnd; r++)
        {
            if (r < 0 || r >= Region.Height) continue;
            for (int c = cStart; c < cEnd; c++)
            {
                if (c < 0 || c >= w) continue;
                int idx = r * w + c;
                if ((uint)idx < (uint)_cells.Length)
                    _cells[idx] = RawRegionSpacer;
            }
        }
    }

    /// <summary>
    /// Returns true if <paramref name="region"/> is registered in the current frame's
    /// raw region list. Used during cleanup detection in <see cref="ToAnsiFrame"/>.
    /// </summary>
    private bool IsRawRegionInCurrentFrame(Region region)
    {
        if (_rawRegions is null) return false;
        foreach (var cur in _rawRegions)
            if (cur.Region == region) return true;
        return false;
    }

    /// <summary>
    /// Returns true if the previous frame contains a <see cref="RawEntry"/> for the same
    /// region with the same content hash — meaning the payload is unchanged and can be
    /// skipped this frame.
    /// </summary>
    private bool ShouldSkipRawEmit(RawEntry entry)
    {
        if (_prevRawRegions is null) return false;
        foreach (var prev in _prevRawRegions)
            if (prev.Region == entry.Region && prev.Hash == entry.Hash) return true;
        return false;
    }
}