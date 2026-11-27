using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleForge.Terminal;

/// <summary>
/// Reads what the terminal sends on stdin, as characters, with a timeout.
/// </summary>
/// <remarks>
/// This replaces <c>Console.ReadKey</c>. ReadKey hands back keys that .NET has already
/// decoded from the bytes, which leaves nothing to decode escape sequences, Kitty keyboard
/// reports or terminal replies from, and it cannot wait with a timeout, which is what tells
/// a lone Escape from the start of a sequence. Raw characters plus a timeout give
/// <see cref="InputDecoder"/> both.
/// </remarks>
internal abstract class StdinReader
{
    /// <summary><see cref="Read"/> result: stdin is closed or unusable; stop reading.</summary>
    internal const int Closed = -1;

    /// <summary>
    /// <see cref="Read"/> result: woke without characters (a signal, or console records that
    /// carry no text). Unlike a timeout, it says nothing about how long input was quiet.
    /// </summary>
    internal const int Nothing = -2;

    /// <summary>
    /// Wait up to <paramref name="timeoutMs"/> for input and read what is available into
    /// <paramref name="buffer"/>. Returns the number of characters read, 0 on timeout, or
    /// <see cref="Closed"/> / <see cref="Nothing"/>.
    /// </summary>
    internal abstract int Read(char[] buffer, int timeoutMs);

    /// <summary>Size a <see cref="Read"/> buffer must be at least.</summary>
    internal const int MinBufferSize = 4096 + 8;

    /// <summary>The reader for this platform's stdin, or null when stdin is not a terminal.</summary>
    internal static StdinReader? Open()
    {
        // Matches the old ReadKey loop, which stopped when stdin was redirected.
        if (Console.IsInputRedirected) return null;

        if (OperatingSystem.IsWindows())
            return WindowsConsole.OpenInput();
#if !WINDOWS
        return new UnixStdinReader();
#else
        return null;
#endif
    }
}

#if !WINDOWS
/// <summary>poll(2) + read(2) on file descriptor 0, decoded as UTF-8.</summary>
internal sealed class UnixStdinReader : StdinReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int   Fd;
        public short Events;
        public short Revents;
    }

    private const short POLLIN = 0x001;

    // EINTR is 4 everywhere; EAGAIN is 11 on Linux and 35 on macOS / the BSDs.
    private const int EINTR = 4;

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int poll(ref PollFd fds, nuint nfds, int timeout);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint read(int fd, [Out] byte[] buf, nuint count);

    private readonly byte[] _bytes = new byte[4096];

    // Stateful, so a multi-byte character split across two reads still decodes.
    private readonly Decoder _utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetDecoder();

    internal override int Read(char[] buffer, int timeoutMs)
    {
        var fd = new PollFd { Fd = 0, Events = POLLIN };
        int ready = poll(ref fd, 1, timeoutMs);
        if (ready < 0)
            // SIGWINCH interrupts poll on every resize; that is not the end of input.
            return Marshal.GetLastPInvokeError() == EINTR ? Nothing : Closed;
        if (ready == 0) return 0;

        // POLLHUP / POLLERR / POLLNVAL without POLLIN: the terminal went away.
        if ((fd.Revents & POLLIN) == 0) return Closed;

        nint n = read(0, _bytes, (nuint)_bytes.Length);
        if (n < 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            return errno is EINTR or 11 or 35 ? Nothing : Closed;
        }
        if (n == 0) return Closed; // EOF

        int chars = _utf8.GetChars(_bytes, 0, (int)n, buffer, 0, flush: false);
        return chars == 0 ? Nothing : chars;
    }
}
#endif
