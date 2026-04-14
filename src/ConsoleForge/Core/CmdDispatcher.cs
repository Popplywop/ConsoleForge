using System.Threading.Channels;

namespace ConsoleForge.Core;

/// <summary>
/// Fires an <see cref="ICmd"/> on the thread pool and writes the resulting
/// <see cref="IMsg"/> to <paramref name="channel"/>.
/// </summary>
internal static class CmdDispatcher
{
    /// <summary>
    /// Fire-and-forget: runs <paramref name="cmd"/> on the thread pool,
    /// then writes its result to <paramref name="channel"/>.
    /// If the cmd throws, a <see cref="CmdErrorMsg"/> is written instead.
    /// </summary>
    internal static void Dispatch(ICmd? cmd, ChannelWriter<IMsg> channel,
        CancellationToken cancellationToken = default)
    {
        if (cmd is null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await cmd();
                channel.TryWrite(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Suppress cancellation on clean shutdown.
            }
            catch (Exception ex)
            {
                channel.TryWrite(new CmdErrorMsg(ex));
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Dispatch and await completion: runs <paramref name="cmd"/>, writes the
    /// result to <paramref name="channel"/>, and returns only after the write.
    /// Useful for testing and benchmarking roundtrip latency.
    /// If the cmd throws, a <see cref="CmdErrorMsg"/> is written instead.
    /// </summary>
    internal static async Task DispatchAndWait(ICmd? cmd, ChannelWriter<IMsg> channel,
        CancellationToken cancellationToken = default)
    {
        if (cmd is null) return;

        try
        {
            var result = await Task.Run(async () => await cmd(), cancellationToken);
            channel.TryWrite(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Suppress cancellation on clean shutdown.
        }
        catch (Exception ex)
        {
            channel.TryWrite(new CmdErrorMsg(ex));
        }
    }
}
