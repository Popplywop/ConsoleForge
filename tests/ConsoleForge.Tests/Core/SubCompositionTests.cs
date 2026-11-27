using System.Threading.Channels;
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Testing;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Core;

/// <summary>
/// Children's subscriptions, composed by the parent (WISHLIST 1): <see cref="Sub.Batch"/>,
/// <see cref="Sub.Map"/> and <see cref="Component.Subscriptions"/>.
/// </summary>
public class SubCompositionTests
{
    private sealed record TickMsg(int N) : IMsg;
    private sealed record Tagged(IMsg Inner) : IMsg;

    /// <summary>A subscription that yields <paramref name="msgs"/> and ends.</summary>
    private static ISub Yields(params IMsg[] msgs) => Sub.FromAsyncEnumerable(ct => Items(msgs));

    private static async IAsyncEnumerable<IMsg> Items(IMsg[] msgs)
    {
        foreach (var m in msgs) { await Task.Yield(); yield return m; }
    }

    private static async Task<List<IMsg>> Drain(ISub sub)
    {
        var list = new List<IMsg>();
        await foreach (var m in sub(CancellationToken.None)) list.Add(m);
        return list;
    }

    private sealed record Page(bool Loading) : IComponent, IHasSubscriptions
    {
        public ICmd? Init() => null;
        public (IModel Model, ICmd? Cmd) Update(IMsg msg) => (this, null);
        public IWidget View() => new TextBlock("page");
        public IReadOnlyList<(string Key, ISub Sub)> Subscriptions() =>
            Loading ? [("spinner", Yields(new TickMsg(1)))] : [];
    }

    private sealed record Plain : IComponent
    {
        public ICmd? Init() => null;
        public (IModel Model, ICmd? Cmd) Update(IMsg msg) => (this, null);
        public IWidget View() => new TextBlock("plain");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    [Fact]
    public void ComponentSubscriptions_PrefixesKeys()
    {
        var subs = Component.Subscriptions("prlist", new Page(Loading: true));
        Assert.Equal("prlist/spinner", Assert.Single(subs).Key);
    }

    [Fact]
    public void ComponentSubscriptions_FollowTheChildsState()
    {
        Assert.Empty(Component.Subscriptions("prlist", new Page(Loading: false)));
        Assert.Empty(Component.Subscriptions("modal", null));
        Assert.Empty(Component.Subscriptions("plain", new Plain()));
    }

    [Fact]
    public async Task ComponentSubscriptions_MapTagsMessages()
    {
        var (_, sub) = Assert.Single(Component.Subscriptions("a", new Page(true), m => new Tagged(m)));
        var msg = Assert.Single(await Drain(sub));
        Assert.Equal(new Tagged(new TickMsg(1)), msg);
    }

    [Fact]
    public async Task Map_TransformsEveryMessage()
    {
        var mapped = Sub.Map(Yields(new TickMsg(1), new TickMsg(2)), m => new Tagged(m));
        Assert.Equal<IMsg>([new Tagged(new TickMsg(1)), new Tagged(new TickMsg(2))], await Drain(mapped));
    }

    [Fact]
    public void Batch_ConcatenatesInOrder()
    {
        var own = Yields();
        var all = Sub.Batch(
            [("clock", own)],
            Component.Subscriptions("left", new Page(true)),
            Component.Subscriptions("right", new Page(true)));

        Assert.Equal<string>(["clock", "left/spinner", "right/spinner"], all.Select(s => s.Key).ToList());
    }

    [Fact]
    public void Batch_RejectsDuplicateKeys()
    {
        // The framework would start only one; the other would silently never run.
        var ex = Assert.Throws<ArgumentException>(() => Sub.Batch(
            Component.Subscriptions("page", new Page(true)),
            Component.Subscriptions("page", new Page(true))));
        Assert.Contains("page/spinner", ex.Message);
    }

    // ── Through App ───────────────────────────────────────────────────────────

    /// <summary>
    /// A child whose subscription is fed by the test through a channel, and reports when
    /// the framework cancels it.
    /// </summary>
    private sealed record Child(Channel<IMsg> Feed, TaskCompletionSource Cancelled) : IComponent, IHasSubscriptions
    {
        public ICmd? Init() => null;
        public (IModel Model, ICmd? Cmd) Update(IMsg msg) => (this, null);
        public IWidget View() => new TextBlock("child");

        public IReadOnlyList<(string Key, ISub Sub)> Subscriptions() =>
            [("feed", ct =>
            {
                ct.Register(() => Cancelled.TrySetResult());
                return Feed.Reader.ReadAllAsync(ct);
            })];
    }

    private sealed record CloseChildMsg : IMsg;

    private sealed record Parent(Child? Kid, TaskCompletionSource<IMsg> SawTick) : IModel, IHasSubscriptions
    {
        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg)
        {
            if (msg is TickMsg) SawTick.TrySetResult(msg);
            return msg switch
            {
                CloseChildMsg => (this with { Kid = null }, null),
                KeyMsg { Key: ConsoleKey.Q } => (this, Cmd.Quit()),
                _ => (this, null),
            };
        }

        public IWidget View() => new TextBlock(Kid is null ? "no child" : "child");

        public IReadOnlyList<(string Key, ISub Sub)> Subscriptions() =>
            Sub.Batch(Component.Subscriptions("kid", Kid));
    }

    [Fact]
    public async Task ChildSubscription_RunsWhileTheChildExists()
    {
        var feed      = Channel.CreateUnbounded<IMsg>();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawTick   = new TaskCompletionSource<IMsg>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal  = new VirtualTerminal(20, 4);
        var run = App.Run(new Parent(new Child(feed, cancelled), sawTick), terminal, Theme.Dark);

        await terminal.WaitForFrames(1);

        // Running: what the child's subscription produces reaches the root's Update.
        await feed.Writer.WriteAsync(new TickMsg(7));
        Assert.Equal(new TickMsg(7), await sawTick.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        // Remove the child from the model: its subscription is cancelled with it.
        Assert.False(cancelled.Task.IsCompleted);
        await feed.Writer.WriteAsync(new CloseChildMsg());            // routed through the sub
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));

        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await run;
    }
}
