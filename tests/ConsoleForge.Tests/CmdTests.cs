using System.Threading.Channels;

using ConsoleForge.Core;

namespace ConsoleForge.Tests;

/// <summary>
/// Unit tests for <see cref="Cmd"/> factory methods and <see cref="CmdDispatcher"/>.
/// </summary>
public class CmdTests
{
    // ── Cmd.Quit ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cmd_Quit_ReturnsQuitMsg()
    {
        var cmd = Cmd.Quit();
        var msg = await cmd();
        Assert.IsType<QuitMsg>(msg);
    }

    // ── Cmd.Run ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cmd_Run_InvokesFactory()
    {
        var called = false;
        var cmd = Cmd.Run(async ct =>
        {
            called = true;
            await Task.Yield();
            return new TestMsg("hello");
        }, TestContext.Current.CancellationToken);

        var msg = await cmd();
        Assert.True(called);
        Assert.Equal("hello", ((TestMsg)msg).Value);
    }

    [Fact]
    public async Task Cmd_Run_PassesCancellationToken()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var receivedToken = CancellationToken.None;

        var cmd = Cmd.Run(ct =>
        {
            receivedToken = ct;
            return Task.FromResult<IMsg>(new TestMsg("ok"));
        }, cts.Token);

        await cmd();
        Assert.Equal(cts.Token, receivedToken);
    }

    // ── Cmd.Batch ────────────────────────────────────────────────────────────

    [Fact]
    public void Cmd_Batch_NullsOnly_ReturnsNull()
    {
        var result = Cmd.Batch(null, null, null);
        Assert.Null(result);
    }

    [Fact]
    public void Cmd_Batch_SingleNonNull_ReturnsThatCmd()
    {
        var inner = Cmd.Quit();
        var result = Cmd.Batch(null, inner, null);
        Assert.Same(inner, result);
    }

    [Fact]
    public async Task Cmd_Batch_MultipleCmds_ResolvesToDispatchRequest()
    {
        // Batch must NOT await its children (no barrier): it resolves
        // immediately to a BatchDispatchMsg carrying them, and the event loop
        // fires each independently so messages stream in as they complete.
        ICmd a = () => Task.FromResult<IMsg>(new TestMsg("a"));
        ICmd b = () => Task.FromResult<IMsg>(new TestMsg("b"));
        var cmd = Cmd.Batch(a, b);

        Assert.NotNull(cmd);
        var msg = await cmd();

        var dispatch = Assert.IsType<BatchDispatchMsg>(msg);
        Assert.Equal(2, dispatch.Cmds.Count);
        Assert.Same(a, dispatch.Cmds[0]);
        Assert.Same(b, dispatch.Cmds[1]);
    }

    [Fact]
    public async Task Cmd_Batch_DoesNotAwaitSlowChildren()
    {
        // Regression: the old implementation was Task.WhenAll — a never-ending
        // child hung the whole batch (spinner ticks waited on fetches).
        var never = new TaskCompletionSource<IMsg>();
        var cmd = Cmd.Batch(
            () => never.Task,
            () => Task.FromResult<IMsg>(new TestMsg("fast")));

        var resolved = await cmd!().WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.IsType<BatchDispatchMsg>(resolved);
    }

    [Fact]
    public async Task Cmd_Batch_Nested_UnfoldsOneLevelPerDispatch()
    {
        // Regression: nested batches were silently swallowed by the event loop.
        ICmd a = () => Task.FromResult<IMsg>(new TestMsg("a"));
        ICmd b = () => Task.FromResult<IMsg>(new TestMsg("b"));
        ICmd c = () => Task.FromResult<IMsg>(new TestMsg("c"));
        var outer = Cmd.Batch(Cmd.Batch(a, b), c);

        var outerDispatch = Assert.IsType<BatchDispatchMsg>(await outer!());
        Assert.Equal(2, outerDispatch.Cmds.Count);

        var innerDispatch = Assert.IsType<BatchDispatchMsg>(await outerDispatch.Cmds[0]());
        Assert.Same(a, innerDispatch.Cmds[0]);
        Assert.Same(b, innerDispatch.Cmds[1]);
        Assert.Same(c, outerDispatch.Cmds[1]);
    }

    // ── Cmd.Sequence ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Cmd_Sequence_ExecutesInOrder()
    {
        var order = new List<int>();

        var cmd = Cmd.Sequence(
            async () => { order.Add(1); await Task.Yield(); return new TestMsg("1"); },
            async () => { order.Add(2); await Task.Yield(); return new TestMsg("2"); },
            async () => { order.Add(3); await Task.Yield(); return new TestMsg("3"); });

        Assert.NotNull(cmd);
        var msg = await cmd();

        var seqMsg = Assert.IsType<SequenceMsg>(msg);
        Assert.Equal(3, seqMsg.Messages.Length);
        Assert.Equal([1, 2, 3], order);
    }

    // ── Cmd.Tick ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cmd_Tick_DelaysAndReturnsMessage()
    {
        var before = DateTimeOffset.UtcNow;
        var cmd = Cmd.Tick(
            TimeSpan.FromMilliseconds(50),
            ts => new TestMsg(ts.ToUnixTimeMilliseconds().ToString()),
            TestContext.Current.CancellationToken);
        var msg = await cmd();
        var after = DateTimeOffset.UtcNow;

        var testMsg = Assert.IsType<TestMsg>(msg);
        var ts = long.Parse(testMsg.Value);
        Assert.InRange(ts, before.ToUnixTimeMilliseconds(), after.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Cmd_Tick_RespectsCancellation()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var cmd = Cmd.Tick(TimeSpan.FromSeconds(60), _ => new TestMsg("never"), cts.Token);

        cts.CancelAfter(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cmd());
    }

    // ── Cmd.Debounce / Cmd.Throttle ──────────────────────────────────────────
    //
    // Both resolve to a request the event loop handles; the state that makes them
    // rate-limit lives there, keyed, not in the returned closure. Behaviour is
    // covered end-to-end in Core/RateLimitTests — these only pin the request shape.

    [Fact]
    public async Task Cmd_Debounce_ResolvesToAKeyedRequest()
    {
        var window = TimeSpan.FromMilliseconds(200);
        Func<DateTimeOffset, IMsg> fn = _ => new TestMsg("fired");

        var msg = await Cmd.Debounce("poster", window, fn)();

        var request = Assert.IsType<RateLimitDispatchMsg>(msg);
        Assert.Equal("poster", request.Key);
        Assert.Equal(window, request.Interval);
        Assert.Equal(RateLimitMode.Debounce, request.Mode);
        Assert.Same(fn, request.Fn);
    }

    [Fact]
    public async Task Cmd_Throttle_ResolvesToAKeyedRequest()
    {
        var msg = await Cmd.Throttle("scroll", TimeSpan.FromSeconds(1), _ => new TestMsg("fired"))();

        var request = Assert.IsType<RateLimitDispatchMsg>(msg);
        Assert.Equal("scroll", request.Key);
        Assert.Equal(RateLimitMode.Throttle, request.Mode);
    }

    /// <summary>
    /// Resolution is synchronous, so the request reaches the loop in the same drain
    /// pass as the message that produced it rather than a frame later.
    /// </summary>
    [Fact]
    public void Cmd_Debounce_ResolvesSynchronously()
    {
        var task = Cmd.Debounce("poster", TimeSpan.FromSeconds(1), _ => new TestMsg("fired"))();
        Assert.True(task.IsCompletedSuccessfully);
    }

    // ── CmdDispatcher ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CmdDispatcher_Dispatch_WritesResultToChannel()
    {
        var ct = TestContext.Current.CancellationToken;
        var channel = Channel.CreateUnbounded<IMsg>();
        var cmd = Cmd.Run(_ => Task.FromResult<IMsg>(new TestMsg("dispatched")), ct);

        CmdDispatcher.Dispatch(cmd, channel.Writer, ct);

        var msg = await channel.Reader.ReadAsync(ct);
        Assert.IsType<TestMsg>(msg);
        Assert.Equal("dispatched", ((TestMsg)msg).Value);
    }

    [Fact]
    public async Task CmdDispatcher_Dispatch_NullCmd_WritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var channel = Channel.CreateUnbounded<IMsg>();
        CmdDispatcher.Dispatch(null, channel.Writer, ct);

        // Give it a tick to make sure nothing arrives
        await Task.Delay(50, ct);
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task CmdDispatcher_Dispatch_ThrowingCmd_WritesCmdErrorMsg()
    {
        var ct = TestContext.Current.CancellationToken;
        var channel = Channel.CreateUnbounded<IMsg>();
        static Task<IMsg> ThrowingCmd() => throw new InvalidOperationException("boom");

        CmdDispatcher.Dispatch(ThrowingCmd, channel.Writer, ct);

        var msg = await channel.Reader.ReadAsync(ct);
        var errorMsg = Assert.IsType<CmdErrorMsg>(msg);
        Assert.IsType<InvalidOperationException>(errorMsg.Exception);
        Assert.Equal("boom", errorMsg.Exception.Message);
    }

    [Fact]
    public async Task CmdDispatcher_DispatchAndWait_CompletesAfterCmd()
    {
        var ct = TestContext.Current.CancellationToken;
        var channel = Channel.CreateUnbounded<IMsg>();
        var cmd = Cmd.Run(_ => Task.FromResult<IMsg>(new TestMsg("waited")), ct);

        await CmdDispatcher.DispatchAndWait(cmd, channel.Writer, ct);

        // Message must already be in channel when DispatchAndWait returns
        Assert.True(channel.Reader.TryRead(out var msg));
        Assert.IsType<TestMsg>(msg);
    }

    [Fact]
    public async Task CmdDispatcher_DispatchAndWait_ThrowingCmd_WritesCmdErrorMsg()
    {
        var ct = TestContext.Current.CancellationToken;
        var channel = Channel.CreateUnbounded<IMsg>();
        static Task<IMsg> Boom() => Task.FromException<IMsg>(new ArgumentException("bad"));

        await CmdDispatcher.DispatchAndWait(Boom, channel.Writer, ct);

        Assert.True(channel.Reader.TryRead(out var msg));
        var errorMsg = Assert.IsType<CmdErrorMsg>(msg);
        Assert.IsType<ArgumentException>(errorMsg.Exception);
    }

    [Fact]
    public async Task CmdDispatcher_Dispatch_Cancelled_SuppressesOperationCanceledException()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cts.Cancel();

        var channel = Channel.CreateUnbounded<IMsg>();
        async Task<IMsg> LongRunning()
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
            return new TestMsg("never");
        }

        CmdDispatcher.Dispatch(LongRunning, channel.Writer, cts.Token);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        // Cancelled cmd must not write anything (no CmdErrorMsg for OperationCanceledException)
        Assert.False(channel.Reader.TryRead(out _));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed record TestMsg(string Value) : IMsg;
}