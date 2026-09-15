using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Testing;
using ConsoleForge.Widgets;
using Microsoft.Extensions.Time.Testing;

namespace ConsoleForge.Tests.Core;

/// <summary>
/// <see cref="Cmd.Debounce"/> and <see cref="Cmd.Throttle"/> keep their state in the
/// running <see cref="App"/>, keyed, rather than in the closure the factory returns.
/// The distinction is the whole feature: <c>Update</c> builds a fresh cmd on every
/// call, so closure-held state meant the documented usage silently never rate-limited.
/// Every test here therefore dispatches a <em>newly constructed</em> cmd each time.
/// </summary>
public class RateLimitTests
{
    // ── harness ──────────────────────────────────────────────────────────────

    /// <summary>What the model saw, and what actually fired.</summary>
    /// <remarks>
    /// Two separate records, because they answer different questions. <see cref="Seen"/>
    /// is what reached <c>Update</c>; <see cref="Fires"/> is every invocation of the
    /// rate-limited function itself, which the loop performs whether or not the
    /// resulting message is ever delivered. A window cancelled at shutdown writes to
    /// a channel nobody reads, so only <see cref="Fires"/> can see it.
    /// </remarks>
    private sealed class Recorder
    {
        private readonly object _gate = new();
        private readonly List<IMsg> _seen = [];
        private readonly List<string> _fires = [];

        private Func<bool>? _predicate;
        private TaskCompletionSource? _waiter;

        public void RecordMsg(IMsg msg) => Record(() => _seen.Add(msg));

        public void RecordFire(string payload) => Record(() => _fires.Add(payload));

        private void Record(Action mutate)
        {
            TaskCompletionSource? signal = null;
            lock (_gate)
            {
                mutate();
                if (_predicate is not null && _predicate())
                {
                    signal = _waiter;
                    _predicate = null;
                    _waiter = null;
                }
            }
            // Outside the lock: a continuation must not run holding it.
            signal?.TrySetResult();
        }

        /// <summary>Completes once <paramref name="predicate"/> holds. One waiter at a time.</summary>
        public Task Until(Func<bool> predicate)
        {
            lock (_gate)
            {
                if (predicate()) return Task.CompletedTask;
                _waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _predicate = predicate;
                return _waiter.Task;
            }
        }

        public Task UntilFires(int count) => Until(() => FireCount >= count);

        public Task UntilSeen<T>(int count) where T : IMsg =>
            Until(() => _seen.OfType<T>().Count() >= count);

        public int FireCount { get { lock (_gate) return _fires.Count; } }

        public IReadOnlyList<string> Fires { get { lock (_gate) return [.. _fires]; } }

        /// <summary>Index into <see cref="Seen"/>, for ignoring startup traffic.</summary>
        public int Mark { get { lock (_gate) return _seen.Count; } }

        public IReadOnlyList<IMsg> Since(int mark) { lock (_gate) return [.. _seen.Skip(mark)]; }
    }

    /// <summary>Signals that the loop has finished handling the rate-limit request.</summary>
    /// <remarks>
    /// Armed behind the rate-limited cmd in a <see cref="Cmd.Batch"/>. Batch children
    /// are dispatched in order and both resolve synchronously, so they reach the channel
    /// in order and the loop drains them in order — this message arriving means the
    /// debounce timer is registered against the fake clock. Advancing the clock before
    /// that would schedule the window from the new "now" and it would never fire.
    /// </remarks>
    private sealed record ArmedMsg : IMsg;

    private sealed record FiredMsg(string Payload) : IMsg;

    /// <summary>Which factory the model under test exercises.</summary>
    private enum Kind { Debounce, Throttle }

    /// <summary>
    /// Presses map to rate-limit requests: a/b/c share the key <c>"alpha"</c>, x/y share
    /// <c>"beta"</c>, and q quits. The pressed character is the payload, so "which
    /// invocation won" is readable off the result.
    /// </summary>
    private sealed record RateLimitModel(Recorder Log, Kind Mode, TimeSpan Window) : IModel
    {
        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg)
        {
            Log.RecordMsg(msg);

            if (msg is not KeyMsg { Character: char c }) return (this, null);
            if (c == 'q') return (this, Cmd.Quit());

            var key = c is 'x' or 'y' ? "beta" : "alpha";
            var payload = c.ToString();

            // A fresh cmd instance every press — the case the closure-held state
            // could not handle.
            var limited = Mode == Kind.Debounce
                ? Cmd.Debounce(key, Window, _ => Fire(payload))
                : Cmd.Throttle(key, Window, _ => Fire(payload));

            return (this, Cmd.Batch(limited, Cmd.Msg(new ArmedMsg())));
        }

        private IMsg Fire(string payload)
        {
            Log.RecordFire(payload);
            return new FiredMsg(payload);
        }

        public IWidget View() => new TextBlock("rate-limit");
    }

    /// <summary>A running app plus the handles a test needs to drive it.</summary>
    private sealed class Harness(
        Task run, VirtualTerminal terminal, Recorder log, FakeTimeProvider clock, int mark)
    {
        /// <summary>Arrivals are counted across the whole run, so waits are cumulative.</summary>
        private int _armed;

        public Task Run { get; } = run;
        public VirtualTerminal Terminal { get; } = terminal;
        public Recorder Log { get; } = log;
        public FakeTimeProvider Clock { get; } = clock;
        public int Mark { get; } = mark;

        public void Press(char c) => Terminal.EnqueueKey(new KeyMsg(ConsoleKey.NoName, c));

        /// <summary>
        /// Press each character and wait until the loop has armed them all. Nothing may
        /// be enqueued behind an un-armed press: quit is read straight off the channel,
        /// so it would overtake the request the press had only just queued.
        /// </summary>
        public async Task PressArmed(params char[] chars)
        {
            _armed += chars.Length;
            var armed = Log.UntilSeen<ArmedMsg>(_armed);
            foreach (var c in chars) Press(c);
            await armed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        public async Task Quit()
        {
            Press('q');
            await Run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(200);

    private static async Task<Harness> Start(Kind mode)
    {
        var log = new Recorder();
        var clock = new FakeTimeProvider();
        var terminal = new VirtualTerminal(40, 10);
        var run = App.Run(new RateLimitModel(log, mode, Window), terminal, Theme.Dark,
            targetFps: 30, timeProvider: clock);

        // The startup RedrawMsg has been applied once a frame exists; everything the
        // assertions care about happens after this point.
        await terminal.WaitForFrames(1)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return new Harness(run, terminal, log, clock, log.Mark);
    }

    // ── Debounce ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The defect itself. Three presses build three separate <c>Cmd.Debounce</c>
    /// instances under one key; before the state moved into the loop each carried its
    /// own cancellation source and all three fired.
    /// </summary>
    [Fact]
    public async Task Debounce_SeparateCmdInstances_FireOnce()
    {
        var h = await Start(Kind.Debounce);

        await h.PressArmed('a', 'b', 'c');
        h.Clock.Advance(Window);
        await h.Log.UntilFires(1).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await h.Quit();

        Assert.Equal(1, h.Log.FireCount);
    }

    /// <summary>
    /// The surviving invocation is the most recent one, not the first. This is what
    /// makes a per-item closure safe — PlexTui debounces a different poster URL per row.
    /// </summary>
    [Fact]
    public async Task Debounce_LatestPayloadWins()
    {
        var h = await Start(Kind.Debounce);

        await h.PressArmed('a', 'b', 'c');
        h.Clock.Advance(Window);
        await h.Log.UntilFires(1).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await h.Quit();

        Assert.Equal(["c"], h.Log.Fires);
    }

    [Fact]
    public async Task Debounce_DistinctKeys_AreIndependent()
    {
        var h = await Start(Kind.Debounce);

        await h.PressArmed('a', 'x');
        h.Clock.Advance(Window);
        await h.Log.UntilFires(2).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await h.Quit();

        Assert.Equal(["a", "x"], [.. h.Log.Fires.Order()]);
    }

    /// <summary>
    /// A superseded window produces nothing at all. It used to resolve to a
    /// <see cref="RedrawMsg"/> sentinel, which is a real message the model had to
    /// recognise and discard, and which forced a repaint per suppressed call.
    /// </summary>
    [Fact]
    public async Task Debounce_SupersededWindow_ProducesNoMessage()
    {
        var h = await Start(Kind.Debounce);

        await h.PressArmed('a', 'b', 'c');
        h.Clock.Advance(Window);
        await h.Log.UntilFires(1).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await h.Quit();

        var after = h.Log.Since(h.Mark);
        Assert.Single(after.OfType<FiredMsg>());
        Assert.Empty(after.OfType<RedrawMsg>());
    }

    [Fact]
    public async Task Debounce_BeforeWindowElapses_NothingFires()
    {
        var h = await Start(Kind.Debounce);

        await h.PressArmed('a');
        h.Clock.Advance(Window - TimeSpan.FromMilliseconds(1));

        await h.Quit();

        Assert.Equal(0, h.Log.FireCount);
    }

    /// <summary>
    /// A window still open at shutdown must not fire. Its token is linked to the app's,
    /// so cancellation reaches it; asserting on delivered messages could not see this,
    /// because after the loop exits nothing drains the channel.
    /// </summary>
    [Fact]
    public async Task Debounce_PendingWindow_DoesNotFireAfterShutdown()
    {
        var h = await Start(Kind.Debounce);

        await h.PressArmed('a');
        await h.Quit();

        h.Clock.Advance(Window * 4);

        Assert.Equal(0, h.Log.FireCount);
    }

    /// <summary>A fired window releases its key, rather than wedging it shut.</summary>
    [Fact]
    public async Task Debounce_FiresAgainAfterCompletedWindow()
    {
        var h = await Start(Kind.Debounce);

        await h.PressArmed('a');
        h.Clock.Advance(Window);
        await h.Log.UntilFires(1).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await h.PressArmed('b');
        h.Clock.Advance(Window);
        await h.Log.UntilFires(2).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await h.Quit();

        Assert.Equal(["a", "b"], h.Log.Fires);
    }

    // ── Throttle ─────────────────────────────────────────────────────────────

    /// <summary>Leading edge: the first call passes, the rest of the window is dropped.</summary>
    [Fact]
    public async Task Throttle_FirstPasses_SecondInsideWindowIsDropped()
    {
        var h = await Start(Kind.Throttle);

        await h.PressArmed('a', 'b');
        await h.Quit();

        Assert.Equal(["a"], h.Log.Fires);
    }

    [Fact]
    public async Task Throttle_AfterWindowElapses_PassesAgain()
    {
        var h = await Start(Kind.Throttle);

        await h.PressArmed('a');
        h.Clock.Advance(Window);
        await h.PressArmed('b');

        await h.Quit();

        Assert.Equal(["a", "b"], h.Log.Fires);
    }

    /// <summary>A dropped call produces nothing — no sentinel, no repaint.</summary>
    [Fact]
    public async Task Throttle_DroppedCall_ProducesNoMessage()
    {
        var h = await Start(Kind.Throttle);

        await h.PressArmed('a', 'b');
        await h.Quit();

        var after = h.Log.Since(h.Mark);
        Assert.Single(after.OfType<FiredMsg>());
        Assert.Empty(after.OfType<RedrawMsg>());
    }

    [Fact]
    public async Task Throttle_DistinctKeys_AreIndependent()
    {
        var h = await Start(Kind.Throttle);

        await h.PressArmed('a', 'x');
        await h.Quit();

        Assert.Equal(["a", "x"], [.. h.Log.Fires.Order()]);
    }
}
