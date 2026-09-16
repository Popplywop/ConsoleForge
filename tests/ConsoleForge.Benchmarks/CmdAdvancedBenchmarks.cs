using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using ConsoleForge.Core;

/// <summary>
/// Benchmarks for newer <see cref="Cmd"/> factory methods:
/// <see cref="Cmd.Run"/>, <see cref="Cmd.Tick"/>, <see cref="Cmd.Debounce"/>,
/// <see cref="Cmd.Throttle"/>, and error-dispatch via <see cref="CmdErrorMsg"/>.
/// Debounce/Throttle windows are applied by the event loop, not by the cmd, so what
/// is measured here is the dispatch, not the rate limit.
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

    // Debounce/Throttle: the rate-limiting itself is the event loop's work, keyed
    // and held there. What a dispatch costs is building the request and writing it
    // to the channel, which is what these measure.
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

        _debounceCmd = Cmd.Debounce("bench", TimeSpan.Zero, ts => new RedrawMsg());
        _throttleCmd = Cmd.Throttle("bench", TimeSpan.Zero, ts => new RedrawMsg());
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
    /// Roundtrip: invoke a <c>Cmd.Debounce</c> cmd. Resolution is synchronous —
    /// it builds the keyed request the event loop acts on.
    /// </summary>
    [Benchmark]
    public async Task Dispatch_Debounce_Request()
        => await CmdDispatcher.DispatchAndWait(_debounceCmd, _channel.Writer);

    /// <summary>
    /// Roundtrip: invoke a <c>Cmd.Throttle</c> cmd — same shape as debounce, and
    /// the same cost, since the mode only changes what the loop does with it.
    /// </summary>
    [Benchmark]
    public async Task Dispatch_Throttle_Request()
        => await CmdDispatcher.DispatchAndWait(_throttleCmd, _channel.Writer);
}
