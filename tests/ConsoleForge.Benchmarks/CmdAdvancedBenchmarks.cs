using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using ConsoleForge.Core;

/// <summary>
/// Benchmarks for newer <see cref="Cmd"/> factory methods:
/// <see cref="Cmd.Run"/>, <see cref="Cmd.Tick"/>, <see cref="Cmd.Debounce"/>,
/// <see cref="Cmd.Throttle"/>, and error-dispatch via <see cref="CmdErrorMsg"/>.
///
/// All benchmarks that exercise the roundtrip use <c>DispatchAndWait</c>, matching
/// the baseline established in <see cref="CmdDispatchBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
public class CmdAdvancedBenchmarks
{
    // ── fields ───────────────────────────────────────────────────────────────

    private ICmd _runCmd       = null!;
    private ICmd _throwingCmd  = null!;
    private ICmd _tickCmd      = null!;

    // Debounce/Throttle: same cmd instance is reused across iterations to
    // preserve the internal state (CTS / lastFired timestamp) that drives
    // their behaviour.
    private ICmd _debounceCmd  = null!;
    private ICmd _throttleCmd  = null!;

    private Channel<IMsg> _channel = null!;

    [GlobalSetup]
    public void Setup()
    {
        _channel = Channel.CreateUnbounded<IMsg>();

        // Cmd.Run — wraps a synchronous-ish async fn (Task.FromResult equivalent).
        _runCmd = Cmd.Run(_ => Task.FromResult<IMsg>(new RedrawMsg()));

        // Throwing cmd — CmdDispatcher should catch and write a CmdErrorMsg.
        _throwingCmd = Cmd.Run(_ => throw new InvalidOperationException("bench-error"));

        // Cmd.Tick with a zero interval so Task.Delay completes immediately.
        _tickCmd = Cmd.Tick(TimeSpan.Zero, ts => new RedrawMsg());

        // Debounce / Throttle with a zero window so they always pass through.
        _debounceCmd = Cmd.Debounce(TimeSpan.Zero, ts => new RedrawMsg());
        _throttleCmd = Cmd.Throttle(TimeSpan.Zero, ts => new RedrawMsg());
    }

    [IterationCleanup]
    public void Drain()
    {
        while (_channel.Reader.TryRead(out _)) { }
    }

    // ── benchmarks ───────────────────────────────────────────────────────────

    /// <summary>
    /// Roundtrip: dispatch a <c>Cmd.Run</c> cmd (zero-cost async body).
    /// Measures the overhead added by Cmd.Run's delegate indirection vs the
    /// bare-Task.FromResult baseline in <see cref="CmdDispatchBenchmarks"/>.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task Dispatch_CmdRun()
        => await CmdDispatcher.DispatchAndWait(_runCmd, _channel.Writer);

    /// <summary>
    /// Roundtrip: dispatch a throwing <c>Cmd.Run</c> cmd.
    /// CmdDispatcher catches the exception and writes a <see cref="CmdErrorMsg"/>.
    /// Measures the exception-handling path cost.
    /// </summary>
    [Benchmark]
    public async Task Dispatch_ThrowingCmd_ErrorPath()
        => await CmdDispatcher.DispatchAndWait(_throwingCmd, _channel.Writer);

    /// <summary>
    /// Roundtrip: dispatch <c>Cmd.Tick(TimeSpan.Zero, …)</c>.
    /// Task.Delay(0) completes on the next scheduler turn; this measures the
    /// overhead of the async delay path vs an immediate Task.FromResult.
    /// </summary>
    [Benchmark]
    public async Task Dispatch_Tick_ZeroInterval()
        => await CmdDispatcher.DispatchAndWait(_tickCmd, _channel.Writer);

    /// <summary>
    /// Roundtrip: invoke a <c>Cmd.Debounce</c> cmd when the window has already
    /// expired (pass-through path — no cancellation, no waiting).
    /// </summary>
    [Benchmark]
    public async Task Dispatch_Debounce_PassThrough()
        => await CmdDispatcher.DispatchAndWait(_debounceCmd, _channel.Writer);

    /// <summary>
    /// Roundtrip: invoke a <c>Cmd.Throttle</c> cmd when the window has already
    /// expired (pass-through path — returns fn result immediately).
    /// </summary>
    [Benchmark]
    public async Task Dispatch_Throttle_PassThrough()
        => await CmdDispatcher.DispatchAndWait(_throttleCmd, _channel.Writer);

    /// <summary>
    /// Throttle drop path: two back-to-back invocations of the same throttled cmd.
    /// The second call is within the throttle window and returns RedrawMsg without
    /// invoking fn. Measures the cost of the fast-drop branch.
    /// </summary>
    [Benchmark]
    public async Task Dispatch_Throttle_Drop()
    {
        // First call passes through and sets _lastFired = now.
        var throttleOnce = Cmd.Throttle(TimeSpan.FromHours(1), ts => new RedrawMsg());
        await CmdDispatcher.DispatchAndWait(throttleOnce, _channel.Writer); // pass-through
        await CmdDispatcher.DispatchAndWait(throttleOnce, _channel.Writer); // dropped
    }
}
