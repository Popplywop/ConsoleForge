using System.Collections.Concurrent;
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Testing;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Core;

public class PasteTests
{
    private sealed record PasteModel(ConcurrentQueue<string> Pastes) : IModel
    {
        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
        {
            PasteMsg p => (Record(p.Text), null),
            KeyMsg { Key: ConsoleKey.Q } => (this, Cmd.Quit()),
            _ => (this, null),
        };

        private PasteModel Record(string text)
        {
            Pastes.Enqueue(text);
            return this with { };
        }

        public IWidget View() => new TextBlock("paste");
    }

    [Fact]
    public async Task Paste_ReachesModelAsPasteMsg()
    {
        var pastes   = new ConcurrentQueue<string>();
        var terminal = new VirtualTerminal(20, 4);
        var run = App.Run(new PasteModel(pastes), terminal, Theme.Dark, enableBracketedPaste: true);

        await terminal.WaitForFrames(1);
        Assert.True(terminal.BracketedPasteEnabled);

        var baseline = terminal.FramesFlushed;
        terminal.EnqueuePaste("one\ntwo");
        await terminal.WaitForFrames(baseline + 1);

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await run;

        Assert.Equal<string>(["one\ntwo"], [.. pastes]);
        Assert.False(terminal.BracketedPasteEnabled);
    }

    [Fact]
    public async Task BracketedPaste_IsOffByDefault()
    {
        var terminal = new VirtualTerminal(20, 4);
        var run = App.Run(new PasteModel(new()), terminal, Theme.Dark);

        await terminal.WaitForFrames(1);
        Assert.False(terminal.BracketedPasteEnabled);

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await run;
    }
}
