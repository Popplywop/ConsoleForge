using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Testing;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Core;

/// <summary>
/// <see cref="RegionChangedMsg"/>: the framework tells the model where its keyed widgets
/// ended up, so viewport sizes come from layout instead of hard-coded guesses (WISHLIST 9).
/// </summary>
public class RegionReporterTests
{
    private static IWidget Screen(int headerRows, string? listKey = "results", string? inputKey = "query") =>
        new Container(Axis.Vertical,
        [
            new TextBlock("header") { Height = SizeConstraint.Fixed(headerRows) },
            new TextInput { FocusKey = inputKey, Height = SizeConstraint.Fixed(1) },
            new List(["a", "b", "c"]) { FocusKey = listKey },
        ]);

    private static List<RegionChangedMsg> Report(RegionReporter reporter, IWidget root, int width = 30, int height = 10)
    {
        var posted = new List<RegionChangedMsg>();
        reporter.Report(root, LayoutEngine.Resolve(root, width, height), posted.Add);
        return posted;
    }

    [Fact]
    public void FirstFrame_ReportsEveryKeyedFocusable()
    {
        var posted = Report(new RegionReporter(), Screen(headerRows: 2));

        Assert.Equal<RegionChangedMsg>(
            [
                new("query", new Region(0, 2, 30, 1)),
                new("results", new Region(0, 3, 30, 7)),
            ],
            posted);
    }

    [Fact]
    public void UnchangedLayout_ReportsNothing()
    {
        var reporter = new RegionReporter();
        Report(reporter, Screen(2));

        // A new widget tree with the same layout: the common case, every frame.
        Assert.Empty(Report(reporter, Screen(2)));
    }

    [Fact]
    public void LayoutChange_ReportsOnlyWhatMoved()
    {
        var reporter = new RegionReporter();
        Report(reporter, Screen(2));

        // Taller header: the input moves down, the list moves and shrinks.
        var posted = Report(reporter, Screen(4));
        Assert.Equal<RegionChangedMsg>(
            [
                new("query", new Region(0, 4, 30, 1)),
                new("results", new Region(0, 5, 30, 5)),
            ],
            posted);

        // Terminal resize: only height changes reach the list.
        posted = Report(reporter, Screen(4), width: 30, height: 12);
        Assert.Equal(new RegionChangedMsg("results", new Region(0, 5, 30, 7)), Assert.Single(posted));
    }

    [Fact]
    public void UnkeyedFocusables_AreNotReported()
    {
        Assert.Empty(Report(new RegionReporter(), Screen(2, listKey: null, inputKey: null)));
    }

    [Fact]
    public void KeyThatLeaves_IsReportedAgainWhenItReturns()
    {
        var reporter = new RegionReporter();
        Report(reporter, Screen(2));
        Report(reporter, Screen(2, listKey: null));        // the list loses its key

        var posted = Report(reporter, Screen(2));
        Assert.Equal("results", Assert.Single(posted).FocusKey);
    }

    [Fact]
    public void DuplicateKeys_FirstInDeclarationOrderWins()
    {
        var posted = Report(new RegionReporter(), Screen(2, listKey: "same", inputKey: "same"));
        Assert.Equal(new RegionChangedMsg("same", new Region(0, 2, 30, 1)), Assert.Single(posted));
    }

    [Fact]
    public void UnchangedFrame_AllocatesNothing()
    {
        // Tier 1: this runs after every frame, so its steady state must be free.
        IWidget root = new Container(Axis.Vertical, [.. Enumerable.Range(0, 20).Select(i =>
            (IWidget)new Container(Axis.Horizontal,
                [new TextBlock($"label {i}"), new TextInput { FocusKey = $"in{i}" }, new Checkbox { FocusKey = $"cb{i}" }]))]);
        var layout   = LayoutEngine.Resolve(root, 120, 40);
        var reporter = new RegionReporter();
        int posted   = 0;
        Action<RegionChangedMsg> post = _ => posted++;

        for (int i = 0; i < 100; i++) reporter.Report(root, layout, post);   // warm up
        Assert.Equal(40, posted);                                           // first frame only

        const int frames = 1000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < frames; i++) reporter.Report(root, layout, post);
        double perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)frames;

        Assert.True(perFrame < 1, $"{perFrame:F1} bytes per unchanged frame");
        Assert.Equal(40, posted);
    }

    // ── Through App ───────────────────────────────────────────────────────────

    private sealed record ListModel(ListState List, TaskCompletionSource<int> Viewport) : IModel
    {
        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg)
        {
            switch (msg)
            {
                case RegionChangedMsg { FocusKey: "results" } r:
                    Viewport.TrySetResult(r.Region.Height);
                    return (this with { List = List.WithViewport(r.Region.Height) }, null);
                case KeyMsg { Key: ConsoleKey.Q }:
                    return (this, Cmd.Quit());
                default:
                    return (this, null);
            }
        }

        public IWidget View() => new Container(Axis.Vertical,
        [
            new TextBlock("header") { Height = SizeConstraint.Fixed(3) },
            new List([.. Enumerable.Range(0, 50).Select(i => $"item {i}")]) { FocusKey = "results" },
        ]);
    }

    [Fact]
    public async Task App_DeliversTheRegionToTheModel()
    {
        var viewport = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = new VirtualTerminal(30, 12);
        var run = App.Run(new ListModel(new ListState(50), viewport), terminal, Theme.Dark);

        // 12 rows minus the 3-row header — derived from layout, not hard-coded.
        Assert.Equal(9, await viewport.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await run;
    }
}
