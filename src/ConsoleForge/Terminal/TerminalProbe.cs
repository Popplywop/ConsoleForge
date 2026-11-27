using System.Diagnostics;

namespace ConsoleForge.Terminal;

/// <summary>What the terminal answered to the Kitty graphics probe.</summary>
internal enum KittyProbeResult
{
    /// <summary>No answer: the terminal could not be asked, or did not reply in time.</summary>
    Unknown,
    /// <summary>The terminal answered the graphics query.</summary>
    Supported,
    /// <summary>The terminal answered the attributes request and not the graphics query.</summary>
    Unsupported,
}

/// <summary>
/// Asks the terminal what it supports, by writing queries and reading the replies from
/// stdin in a short raw-mode session. Used by <see cref="TerminalCapabilities.Detect(TimeSpan)"/>.
/// </summary>
internal static class TerminalProbe
{
    // Primary device attributes. Every VT-compatible terminal answers it, and answers in
    // order, so it marks the point by which any reply to the queries before it has arrived.
    private const string DeviceAttributesQuery = "\x1b[c";

    /// <summary>Ask whether the terminal implements the Kitty graphics protocol.</summary>
    internal static KittyProbeResult QueryKittyGraphics(TimeSpan timeout)
    {
#if WINDOWS
        return KittyProbeResult.Unknown;
#else
        if (OperatingSystem.IsWindows()) return KittyProbeResult.Unknown;
        if (Console.IsInputRedirected || Console.IsOutputRedirected) return KittyProbeResult.Unknown;
        if (StdinReader.Open() is not { } reader) return KittyProbeResult.Unknown;

        if (Termios.GetAttr() is not { } saved) return KittyProbeResult.Unknown;

        // Already raw: something — an AnsiTerminal, most likely, inside App.Run — owns the
        // terminal and is reading stdin. Asking now would race it for the answer and steal
        // the user's keys, so do not ask.
        if (Termios.IsRaw(saved)) return KittyProbeResult.Unknown;

        // Raw mode: the replies must neither echo on screen nor wait for a newline.
        if (!Termios.MakeRaw(saved)) return KittyProbeResult.Unknown;

        try
        {
            Console.Out.Write(KittyProtocol.BuildDetectProbe() + DeviceAttributesQuery);
            Console.Out.Flush();
            return AwaitKittyReply(reader, timeout);
        }
        finally
        {
            Termios.Restore(saved);
        }
#endif
    }

    /// <summary>
    /// Read until the device attributes reply, the end of input, or
    /// <paramref name="timeout"/>, and report whether a graphics reply came first.
    /// Anything else that arrives meanwhile — keys typed during startup — is discarded.
    /// </summary>
    internal static KittyProbeResult AwaitKittyReply(StdinReader reader, TimeSpan timeout)
    {
        var decoder = new InputDecoder();
        var events  = new List<InputEvent>();
        var chars   = new char[StdinReader.MinBufferSize];
        var clock   = Stopwatch.StartNew();
        bool graphics = false;

        while (true)
        {
            int remaining = (int)Math.Ceiling((timeout - clock.Elapsed).TotalMilliseconds);
            if (remaining <= 0) break;

            int n = reader.Read(chars, remaining);
            if (n == StdinReader.Closed) break;
            if (n > 0) decoder.Feed(chars.AsSpan(0, n), events);

            foreach (var ev in events)
            {
                if (ev is KittyGraphicsReplyEvent { ImageId: KittyProtocol.ProbeImageId })
                    graphics = true;
                else if (ev is DeviceAttributesEvent)
                    return graphics ? KittyProbeResult.Supported : KittyProbeResult.Unsupported;
            }
            events.Clear();
        }

        return graphics ? KittyProbeResult.Supported : KittyProbeResult.Unknown;
    }
}
