namespace ConsoleForge.Core;

/// <summary>Factory for creating common command values.</summary>
public static class Cmd
{
    /// <summary>No-op command. Framework skips dispatch.</summary>
    public static readonly ICmd? None = null;

    /// <summary>Returns QuitMsg immediately, ending the program loop.</summary>
    public static ICmd Quit() => () => Task.FromResult<IMsg>(new QuitMsg());

    /// <summary>
    /// Returns the given message immediately (synchronous, no async gap).
    /// Useful when a model update needs to dispatch a follow-up message
    /// (e.g. <see cref="ThemeChangedMsg"/>) in the same event-loop tick.
    /// </summary>
    public static ICmd Msg(IMsg msg) => () => Task.FromResult(msg);

    /// <summary>
    /// Wrap an async function as a command.
    /// <paramref name="fn"/> receives a <see cref="CancellationToken"/> that is cancelled
    /// when the program is shutting down.
    /// </summary>
    public static ICmd Run(Func<CancellationToken, Task<IMsg>> fn) =>
        () => fn(CancellationToken.None);

    /// <summary>
    /// Wrap an async function as a command, binding a specific
    /// <see cref="CancellationToken"/> at creation time.
    /// </summary>
    public static ICmd Run(Func<CancellationToken, Task<IMsg>> fn, CancellationToken cancellationToken) =>
        () => fn(cancellationToken);

    /// <summary>
    /// Run all commands concurrently. Null commands filtered out.
    /// Returns null if zero cmds remain; the single cmd if one remains.
    /// </summary>
    public static ICmd? Batch(params ICmd?[] cmds)
    {
        var active = cmds.Where(c => c is not null).Cast<ICmd>().ToArray();
        if (active.Length == 0) return null;
        if (active.Length == 1) return active[0];

        return async () =>
        {
            var msgs = await Task.WhenAll(active.Select(c => c()));
            return new BatchMsg(msgs);
        };
    }

    /// <summary>
    /// Run commands serially: each waits for the previous to complete.
    /// Null commands filtered out.
    /// </summary>
    public static ICmd? Sequence(params ICmd?[] cmds)
    {
        var active = cmds.Where(c => c is not null).Cast<ICmd>().ToArray();
        if (active.Length == 0) return null;
        if (active.Length == 1) return active[0];

        return async () =>
        {
            var msgs = new IMsg[active.Length];
            for (var i = 0; i < active.Length; i++)
                msgs[i] = await active[i]();
            return new SequenceMsg(msgs);
        };
    }

    /// <summary>
    /// Fire once after <paramref name="interval"/>. Returns <c>fn(timestamp)</c> as the message.
    /// Pass a <see cref="CancellationToken"/> to allow cancellation on program shutdown.
    /// </summary>
    public static ICmd Tick(TimeSpan interval, Func<DateTimeOffset, IMsg> fn,
        CancellationToken cancellationToken = default) =>
        async () =>
        {
            await Task.Delay(interval, cancellationToken);
            return fn(DateTimeOffset.UtcNow);
        };

    /// <summary>
    /// Returns a cmd that, when dispatched, waits <paramref name="interval"/> after the
    /// last dispatch before invoking <paramref name="fn"/>. If re-dispatched within the
    /// window the previous pending invocation is cancelled and the window resets.
    /// <para>
    /// Note: debouncing state is held in the returned closure. Use a single stored
    /// reference to the same cmd instance across re-dispatches for correct behaviour.
    /// </para>
    /// </summary>
    public static ICmd Debounce(TimeSpan interval, Func<DateTimeOffset, IMsg> fn)
    {
        CancellationTokenSource? cts = null;

        return async () =>
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();
            var token = cts.Token;
            try
            {
                await Task.Delay(interval, token);
                return fn(DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException)
            {
                // Debounced away — return a no-op message sentinel.
                // The caller/model should ignore this; use RedrawMsg as a harmless default.
                return new RedrawMsg();
            }
        };
    }

    /// <summary>
    /// Returns a cmd that forwards at most one invocation per <paramref name="interval"/>.
    /// Calls within the throttle window are dropped (not delayed).
    /// </summary>
    public static ICmd Throttle(TimeSpan interval, Func<DateTimeOffset, IMsg> fn)
    {
        DateTimeOffset _lastFired = DateTimeOffset.MinValue;

        return () =>
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastFired >= interval)
            {
                _lastFired = now;
                return Task.FromResult(fn(now));
            }
            // Throttled — return a harmless no-op.
            return Task.FromResult<IMsg>(new RedrawMsg());
        };
    }
}
