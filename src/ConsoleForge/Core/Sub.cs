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
