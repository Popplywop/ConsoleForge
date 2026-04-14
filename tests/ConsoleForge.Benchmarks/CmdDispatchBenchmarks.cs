using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using ConsoleForge.Core;

/// <summary>
/// Benchmarks for <see cref="CmdDispatcher"/> — measures the overhead of
/// dispatching <see cref="ICmd"/> delegates through the channel-based pipeline.
///
/// All benchmarks use <c>DispatchAndWait</c> so they measure the full roundtrip:
/// Task.Run → await cmd() → channel.TryWrite.
/// </summary>
[MemoryDiagnoser]
public class CmdDispatchBenchmarks
{
    // ── Cmds under test ──────────────────────────────────────────────────────

    // Trivial cmd — Task.FromResult, no allocation beyond the Task itself.
    private ICmd _quitCmd = null!;

    // Batch of 5 concurrent trivial cmds.
    private ICmd _batchCmd5 = null!;

    // Sequence of 5 serial trivial cmds.
    private ICmd _sequenceCmd5 = null!;

    // Batch of 20 concurrent trivial cmds — stress fan-out.
    private ICmd _batchCmd20 = null!;

    // Reusable channel — unbounded, drained between iterations.
    private Channel<IMsg> _channel = null!;

    [GlobalSetup]
    public void Setup()
    {
        _channel = Channel.CreateUnbounded<IMsg>();

        _quitCmd = Cmd.Quit();

        // Build arrays of trivial cmds for batch/sequence scenarios
        var cmds5  = Enumerable.Range(0, 5) .Select(_ => Cmd.Quit()).ToArray<ICmd?>();
        var cmds20 = Enumerable.Range(0, 20).Select(_ => Cmd.Quit()).ToArray<ICmd?>();

        _batchCmd5    = Cmd.Batch(cmds5)!;
        _sequenceCmd5 = Cmd.Sequence(cmds5)!;
        _batchCmd20   = Cmd.Batch(cmds20)!;
    }

    // Drain any messages left in the channel so it doesn't grow unbounded.
    [IterationCleanup]
    public void Drain()
    {
        while (_channel.Reader.TryRead(out _)) { }
    }

    // ── Benchmarks ───────────────────────────────────────────────────────────

    /// <summary>
    /// Roundtrip: dispatch a single trivial cmd (Task.FromResult) → channel write.
    /// Baseline for cmd dispatch overhead.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task Dispatch_SingleQuitCmd()
        => await CmdDispatcher.DispatchAndWait(_quitCmd, _channel.Writer);

    /// <summary>
    /// Roundtrip: dispatch Cmd.Batch(5 trivial cmds) — Task.WhenAll fan-out.
    /// </summary>
    [Benchmark]
    public async Task Dispatch_Batch5()
        => await CmdDispatcher.DispatchAndWait(_batchCmd5, _channel.Writer);

    /// <summary>
    /// Roundtrip: dispatch Cmd.Sequence(5 trivial cmds) — serial await chain.
    /// </summary>
    [Benchmark]
    public async Task Dispatch_Sequence5()
        => await CmdDispatcher.DispatchAndWait(_sequenceCmd5, _channel.Writer);

    /// <summary>
    /// Roundtrip: dispatch Cmd.Batch(20 trivial cmds) — high fan-out.
    /// </summary>
    [Benchmark]
    public async Task Dispatch_Batch20()
        => await CmdDispatcher.DispatchAndWait(_batchCmd20, _channel.Writer);

    /// <summary>
    /// Null cmd fast-path: DispatchAndWait(null) should return immediately.
    /// </summary>
    [Benchmark]
    public async Task Dispatch_NullCmd()
        => await CmdDispatcher.DispatchAndWait(null, _channel.Writer);
}
