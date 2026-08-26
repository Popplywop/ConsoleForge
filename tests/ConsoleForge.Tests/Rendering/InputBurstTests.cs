using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Testing;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Rendering;

/// <summary>
/// Holding a key down delivers input far faster than a frame takes to draw.
/// The loop must apply every one of those events and show the final result,
/// without paying for a frame per event.
/// </summary>
public class InputBurstTests
{
    /// <summary>Tracks work done by the loop across a run.</summary>
    private sealed class Counters
    {
        public int Views;    // View() calls — one per rendered frame
        public int Updates;  // key events actually applied
        public int Index;    // final selection
    }

    /// <summary>A scrolling list sized like a real page, so a frame costs real work.</summary>
    private sealed record ListModel(Counters C, int Index = 0, int Scroll = 0) : IModel
    {
        public const int Viewport = 22;

        public static readonly string[] Items =
            [.. Enumerable.Range(0, 1000).Select(i => $"Item {i:D4}")];

        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
        {
            KeyMsg { Key: ConsoleKey.DownArrow } => Move(1),
            KeyMsg { Key: ConsoleKey.UpArrow }   => Move(-1),
            KeyMsg { Key: ConsoleKey.Q }         => (this, Cmd.Quit()),
            _ => (this, null),
        };

        private (IModel, ICmd?) Move(int delta)
        {
            C.Updates++;
            var next = Math.Clamp(Index + delta, 0, Items.Length - 1);
            C.Index = next;
            return (this with
            {
                Index  = next,
                Scroll = List.ComputeScrollOffset(next, Viewport, Scroll),
            }, null);
        }

        public IWidget View()
        {
            Interlocked.Increment(ref C.Views);
            return new Container(Axis.Vertical, [
                new BorderBox("Episodes",
                    new List(Items, Index, scrollOffset: Scroll),
                    style: Style.Default.BorderForeground(Color.Cyan)),
            ]);
        }
    }

    private static async Task<Counters> RunBurst(int keyCount)
    {
        var counters = new Counters();
        var terminal = new VirtualTerminal(80, 24);
        var run = App.Run(new ListModel(counters), terminal, Theme.Dark, targetFps: 30);

        await Task.Delay(100); // let the loop come up

        for (int i = 0; i < keyCount; i++)
            terminal.EnqueueKey(new KeyMsg(ConsoleKey.DownArrow, null));

        await Task.Delay(600); // let it drain

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await Task.WhenAny(run, Task.Delay(2000));

        return counters;
    }

    [Fact]
    public async Task BurstOfKeys_IsFullyApplied()
    {
        const int Keys = 300;
        var c = await RunBurst(Keys);

        Assert.Equal(Keys, c.Updates);
        Assert.Equal(Keys, c.Index);
    }

    [Fact]
    public async Task BurstOfKeys_DoesNotDrawAFramePerKey()
    {
        const int Keys = 300;
        var c = await RunBurst(Keys);

        // The burst arrives in well under a frame interval. Redrawing per event
        // throws away almost all of that work: only the last state is visible.
        Assert.True(c.Views < Keys / 4,
            $"drew {c.Views} frames for {Keys} keys — roughly one frame per key");
    }

    [Fact]
    public async Task LastFrameShowsTheFinalSelection()
    {
        var c = await RunBurst(300);
        Assert.Equal(300, c.Index);
    }

    [Fact]
    public async Task SingleKeypress_IsDrawnPromptly()
    {
        // Coalescing must not swallow an isolated keystroke: when the rate limit
        // defers it, the FPS timer still has to draw it within a frame interval.
        var counters = new Counters();
        var terminal = new VirtualTerminal(80, 24);
        var run = App.Run(new ListModel(counters), terminal, Theme.Dark, targetFps: 30);

        await Task.Delay(100);
        var viewsBefore = counters.Views;

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.DownArrow, null));
        await Task.Delay(100); // three frame intervals at 30fps

        Assert.True(counters.Views > viewsBefore,
            "an isolated keypress produced no frame");
        Assert.Contains("Item 0001", terminal.ScreenContent);

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await Task.WhenAny(run, Task.Delay(2000));
    }
}
