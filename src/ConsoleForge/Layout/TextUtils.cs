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
    // ── Grapheme-aware rune walking ───────────────────────────────────────────

    /// <summary>
    /// Running column counter for a sequence of runes. Applies the variation-selector
    /// rule that <see cref="RuneDisplayWidth"/> cannot see on its own: U+FE0F occupies
    /// no column but promotes a preceding 1-column character to emoji presentation,
    /// which terminals draw 2 columns wide.
    /// </summary>
    internal struct WidthWalker
    {
        private int _previous; // columns contributed by the last spacing rune

        /// <summary>Columns <paramref name="rune"/> adds to the running total.</summary>
        public int Next(Rune rune)
        {
            int width = RuneDisplayWidth(rune);

            if (width == 0)
            {
                if (rune.Value != VariationSelector16 || _previous != 1) return 0;
                _previous = 2;
                return 1; // the base character widens from 1 column to 2
            }

            _previous = width;
            return width;
        }
    }

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
        var walker = new WidthWalker();
        foreach (Rune r in text.AsSpan(i).EnumerateRunes())
            width += walker.Next(r);
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
        var walker = new WidthWalker();
        foreach (Rune r in text[i..].EnumerateRunes())
            width += walker.Next(r);
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
        var walker = new WidthWalker();
        foreach (Rune r in text.AsSpan(i).EnumerateRunes())
        {
            int rw = walker.Next(r);
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
        var sb     = new StringBuilder(targetWidth + 4);
        int width  = 0;
        var walker = new WidthWalker();

        foreach (Rune r in text.EnumerateRunes())
        {
            int rw = walker.Next(r);
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
            var sb     = new StringBuilder(width + 4);
            int col    = 0;
            var walker = new WidthWalker();
            foreach (Rune r in rawLine.EnumerateRunes())
            {
                int rw = walker.Next(r);
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
    /// U+FE0F VARIATION SELECTOR-16. Occupies no column of its own, but forces the
    /// preceding character into emoji presentation, which terminals render 2 columns
    /// wide. See <see cref="VisualWidth(string)"/>.
    /// </summary>
    public const int VariationSelector16 = 0xFE0F;

    /// <summary>
    /// Codepoint ranges whose display width is not 1, as flat
    /// <c>[start, end, width]</c> triples sorted by <c>start</c> and non-overlapping.
    /// Width is <c>0</c> for combining marks and control/format characters, <c>2</c>
    /// for East_Asian_Width Wide and Fullwidth.
    /// <para>
    /// Generated from the Unicode Character Database (UnicodeData.txt +
    /// EastAsianWidth.txt, Unicode 16.0). Ranges below U+0300 are handled by the
    /// early-outs in <see cref="RuneDisplayWidth"/> and omitted here.
    /// Do not hand-edit: regenerate from the UCD when bumping Unicode versions.
    /// </para>
    /// </summary>
    private static readonly int[] NonNarrowRanges =
    [
        0x00300, 0x0036F, 0, 0x00483, 0x00489, 0, 0x00591, 0x005BD, 0,
        0x005BF, 0x005BF, 0, 0x005C1, 0x005C2, 0, 0x005C4, 0x005C5, 0,
        0x005C7, 0x005C7, 0, 0x00600, 0x00605, 0, 0x00610, 0x0061A, 0,
        0x0061C, 0x0061C, 0, 0x0064B, 0x0065F, 0, 0x00670, 0x00670, 0,
        0x006D6, 0x006DD, 0, 0x006DF, 0x006E4, 0, 0x006E7, 0x006E8, 0,
        0x006EA, 0x006ED, 0, 0x0070F, 0x0070F, 0, 0x00711, 0x00711, 0,
        0x00730, 0x0074A, 0, 0x007A6, 0x007B0, 0, 0x007EB, 0x007F3, 0,
        0x007FD, 0x007FD, 0, 0x00816, 0x00819, 0, 0x0081B, 0x00823, 0,
        0x00825, 0x00827, 0, 0x00829, 0x0082D, 0, 0x00859, 0x0085B, 0,
        0x00890, 0x00891, 0, 0x00897, 0x0089F, 0, 0x008CA, 0x00902, 0,
        0x0093A, 0x0093A, 0, 0x0093C, 0x0093C, 0, 0x00941, 0x00948, 0,
        0x0094D, 0x0094D, 0, 0x00951, 0x00957, 0, 0x00962, 0x00963, 0,
        0x00981, 0x00981, 0, 0x009BC, 0x009BC, 0, 0x009C1, 0x009C4, 0,
        0x009CD, 0x009CD, 0, 0x009E2, 0x009E3, 0, 0x009FE, 0x009FE, 0,
        0x00A01, 0x00A02, 0, 0x00A3C, 0x00A3C, 0, 0x00A41, 0x00A42, 0,
        0x00A47, 0x00A48, 0, 0x00A4B, 0x00A4D, 0, 0x00A51, 0x00A51, 0,
        0x00A70, 0x00A71, 0, 0x00A75, 0x00A75, 0, 0x00A81, 0x00A82, 0,
        0x00ABC, 0x00ABC, 0, 0x00AC1, 0x00AC5, 0, 0x00AC7, 0x00AC8, 0,
        0x00ACD, 0x00ACD, 0, 0x00AE2, 0x00AE3, 0, 0x00AFA, 0x00AFF, 0,
        0x00B01, 0x00B01, 0, 0x00B3C, 0x00B3C, 0, 0x00B3F, 0x00B3F, 0,
        0x00B41, 0x00B44, 0, 0x00B4D, 0x00B4D, 0, 0x00B55, 0x00B56, 0,
        0x00B62, 0x00B63, 0, 0x00B82, 0x00B82, 0, 0x00BC0, 0x00BC0, 0,
        0x00BCD, 0x00BCD, 0, 0x00C00, 0x00C00, 0, 0x00C04, 0x00C04, 0,
        0x00C3C, 0x00C3C, 0, 0x00C3E, 0x00C40, 0, 0x00C46, 0x00C48, 0,
        0x00C4A, 0x00C4D, 0, 0x00C55, 0x00C56, 0, 0x00C62, 0x00C63, 0,
        0x00C81, 0x00C81, 0, 0x00CBC, 0x00CBC, 0, 0x00CBF, 0x00CBF, 0,
        0x00CC6, 0x00CC6, 0, 0x00CCC, 0x00CCD, 0, 0x00CE2, 0x00CE3, 0,
        0x00D00, 0x00D01, 0, 0x00D3B, 0x00D3C, 0, 0x00D41, 0x00D44, 0,
        0x00D4D, 0x00D4D, 0, 0x00D62, 0x00D63, 0, 0x00D81, 0x00D81, 0,
        0x00DCA, 0x00DCA, 0, 0x00DD2, 0x00DD4, 0, 0x00DD6, 0x00DD6, 0,
        0x00E31, 0x00E31, 0, 0x00E34, 0x00E3A, 0, 0x00E47, 0x00E4E, 0,
        0x00EB1, 0x00EB1, 0, 0x00EB4, 0x00EBC, 0, 0x00EC8, 0x00ECE, 0,
        0x00F18, 0x00F19, 0, 0x00F35, 0x00F35, 0, 0x00F37, 0x00F37, 0,
        0x00F39, 0x00F39, 0, 0x00F71, 0x00F7E, 0, 0x00F80, 0x00F84, 0,
        0x00F86, 0x00F87, 0, 0x00F8D, 0x00F97, 0, 0x00F99, 0x00FBC, 0,
        0x00FC6, 0x00FC6, 0, 0x0102D, 0x01030, 0, 0x01032, 0x01037, 0,
        0x01039, 0x0103A, 0, 0x0103D, 0x0103E, 0, 0x01058, 0x01059, 0,
        0x0105E, 0x01060, 0, 0x01071, 0x01074, 0, 0x01082, 0x01082, 0,
        0x01085, 0x01086, 0, 0x0108D, 0x0108D, 0, 0x0109D, 0x0109D, 0,
        0x01100, 0x0115F, 2, 0x0135D, 0x0135F, 0, 0x01712, 0x01714, 0,
        0x01732, 0x01733, 0, 0x01752, 0x01753, 0, 0x01772, 0x01773, 0,
        0x017B4, 0x017B5, 0, 0x017B7, 0x017BD, 0, 0x017C6, 0x017C6, 0,
        0x017C9, 0x017D3, 0, 0x017DD, 0x017DD, 0, 0x0180B, 0x0180F, 0,
        0x01885, 0x01886, 0, 0x018A9, 0x018A9, 0, 0x01920, 0x01922, 0,
        0x01927, 0x01928, 0, 0x01932, 0x01932, 0, 0x01939, 0x0193B, 0,
        0x01A17, 0x01A18, 0, 0x01A1B, 0x01A1B, 0, 0x01A56, 0x01A56, 0,
        0x01A58, 0x01A5E, 0, 0x01A60, 0x01A60, 0, 0x01A62, 0x01A62, 0,
        0x01A65, 0x01A6C, 0, 0x01A73, 0x01A7C, 0, 0x01A7F, 0x01A7F, 0,
        0x01AB0, 0x01ACE, 0, 0x01B00, 0x01B03, 0, 0x01B34, 0x01B34, 0,
        0x01B36, 0x01B3A, 0, 0x01B3C, 0x01B3C, 0, 0x01B42, 0x01B42, 0,
        0x01B6B, 0x01B73, 0, 0x01B80, 0x01B81, 0, 0x01BA2, 0x01BA5, 0,
        0x01BA8, 0x01BA9, 0, 0x01BAB, 0x01BAD, 0, 0x01BE6, 0x01BE6, 0,
        0x01BE8, 0x01BE9, 0, 0x01BED, 0x01BED, 0, 0x01BEF, 0x01BF1, 0,
        0x01C2C, 0x01C33, 0, 0x01C36, 0x01C37, 0, 0x01CD0, 0x01CD2, 0,
        0x01CD4, 0x01CE0, 0, 0x01CE2, 0x01CE8, 0, 0x01CED, 0x01CED, 0,
        0x01CF4, 0x01CF4, 0, 0x01CF8, 0x01CF9, 0, 0x01DC0, 0x01DFF, 0,
        0x0200B, 0x0200F, 0, 0x0202A, 0x0202E, 0, 0x02060, 0x02064, 0,
        0x02066, 0x0206F, 0, 0x020D0, 0x020F0, 0, 0x0231A, 0x0231B, 2,
        0x02329, 0x0232A, 2, 0x023E9, 0x023EC, 2, 0x023F0, 0x023F0, 2,
        0x023F3, 0x023F3, 2, 0x025FD, 0x025FE, 2, 0x02614, 0x02615, 2,
        0x02630, 0x02637, 2, 0x02648, 0x02653, 2, 0x0267F, 0x0267F, 2,
        0x0268A, 0x0268F, 2, 0x02693, 0x02693, 2, 0x026A1, 0x026A1, 2,
        0x026AA, 0x026AB, 2, 0x026BD, 0x026BE, 2, 0x026C4, 0x026C5, 2,
        0x026CE, 0x026CE, 2, 0x026D4, 0x026D4, 2, 0x026EA, 0x026EA, 2,
        0x026F2, 0x026F3, 2, 0x026F5, 0x026F5, 2, 0x026FA, 0x026FA, 2,
        0x026FD, 0x026FD, 2, 0x02705, 0x02705, 2, 0x0270A, 0x0270B, 2,
        0x02728, 0x02728, 2, 0x0274C, 0x0274C, 2, 0x0274E, 0x0274E, 2,
        0x02753, 0x02755, 2, 0x02757, 0x02757, 2, 0x02795, 0x02797, 2,
        0x027B0, 0x027B0, 2, 0x027BF, 0x027BF, 2, 0x02B1B, 0x02B1C, 2,
        0x02B50, 0x02B50, 2, 0x02B55, 0x02B55, 2, 0x02CEF, 0x02CF1, 0,
        0x02D7F, 0x02D7F, 0, 0x02DE0, 0x02DFF, 0, 0x02E80, 0x02E99, 2,
        0x02E9B, 0x02EF3, 2, 0x02F00, 0x02FD5, 2, 0x02FF0, 0x03029, 2,
        0x0302A, 0x0302D, 0, 0x0302E, 0x0303E, 2, 0x03041, 0x03096, 2,
        0x03099, 0x0309A, 0, 0x0309B, 0x030FF, 2, 0x03105, 0x0312F, 2,
        0x03131, 0x0318E, 2, 0x03190, 0x031E5, 2, 0x031EF, 0x0321E, 2,
        0x03220, 0x03247, 2, 0x03250, 0x0A48C, 2, 0x0A490, 0x0A4C6, 2,
        0x0A66F, 0x0A672, 0, 0x0A674, 0x0A67D, 0, 0x0A69E, 0x0A69F, 0,
        0x0A6F0, 0x0A6F1, 0, 0x0A802, 0x0A802, 0, 0x0A806, 0x0A806, 0,
        0x0A80B, 0x0A80B, 0, 0x0A825, 0x0A826, 0, 0x0A82C, 0x0A82C, 0,
        0x0A8C4, 0x0A8C5, 0, 0x0A8E0, 0x0A8F1, 0, 0x0A8FF, 0x0A8FF, 0,
        0x0A926, 0x0A92D, 0, 0x0A947, 0x0A951, 0, 0x0A960, 0x0A97C, 2,
        0x0A980, 0x0A982, 0, 0x0A9B3, 0x0A9B3, 0, 0x0A9B6, 0x0A9B9, 0,
        0x0A9BC, 0x0A9BD, 0, 0x0A9E5, 0x0A9E5, 0, 0x0AA29, 0x0AA2E, 0,
        0x0AA31, 0x0AA32, 0, 0x0AA35, 0x0AA36, 0, 0x0AA43, 0x0AA43, 0,
        0x0AA4C, 0x0AA4C, 0, 0x0AA7C, 0x0AA7C, 0, 0x0AAB0, 0x0AAB0, 0,
        0x0AAB2, 0x0AAB4, 0, 0x0AAB7, 0x0AAB8, 0, 0x0AABE, 0x0AABF, 0,
        0x0AAC1, 0x0AAC1, 0, 0x0AAEC, 0x0AAED, 0, 0x0AAF6, 0x0AAF6, 0,
        0x0ABE5, 0x0ABE5, 0, 0x0ABE8, 0x0ABE8, 0, 0x0ABED, 0x0ABED, 0,
        0x0AC00, 0x0D7A3, 2, 0x0F900, 0x0FAFF, 2, 0x0FB1E, 0x0FB1E, 0,
        0x0FE00, 0x0FE0F, 0, 0x0FE10, 0x0FE19, 2, 0x0FE20, 0x0FE2F, 0,
        0x0FE30, 0x0FE52, 2, 0x0FE54, 0x0FE66, 2, 0x0FE68, 0x0FE6B, 2,
        0x0FEFF, 0x0FEFF, 0, 0x0FF01, 0x0FF60, 2, 0x0FFE0, 0x0FFE6, 2,
        0x0FFF9, 0x0FFFB, 0, 0x101FD, 0x101FD, 0, 0x102E0, 0x102E0, 0,
        0x10376, 0x1037A, 0, 0x10A01, 0x10A03, 0, 0x10A05, 0x10A06, 0,
        0x10A0C, 0x10A0F, 0, 0x10A38, 0x10A3A, 0, 0x10A3F, 0x10A3F, 0,
        0x10AE5, 0x10AE6, 0, 0x10D24, 0x10D27, 0, 0x10D69, 0x10D6D, 0,
        0x10EAB, 0x10EAC, 0, 0x10EFC, 0x10EFF, 0, 0x10F46, 0x10F50, 0,
        0x10F82, 0x10F85, 0, 0x11001, 0x11001, 0, 0x11038, 0x11046, 0,
        0x11070, 0x11070, 0, 0x11073, 0x11074, 0, 0x1107F, 0x11081, 0,
        0x110B3, 0x110B6, 0, 0x110B9, 0x110BA, 0, 0x110BD, 0x110BD, 0,
        0x110C2, 0x110C2, 0, 0x110CD, 0x110CD, 0, 0x11100, 0x11102, 0,
        0x11127, 0x1112B, 0, 0x1112D, 0x11134, 0, 0x11173, 0x11173, 0,
        0x11180, 0x11181, 0, 0x111B6, 0x111BE, 0, 0x111C9, 0x111CC, 0,
        0x111CF, 0x111CF, 0, 0x1122F, 0x11231, 0, 0x11234, 0x11234, 0,
        0x11236, 0x11237, 0, 0x1123E, 0x1123E, 0, 0x11241, 0x11241, 0,
        0x112DF, 0x112DF, 0, 0x112E3, 0x112EA, 0, 0x11300, 0x11301, 0,
        0x1133B, 0x1133C, 0, 0x11340, 0x11340, 0, 0x11366, 0x1136C, 0,
        0x11370, 0x11374, 0, 0x113BB, 0x113C0, 0, 0x113CE, 0x113CE, 0,
        0x113D0, 0x113D0, 0, 0x113D2, 0x113D2, 0, 0x113E1, 0x113E2, 0,
        0x11438, 0x1143F, 0, 0x11442, 0x11444, 0, 0x11446, 0x11446, 0,
        0x1145E, 0x1145E, 0, 0x114B3, 0x114B8, 0, 0x114BA, 0x114BA, 0,
        0x114BF, 0x114C0, 0, 0x114C2, 0x114C3, 0, 0x115B2, 0x115B5, 0,
        0x115BC, 0x115BD, 0, 0x115BF, 0x115C0, 0, 0x115DC, 0x115DD, 0,
        0x11633, 0x1163A, 0, 0x1163D, 0x1163D, 0, 0x1163F, 0x11640, 0,
        0x116AB, 0x116AB, 0, 0x116AD, 0x116AD, 0, 0x116B0, 0x116B5, 0,
        0x116B7, 0x116B7, 0, 0x1171D, 0x1171D, 0, 0x1171F, 0x1171F, 0,
        0x11722, 0x11725, 0, 0x11727, 0x1172B, 0, 0x1182F, 0x11837, 0,
        0x11839, 0x1183A, 0, 0x1193B, 0x1193C, 0, 0x1193E, 0x1193E, 0,
        0x11943, 0x11943, 0, 0x119D4, 0x119D7, 0, 0x119DA, 0x119DB, 0,
        0x119E0, 0x119E0, 0, 0x11A01, 0x11A0A, 0, 0x11A33, 0x11A38, 0,
        0x11A3B, 0x11A3E, 0, 0x11A47, 0x11A47, 0, 0x11A51, 0x11A56, 0,
        0x11A59, 0x11A5B, 0, 0x11A8A, 0x11A96, 0, 0x11A98, 0x11A99, 0,
        0x11C30, 0x11C36, 0, 0x11C38, 0x11C3D, 0, 0x11C3F, 0x11C3F, 0,
        0x11C92, 0x11CA7, 0, 0x11CAA, 0x11CB0, 0, 0x11CB2, 0x11CB3, 0,
        0x11CB5, 0x11CB6, 0, 0x11D31, 0x11D36, 0, 0x11D3A, 0x11D3A, 0,
        0x11D3C, 0x11D3D, 0, 0x11D3F, 0x11D45, 0, 0x11D47, 0x11D47, 0,
        0x11D90, 0x11D91, 0, 0x11D95, 0x11D95, 0, 0x11D97, 0x11D97, 0,
        0x11EF3, 0x11EF4, 0, 0x11F00, 0x11F01, 0, 0x11F36, 0x11F3A, 0,
        0x11F40, 0x11F40, 0, 0x11F42, 0x11F42, 0, 0x11F5A, 0x11F5A, 0,
        0x13430, 0x13440, 0, 0x13447, 0x13455, 0, 0x1611E, 0x16129, 0,
        0x1612D, 0x1612F, 0, 0x16AF0, 0x16AF4, 0, 0x16B30, 0x16B36, 0,
        0x16F4F, 0x16F4F, 0, 0x16F8F, 0x16F92, 0, 0x16FE0, 0x16FE3, 2,
        0x16FE4, 0x16FE4, 0, 0x16FF0, 0x16FF1, 2, 0x17000, 0x187F7, 2,
        0x18800, 0x18CD5, 2, 0x18CFF, 0x18D08, 2, 0x1AFF0, 0x1AFF3, 2,
        0x1AFF5, 0x1AFFB, 2, 0x1AFFD, 0x1AFFE, 2, 0x1B000, 0x1B122, 2,
        0x1B132, 0x1B132, 2, 0x1B150, 0x1B152, 2, 0x1B155, 0x1B155, 2,
        0x1B164, 0x1B167, 2, 0x1B170, 0x1B2FB, 2, 0x1BC9D, 0x1BC9E, 0,
        0x1BCA0, 0x1BCA3, 0, 0x1CF00, 0x1CF2D, 0, 0x1CF30, 0x1CF46, 0,
        0x1D167, 0x1D169, 0, 0x1D173, 0x1D182, 0, 0x1D185, 0x1D18B, 0,
        0x1D1AA, 0x1D1AD, 0, 0x1D242, 0x1D244, 0, 0x1D300, 0x1D356, 2,
        0x1D360, 0x1D376, 2, 0x1DA00, 0x1DA36, 0, 0x1DA3B, 0x1DA6C, 0,
        0x1DA75, 0x1DA75, 0, 0x1DA84, 0x1DA84, 0, 0x1DA9B, 0x1DA9F, 0,
        0x1DAA1, 0x1DAAF, 0, 0x1E000, 0x1E006, 0, 0x1E008, 0x1E018, 0,
        0x1E01B, 0x1E021, 0, 0x1E023, 0x1E024, 0, 0x1E026, 0x1E02A, 0,
        0x1E08F, 0x1E08F, 0, 0x1E130, 0x1E136, 0, 0x1E2AE, 0x1E2AE, 0,
        0x1E2EC, 0x1E2EF, 0, 0x1E4EC, 0x1E4EF, 0, 0x1E5EE, 0x1E5EF, 0,
        0x1E8D0, 0x1E8D6, 0, 0x1E944, 0x1E94A, 0, 0x1F004, 0x1F004, 2,
        0x1F0CF, 0x1F0CF, 2, 0x1F18E, 0x1F18E, 2, 0x1F191, 0x1F19A, 2,
        0x1F200, 0x1F202, 2, 0x1F210, 0x1F23B, 2, 0x1F240, 0x1F248, 2,
        0x1F250, 0x1F251, 2, 0x1F260, 0x1F265, 2, 0x1F300, 0x1F320, 2,
        0x1F32D, 0x1F335, 2, 0x1F337, 0x1F37C, 2, 0x1F37E, 0x1F393, 2,
        0x1F3A0, 0x1F3CA, 2, 0x1F3CF, 0x1F3D3, 2, 0x1F3E0, 0x1F3F0, 2,
        0x1F3F4, 0x1F3F4, 2, 0x1F3F8, 0x1F43E, 2, 0x1F440, 0x1F440, 2,
        0x1F442, 0x1F4FC, 2, 0x1F4FF, 0x1F53D, 2, 0x1F54B, 0x1F54E, 2,
        0x1F550, 0x1F567, 2, 0x1F57A, 0x1F57A, 2, 0x1F595, 0x1F596, 2,
        0x1F5A4, 0x1F5A4, 2, 0x1F5FB, 0x1F64F, 2, 0x1F680, 0x1F6C5, 2,
        0x1F6CC, 0x1F6CC, 2, 0x1F6D0, 0x1F6D2, 2, 0x1F6D5, 0x1F6D7, 2,
        0x1F6DC, 0x1F6DF, 2, 0x1F6EB, 0x1F6EC, 2, 0x1F6F4, 0x1F6FC, 2,
        0x1F7E0, 0x1F7EB, 2, 0x1F7F0, 0x1F7F0, 2, 0x1F90C, 0x1F93A, 2,
        0x1F93C, 0x1F945, 2, 0x1F947, 0x1F9FF, 2, 0x1FA70, 0x1FA7C, 2,
        0x1FA80, 0x1FA89, 2, 0x1FA8F, 0x1FAC6, 2, 0x1FACE, 0x1FADC, 2,
        0x1FADF, 0x1FAE9, 2, 0x1FAF0, 0x1FAF8, 2, 0x20000, 0x2FFFD, 2,
        0x30000, 0x3FFFD, 2, 0xE0001, 0xE0001, 0, 0xE0020, 0xE007F, 0,
        0xE0100, 0xE01EF, 0,
    ];

    /// <summary>
    /// Returns the number of terminal columns a single <see cref="Rune"/> occupies:
    /// <list type="bullet">
    ///   <item><description><c>0</c> — combining marks, control and format characters
    ///   (including ZWJ and the variation selectors). These attach to the preceding
    ///   character rather than advancing the cursor.</description></item>
    ///   <item><description><c>2</c> — East_Asian_Width Wide or Fullwidth: CJK,
    ///   Hangul, fullwidth forms, and emoji that have default emoji presentation.</description></item>
    ///   <item><description><c>1</c> — everything else, including pictographs with
    ///   default *text* presentation such as U+1F39E FILM FRAMES or U+2699 GEAR.
    ///   Those widen to 2 columns only when followed by
    ///   <see cref="VariationSelector16"/>, which callers handle at string level via
    ///   <see cref="WidthWalker"/>.</description></item>
    /// </list>
    /// Allocation-free. Printable ASCII and Latin-1 return without touching the table.
    /// </summary>
    public static int RuneDisplayWidth(Rune rune)
    {
        int v = rune.Value;

        // Printable ASCII — the overwhelmingly common case.
        if (v < 0x7F) return v < 0x20 ? 0 : 1;

        // Below U+0300 the only non-narrow codepoints are the C1 controls and
        // U+00AD SOFT HYPHEN, so Latin-1 and Latin Extended never reach the table.
        if (v < 0x0300) return v <= 0x9F || v == 0x00AD ? 0 : 1;

        return LookupWidth(v);
    }

    /// <summary>Binary search of <see cref="NonNarrowRanges"/>; 1 when no range matches.</summary>
    private static int LookupWidth(int codepoint)
    {
        var ranges = NonNarrowRanges;
        int lo = 0, hi = (ranges.Length / 3) - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int i   = mid * 3;
            if (codepoint < ranges[i])          hi = mid - 1;
            else if (codepoint > ranges[i + 1]) lo = mid + 1;
            else return ranges[i + 2];
        }
        return 1;
    }

}
