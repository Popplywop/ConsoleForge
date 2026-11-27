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

    // Hashes of payloads transmitted by Preload and not yet placed: the terminal holds
    // their content, so their first appearance costs Place, not Encode. An entry is
    // consumed by that first appearance; from then on the frame-to-frame tracking above
    // takes over. Dropped on resize, with everything else the terminal is assumed to
    // hold — the cost of being wrong is one re-upload.
    private HashSet<int>? _held;

    // Hashes of payloads the terminal reported it no longer holds — evicted past its
    // storage quota. The next frame encodes them in full, wherever the tracking above
    // says they were already uploaded. Cleared by that frame.
    private HashSet<int>? _forgotten;

    // Write stamps: _stamps[i] is the sequence number of the write that last touched
    // cell i this frame (0 = untouched). Every Write, raw-escape fill and cursor
    // placement takes the next number. A widget's cached cells are the ones stamped
    // inside the span of its own render, so the cache can tell what the widget drew
    // from what was drawn under it before, or over it afterwards — an overlay that
    // was open last frame, or a neighbour that overflowed. Reset rewinds _seq.
    private int[]? _stamps; // allocated by the first cache lookup; null while nothing asks
    private int[]? _prevStamps;
    private int _seq;
    private int _cursorStamp;

    // Whether any wide glyph is in the buffer this frame (and was, last frame). Until
    // one is, no write can land on half of one, so Displace has nothing to do — which
    // keeps it off the per-cell cost of the common all-narrow screen.
    private bool _anyWide, _prevAnyWide;

    // One widget the cache may serve next frame: where it rendered, the stamps its own
    // render took, the raw payloads it registered and the cursor it placed. The last
    // two are side channels outside the cell buffer, replayed on a hit — copying the
    // cells alone would drop the cursor and let the frame delete the widget's images.
    private struct CacheEntry
    {
        public IWidget Widget;
        public Region Region;
        public int SeqStart, SeqEnd;   // SeqStart < 0: render span unknown, never served
        public int RawStart, RawEnd;   // range in that frame's _rawRegions
        public CursorDescriptor? Cursor;
    }

    // Renders in progress: pushed by a cache miss, popped by the RegisterWidget that
    // follows it, so a registration knows where its widget's render began.
    private struct PendingRender
    {
        public IWidget Widget;
        public int SeqStart, RawStart;
    }

    // Widget render cache: flat arrays for widget→entry map from previous frame.
    // Used by Container.Render to skip re-rendering unchanged model-stored widgets.
    // Flat arrays are cheaper than Dictionary for typical widget counts (<100).
    // Two buffers that ping-pong in Reset. The previous frame's map must stay
    // readable for the whole frame, so RegisterWidget may never consume it.
    private CacheEntry[]? _prevEntries;
    private int           _prevWidgetCount;
    // Lazy-allocated on first RegisterWidget call — leaf widgets (TextBlock,
    // TextInput, ProgressBar, etc.) never call Register so they pay nothing.
    private CacheEntry[]? _curEntries;
    private int           _curWidgetCount;
    private PendingRender[] _pending = new PendingRender[8];
    private int             _pendingCount;

    /// <inheritdoc/>
    public Region         Region       { get; private set; }
    /// <summary>Active theme for style inheritance. Updated by <see cref="Reset"/>.</summary>
    public Theme          Theme        { get; private set; }
    /// <inheritdoc/>
    public ColorProfile   ColorProfile { get; private set; }
    /// <inheritdoc/>
    public ResolvedLayout Layout       { get; private set; }

    /// <inheritdoc/>
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
            _cells  = new string[region.Width * region.Height];
            _stamps = null;
            _prev   = null;
            _prevStamps = null;
            // Force raw regions to re-emit after resize — placed images are
            // cleared by the terminal when the viewport changes.
            _prevRawRegions = null;
            _held           = null;
        }
        else
        {
            Array.Clear(_cells, 0, _cells.Length);
            if (_stamps is not null) Array.Clear(_stamps, 0, _stamps.Length);
        }
        _seq          = 0;
        _cursorStamp  = 0;
        _prevAnyWide  = _anyWide && _prev is not null;
        _anyWide      = false;
        _pendingCount = 0;

        // Swap widget maps: this frame writes into the array the previous frame read
        // from, and vice versa. Both stay allocated, so steady-state frames allocate
        // nothing here and _prevEntries survives the whole frame — RegisterWidget used
        // to steal it, which switched the cache off for every widget after the first.
        (_prevEntries, _curEntries) = (_curEntries, _prevEntries);
        _prevWidgetCount = _curWidgetCount;
        _curWidgetCount  = 0;

        // Invalidate the caches AFTER the swap so TryReuseWidget cannot serve stale
        // cells from the old theme or a differently-sized buffer. Zeroing the count
        // rather than dropping the array keeps the buffer available for recycling.
        //
        // On a style change the previous cell buffer goes too, forcing a full redraw.
        // Both the widget cells and the themed default cell behind them are re-rendered
        // under the new theme, and cells no widget writes to differ only in that
        // default — without this the diff would match them against the new default and
        // skip repainting, leaving the old background on screen.
        if (styleChanged) _prev = null;
        if (styleChanged || sizeChanged) _prevWidgetCount = 0;
    }

    /// <summary>
    /// Write a styled string at absolute terminal position (col, row).
    /// Clips text that extends beyond or starts before the Region bounds.
    /// Each visible character is stored as a pre-rendered styled cell.
    /// Uses Rune-based enumeration to avoid StringInfo allocations per character.
    /// ASCII fast path skips grapheme cluster logic entirely.
    /// </summary>
    public void Write(int col, int row, string text, Style style)
        => WriteClipped(col, row, text, style, Region.Col, Region.Col + Region.Width);

    /// <summary>
    /// <see cref="Write"/>, clipped to columns <c>[clipLeft, clipRight)</c> as well as to
    /// <see cref="Region"/>. <see cref="SubRenderContext"/> writes through this, so text
    /// longer than a widget's region stops at its edge instead of drawing over the
    /// neighbouring widget.
    /// </summary>
    internal void WriteClipped(int col, int row, string text, Style style, int clipLeft, int clipRight)
    {
        if (row < Region.Row || row >= Region.Row + Region.Height) return;
        if (text.Length == 0) return;

        // Clip window in cell columns within this context's row.
        int minC = Math.Max(0, clipLeft - Region.Col);
        int maxC = Math.Min(Region.Width, clipRight - Region.Col);
        if (minC >= maxC) return;

        int cellRow    = row - Region.Row;
        int rowStart   = cellRow * Region.Width;
        int baseCol    = col - Region.Col;
        if (baseCol >= maxC) return;
        int cellOffset = 0; // column offset within the string
        int stamp      = ++_seq;

        // ASCII fast path: no surrogate pairs, no wide chars — tight inner loop
        if (IsAscii(text))
        {
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                // C0 controls and DEL draw nothing (VisualWidth counts them as zero),
                // and passed through they would move the terminal's cursor — a CR
                // returns to column 0, a tab jumps ahead, an ESC starts a sequence the
                // text never meant to send. A tab is shown as one blank.
                if (ch < 0x20 || ch == 0x7F)
                {
                    if (ch != '\t') continue;
                    ch = ' ';
                }

                int cellCol = baseCol + cellOffset++;
                if (cellCol < minC) continue;
                if (cellCol >= maxC) break;

                int idx = rowStart + cellCol;
                if (_anyWide) Displace(idx, rowStart, stamp);
                _cells[idx]  = style.RenderChar(ch, ColorProfile);
                if (_stamps is not null) _stamps[idx] = stamp;
            }
            return;
        }

        // General path: enumerate runes, coalescing zero-width runes (combining marks,
        // ZWJ, variation selectors) onto the preceding character. A grapheme cluster
        // occupies exactly one cell, which is how the terminal draws it — giving a
        // combining mark its own cell would shift the rest of the line one column
        // right of where the terminal actually puts it, and the frame diff would
        // never notice the drift.
        int clusterStart   = 0;  // the cluster's first char in text
        int pos            = 0;  // char index of the current rune in text
        int clusterIdx     = -1; // cell holding the current cluster, -1 when none
        int clusterCol     = -1; // that cell's column within the region
        int clusterWidth   = 0;

        foreach (Rune rune in text.EnumerateRunes())
        {
            int runeStart = pos;
            pos += rune.Utf16SequenceLength;

            Rune r = rune;
            if (Rune.IsControl(r))
            {
                // Same rule as the ASCII path. C1 controls (U+0080–U+009F) matter just
                // as much: U+009B is a one-byte CSI to a terminal reading 8-bit controls.
                // Dropping one also ends the cluster, which must stay a contiguous run.
                if (r.Value != '\t') { clusterIdx = -1; continue; }
                r = new Rune(' ');
            }

            int width = RuneDisplayWidth(r);

            if (width == 0)
            {
                if (clusterIdx < 0) continue; // no base character to attach to

                // U+FE0F selects emoji presentation, widening the base from 1 to 2.
                if (r.Value == TextUtils.VariationSelector16 && clusterWidth == 1)
                {
                    if (clusterCol + 1 >= maxC)
                    {
                        // No room for the right half: drop the selector, keep it narrow,
                        // and close the cluster so a later mark cannot bring it back.
                        clusterIdx = -1;
                        continue;
                    }
                    clusterWidth = 2;
                    cellOffset++;
                    Displace(clusterIdx + 1, rowStart, stamp);
                    _anyWide = true;
                    _cells[clusterIdx + 1]  = WideCharSpacer;
                    if (_stamps is not null) _stamps[clusterIdx + 1] = stamp;
                }

                _cells[clusterIdx] = style.RenderCell(text[clusterStart..pos], ColorProfile);
                continue;
            }

            int cellCol = baseCol + cellOffset;
            cellOffset += width;
            clusterIdx = -1;

            if (cellCol + width <= minC) continue;
            if (cellCol >= maxC) break;

            // A wide glyph cut by either clip edge shows as a blank in its visible half:
            // the terminal cannot draw half a glyph, and drawing all of it would paint a
            // column outside the clip.
            if (width == 2 && (cellCol < minC || cellCol + 1 >= maxC))
            {
                int blankCol = cellCol < minC ? minC : cellCol;
                int bIdx = rowStart + blankCol;
                Displace(bIdx, rowStart, stamp);
                _cells[bIdx]  = style.RenderChar(' ', ColorProfile);
                if (_stamps is not null) _stamps[bIdx] = stamp;
                continue;
            }

            int idx = rowStart + cellCol;
            Displace(idx, rowStart, stamp);
            clusterStart = runeStart;
            _cells[idx]  = r.IsBmp
                ? style.RenderChar((char)r.Value, ColorProfile)
                : style.RenderCell(r.ToString(), ColorProfile);
            if (_stamps is not null) _stamps[idx] = stamp;

            // A tab drawn as a blank takes no marks: the cluster text would still be a tab.
            clusterIdx   = r.Value == rune.Value ? idx : -1;
            clusterCol   = cellCol;
            clusterWidth = width;

            if (width == 2)
            {
                Displace(idx + 1, rowStart, stamp);
                _anyWide = true;
                _cells[idx + 1]  = WideCharSpacer; // sentinel: right half of wide glyph
                if (_stamps is not null) _stamps[idx + 1] = stamp;
            }
        }
    }

    private static bool AnyAbove(ReadOnlySpan<int> stamps, int limit)
    {
        foreach (int s in stamps) if (s > limit) return true;
        return false;
    }

    private static bool AnyAtOrBelow(ReadOnlySpan<int> stamps, int limit)
    {
        foreach (int s in stamps) if (s <= limit) return true;
        return false;
    }

    /// <summary>
    /// Prepare cell <paramref name="idx"/> to be overwritten without leaving half a wide
    /// glyph behind. A terminal erases the whole glyph when either half is overwritten;
    /// the buffer must agree, or the other half keeps a glyph (or spacer) the screen no
    /// longer shows, and every later cell on the row is emitted one column off.
    /// </summary>
    private void Displace(int idx, int rowStart, int stamp)
    {
        if (!_anyWide) return;
        int rowEnd = rowStart + Region.Width;
        if (ReferenceEquals(_cells[idx], WideCharSpacer))
        {
            // Overwriting a right half: the glyph to its left is erased.
            if (idx - 1 >= rowStart && !ReferenceEquals(_cells[idx - 1], WideCharSpacer))
            {
                _cells[idx - 1]  = null!;
                if (_stamps is not null) _stamps[idx - 1] = stamp;
            }
        }
        else if (idx + 1 < rowEnd && ReferenceEquals(_cells[idx + 1], WideCharSpacer))
        {
            // Overwriting a wide glyph's left half: its right half goes blank.
            _cells[idx + 1]  = null!;
            if (_stamps is not null) _stamps[idx + 1] = stamp;
        }
    }

    /// <summary>
    /// Returns true if every character in the string is ASCII (&lt; 128).
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
    /// <remarks>
    /// Only a widget whose render began with a <see cref="TryReuseWidget"/> miss can be
    /// served next frame: the miss marks where its render started, and without that the
    /// cache cannot tell the widget's own cells from what was beneath it.
    /// </remarks>
    public void RegisterWidget(IWidget widget, Region region)
    {
        // Find the miss that opened this render. Normally it is on top; a render that
        // missed without registering (a custom container) is discarded on the way down.
        int seqStart = -1, rawStart = 0;
        for (int i = _pendingCount - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_pending[i].Widget, widget)) continue;
            seqStart = _pending[i].SeqStart;
            rawStart = _pending[i].RawStart;
            _pending[i] = default;
            _pendingCount = i;
            break;
        }

        int seqEnd = _seq;
        AddEntry(new CacheEntry
        {
            Widget   = widget,
            Region   = region,
            SeqStart = seqStart,
            SeqEnd   = seqEnd,
            RawStart = rawStart,
            RawEnd   = _rawRegions?.Count ?? 0,
            Cursor   = seqStart >= 0 && _cursorStamp > seqStart && _cursorStamp <= seqEnd ? Cursor : null,
        });
    }

    private void AddEntry(in CacheEntry entry)
    {
        // Lazy-allocate on first use. Never take _prevEntries: TryReuseWidget reads it
        // for the rest of the frame, and every widget registered after this one would
        // miss. Reset recycles the buffers instead, so this allocates once, not
        // per frame.
        if (_curEntries is null)
            _curEntries = new CacheEntry[32];
        else if (_curWidgetCount >= _curEntries.Length)
            Array.Resize(ref _curEntries, _curEntries.Length * 2);

        _curEntries[_curWidgetCount++] = entry;
    }

    /// <summary>
    /// If <paramref name="widget"/> (same object reference) was rendered at exactly
    /// <paramref name="region"/> last frame AND the previous cell buffer exists,
    /// copy those cells into the current buffer and return <see langword="true"/>
    /// (caller should skip rendering). Otherwise return <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Only the cells the widget drew itself are copied, and only when nothing drew over
    /// them later that frame — an overlay that has since closed, or a neighbour that
    /// overflowed, would otherwise be copied back with them and stay on screen. The
    /// cursor the widget placed and the raw payloads it registered are replayed too.
    /// </remarks>
    public bool TryReuseWidget(IWidget widget, Region region)
    {
        // Stamps start with the first lookup. Writes before it are unstamped, which reads
        // as "drawn beneath": correct, since no cached widget's render can enclose them.
        _stamps ??= new int[_cells.Length];

        if (_prev is not null && _prevStamps is not null && _prevEntries is not null)
        {
            // Linear scan — typical widget count < 100, faster than Dictionary for small N.
            for (int i = 0; i < _prevWidgetCount; i++)
            {
                ref readonly var e = ref _prevEntries[i];
                if (!ReferenceEquals(e.Widget, widget) || e.Region != region) continue;
                if (e.SeqStart < 0 || !ReplayCells(e)) break;

                for (int k = e.RawStart; k < e.RawEnd; k++)
                    (_rawRegions ??= new()).Add(_prevRawRegions![k]);
                if (e.Cursor is { } cursor) SetCursorDescriptor(cursor);

                AddEntry(new CacheEntry
                {
                    Widget   = widget,
                    Region   = region,
                    SeqStart = _replayStamp - 1,
                    SeqEnd   = _seq,
                    RawStart = (_rawRegions?.Count ?? 0) - (e.RawEnd - e.RawStart),
                    RawEnd   = _rawRegions?.Count ?? 0,
                    Cursor   = e.Cursor,
                });
                return true;
            }
        }

        // Miss: the caller renders the widget now. Note where that render begins.
        if (_pendingCount == _pending.Length)
            Array.Resize(ref _pending, _pending.Length * 2);
        _pending[_pendingCount++] = new PendingRender
        {
            Widget   = widget,
            SeqStart = _seq,
            RawStart = _rawRegions?.Count ?? 0,
        };
        return false;
    }

    private int _replayStamp;

    /// <summary>
    /// Copy <paramref name="e"/>'s own cells from the previous frame, unless something
    /// drew over its region after it. Returns false, copying nothing, in that case.
    /// </summary>
    private bool ReplayCells(in CacheEntry e)
    {
        int w      = Region.Width;
        int rStart = Math.Max(0, e.Region.Row - Region.Row);
        int rEnd   = Math.Min(Region.Height, e.Region.Row - Region.Row + e.Region.Height);
        int cStart = Math.Max(0, e.Region.Col - Region.Col);
        int cEnd   = Math.Min(w, e.Region.Col - Region.Col + e.Region.Width);
        if (rStart >= rEnd || cStart >= cEnd) return true;
        int len = cEnd - cStart;
        var prevStamps = _prevStamps!;

        // Plain loops: the generic span searches box their arguments until tiered up.
        for (int r = rStart; r < rEnd; r++)
            if (AnyAbove(prevStamps.AsSpan(r * w + cStart, len), e.SeqEnd))
                return false;

        // One stamp for the whole replay; the new entry spans exactly it.
        int stamp = _replayStamp = ++_seq;
        var stamps = _stamps!;
        for (int r = rStart; r < rEnd; r++)
        {
            int rowIdx = r * w + cStart;
            var rowStamps = prevStamps.AsSpan(rowIdx, len);

            // The usual case: the widget drew every cell of the row. Copy it whole.
            if (!AnyAtOrBelow(rowStamps, e.SeqStart))
            {
                _prev.AsSpan(rowIdx, len).CopyTo(_cells.AsSpan(rowIdx, len));
                stamps.AsSpan(rowIdx, len).Fill(stamp);
                continue;
            }

            for (int c = 0; c < len; c++)
            {
                if (rowStamps[c] <= e.SeqStart) continue; // drawn beneath the widget, not by it
                _cells[rowIdx + c]  = _prev![rowIdx + c];
                stamps[rowIdx + c] = stamp;
            }
        }
        if (_prevAnyWide) _anyWide = true; // the copied cells may hold wide glyphs
        return true;
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
        // 1. Emit cleanup for payloads that are gone this frame, and for those that moved.
        //    A move needs one too: graphics protocols place an image *in addition to* its
        //    existing placements rather than replacing them, so leaving the old position
        //    alone strands a copy of the image there. All cleanups go out before any
        //    placement below, so a delete cannot land on top of a placement just made.
        if (_prevRawRegions is not null)
        {
            foreach (var prev in _prevRawRegions)
            {
                if (!IsRawPayloadStillAtSameRegion(prev))
                {
                    var cleanup = prev.Payload.Cleanup(prev.Region);
                    if (cleanup is not null) sb.Append(cleanup);
                }
            }
        }

        // 2. Emit current raw regions. Matching on payload identity rather than on region
        //    is what makes motion affordable: a scrolling row changes every region every
        //    frame, and re-uploading each image per frame costs the whole payload again.
        //    - Not present last frame    → full Encode() (upload + place), unless it was
        //      preloaded: the terminal holds it already, so Place() as for a move.
        //    - Present, at a new region  → Place(): step 1 already deleted the placement at
        //      the old region, so this one is mandatory — skipping it leaves nothing on
        //      screen — but it must not re-upload.
        //    - Present, same region      → Refresh(), which is optional and usually null;
        //      a payload that stayed put generally needs nothing at all.
        if (_rawRegions is not null)
        {
            foreach (var entry in _rawRegions)
            {
                bool alreadyUploaded = (WasRawPayloadPresentLastFrame(entry)
                                        || _held?.Contains(entry.Hash) == true)
                                    && _forgotten?.Contains(entry.Hash) != true;

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

                if (!alreadyUploaded)
                {
                    EmitCursorMove();
                    foreach (var seq in entry.Payload.Encode(entry.Region, ColorProfile))
                        sb.Append(seq);
                }
                else if (!WasRawPayloadAtSameRegionLastFrame(entry))
                {
                    EmitCursorMove();
                    foreach (var seq in entry.Payload.Place(entry.Region, ColorProfile))
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

        // A preloaded payload that appeared this frame is on screen now; the entry above
        // served every copy of it, and the frame-to-frame tracking covers it from here.
        if (_held is { Count: > 0 } && _rawRegions is not null)
            foreach (var entry in _rawRegions)
                _held.Remove(entry.Hash);

        // A forgotten payload on screen was just re-encoded; one that is not will be when
        // it next appears, since it is then new to the frame-to-frame tracking anyway.
        _forgotten = null;

        // Swap buffers: current → previous; old previous → current (reused, cleared by Reset next frame)
        var oldPrev = _prev;
        _prev       = _cells;
        (_prevStamps, _stamps) = (_stamps, _prevStamps);
        _prevWidth  = w;
        _prevHeight = h;
        // Reuse the old prev array as next frame's current buffer (Reset() will clear it)
        _cells = oldPrev ?? new string[w * h];

        return sb.ToString();
    }

    /// <inheritdoc/>
    public void SetCursorDescriptor(CursorDescriptor cursor)
    {
        Cursor = cursor;
        _cursorStamp = ++_seq;
    }

    /// <summary>
    /// Register a raw escape payload for <paramref name="region"/>.
    /// Cells covered by the region are filled with <see cref="RawRegionSpacer"/> sentinels
    /// so <see cref="ToAnsiFrame"/> skips them during the cell diff pass.
    /// The payload's sequences are emitted after the cell diff in the same frame.
    /// </summary>
    public void WriteRawEscape(Region region, IRawEscapePayload payload)
    {
        (_rawRegions ??= new()).Add(new RawEntry(region, payload, payload.ContentHash));
        SentinelFillRegion(region, ++_seq);
    }

    /// <summary>
    /// Sequences that transmit <paramref name="payload"/> to the terminal ahead of its
    /// first appearance, or null when there is nothing to send. Call between frames.
    /// </summary>
    /// <remarks>
    /// Returns null when the payload is on screen already, was already preloaded, or has
    /// no transmit step (<see cref="IRawEscapePayload.Transmit"/> returned null). Otherwise
    /// the payload is recorded as held, and the frame it first appears in places it
    /// instead of encoding it — which is the point: the upload goes out when the bytes
    /// arrive, not on the frame where its latency is visible.
    /// </remarks>
    public string? Preload(IRawEscapePayload payload)
    {
        int hash = payload.ContentHash;

        // Between frames _rawRegions holds the frame on screen.
        if (_rawRegions is not null)
            foreach (var entry in _rawRegions)
                if (entry.Hash == hash) return null;

        if (_held?.Contains(hash) == true) return null;

        var sequences = payload.Transmit(ColorProfile);
        if (sequences is null) return null;

        (_held ??= new()).Add(hash);
        return string.Concat(sequences);
    }

    /// <summary>
    /// Record that the terminal no longer holds the payload with
    /// <paramref name="contentHash"/> — it evicted the image — so the next frame encodes it
    /// in full rather than placing data that is gone. Call between frames.
    /// </summary>
    internal void ForgetRawPayload(int contentHash)
    {
        _held?.Remove(contentHash);
        (_forgotten ??= new()).Add(contentHash);
    }

    // ── Raw region helpers ───────────────────────────────────────────────────────

    /// <summary>Fill every cell in <paramref name="region"/> with <see cref="RawRegionSpacer"/>.</summary>
    private void SentinelFillRegion(Region region, int stamp)
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
                {
                    _cells[idx]  = RawRegionSpacer;
                    if (_stamps is not null) _stamps[idx] = stamp;
                }
            }
        }
    }

    /// <summary>
    /// Sequences that remove every raw-escape payload currently on screen, for use when
    /// tearing the application down.
    /// </summary>
    /// <remarks>
    /// Pixel graphics live outside the cell buffer, so leaving the alternate screen does
    /// not necessarily take them with it. Under tmux especially: the payloads were passed
    /// through to the outer terminal, whose screen tmux does not model, so nothing else
    /// will ever clean them up and they outlive the process that drew them.
    /// </remarks>
    public string? BuildRawCleanup()
    {
        // After the final frame the current list holds it; before any Reset, so does prev.
        var entries = _rawRegions ?? _prevRawRegions;
        if (entries is null || entries.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            var cleanup = entry.Payload.Cleanup(entry.Region);
            if (cleanup is not null) sb.Append(cleanup);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>
    /// Returns true if <paramref name="prev"/>'s payload is still on screen this frame at
    /// exactly the same region — the one case needing neither a delete nor a re-place.
    /// Anything else (gone, or moved) is cleaned up first.
    /// </summary>
    private bool IsRawPayloadStillAtSameRegion(RawEntry prev)
    {
        if (_rawRegions is null) return false;
        foreach (var cur in _rawRegions)
            if (cur.Hash == prev.Hash && cur.Region == prev.Region) return true;
        return false;
    }

    /// <summary>
    /// Returns true if this payload was on screen last frame, anywhere. The terminal still
    /// holds its data, so it can be re-placed instead of re-transmitted.
    /// </summary>
    private bool WasRawPayloadPresentLastFrame(RawEntry entry)
    {
        if (_prevRawRegions is null) return false;
        foreach (var prev in _prevRawRegions)
            if (prev.Hash == entry.Hash) return true;
        return false;
    }

    /// <summary>
    /// True when this payload occupied this same region last frame, i.e. it did not move.
    /// The inverse of the test in <see cref="IsRawPayloadStillAtSameRegion"/>, read from the
    /// current frame's side: that one decides whether a vacated region needs a delete, this
    /// one whether the payload needs re-placing where it now sits.
    /// </summary>
    private bool WasRawPayloadAtSameRegionLastFrame(RawEntry entry)
    {
        if (_prevRawRegions is null) return false;
        foreach (var prev in _prevRawRegions)
            if (prev.Hash == entry.Hash && prev.Region == entry.Region) return true;
        return false;
    }
}