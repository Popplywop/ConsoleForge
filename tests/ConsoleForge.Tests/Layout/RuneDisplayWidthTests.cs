using System.Text;
using ConsoleForge.Layout;

namespace ConsoleForge.Tests.Layout;

/// <summary>
/// Column widths must match what a terminal actually advances the cursor by,
/// otherwise every glyph after the offender lands one column off and the frame
/// diff — which trusts its own model of the screen — never repairs it.
/// Expectations here are East_Asian_Width from the Unicode Character Database.
/// </summary>
public class RuneDisplayWidthTests
{
    private static int Width(string s) => TextUtils.RuneDisplayWidth(Rune.GetRuneAt(s, 0));

    [Theory]
    // Plain text.
    [InlineData("A")]
    [InlineData("1")]
    [InlineData(" ")]
    // Latin-1 / punctuation that is East_Asian_Width Ambiguous — narrow by default.
    [InlineData("·")] // MIDDLE DOT
    [InlineData("—")] // EM DASH
    [InlineData("…")] // HORIZONTAL ELLIPSIS
    // Pictographs whose *default* presentation is text, not emoji: terminals
    // render these in a single column unless U+FE0F follows.
    [InlineData("\U0001F39E")] // FILM FRAMES
    [InlineData("\U0001F5A5")] // DESKTOP COMPUTER
    [InlineData("⚙")]     // GEAR
    [InlineData("⚠")]     // WARNING SIGN
    [InlineData("▶")]     // BLACK RIGHT-POINTING TRIANGLE
    [InlineData("▸")]     // BLACK RIGHT-POINTING SMALL TRIANGLE
    [InlineData("★")]     // BLACK STAR
    [InlineData("✓")]     // CHECK MARK
    // Box drawing — the border characters every BorderBox emits.
    [InlineData("─")]
    [InlineData("╭")]
    public void NarrowCharacters_AreOneColumn(string s) => Assert.Equal(1, Width(s));

    [Theory]
    // Default-emoji-presentation pictographs.
    [InlineData("\U0001F4DA")] // BOOKS
    [InlineData("\U0001F4FA")] // TELEVISION
    [InlineData("\U0001F600")] // GRINNING FACE
    [InlineData("⌚")]     // WATCH
    [InlineData("⭐")]     // WHITE MEDIUM STAR
    // CJK, kana, Hangul, fullwidth forms.
    [InlineData("一")]     // CJK UNIFIED IDEOGRAPH-4E00
    [InlineData("あ")]     // HIRAGANA LETTER A
    [InlineData("가")]     // HANGUL SYLLABLE GA
    [InlineData("Ａ")]     // FULLWIDTH LATIN CAPITAL LETTER A
    [InlineData("\U00020000")] // CJK Extension B
    public void WideCharacters_AreTwoColumns(string s) => Assert.Equal(2, Width(s));

    [Theory]
    [InlineData("́")] // COMBINING ACUTE ACCENT
    [InlineData("̈")] // COMBINING DIAERESIS
    [InlineData("‍")] // ZERO WIDTH JOINER
    [InlineData("​")] // ZERO WIDTH SPACE
    [InlineData("️")] // VARIATION SELECTOR-16
    [InlineData("︎")] // VARIATION SELECTOR-15
    [InlineData("­")] // SOFT HYPHEN
    public void ZeroWidthCharacters_AreZeroColumns(string s) => Assert.Equal(0, Width(s));

    // ── String-level width ────────────────────────────────────────────────────

    [Fact]
    public void CombiningMarks_DoNotAddColumns()
    {
        Assert.Equal(1, TextUtils.VisualWidth("é"));
        Assert.Equal(3, TextUtils.VisualWidth("ééé"));
        // Precomposed and decomposed forms must measure the same.
        Assert.Equal(TextUtils.VisualWidth("é"), TextUtils.VisualWidth("é"));
    }

    [Fact]
    public void VariationSelector16_WidensTextPresentationBase()
    {
        Assert.Equal(1, TextUtils.VisualWidth("⚙"));         // gear, text presentation
        Assert.Equal(2, TextUtils.VisualWidth("⚙️"));   // gear, emoji presentation
        Assert.Equal(1, TextUtils.VisualWidth("\U0001F39E"));
        Assert.Equal(2, TextUtils.VisualWidth("\U0001F39E️"));
    }

    [Fact]
    public void VariationSelector16_DoesNotWidenAnAlreadyWideBase()
    {
        Assert.Equal(2, TextUtils.VisualWidth("\U0001F4FA"));
        Assert.Equal(2, TextUtils.VisualWidth("\U0001F4FA️"));
    }

    /// <summary>The exact strings PlexTui puts in its panel titles.</summary>
    [Fact]
    public void PlexTuiPanelTitles_MeasureAsRendered()
    {
        Assert.Equal(2 + 2 + 9,  TextUtils.VisualWidth("\U0001F4DA  Libraries"));
        Assert.Equal(1 + 3 + 8,  TextUtils.VisualWidth("\U0001F39E   Episodes"));
        Assert.Equal(2 + 2 + 7,  TextUtils.VisualWidth("\U0001F4FA  Seasons"));
    }

    // ── Truncation and fitting must agree with VisualWidth ────────────────────

    [Theory]
    [InlineData("\U0001F39E abc")]
    [InlineData("\U0001F4FA abc")]
    [InlineData("éabc")]
    [InlineData("⚙️abc")]
    [InlineData("一一abc")]
    public void FitToWidth_ProducesExactlyTheRequestedColumns(string text)
    {
        for (int target = 1; target <= 10; target++)
            Assert.Equal(target, TextUtils.VisualWidth(TextUtils.FitToWidth(text, target)));
    }

    [Theory]
    [InlineData("\U0001F39E abc")]
    [InlineData("\U0001F4FA abc")]
    [InlineData("éabc")]
    [InlineData("一一abc")]
    public void TruncateToWidth_NeverExceedsTheLimit(string text)
    {
        for (int limit = 0; limit <= 10; limit++)
            Assert.True(TextUtils.VisualWidth(TextUtils.TruncateToWidth(text, limit)) <= limit);
    }

    // ── Table integrity ───────────────────────────────────────────────────────

    [Fact]
    public void EveryCodepoint_HasAWidthOfZeroOneOrTwo()
    {
        // Also proves the range table stays sorted and non-overlapping: the binary
        // search would fall out of the set if a regeneration produced bad data.
        for (int v = 0; v <= 0x10FFFF; v++)
        {
            if (v is >= 0xD800 and <= 0xDFFF) continue; // surrogates are not runes
            Assert.InRange(TextUtils.RuneDisplayWidth(new Rune(v)), 0, 2);
        }
    }

    [Fact]
    public void PrintableAsciiAndLatin1_AreOneColumn()
    {
        for (int v = 0x20; v < 0x7F; v++)
            Assert.Equal(1, TextUtils.RuneDisplayWidth(new Rune(v)));

        // Above the C1 controls, U+00AD SOFT HYPHEN is the only zero-width
        // codepoint before the combining marks begin at U+0300.
        for (int v = 0xA0; v < 0x300; v++)
            Assert.Equal(v == 0xAD ? 0 : 1, TextUtils.RuneDisplayWidth(new Rune(v)));
    }
}
