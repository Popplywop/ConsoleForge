namespace ConsoleForge.Core;

/// <summary>Factory for creating common command values.</summary>
public static class Cmd
{
    /// <summary>No-op command. Framework skips dispatch.</summary>
    public static readonly ICmd? None = null;

    /// <summary>Returns QuitMsg immediately, ending the program loop.</summary>
    public static ICmd Quit() => () => new QuitMsg();

    /// <summary>
    /// Run all commands concurrently. Null commands filtered out.
    /// Returns null if zero cmds remain; the single cmd if one remains.
    /// </summary>
    public static ICmd? Batch(params ICmd?[] cmds)
    {
        var active = cmds.Where(c => c is not null).Cast<ICmd>().ToArray();
        if (active.Length == 0) return null;
        if (active.Length == 1) return active[0];

        return () =>
        {
            var tasks = active.Select(c => Task.Run(() => c())).ToArray();
            Task.WaitAll(tasks);
            var msgs = tasks.Select(t => t.Result).ToArray();
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

        return () =>
        {
            var msgs = new IMsg[active.Length];
            for (var i = 0; i < active.Length; i++)
                msgs[i] = active[i]();
            return new SequenceMsg(msgs);
        };
    }

    /// <summary>
    /// Fire once after interval. Returns fn(timestamp) as the message.
    /// </summary>
    public static ICmd Tick(TimeSpan interval, Func<DateTimeOffset, IMsg> fn) =>
        () =>
        {
            Task.Delay(interval).Wait();
            return fn(DateTimeOffset.UtcNow);
        };
}
