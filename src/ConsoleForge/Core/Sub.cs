namespace ConsoleForge.Core;

/// <summary>Factory helpers for creating common <see cref="ISub"/> values.</summary>
public static class Sub
{
    /// <summary>
    /// A subscription that fires <paramref name="fn"/> every <paramref name="interval"/>.
    /// Messages are produced indefinitely until the token is cancelled.
    /// </summary>
    public static ISub Interval(TimeSpan interval, Func<DateTimeOffset, IMsg> fn) =>
        ct => IntervalCore(interval, fn, ct);

    private static async IAsyncEnumerable<IMsg> IntervalCore(
        TimeSpan interval,
        Func<DateTimeOffset, IMsg> fn,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            yield return fn(DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// A subscription that produces <paramref name="map"/> of every message
    /// <paramref name="sub"/> produces — Elm's <c>Sub.map</c>. A null message (one the
    /// framework would drop) stays null.
    /// </summary>
    public static ISub Map(ISub sub, Func<IMsg, IMsg> map)
    {
        ArgumentNullException.ThrowIfNull(sub);
        ArgumentNullException.ThrowIfNull(map);
        return ct => MapCore(sub, map, ct);
    }

    private static async IAsyncEnumerable<IMsg> MapCore(
        ISub sub,
        Func<IMsg, IMsg> map,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var msg in sub(ct).WithCancellation(ct))
            // Null is a legal "nothing to dispatch" from a subscription; there is nothing to map.
            yield return msg is null ? msg! : map(msg);
    }

    /// <summary>
    /// Combine keyed subscription lists into one, in order — Elm's <c>Sub.batch</c>, with
    /// the keys this framework reconciles by. Typically the root's own subscriptions plus
    /// each child's from <see cref="Component.Subscriptions"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Two entries share a key. The framework would run only one of them, so the other
    /// would silently never start.
    /// </exception>
    public static IReadOnlyList<(string Key, ISub Sub)> Batch(params IEnumerable<(string Key, ISub Sub)>[] groups)
    {
        var result = new List<(string Key, ISub Sub)>();
        var keys   = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var entry in group)
            {
                if (!keys.Add(entry.Key))
                    throw new ArgumentException(
                        $"Subscription key '{entry.Key}' appears twice; only one of them would run. " +
                        "Give each child a distinct prefix in Component.Subscriptions.", nameof(groups));
                result.Add(entry);
            }
        }
        return result;
    }

    /// <summary>
    /// Wrap an arbitrary <see cref="IAsyncEnumerable{IMsg}"/> factory as a subscription.
    /// </summary>
    public static ISub FromAsyncEnumerable(Func<CancellationToken, IAsyncEnumerable<IMsg>> factory) =>
        ct => factory(ct);

    /// <summary>
    /// Wrap an <see cref="IObservable{IMsg}"/> as a subscription.
    /// Messages are forwarded until the token is cancelled or the observable completes.
    /// </summary>
    public static ISub FromObservable(IObservable<IMsg> observable) =>
        ct => ObservableCore(observable, ct);

    private static async IAsyncEnumerable<IMsg> ObservableCore(
        IObservable<IMsg> observable,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<IMsg?>();
        using var sub = observable.Subscribe(
            onNext: msg => channel.Writer.TryWrite(msg),
            onError: _ => channel.Writer.TryComplete(),
            onCompleted: () => channel.Writer.TryComplete());

        await foreach (var msg in channel.Reader.ReadAllAsync(ct))
        {
            if (msg is not null)
                yield return msg;
        }
    }
}
