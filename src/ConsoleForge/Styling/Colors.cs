namespace ConsoleForge.Styling;

/// <summary>
/// Abstraction over ANSI/256/TrueColor values.
/// Implementations are downsampled at render time via <see cref="ColorProfile"/>.
/// </summary>
public interface IColor
{
    /// <summary>
    /// Produce an ANSI escape sequence fragment for this color,
    /// downsampled to the given profile (never upgrades).
    /// </summary>
    string ToAnsiSequence(bool isForeground, ColorProfile profile);
}

/// <summary>No color set (transparent / inherit).</summary>
public sealed record NoColor : IColor
{
    /// <inheritdoc/>
    public string ToAnsiSequence(bool isForeground, ColorProfile profile) => string.Empty;
}

/// <summary>One of the 8 or 16 standard ANSI colors.</summary>
public sealed record AnsiColor(int Index) : IColor
{
    // 32-entry static table: [0-7] fg normal, [8-15] fg bright, [16-23] bg normal, [24-31] bg bright
    private static readonly string[] _table = BuildAnsiTable();
    internal static string[] AnsiTable => _table;

    private static string[] BuildAnsiTable()
    {
        var t = new string[32];
        for (int i = 0; i < 8; i++)
        {
            t[i]      = (30 + i).ToString();       // fg normal
            t[8 + i]  = (90 + i).ToString();       // fg bright
            t[16 + i] = (40 + i).ToString();       // bg normal
            t[24 + i] = (100 + i).ToString();      // bg bright
        }
        return t;
    }

    /// <inheritdoc/>
    public string ToAnsiSequence(bool isForeground, ColorProfile profile)
    {
        if (profile == ColorProfile.NoColor) return string.Empty;
        // Index 0-7 = normal, 8-15 = bright
        int slot = isForeground
            ? (Index < 8 ? Index : 8 + (Index - 8))
            : (Index < 8 ? 16 + Index : 24 + (Index - 8));
        return _table[slot];
    }
}

/// <summary>One of the 256 xterm palette colors.</summary>
public sealed record Ansi256Color(int Index) : IColor
{
    // 512-entry static table: [0-255] fg sequences, [256-511] bg sequences
    private static readonly string[] _table = BuildAnsi256Table();
    internal static string[] Ansi256Table => _table;

    private static string[] BuildAnsi256Table()
    {
        var t = new string[512];
        for (int i = 0; i < 256; i++)
        {
            t[i]       = $"38;5;{i}";
            t[256 + i] = $"48;5;{i}";
        }
        return t;
    }

    /// <inheritdoc/>
    public string ToAnsiSequence(bool isForeground, ColorProfile profile)
    {
        if (profile == ColorProfile.NoColor) return string.Empty;
        if (profile == ColorProfile.Ansi)
        {
            // Downsample: reuse AnsiColor table directly — no new AnsiColor allocation
            int ansiIdx = Index < 16 ? Index : 15;
            return new AnsiColor(ansiIdx).ToAnsiSequence(isForeground, profile);
        }
        return isForeground ? _table[Index] : _table[256 + Index];
    }
}

/// <summary>24-bit RGB true color.</summary>
public sealed record TrueColor(byte R, byte G, byte B) : IColor
{
    /// <inheritdoc/>
    public string ToAnsiSequence(bool isForeground, ColorProfile profile)
    {
        if (profile == ColorProfile.NoColor) return string.Empty;
        if (profile == ColorProfile.Ansi)
        {
            // Inline downsample to nearest ANSI color — no new AnsiColor allocation
            int ansiIdx = ToNearestAnsi();
            int slot = isForeground
                ? (ansiIdx < 8 ? ansiIdx : 8 + (ansiIdx - 8))
                : (ansiIdx < 8 ? 16 + ansiIdx : 24 + (ansiIdx - 8));
            return AnsiColor.AnsiTable[slot];
        }
        if (profile == ColorProfile.Ansi256)
        {
            // Inline downsample to 256 color — no new Ansi256Color allocation
            int idx = ToNearest256();
            return isForeground
                ? Ansi256Color.Ansi256Table[idx]
                : Ansi256Color.Ansi256Table[256 + idx];
        }
        return isForeground ? $"38;2;{R};{G};{B}" : $"48;2;{R};{G};{B}";
    }

    private int ToNearestAnsi()
    {
        // Basic nearest-color mapping using distance to standard 16 ANSI colors
        ReadOnlySpan<(byte r, byte g, byte b)> ansiPalette =
        [
            (0,0,0),(128,0,0),(0,128,0),(128,128,0),
            (0,0,128),(128,0,128),(0,128,128),(192,192,192),
            (128,128,128),(255,0,0),(0,255,0),(255,255,0),
            (0,0,255),(255,0,255),(0,255,255),(255,255,255)
        ];
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < ansiPalette.Length; i++)
        {
            var (r, g, b) = ansiPalette[i];
            double d = Math.Sqrt(Math.Pow(R - r, 2) + Math.Pow(G - g, 2) + Math.Pow(B - b, 2));
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private int ToNearest256()
    {
        // Use the 6x6x6 color cube (indices 16–231) for RGB approximation
        int ri = (int)Math.Round(R / 255.0 * 5);
        int gi = (int)Math.Round(G / 255.0 * 5);
        int bi = (int)Math.Round(B / 255.0 * 5);
        return 16 + 36 * ri + 6 * gi + bi;
    }
}

/// <summary>
/// Factory for common named colors and hex/RGB parsing.
/// </summary>
/// <example>
/// <code>
/// Color.FromHex("#FF5733")   // → TrueColor(255, 87, 51)
/// Color.FromRgb(0, 128, 255) // → TrueColor
/// Color.Red                  // → AnsiColor(1)
/// </code>
/// </example>
public static class Color
{
    /// <summary>Parse a 6-digit hex string (with or without leading <c>#</c>) into a <see cref="TrueColor"/>.</summary>
    /// <param name="hex">Hex string, e.g. <c>"#FF5733"</c> or <c>"FF5733"</c>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="hex"/> is not exactly 6 hex digits.</exception>
    public static IColor FromHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6) throw new ArgumentException("Hex color must be 6 digits.", nameof(hex));
        byte r = Convert.ToByte(s[..2], 16);
        byte g = Convert.ToByte(s[2..4], 16);
        byte b = Convert.ToByte(s[4..6], 16);
        return new TrueColor(r, g, b);
    }

    /// <summary>Create a <see cref="TrueColor"/> from individual R, G, B byte components.</summary>
    public static IColor FromRgb(byte r, byte g, byte b) => new TrueColor(r, g, b);

    /// <summary>
    /// Create a color from an ANSI index (0–15 → <see cref="AnsiColor"/>;
    /// 16–255 → <see cref="Ansi256Color"/>).
    /// </summary>
    public static IColor FromAnsi(int index) => index <= 15 ? new AnsiColor(index) : new Ansi256Color(index);

    // ── Named ANSI colours ────────────────────────────────────────────────────

    /// <summary>ANSI color 0 — black.</summary>
    public static readonly IColor Black        = new AnsiColor(0);
    /// <summary>ANSI color 1 — red.</summary>
    public static readonly IColor Red          = new AnsiColor(1);
    /// <summary>ANSI color 2 — green.</summary>
    public static readonly IColor Green        = new AnsiColor(2);
    /// <summary>ANSI color 3 — yellow.</summary>
    public static readonly IColor Yellow       = new AnsiColor(3);
    /// <summary>ANSI color 4 — blue.</summary>
    public static readonly IColor Blue         = new AnsiColor(4);
    /// <summary>ANSI color 5 — magenta.</summary>
    public static readonly IColor Magenta      = new AnsiColor(5);
    /// <summary>ANSI color 6 — cyan.</summary>
    public static readonly IColor Cyan         = new AnsiColor(6);
    /// <summary>ANSI color 7 — white (light grey).</summary>
    public static readonly IColor White        = new AnsiColor(7);
    /// <summary>ANSI color 8 — bright black / dark grey.</summary>
    public static readonly IColor BrightBlack  = new AnsiColor(8);
    /// <summary>Alias for <see cref="BrightBlack"/>.</summary>
    public static readonly IColor DarkGray     = new AnsiColor(8);
    /// <summary>ANSI color 9 — bright red.</summary>
    public static readonly IColor BrightRed    = new AnsiColor(9);
    /// <summary>ANSI color 10 — bright green.</summary>
    public static readonly IColor BrightGreen  = new AnsiColor(10);
    /// <summary>ANSI color 11 — bright yellow.</summary>
    public static readonly IColor BrightYellow = new AnsiColor(11);
    /// <summary>ANSI color 12 — bright blue.</summary>
    public static readonly IColor BrightBlue   = new AnsiColor(12);
    /// <summary>ANSI color 13 — bright magenta.</summary>
    public static readonly IColor BrightMagenta = new AnsiColor(13);
    /// <summary>ANSI color 14 — bright cyan.</summary>
    public static readonly IColor BrightCyan   = new AnsiColor(14);
    /// <summary>ANSI color 15 — bright white.</summary>
    public static readonly IColor BrightWhite  = new AnsiColor(15);
}
