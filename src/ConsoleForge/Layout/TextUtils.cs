using System.Runtime.CompilerServices;
using System.Text;

namespace ConsoleForge.Layout;

/// <summary>
/// Terminal-aware text utilities: visual column width, truncation, and padding
/// that correctly handle multi-byte Unicode characters and wide glyphs
/// (CJK ideographs, full-width forms, emoji) which occupy 2 terminal columns.
/// </summary>
/// <remarks>
/// <b>Performance</b> — All methods use <see cref="string.IsAscii"/> (SIMD-vectorized in
/// .NET 8) as the primary fast-path gate. For pure-ASCII strings (the common case for
/// widget labels and UI text) the hot path degenerates to a single vectorized scan plus
/// O(1) arithmetic — no Rune enumeration, no <see cref="StringBuilder"/> allocation.
/// </remarks>
public static class TextUtils
{
    // ── Visual width ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the number of terminal columns occupied by <paramref name="text"/>.
    /// Pure ASCII strings return <c>text.Length</c> via a single SIMD scan.
    /// Wide characters (CJK, emoji, full-width) count as 2 columns.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int VisualWidth(string text)
    {
        if (System.Text.Ascii.IsValid(text)) return text.Length;

        // Non-ASCII: find the first non-ASCII boundary, count prefix as-is,
        // then Rune-enumerate the remainder.
        int i = 0;
        while (i < text.Length && text[i] < 128) i++;

        int width = i;
        foreach (Rune r in text.AsSpan(i).EnumerateRunes())
            width += RuneDisplayWidth(r);
        return width;
    }

    /// <summary>Returns the number of terminal columns occupied by the span.</summary>
    public static int VisualWidth(ReadOnlySpan<char> text)
    {
        // Span overload: manual ASCII scan (no string.IsAscii overload for spans in net8).
        int i = 0;
        while (i < text.Length && text[i] < 128) i++;
        if (i == text.Length) return text.Length;

        int width = i;
        foreach (Rune r in text[i..].EnumerateRunes())
            width += RuneDisplayWidth(r);
        return width;
    }

    // ── Truncation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Truncates <paramref name="text"/> so its visual width does not exceed
    /// <paramref name="maxWidth"/> terminal columns.
    /// Returns the original string reference unchanged when it already fits.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string TruncateToWidth(string text, int maxWidth)
    {
        if (maxWidth <= 0) return string.Empty;

        if (System.Text.Ascii.IsValid(text))
            return text.Length <= maxWidth ? text : text[..maxWidth];

        // Non-ASCII: scan ASCII prefix char-by-char, then Rune-enumerate remainder.
        int i = 0;
        while (i < text.Length && text[i] < 128)
        {
            if (i >= maxWidth) return text[..maxWidth];
            i++;
        }
        if (i == text.Length) return text;

        int width = i;
        int chars = i;
        foreach (Rune r in text.AsSpan(i).EnumerateRunes())
        {
            int rw = RuneDisplayWidth(r);
            if (width + rw > maxWidth) break;
            width += rw;
            chars += r.Utf16SequenceLength;
        }
        return chars >= text.Length ? text : text[..chars];
    }

    // ── Padding ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Right-pads or truncates <paramref name="text"/> so its visual width equals exactly
    /// <paramref name="targetWidth"/> terminal columns.
    /// Wide characters that would overflow by exactly 1 column have a space substituted.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string FitToWidth(string text, int targetWidth)
    {
        if (targetWidth <= 0) return string.Empty;

        if (System.Text.Ascii.IsValid(text))
        {
            if (text.Length == targetWidth) return text;
            if (text.Length  > targetWidth) return text[..targetWidth];
            return text.PadRight(targetWidth);
        }

        // Non-ASCII general path.
        var sb    = new StringBuilder(targetWidth + 4);
        int width = 0;

        foreach (Rune r in text.EnumerateRunes())
        {
            int rw = RuneDisplayWidth(r);
            if (width + rw > targetWidth)
            {
                if (rw == 2 && width + 1 == targetWidth)
                {
                    sb.Append(' ');
                    width++;
                }
                break;
            }
            sb.Append(r.ToString());
            width += rw;
        }

        while (width < targetWidth) { sb.Append(' '); width++; }
        return sb.ToString();
    }

    // ── Wrap ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits <paramref name="text"/> into lines of at most <paramref name="width"/>
    /// terminal columns. Hard newlines in the source always produce a line break.
    /// </summary>
    public static List<string> WrapToWidth(string text, int width)
    {
        if (width <= 0) return [];

        var result = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            if (rawLine.Length == 0) { result.Add(""); continue; }

            if (System.Text.Ascii.IsValid(rawLine))
            {
                // Pure ASCII fast path.
                var rem = rawLine.AsSpan();
                while (rem.Length > width)
                {
                    result.Add(rem[..width].ToString());
                    rem = rem[width..];
                }
                result.Add(rem.ToString());
                continue;
            }

            // General path: rune-aware wrapping.
            var sb  = new StringBuilder(width + 4);
            int col = 0;
            foreach (Rune r in rawLine.EnumerateRunes())
            {
                int rw = RuneDisplayWidth(r);
                if (col + rw > width)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    col = 0;
                }
                sb.Append(r.ToString());
                col += rw;
            }
            if (sb.Length > 0 || col == 0) result.Add(sb.ToString());
        }
        return result;
    }

    // ── Rune width ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the terminal display width of a single <see cref="Rune"/>:
    /// 2 for wide characters (CJK, full-width, most emoji), 1 for everything else.
    /// Uses codepoint range arithmetic — no allocation.
    /// </summary>
    public static int RuneDisplayWidth(Rune rune)
    {
        int v = rune.Value;
        return v switch
        {
            >= 0x1100 and <= 0x115F   => 2, // Hangul Jamo
            >= 0x2E80 and <= 0x303E   => 2, // CJK radicals, Kangxi
            >= 0x3041 and <= 0x33FF   => 2, // Japanese kana + CJK compatibility
            >= 0x3400 and <= 0x4DBF   => 2, // CJK Extension A
            >= 0x4E00 and <= 0x9FFF   => 2, // CJK Unified Ideographs
            >= 0xA000 and <= 0xA4CF   => 2, // Yi syllables
            >= 0xAC00 and <= 0xD7AF   => 2, // Hangul syllables
            >= 0xF900 and <= 0xFAFF   => 2, // CJK compatibility ideographs
            >= 0xFE10 and <= 0xFE1F   => 2, // Vertical forms
            >= 0xFE30 and <= 0xFE4F   => 2, // CJK compatibility forms
            >= 0xFE50 and <= 0xFE6F   => 2, // Small form variants
            >= 0xFF01 and <= 0xFF60   => 2, // Fullwidth ASCII + punctuation
            >= 0xFFE0 and <= 0xFFE6   => 2, // Fullwidth signs
            >= 0x1B000 and <= 0x1B0FF => 2, // Kana supplement
            >= 0x1F004 and <= 0x1F0CF => 2, // Mahjong / playing cards
            >= 0x1F300 and <= 0x1FAFF => 2, // Misc symbols, emoji, pictographs
            >= 0x20000 and <= 0x2FA1F => 2, // CJK extensions B–F + compatibility
            _ => 1
        };
    }
}
