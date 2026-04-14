namespace ConsoleForge.Core;

/// <summary>
/// A subscription: a long-running async function that produces messages continuously
/// until the supplied <see cref="CancellationToken"/> is cancelled.
/// </summary>
/// <remarks>
/// Return <see langword="null"/> from the callback to suppress dispatching a message
/// (e.g. after an interval fires with no meaningful data). The framework will not
/// write null messages to the channel.
/// </remarks>
public delegate IAsyncEnumerable<IMsg> ISub(CancellationToken cancellationToken);

/// <summary>
/// Optional interface for models that declare long-running subscriptions.
/// Implement alongside <see cref="IModel"/> to participate in subscription management.
/// </summary>
/// <remarks>
/// After every <c>Update</c> call the framework invokes <see cref="Subscriptions"/> and
/// compares the returned set (by subscription key) with the currently running set.
/// New subscriptions are started; subscriptions whose key is no longer present are cancelled.
/// </remarks>
public interface IHasSubscriptions
{
    /// <summary>
    /// Return the set of subscriptions that should be active given the current model state.
    /// Each entry is a (key, sub) pair. The key uniquely identifies the subscription —
    /// use a stable string (e.g. a feature flag name or device id) so the framework can
    /// detect start/stop transitions.
    /// </summary>
    IReadOnlyList<(string Key, ISub Sub)> Subscriptions();
}
