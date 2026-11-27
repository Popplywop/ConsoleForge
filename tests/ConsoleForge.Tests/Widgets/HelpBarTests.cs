using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

public class HelpBarTests
{
    /// <summary>Records writes as one line of text plus the style of every write.</summary>
    private sealed class LineContext(int width) : IRenderContext
    {
        public Region Region { get; } = new(0, 0, width, 1);
        public Theme Theme => Theme.Default;
        public ColorProfile ColorProfile => ColorProfile.TrueColor;
        public ResolvedLayout Layout { get; } = new();
        public CursorDescriptor? Cursor => null;

        private readonly char[] _line = Enumerable.Repeat(' ', width).ToArray();
        public readonly List<(string Text, Style Style)> Writes = [];

        public string Line => new string(_line).TrimEnd();

        public void Write(int col, int row, string text, Style style)
        {
            Writes.Add((text, style));
            for (int i = 0; i < text.Length; i++)
            {
                Assert.InRange(col + i, 0, width - 1); // never outside the region
                _line[col + i] = text[i];
            }
        }

        public void WriteRawEscape(Region region, IRawEscapePayload payload) { }
        public void SetCursorDescriptor(CursorDescriptor cursor) { }
    }

    private static readonly KeyBinding Quit = new("q", "quit", KeyPattern.OfChar('q'));
    private static readonly KeyBinding Back = new("esc", "back", KeyPattern.Of(ConsoleKey.Escape));
    private static readonly KeyBinding Help = new("?", "help", KeyPattern.OfChar('?'));

    private static LineContext Render(HelpBar bar, int width = 60)
    {
        var ctx = new LineContext(width);
        bar.Render(ctx);
        return ctx;
    }

    [Fact]
    public void ListsBindingsInOrder()
    {
        Assert.Equal("q quit · esc back · ? help", Render(new HelpBar(Quit, Back, Help)).Line);
    }

    [Fact]
    public void SkipsDisabledBindings()
    {
        Assert.Equal("q quit · ? help", Render(new HelpBar(Quit, Back with { Enabled = false }, Help)).Line);
    }

    [Fact]
    public void RendersAKeyMapsBindings()
    {
        var map = new KeyMap().On(Quit, () => new RedrawMsg()).On(Help, () => new RedrawMsg());
        Assert.Equal("q quit · ? help", Render(new HelpBar(map.Bindings)).Line);
    }

    [Theory]
    [InlineData(17, "q quit · esc back")]          // exactly fits two
    [InlineData(16, "q quit …")]                   // second does not fit
    [InlineData(6,  "q quit")]                     // only the first, no room to say more
    [InlineData(5,  "…")]                          // not even the first
    public void DropsWhatDoesNotFit_FromTheEnd(int width, string expected)
    {
        var line = Render(new HelpBar(Quit, Back), width).Line;
        Assert.Equal(expected, line);
    }

    [Fact]
    public void KeysStandOut_DescriptionsAreMuted()
    {
        var ctx = Render(new HelpBar(Quit, Back));
        var key  = Assert.Single(ctx.Writes, w => w.Text == "q").Style;
        var desc = Assert.Single(ctx.Writes, w => w.Text == " quit").Style;
        Assert.Equal(Theme.Default.DisabledStyle.Inherit(Theme.Default.BaseStyle), desc);
        Assert.NotEqual(desc, key);
    }

    [Fact]
    public void Measure_IsTheFullLine()
    {
        var bar = new HelpBar(Quit, Back, Help);
        Assert.Equal(new Size(26, 1), bar.Measure(80, 5));
        Assert.Equal(new Size(10, 1), bar.Measure(10, 5));
        Assert.Equal(new Size(0, 0), new HelpBar(Quit with { Enabled = false }).Measure(80, 5));
    }
}
