using System.Text;
using ConsoleForge.Layout;

namespace ConsoleForge.Tests.Layout;

/// <summary>Unit tests for <see cref="TextUtils"/>.</summary>
public class TextUtilsTests
{
    // ── VisualWidth ───────────────────────────────────────────────────────────

    [Fact]
    public void VisualWidth_EmptyString_IsZero()
    {
        Assert.Equal(0, TextUtils.VisualWidth(""));
    }

    [Fact]
    public void VisualWidth_AsciiString_EqualsByteLength()
    {
        Assert.Equal(5, TextUtils.VisualWidth("hello"));
        Assert.Equal(3, TextUtils.VisualWidth("ABC"));
    }

    [Fact]
    public void VisualWidth_CjkChar_CountsAsTwo()
    {
        // 日 is U+65E5, in the CJK Unified Ideographs block → width 2
        Assert.Equal(2, TextUtils.VisualWidth("日"));
    }

    [Fact]
    public void VisualWidth_ThreeCjkChars_SixColumns()
    {
        Assert.Equal(6, TextUtils.VisualWidth("日本語"));
    }

    [Fact]
    public void VisualWidth_MixedAsciiAndCjk()
    {
        // "A日B" = 1 + 2 + 1 = 4
        Assert.Equal(4, TextUtils.VisualWidth("A日B"));
    }

    [Fact]
    public void VisualWidth_Emoji_CountsAsTwo()
    {
        // 😀 U+1F600, in emoji block → width 2
        Assert.Equal(2, TextUtils.VisualWidth("😀"));
    }

    [Fact]
    public void VisualWidth_FullwidthLatin_CountsAsTwo()
    {
        // Ａ U+FF21 is fullwidth Latin Capital A → width 2
        Assert.Equal(2, TextUtils.VisualWidth("Ａ"));
    }

    [Fact]
    public void VisualWidth_SpanOverload_SameAsStringOverload()
    {
        var s = "A日B";
        Assert.Equal(TextUtils.VisualWidth(s), TextUtils.VisualWidth(s.AsSpan()));
    }

    // ── TruncateToWidth ───────────────────────────────────────────────────────

    [Fact]
    public void TruncateToWidth_ShortAscii_Unchanged()
    {
        Assert.Equal("hi", TextUtils.TruncateToWidth("hi", 10));
    }

    [Fact]
    public void TruncateToWidth_ExactAscii_Unchanged()
    {
        Assert.Equal("hello", TextUtils.TruncateToWidth("hello", 5));
    }

    [Fact]
    public void TruncateToWidth_LongAscii_Truncated()
    {
        Assert.Equal("hel", TextUtils.TruncateToWidth("hello", 3));
    }

    [Fact]
    public void TruncateToWidth_CjkExact_Unchanged()
    {
        // "日本" = 4 columns → fits in 4
        Assert.Equal("日本", TextUtils.TruncateToWidth("日本", 4));
    }

    [Fact]
    public void TruncateToWidth_CjkTruncatesMidChar()
    {
        // "日本語" = 6 cols, truncate to 5 → "日本" (4 cols) because "語" would push to 6
        var result = TextUtils.TruncateToWidth("日本語", 5);
        Assert.Equal("日本", result);
        Assert.Equal(4, TextUtils.VisualWidth(result));
    }

    [Fact]
    public void TruncateToWidth_ZeroWidth_ReturnsEmpty()
    {
        Assert.Equal("", TextUtils.TruncateToWidth("hello", 0));
    }

    [Fact]
    public void TruncateToWidth_NegativeWidth_ReturnsEmpty()
    {
        Assert.Equal("", TextUtils.TruncateToWidth("hello", -1));
    }

    [Fact]
    public void TruncateToWidth_MixedAsciiCjk_CorrectResult()
    {
        // "A日B" = 4 cols, truncate to 3 → "A日" = 3... wait: A=1, 日=2, total 3 fits
        Assert.Equal("A日", TextUtils.TruncateToWidth("A日B", 3));
    }

    // ── FitToWidth ────────────────────────────────────────────────────────────

    [Fact]
    public void FitToWidth_ShortAscii_PadsRight()
    {
        var result = TextUtils.FitToWidth("hi", 5);
        Assert.Equal("hi   ", result);
        Assert.Equal(5, result.Length);
    }

    [Fact]
    public void FitToWidth_ExactAscii_Unchanged()
    {
        var result = TextUtils.FitToWidth("hello", 5);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void FitToWidth_LongAscii_Truncated()
    {
        var result = TextUtils.FitToWidth("hello world", 5);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void FitToWidth_CjkPadsToExact()
    {
        // "日" = 2 cols, fit to 5 → "日   " (3 spaces to pad to 5)
        var result = TextUtils.FitToWidth("日", 5);
        Assert.Equal(5, TextUtils.VisualWidth(result));
    }

    [Fact]
    public void FitToWidth_CjkWouldOverflow_SubstitutesSpace()
    {
        // "日本" = 4 cols, fit to 3 → "日 " (wide char 日 takes 2, then 1 space, total 3)
        // Actually: 日 fits (2 cols), 本 would make 4 > 3, but 本 is 2 wide and we need 1 more col →
        // substitute a space for the partial wide char
        var result = TextUtils.FitToWidth("日本", 3);
        Assert.Equal(3, TextUtils.VisualWidth(result));
    }

    [Fact]
    public void FitToWidth_ZeroWidth_ReturnsEmpty()
    {
        Assert.Equal("", TextUtils.FitToWidth("hello", 0));
    }

    [Fact]
    public void FitToWidth_EmptyString_ReturnsPaddedSpaces()
    {
        var result = TextUtils.FitToWidth("", 4);
        Assert.Equal("    ", result);
    }

    // ── WrapToWidth ───────────────────────────────────────────────────────────

    [Fact]
    public void WrapToWidth_ShortLine_NoWrap()
    {
        var lines = TextUtils.WrapToWidth("hello", 10);
        Assert.Single(lines);
        Assert.Equal("hello", lines[0]);
    }

    [Fact]
    public void WrapToWidth_ExactWidth_NoWrap()
    {
        var lines = TextUtils.WrapToWidth("hello", 5);
        Assert.Single(lines);
        Assert.Equal("hello", lines[0]);
    }

    [Fact]
    public void WrapToWidth_LongAscii_WrapsAtWidth()
    {
        var lines = TextUtils.WrapToWidth("abcdefghij", 4);
        Assert.Equal(3, lines.Count);
        Assert.Equal("abcd", lines[0]);
        Assert.Equal("efgh", lines[1]);
        Assert.Equal("ij",   lines[2]);
    }

    [Fact]
    public void WrapToWidth_HardNewline_AlwaysBreaks()
    {
        var lines = TextUtils.WrapToWidth("a\nb\nc", 10);
        Assert.Equal(3, lines.Count);
        Assert.Equal("a", lines[0]);
        Assert.Equal("b", lines[1]);
        Assert.Equal("c", lines[2]);
    }

    [Fact]
    public void WrapToWidth_EmptyLine_PreservedAsEmpty()
    {
        var lines = TextUtils.WrapToWidth("a\n\nb", 10);
        Assert.Equal(3, lines.Count);
        Assert.Equal("", lines[1]);
    }

    [Fact]
    public void WrapToWidth_CjkWrapsByColumn()
    {
        // "日本語文字" = 10 cols, wrap to 4 → ["日本", "語文", "字"]
        var lines = TextUtils.WrapToWidth("日本語文字", 4);
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.True(TextUtils.VisualWidth(l) <= 4));
    }

    [Fact]
    public void WrapToWidth_ZeroWidth_ReturnsEmpty()
    {
        var lines = TextUtils.WrapToWidth("hello", 0);
        Assert.Empty(lines);
    }

    // ── RuneDisplayWidth ─────────────────────────────────────────────────────

    [Fact]
    public void RuneDisplayWidth_AsciiLetter_IsOne()
    {
        Assert.Equal(1, TextUtils.RuneDisplayWidth(new Rune('A')));
    }

    [Fact]
    public void RuneDisplayWidth_Space_IsOne()
    {
        Assert.Equal(1, TextUtils.RuneDisplayWidth(new Rune(' ')));
    }

    [Fact]
    public void RuneDisplayWidth_CjkIdeograph_IsTwo()
    {
        // 中 U+4E2D in CJK Unified Ideographs
        Assert.Equal(2, TextUtils.RuneDisplayWidth(new Rune('中')));
    }

    [Fact]
    public void RuneDisplayWidth_HangulSyllable_IsTwo()
    {
        // 가 U+AC00
        Assert.Equal(2, TextUtils.RuneDisplayWidth(new Rune('가')));
    }

    [Fact]
    public void RuneDisplayWidth_FullwidthLatin_IsTwo()
    {
        // Ａ U+FF21
        Assert.Equal(2, TextUtils.RuneDisplayWidth(new Rune('Ａ')));
    }
}
