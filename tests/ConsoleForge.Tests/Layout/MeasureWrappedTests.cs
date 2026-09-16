using ConsoleForge.Layout;

namespace ConsoleForge.Tests.Layout;

/// <summary>
/// <see cref="TextUtils.MeasureWrapped"/> reports the shape of the wrap that
/// <see cref="TextUtils.WrapToWidth"/> produces, without building it. Two implementations
/// of the same rule is exactly the shape of bug that made layout and render disagree, so
/// these pin them together rather than testing the measuring one on its own.
/// </summary>
public class MeasureWrappedTests
{
    private static readonly string[] Corpus =
    [
        "",
        "a",
        "hello",
        "hello world",
        "exactly-ten",
        "a\nb\nc",
        "\n",
        "\n\n\n",
        "trailing\n",
        "\nleading",
        "one very long line that will certainly need to wrap several times over",
        "line one\nand a much longer second line that wraps\nshort",
        "日本語のテキスト",                 // wide, 2 columns each
        "mixed 日本語 and ascii",
        "emoji 🎞 in text",                 // surrogate pair, narrow
        "e\u0301combining",                 // combining mark, zero width
        "flag \U0001F1EF\U0001F1F5 here",   // regional indicators
        "vs16 \u2764\uFE0F end",            // variation selector promotes to wide
    ];

    private static readonly int[] Widths = [1, 2, 3, 4, 5, 7, 10, 11, 20, 80];

    [Fact]
    public void LineCount_MatchesWrapToWidth_AcrossTheCorpus()
    {
        foreach (var text in Corpus)
        foreach (var width in Widths)
        {
            int measured = TextUtils.MeasureWrapped(text, width, out _);
            int actual   = TextUtils.WrapToWidth(text, width).Count;

            Assert.True(measured == actual,
                $"line count disagrees for {Describe(text)} at width {width}: " +
                $"measured {measured}, wrapped {actual}");
        }
    }

    [Fact]
    public void WidestLine_MatchesWrapToWidth_AcrossTheCorpus()
    {
        foreach (var text in Corpus)
        foreach (var width in Widths)
        {
            TextUtils.MeasureWrapped(text, width, out int measured);

            int actual = 0;
            foreach (var line in TextUtils.WrapToWidth(text, width))
                actual = Math.Max(actual, TextUtils.VisualWidth(line));

            Assert.True(measured == actual,
                $"widest line disagrees for {Describe(text)} at width {width}: " +
                $"measured {measured}, wrapped {actual}");
        }
    }

    [Fact]
    public void NonPositiveWidth_ProducesNoLines()
    {
        // Matches WrapToWidth, which returns an empty list rather than throwing.
        Assert.Equal(0, TextUtils.MeasureWrapped("anything", 0, out int widest));
        Assert.Equal(0, widest);
        Assert.Empty(TextUtils.WrapToWidth("anything", 0));
    }

    [Fact]
    public void MeasuringDoesNotAllocate()
    {
        // The reason this method exists: Measure runs every frame, and WrapToWidth splits
        // the string and materialises every line only for them to be thrown away.
        const string Text = "a reasonably long line that wraps\nand a second line";

        for (int i = 0; i < 32; i++) TextUtils.MeasureWrapped(Text, 12, out _); // warm up

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) TextUtils.MeasureWrapped(Text, 12, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private static string Describe(string text) =>
        $"\"{text.Replace("\n", "\\n")}\"";
}
