using System.Collections.Concurrent;
using System.Text;

namespace ConsoleForge.Styling;

/// <summary>
/// Immutable value type carrying visual style properties.
/// Uses a bitmask to distinguish "unset" from "set to default".
/// All mutating methods return a new Style value (fluent builder pattern).
/// </summary>
public readonly struct Style : IEquatable<Style>
{
    // Cache of computed ANSI open/close sequences keyed by (props, fgHash, bgHash, profile).
    // Style is immutable so these are deterministic — compute once, reuse forever.
    private static readonly ConcurrentDictionary<(uint props, int fgHash, int bgHash, ColorProfile profile), (string open, string close)>
        _ansiCache = new();

    // Per-char styled cell cache: keyed by (props, fgHash, bgHash, profile, char).
    // Covers the dominant hot path: single ASCII chars written to cells each frame.
    // Cache is bounded: 128 ASCII chars × finite distinct styles used per app.
    private static readonly ConcurrentDictionary<(uint props, int fgHash, int bgHash, ColorProfile profile, char ch), string>
        _charCache = new();

    // Bitmask constants for set properties
    private const uint FgBit        = 1 << 0;
    private const uint BgBit        = 1 << 1;
    private const uint BoldBit      = 1 << 2;
    private const uint ItalicBit    = 1 << 3;
    private const uint UnderlineBit = 1 << 4;
    private const uint StrikeBit    = 1 << 5;
    private const uint FaintBit     = 1 << 6;
    private const uint BlinkBit     = 1 << 7;
    private const uint ReverseBit   = 1 << 8;
    private const uint PaddingBit   = 1 << 9;
    private const uint MarginBit    = 1 << 10;
    private const uint WidthBit     = 1 << 11;
    private const uint HeightBit    = 1 << 12;
    private const uint AlignBit     = 1 << 13;
    private const uint BorderBit    = 1 << 14;
    private const uint BorderFgBit  = 1 << 15;
    private const uint BorderBgBit  = 1 << 16;
    private const uint BorderTopBit    = 1 << 17;
    private const uint BorderRightBit  = 1 << 18;
    private const uint BorderBottomBit = 1 << 19;
    private const uint BorderLeftBit   = 1 << 20;
    private const uint BoldValueBit    = 1 << 21;
    private const uint ItalicValueBit  = 1 << 22;
    private const uint UnderlineValueBit = 1 << 23;
    private const uint StrikeValueBit  = 1 << 24;
    private const uint FaintValueBit   = 1 << 25;
    private const uint BlinkValueBit   = 1 << 26;
    private const uint ReverseValueBit = 1 << 27;

    private readonly uint _props;
    private readonly IColor? _fg;
    private readonly IColor? _bg;
    private readonly IColor? _borderFg;
    private readonly IColor? _borderBg;
    private readonly BorderSpec _border;
    private readonly int _paddingTop, _paddingRight, _paddingBottom, _paddingLeft;
    private readonly int _marginTop, _marginRight, _marginBottom, _marginLeft;
    private readonly int _width, _height;
    private readonly HorizontalAlign _align;

    /// <summary>The empty style (no properties set). Fast path: Render returns text unchanged.</summary>
    public static readonly Style Default = default;

    private Style(
        uint props,
        IColor? fg = null, IColor? bg = null,
        IColor? borderFg = null, IColor? borderBg = null,
        BorderSpec border = default,
        int paddingTop = 0, int paddingRight = 0, int paddingBottom = 0, int paddingLeft = 0,
        int marginTop = 0, int marginRight = 0, int marginBottom = 0, int marginLeft = 0,
        int width = 0, int height = 0,
        HorizontalAlign align = HorizontalAlign.Left)
    {
        _props = props;
        _fg = fg; _bg = bg;
        _borderFg = borderFg; _borderBg = borderBg;
        _border = border;
        _paddingTop = paddingTop; _paddingRight = paddingRight;
        _paddingBottom = paddingBottom; _paddingLeft = paddingLeft;
        _marginTop = marginTop; _marginRight = marginRight;
        _marginBottom = marginBottom; _marginLeft = marginLeft;
        _width = width; _height = height;
        _align = align;
    }

    // ── Color ────────────────────────────────────────────────────────
    public Style Foreground(IColor color) => With(FgBit, fg: color);
    public Style Background(IColor color) => With(BgBit, bg: color);

    // ── Text decoration ──────────────────────────────────────────────
    public Style Bold(bool value = true) => With(BoldBit | (value ? BoldValueBit : 0), clearBits: value ? 0 : BoldValueBit);
    public Style Italic(bool value = true) => With(ItalicBit | (value ? ItalicValueBit : 0), clearBits: value ? 0 : ItalicValueBit);
    public Style Underline(bool value = true) => With(UnderlineBit | (value ? UnderlineValueBit : 0), clearBits: value ? 0 : UnderlineValueBit);
    public Style Strikethrough(bool value = true) => With(StrikeBit | (value ? StrikeValueBit : 0), clearBits: value ? 0 : StrikeValueBit);
    public Style Faint(bool value = true) => With(FaintBit | (value ? FaintValueBit : 0), clearBits: value ? 0 : FaintValueBit);
    public Style Blink(bool value = true) => With(BlinkBit | (value ? BlinkValueBit : 0), clearBits: value ? 0 : BlinkValueBit);
    public Style Reverse(bool value = true) => With(ReverseBit | (value ? ReverseValueBit : 0), clearBits: value ? 0 : ReverseValueBit);

    // ── Spacing ──────────────────────────────────────────────────────
    public Style Padding(int all) => Padding(all, all, all, all);
    public Style Padding(int vertical, int horizontal) => Padding(vertical, horizontal, vertical, horizontal);
    public Style Padding(int top, int right, int bottom, int left) =>
        new(_props | PaddingBit, _fg, _bg, _borderFg, _borderBg, _border,
            top, right, bottom, left, _marginTop, _marginRight, _marginBottom, _marginLeft,
            _width, _height, _align);

    public Style Margin(int all) => Margin(all, all, all, all);
    public Style Margin(int vertical, int horizontal) => Margin(vertical, horizontal, vertical, horizontal);
    public Style Margin(int top, int right, int bottom, int left) =>
        new(_props | MarginBit, _fg, _bg, _borderFg, _borderBg, _border,
            _paddingTop, _paddingRight, _paddingBottom, _paddingLeft,
            top, right, bottom, left, _width, _height, _align);

    // ── Size ─────────────────────────────────────────────────────────
    public Style Width(int columns) => new(_props | WidthBit, _fg, _bg, _borderFg, _borderBg, _border,
        _paddingTop, _paddingRight, _paddingBottom, _paddingLeft,
        _marginTop, _marginRight, _marginBottom, _marginLeft, columns, _height, _align);

    public Style Height(int rows) => new(_props | HeightBit, _fg, _bg, _borderFg, _borderBg, _border,
        _paddingTop, _paddingRight, _paddingBottom, _paddingLeft,
        _marginTop, _marginRight, _marginBottom, _marginLeft, _width, rows, _align);

    // ── Alignment ────────────────────────────────────────────────────
    public Style Align(HorizontalAlign align) => new(_props | AlignBit, _fg, _bg, _borderFg, _borderBg, _border,
        _paddingTop, _paddingRight, _paddingBottom, _paddingLeft,
        _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, align);

    // ── Borders ──────────────────────────────────────────────────────
    public Style Border(BorderSpec border) => new(_props | BorderBit | BorderTopBit | BorderRightBit | BorderBottomBit | BorderLeftBit,
        _fg, _bg, _borderFg, _borderBg, border,
        _paddingTop, _paddingRight, _paddingBottom, _paddingLeft,
        _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);

    public Style BorderTop(bool enabled = true) => enabled
        ? new(_props | BorderBit | BorderTopBit, _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align)
        : new(_props & ~BorderTopBit, _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);

    public Style BorderRight(bool enabled = true) => enabled
        ? new(_props | BorderBit | BorderRightBit, _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align)
        : new(_props & ~BorderRightBit, _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);

    public Style BorderBottom(bool enabled = true) => enabled
        ? new(_props | BorderBit | BorderBottomBit, _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align)
        : new(_props & ~BorderBottomBit, _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);

    public Style BorderLeft(bool enabled = true) => enabled
        ? new(_props | BorderBit | BorderLeftBit, _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align)
        : new(_props & ~BorderLeftBit, _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);

    public Style BorderForeground(IColor color) => With(BorderFgBit, borderFg: color);
    public Style BorderBackground(IColor color) => With(BorderBgBit, borderBg: color);

    // ── Unset ────────────────────────────────────────────────────────
    public Style UnsetForeground() => new(_props & ~FgBit, null, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);
    public Style UnsetBackground() => new(_props & ~BgBit, _fg, null, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);
    public Style UnsetBold() => new(_props & ~(BoldBit | BoldValueBit), _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);
    public Style UnsetItalic() => new(_props & ~(ItalicBit | ItalicValueBit), _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);
    public Style UnsetUnderline() => new(_props & ~(UnderlineBit | UnderlineValueBit), _fg, _bg, _borderFg, _borderBg, _border, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);
    public Style UnsetBorder() => new(_props & ~(BorderBit | BorderTopBit | BorderRightBit | BorderBottomBit | BorderLeftBit | BorderFgBit | BorderBgBit), _fg, _bg, null, null, default, _paddingTop, _paddingRight, _paddingBottom, _paddingLeft, _marginTop, _marginRight, _marginBottom, _marginLeft, _width, _height, _align);

    // ── Inheritance ──────────────────────────────────────────────────
    /// <summary>
    /// Copy properties from parent that are set in parent but not yet set in this.
    /// Margins and padding are NOT inherited (they are local properties).
    /// </summary>
    public Style Inherit(Style parent)
    {
        if (parent._props == 0) return this;

        var result = this;
        // Inherit each property only if not already set in this style
        if ((_props & FgBit) == 0 && (parent._props & FgBit) != 0) result = result.Foreground(parent._fg!);
        if ((_props & BgBit) == 0 && (parent._props & BgBit) != 0) result = result.Background(parent._bg!);
        if ((_props & BoldBit) == 0 && (parent._props & BoldBit) != 0) result = result.Bold((parent._props & BoldValueBit) != 0);
        if ((_props & ItalicBit) == 0 && (parent._props & ItalicBit) != 0) result = result.Italic((parent._props & ItalicValueBit) != 0);
        if ((_props & UnderlineBit) == 0 && (parent._props & UnderlineBit) != 0) result = result.Underline((parent._props & UnderlineValueBit) != 0);
        if ((_props & StrikeBit) == 0 && (parent._props & StrikeBit) != 0) result = result.Strikethrough((parent._props & StrikeValueBit) != 0);
        if ((_props & FaintBit) == 0 && (parent._props & FaintBit) != 0) result = result.Faint((parent._props & FaintValueBit) != 0);
        if ((_props & BlinkBit) == 0 && (parent._props & BlinkBit) != 0) result = result.Blink((parent._props & BlinkValueBit) != 0);
        if ((_props & ReverseBit) == 0 && (parent._props & ReverseBit) != 0) result = result.Reverse((parent._props & ReverseValueBit) != 0);
        if ((_props & BorderBit) == 0 && (parent._props & BorderBit) != 0) result = result.Border(parent._border);
        if ((_props & BorderFgBit) == 0 && (parent._props & BorderFgBit) != 0) result = result.BorderForeground(parent._borderFg!);
        if ((_props & BorderBgBit) == 0 && (parent._props & BorderBgBit) != 0) result = result.BorderBackground(parent._borderBg!);
        if ((_props & AlignBit) == 0 && (parent._props & AlignBit) != 0) result = result.Align(parent._align);
        return result;
    }

    // ── Rendering ────────────────────────────────────────────────────
    /// <summary>
    /// Apply all set style properties to text and return the styled ANSI string.
    /// If no properties are set (_props == 0), returns text unchanged (fast path).
    /// ANSI open/close sequences are cached per (props, fg, bg, profile) — computed once.
    /// </summary>
    public string Render(string text, ColorProfile colorProfile = ColorProfile.TrueColor)
    {
        if (_props == 0) return text;

        // Apply text transformations (padding, width, height) first
        var processedText = ApplyPadding(text);
        if ((_props & WidthBit) != 0)
            processedText = ApplyWidth(processedText, _width, _align);
        if ((_props & HeightBit) != 0)
            processedText = ApplyHeight(processedText, _height);

        // Look up or compute cached ANSI open/close sequences
        var (open, close) = GetCachedAnsiSequences(colorProfile);
        if (open.Length == 0) return processedText;

        return string.Concat(open, processedText, close);
    }

    /// <summary>
    /// Render a single char to a styled cell string.
    /// Hot path: result cached per (style, profile, char) — zero alloc on hit.
    /// Bypasses padding/width/height transforms (single-char cell has none).
    /// </summary>
    public string RenderChar(char ch, ColorProfile colorProfile = ColorProfile.TrueColor)
    {
        if (_props == 0) return ch <= 127 ? _asciiStrings[ch] : ch.ToString();

        int fgHash = _fg?.GetHashCode() ?? 0;
        int bgHash = _bg?.GetHashCode() ?? 0;
        var key = (_props, fgHash, bgHash, colorProfile, ch);

        if (_charCache.TryGetValue(key, out var cached)) return cached;

        var (open, close) = GetCachedAnsiSequences(colorProfile);
        var result = open.Length == 0
            ? (ch <= 127 ? _asciiStrings[ch] : ch.ToString())
            : string.Concat(open, ch <= 127 ? _asciiStrings[ch] : ch.ToString(), close);

        _charCache.TryAdd(key, result);
        return result;
    }

    // Pre-interned single-char strings for ASCII 0-127. Avoids alloc for unstyled cells.
    private static readonly string[] _asciiStrings = Enumerable.Range(0, 128)
        .Select(i => ((char)i).ToString())
        .ToArray();

    /// <summary>
    /// Return cached (open, close) ANSI escape sequences for this style + profile.
    /// Computed once and stored in a static dictionary — no StringBuilder per call.
    /// </summary>
    private (string open, string close) GetCachedAnsiSequences(ColorProfile colorProfile)
    {
        int fgHash = _fg?.GetHashCode() ?? 0;
        int bgHash = _bg?.GetHashCode() ?? 0;
        var key = (_props, fgHash, bgHash, colorProfile);

        if (_ansiCache.TryGetValue(key, out var cached)) return cached;

        // Copy fields needed by ComputeAnsiSequences out to locals — structs can't
        // capture 'this' inside lambdas, so we compute directly here.
        var result = ComputeAnsiSequences(colorProfile);
        _ansiCache.TryAdd(key, result);
        return result;
    }

    private (string open, string close) ComputeAnsiSequences(ColorProfile colorProfile)
    {
        // Collect SGR codes — use a fixed-size array (max 9 codes)
        var codesBuf = new string[9];
        int count = 0;

        if (colorProfile != ColorProfile.NoColor)
        {
            if ((_props & FgBit) != 0 && _fg is not null)
            {
                var seq = _fg.ToAnsiSequence(true, colorProfile);
                if (seq.Length > 0) codesBuf[count++] = seq;
            }
            if ((_props & BgBit) != 0 && _bg is not null)
            {
                var seq = _bg.ToAnsiSequence(false, colorProfile);
                if (seq.Length > 0) codesBuf[count++] = seq;
            }
        }

        if ((_props & BoldBit) != 0 && (_props & BoldValueBit) != 0) codesBuf[count++] = "1";
        if ((_props & FaintBit) != 0 && (_props & FaintValueBit) != 0) codesBuf[count++] = "2";
        if ((_props & ItalicBit) != 0 && (_props & ItalicValueBit) != 0) codesBuf[count++] = "3";
        if ((_props & UnderlineBit) != 0 && (_props & UnderlineValueBit) != 0) codesBuf[count++] = "4";
        if ((_props & BlinkBit) != 0 && (_props & BlinkValueBit) != 0) codesBuf[count++] = "5";
        if ((_props & ReverseBit) != 0 && (_props & ReverseValueBit) != 0) codesBuf[count++] = "7";
        if ((_props & StrikeBit) != 0 && (_props & StrikeValueBit) != 0) codesBuf[count++] = "9";

        if (count == 0) return (string.Empty, string.Empty);

        var sb = new StringBuilder(32);
        sb.Append("\x1b[");
        sb.Append(codesBuf[0]);
        for (int i = 1; i < count; i++) { sb.Append(';'); sb.Append(codesBuf[i]); }
        sb.Append('m');
        return (sb.ToString(), "\x1b[0m");
    }

    // ── Expose accessors needed by border rendering ───────────────────
    internal bool HasBorder => (_props & BorderBit) != 0;
    internal bool HasBorderTop => (_props & BorderTopBit) != 0;
    internal bool HasBorderRight => (_props & BorderRightBit) != 0;
    internal bool HasBorderBottom => (_props & BorderBottomBit) != 0;
    internal bool HasBorderLeft => (_props & BorderLeftBit) != 0;
    internal BorderSpec BorderChars => _border;
    internal IColor? BorderFg => _borderFg;
    internal IColor? BorderBg => _borderBg;
    internal int PaddingTop => _paddingTop;
    internal int PaddingRight => _paddingRight;
    internal int PaddingBottom => _paddingBottom;
    internal int PaddingLeft => _paddingLeft;

    // ── IEquatable ───────────────────────────────────────────────────
    public bool Equals(Style other) => _props == other._props &&
        Equals(_fg, other._fg) && Equals(_bg, other._bg) &&
        Equals(_borderFg, other._borderFg) && Equals(_borderBg, other._borderBg) &&
        _border == other._border &&
        _paddingTop == other._paddingTop && _paddingRight == other._paddingRight &&
        _paddingBottom == other._paddingBottom && _paddingLeft == other._paddingLeft &&
        _marginTop == other._marginTop && _marginRight == other._marginRight &&
        _marginBottom == other._marginBottom && _marginLeft == other._marginLeft &&
        _width == other._width && _height == other._height && _align == other._align;

    public override bool Equals(object? obj) => obj is Style s && Equals(s);
    public override int GetHashCode() => HashCode.Combine(_props, _fg, _bg, _width, _height);
    public static bool operator ==(Style left, Style right) => left.Equals(right);
    public static bool operator !=(Style left, Style right) => !left.Equals(right);

    // ── Helpers ───────────────────────────────────────────────────────
    private Style With(uint setBits, uint clearBits = 0,
        IColor? fg = null, IColor? bg = null,
        IColor? borderFg = null, IColor? borderBg = null) =>
        new((_props | setBits) & ~clearBits,
            fg ?? _fg, bg ?? _bg,
            borderFg ?? _borderFg, borderBg ?? _borderBg, _border,
            _paddingTop, _paddingRight, _paddingBottom, _paddingLeft,
            _marginTop, _marginRight, _marginBottom, _marginLeft,
            _width, _height, _align);

    private string ApplyPadding(string text)
    {
        if ((_props & PaddingBit) == 0) return text;
        var lines = text.Split('\n');
        var padded = new List<string>(lines.Length + _paddingTop + _paddingBottom);

        var hPad = new string(' ', _paddingLeft);
        var hPadRight = new string(' ', _paddingRight);

        for (var i = 0; i < _paddingTop; i++) padded.Add("");
        foreach (var line in lines) padded.Add(hPad + line + hPadRight);
        for (var i = 0; i < _paddingBottom; i++) padded.Add("");

        return string.Join('\n', padded);
    }

    private static string ApplyWidth(string text, int width, HorizontalAlign align)
    {
        var lines = text.Split('\n');
        var result = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length > width) result[i] = line[..width];
            else
            {
                var pad = width - line.Length;
                result[i] = align switch
                {
                    HorizontalAlign.Center => new string(' ', pad / 2) + line + new string(' ', pad - pad / 2),
                    HorizontalAlign.Right  => new string(' ', pad) + line,
                    _                      => line + new string(' ', pad)
                };
            }
        }
        return string.Join('\n', result);
    }

    private static string ApplyHeight(string text, int height)
    {
        var lines = text.Split('\n').ToList();
        while (lines.Count < height) lines.Add("");
        if (lines.Count > height) lines = lines[..height];
        return string.Join('\n', lines);
    }
}

public enum HorizontalAlign { Left, Center, Right }
