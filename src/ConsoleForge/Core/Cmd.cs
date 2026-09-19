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
    /// Each command's message is delivered as soon as that command completes —
    /// batching is NOT a barrier (a slow fetch doesn't delay a fast tick).
    /// Nesting is safe: an inner Batch dispatches its own children the same way.
    /// </summary>
    public static ICmd? Batch(params ICmd?[] cmds)
    {
        var active = cmds.Where(c => c is not null).Cast<ICmd>().ToArray();
        if (active.Length == 0) return null;
        if (active.Length == 1) return active[0];

        // Resolve immediately to a dispatch request; the event loop fires each
        // child independently so results stream in as they complete.
        return () => Task.FromResult<IMsg>(new BatchDispatchMsg(active));
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
    /// Invokes <paramref name="fn"/> once <paramref name="interval"/> has passed without
    /// another dispatch under <paramref name="key"/>. Re-dispatching inside the window
    /// supersedes the pending invocation and restarts it; the superseded one produces no
    /// message at all.
    /// <para>
    /// The window belongs to <paramref name="key"/> and is held by the running
    /// <see cref="App"/>, not by the returned cmd, so a cmd built fresh in
    /// <c>Update</c> — the only way the Elm loop builds one — debounces correctly. The
    /// most recent <paramref name="fn"/> under a key is the one that runs, so a closure
    /// that varies per item is safe.
    /// </para>
    /// <para>
    /// Keys are namespaced by the application, as subscription keys are. A window still
    /// open when the program exits is cancelled rather than fired.
    /// </para>
    /// </summary>
    /// <param name="key">Identifies the window. Dispatches sharing it supersede one another.</param>
    /// <param name="interval">Quiet period that must elapse before <paramref name="fn"/> runs.</param>
    /// <param name="fn">Builds the message to dispatch, given the time the window elapsed.</param>
    public static ICmd Debounce(string key, TimeSpan interval, Func<DateTimeOffset, IMsg> fn) =>
        () => Task.FromResult<IMsg>(new RateLimitDispatchMsg(key, interval, RateLimitMode.Debounce, fn));

    /// <summary>
    /// Invokes <paramref name="fn"/> at most once per <paramref name="interval"/> under
    /// <paramref name="key"/>, on the leading edge. Dispatches inside the window are
    /// dropped rather than delayed, and produce no message.
    /// <para>
    /// As with <see cref="Debounce"/>, the window is held by the running
    /// <see cref="App"/> against <paramref name="key"/>, so a cmd built fresh in
    /// <c>Update</c> throttles correctly. Keys persist for the life of the program —
    /// throttle against a fixed key, not a per-item identifier.
    /// </para>
    /// </summary>
    /// <param name="key">Identifies the window. Dispatches sharing it share one rate limit.</param>
    /// <param name="interval">Minimum time between runs of <paramref name="fn"/>.</param>
    /// <param name="fn">Builds the message to dispatch, given the time of the leading edge.</param>
    public static ICmd Throttle(string key, TimeSpan interval, Func<DateTimeOffset, IMsg> fn) =>
        () => Task.FromResult<IMsg>(new RateLimitDispatchMsg(key, interval, RateLimitMode.Throttle, fn));
}