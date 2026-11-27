using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Testing;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Core;

/// <summary>
/// Dragging a window edge delivers a resize per step — two, since a terminal raises both
/// its <c>Resized</c> event and a <c>ResizeInputEvent</c> — and each one used to be drawn
/// in full the moment it was handled, ahead of the next one already queued behind it.
/// </summary>
public class ResizeCoalescingTests
{
    private sealed class Counters
    {
        public int Views;
        public int LastWidth;
    }

    private sealed record SizedModel(Counters C, int Width = 0) : IModel
    {
        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
        {
            WindowResizeMsg r => (this with { Width = r.Width }, null),
            KeyMsg { Key: ConsoleKey.Q } => (this, Cmd.Quit()),
            _ => (this, null),
        };

        public IWidget View()
        {
            Interlocked.Increment(ref C.Views);
            C.LastWidth = Width;
            return new Container(Axis.Vertical, [.. Enumerable.Range(0, 20)
                .Select(i => (IWidget)new BorderBox($"Pane {i}", new TextBlock($"width {Width}")))]);
        }
    }

    [Fact]
    public async Task ARunOfResizes_IsDrawnOnceNotOncePerEvent()
    {
        const int steps = 60;
        var c = new Counters();
        var terminal = new VirtualTerminal(80, 24);
        var run = App.Run(new SizedModel(c), terminal, Theme.Dark, targetFps: 30);

        await terminal.WaitForFrames(1);
        int before = c.Views;

        // Narrowing only: VirtualTerminal keeps the screen it was created with.
        for (int i = 0; i < steps; i++)
            terminal.SimulateResize(79 - i, 24);

        // Quit is queued behind the resizes and messages are handled in order, so the loop
        // exiting means every resize was applied.
        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await run;

        int drawn = c.Views - before;
        Assert.True(drawn < steps / 2,
            $"drew {drawn} frames for {steps} resizes ({steps * 2} messages) — about one per event");
        Assert.Equal(80 - steps, c.LastWidth); // the last frame has the final size
    }
}
