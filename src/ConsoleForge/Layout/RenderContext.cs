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
    // Double buffer: _cells = current frame being written; _prev = last emitted frame.
    private string[] _cells;
    private string[]? _prev; // null = no previous frame (first render)
    private int _prevWidth;
    private int _prevHeight;

    public Region         Region       { get; private set; }
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
        bool sizeChanged = region.Width != Region.Width || region.Height != Region.Height;

        Region       = region;
        Theme        = theme;
        ColorProfile = colorProfile;
        Layout       = layout;

        if (sizeChanged)
        {
            // Dimension changed — allocate new buffers, discard prev (forces full redraw)
            _cells = new string[region.Width * region.Height];
            _prev  = null;
        }
        else
        {
            // Same dimensions — clear current buffer in place, prev buffer retained for diff
            Array.Clear(_cells, 0, _cells.Length);
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
                _cells[idx + 1] = " "; // second column of wide char

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
    /// Return the terminal display width of a Rune:
    /// 2 for emoji and wide characters, 1 for everything else.
    /// Uses codepoint arithmetic — no string/StringInfo allocation.
    /// </summary>
    private static int RuneDisplayWidth(Rune rune)
    {
        int v = rune.Value;
        return v switch
        {
            >= 0x1F300 and <= 0x1FAFF => 2, // emoji / misc symbols
            >= 0x2600 and <= 0x27BF   => 1, // dingbats (mostly narrow)
            >= 0x3000 and <= 0x9FFF   => 2, // CJK
            >= 0xF900 and <= 0xFAFF   => 2, // CJK compatibility ideographs
            >= 0xFE30 and <= 0xFE4F   => 2, // CJK compatibility forms
            >= 0xFF00 and <= 0xFF60   => 2, // fullwidth
            >= 0x20000 and <= 0x2FA1F => 2, // CJK extensions
            _ => 1
        };
    }

    /// <summary>
    /// Create a sub-context restricted to a sub-region of this context.
    /// Writes to the sub-context are forwarded to this context with adjusted coordinates.
    /// </summary>
    public SubRenderContext CreateSub(Region subRegion) => new(this, subRegion);

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

        // Estimate capacity: full redraw worst-case; partial redraws will be smaller
        int capacity = fullRedraw
            ? 6 + Region.Height * (10 + Region.Width * 6)
            : Region.Width * Region.Height; // smaller estimate for partial redraws

        var sb = new StringBuilder(capacity);

        int w = Region.Width;
        int h = Region.Height;

        // Cursor position tracking: only emit \x1b[row;colH when position is non-consecutive
        int lastEmittedRow = -1;
        int lastEmittedCol = -1;

        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int idx = r * w + c;
                var cell = _cells[idx];
                string cellContent = cell is { Length: > 0 } ? cell : " ";

                // Skip unchanged cells (diff against previous frame)
                if (!fullRedraw && _prev![idx] is { } prevCell)
                {
                    string prevContent = prevCell is { Length: > 0 } ? prevCell : " ";
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
                lastEmittedCol = c + 1; // next expected column after this cell
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
