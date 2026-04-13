namespace ConsoleForge.Styling;

/// <summary>
/// Abstraction over ANSI/256/TrueColor values.
/// Implementations are downsampled at render time via ColorProfile.
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
/// Factory for common named colors and hex parsing.
/// Color.FromHex("#FF5733") → TrueColor(255, 87, 51)
/// Color.Red → AnsiColor(1)
/// </summary>
public static class Color
{
    public static IColor FromHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6) throw new ArgumentException("Hex color must be 6 digits.", nameof(hex));
        byte r = Convert.ToByte(s[..2], 16);
        byte g = Convert.ToByte(s[2..4], 16);
        byte b = Convert.ToByte(s[4..6], 16);
        return new TrueColor(r, g, b);
    }

    public static IColor FromRgb(byte r, byte g, byte b) => new TrueColor(r, g, b);
    public static IColor FromAnsi(int index) => index <= 15 ? new AnsiColor(index) : new Ansi256Color(index);

    public static readonly IColor Black        = new AnsiColor(0);
    public static readonly IColor Red          = new AnsiColor(1);
    public static readonly IColor Green        = new AnsiColor(2);
    public static readonly IColor Yellow       = new AnsiColor(3);
    public static readonly IColor Blue         = new AnsiColor(4);
    public static readonly IColor Magenta      = new AnsiColor(5);
    public static readonly IColor Cyan         = new AnsiColor(6);
    public static readonly IColor White        = new AnsiColor(7);
    public static readonly IColor BrightBlack  = new AnsiColor(8);
    public static readonly IColor DarkGray     = new AnsiColor(8); // alias
    public static readonly IColor BrightRed    = new AnsiColor(9);
    public static readonly IColor BrightGreen  = new AnsiColor(10);
    public static readonly IColor BrightYellow = new AnsiColor(11);
    public static readonly IColor BrightBlue   = new AnsiColor(12);
    public static readonly IColor BrightMagenta = new AnsiColor(13);
    public static readonly IColor BrightCyan   = new AnsiColor(14);
    public static readonly IColor BrightWhite  = new AnsiColor(15);
}
