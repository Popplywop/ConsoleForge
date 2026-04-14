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
    public async Task Cmd_Batch_MultipleCmds_ReturnsBatchMsg()
    {
        var cmd = Cmd.Batch(
            () => Task.FromResult<IMsg>(new TestMsg("a")),
            () => Task.FromResult<IMsg>(new TestMsg("b")));

        Assert.NotNull(cmd);
        var msg = await cmd!();

        var batchMsg = Assert.IsType<BatchMsg>(msg);
        Assert.Equal(2, batchMsg.Messages.Length);
        var values = batchMsg.Messages.Cast<TestMsg>().Select(m => m.Value).OrderBy(v => v).ToArray();
        Assert.Equal(["a", "b"], values);
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
        var msg = await cmd!();

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

    // ── Cmd.Debounce ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Cmd_Debounce_LastCallWins()
    {
        // A debounce cmd with a 200ms window.
        // Call it three times in quick succession; only the last should produce a non-RedrawMsg.
        var debounced = Cmd.Debounce(TimeSpan.FromMilliseconds(200), _ => new TestMsg("fired"));

        // First two calls — these should be cancelled by the third
        var t1 = debounced();
        var t2 = debounced();
        var t3 = debounced();

        var results = await Task.WhenAll(t1, t2, t3);

        // The last call must return the real message; prior calls return RedrawMsg (cancelled)
        Assert.IsType<TestMsg>(results[2]);
        Assert.Equal("fired", ((TestMsg)results[2]).Value);
        // First two should be RedrawMsg (debounced away)
        Assert.IsType<RedrawMsg>(results[0]);
        Assert.IsType<RedrawMsg>(results[1]);
    }

    // ── Cmd.Throttle ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Cmd_Throttle_FirstCallPassesThrough()
    {
        var throttled = Cmd.Throttle(TimeSpan.FromSeconds(10), _ => new TestMsg("pass"));
        var msg = await throttled();
        Assert.IsType<TestMsg>(msg);
        Assert.Equal("pass", ((TestMsg)msg).Value);
    }

    [Fact]
    public async Task Cmd_Throttle_SecondCallWithinWindowIsDropped()
    {
        var throttled = Cmd.Throttle(TimeSpan.FromSeconds(10), _ => new TestMsg("pass"));
        await throttled(); // first — passes through

        var msg = await throttled(); // second — within window, dropped
        Assert.IsType<RedrawMsg>(msg);
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
        ICmd throwingCmd = () => throw new InvalidOperationException("boom");

        CmdDispatcher.Dispatch(throwingCmd, channel.Writer, ct);

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
        ICmd boom = () => Task.FromException<IMsg>(new ArgumentException("bad"));

        await CmdDispatcher.DispatchAndWait(boom, channel.Writer, ct);

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
        ICmd longRunning = async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
            return new TestMsg("never");
        };

        CmdDispatcher.Dispatch(longRunning, channel.Writer, cts.Token);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        // Cancelled cmd must not write anything (no CmdErrorMsg for OperationCanceledException)
        Assert.False(channel.Reader.TryRead(out _));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed record TestMsg(string Value) : IMsg;
}
