using System.Collections.Concurrent;

using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Testing;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Core;

/// <summary>
/// Click-to-focus is the only reason <see cref="IFocusable.FocusKey"/> exists: the
/// framework hit-tests, because a model cannot, and reports the key so the model can
/// decide. This drives the whole path through <see cref="App"/> — the model's own
/// focus handling is deliberately absent, since what is under test is what the
/// framework sends, not what an application does with it.
/// </summary>
/// <remarks>
/// App-level, so it runs the real loop against a <see cref="VirtualTerminal"/> — but it
/// waits on frames rather than on the clock, so it takes as long as the loop takes and
/// no longer, and cannot flake when the machine is busy.
/// </remarks>
public class ClickToFocusTests
{
    private const int W = 80;
    private const int H = 24;

    /// <summary>Two stacked single-row inputs: the upper keyed, the lower not.</summary>
    /// <remarks>
    /// Every handled message bumps <c>Beat</c> so the returned model is a new instance.
    /// The loop only redraws when the model changes, and the test waits on frames, so a
    /// handler that returned <c>this</c> would leave the test waiting for a frame that is
    /// never drawn.
    /// </remarks>
    private sealed record TwoInputs(ConcurrentQueue<string> Requests, int Beat = 0) : IModel
    {
        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg)
        {
            switch (msg)
            {
                case FocusRequestedMsg f:
                    Requests.Enqueue(f.Key);
                    return (this with { Beat = Beat + 1 }, null);
                // Always redraws, so a frame is guaranteed even when the click under test
                // produced nothing. Messages are processed in order, so a frame drawn for
                // this one means the click ahead of it has already been applied.
                case KeyMsg { Key: ConsoleKey.Spacebar }:
                    return (this with { Beat = Beat + 1 }, null);
                case KeyMsg { Key: ConsoleKey.Q }:
                    return (this, Cmd.Quit());
                default:
                    return (this, null);
            }
        }

        // Row 0 is keyed, row 1 is not. Both are Fixed(1) tall, so the rows are exact.
        public IWidget View() => new Container(Axis.Vertical, [
            new TextInput("top") { FocusKey = "top", Height = SizeConstraint.Fixed(1) },
            new TextInput("bottom") { Height = SizeConstraint.Fixed(1) },
        ]);
    }

    /// <summary>Run the loop, left-click once at (col, row), and return what the model saw.</summary>
    private static async Task<IReadOnlyList<string>> ClickAt(int col, int row)
    {
        var requests = new ConcurrentQueue<string>();
        var terminal = new VirtualTerminal(W, H);
        var run = App.Run(new TwoInputs(requests), terminal, Theme.Dark, targetFps: 30, enableMouse: true);

        // Hit-testing reads the frame on screen, so one has to exist before the click.
        await terminal.WaitForFrames(1);

        // Baseline before injecting: reading it afterwards races the frame being awaited.
        var baseline = terminal.FramesFlushed;
        terminal.EnqueueMouse(new MouseMsg(MouseButton.Left, MouseAction.Press, col, row));
        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Spacebar, ' '));
        await terminal.WaitForFrames(baseline + 1);

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await run;

        return [.. requests];
    }

    [Fact]
    public async Task Click_OnKeyedWidget_RequestsFocusForThatKey()
    {
        var requests = await ClickAt(col: 5, row: 0);

        Assert.Equal<string>(["top"], requests);
    }

    [Fact]
    public async Task Click_OnFocusableWithoutAKey_RequestsNothing()
    {
        // A focusable the application never opted in to click-focus must stay silent.
        // While FocusKey defaulted to string.Empty this fired FocusRequestedMsg("")
        // for every focusable on screen.
        var requests = await ClickAt(col: 5, row: 1);

        Assert.Empty(requests);
    }
}
