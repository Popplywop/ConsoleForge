using System.Runtime.InteropServices;

namespace ConsoleForge.Terminal;

/// <summary>
/// P/Invoke shim for Unix terminal control (tcgetattr / tcsetattr / cfmakeraw).
/// Only compiled on non-Windows platforms.
/// Used exclusively by <see cref="AnsiTerminal.EnterRawMode"/> and
/// <see cref="AnsiTerminal.ExitRawMode"/>.
/// </summary>
#if !WINDOWS
internal static class Termios
{
    // termios struct layout for Linux (x86-64 / arm64)
    // Matches <asm/termbits.h>: c_iflag, c_oflag, c_cflag, c_lflag, c_line, c_cc[32], c_ispeed, c_ospeed
    [StructLayout(LayoutKind.Sequential)]
    internal struct Termios_t
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        public byte c_line;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] c_cc;
        public uint c_ispeed;
        public uint c_ospeed;

        public Termios_t() { c_cc = new byte[32]; }
    }

    // TCSANOW = 0 on Linux
    private const int TCSANOW = 0;

    [DllImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
    private static extern int tcgetattr(int fd, out Termios_t termios);

    [DllImport("libc", EntryPoint = "tcsetattr", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optional_actions, ref Termios_t termios);

    [DllImport("libc", EntryPoint = "cfmakeraw", SetLastError = false)]
    private static extern void cfmakeraw(ref Termios_t termios);

    // stdin fd = 0
    private const int STDIN_FD = 0;

    /// <summary>
    /// Read the current terminal attributes from stdin.
    /// Returns true on success.
    /// </summary>
    internal static bool GetAttr(out Termios_t result)
    {
        result = new Termios_t();
        return tcgetattr(STDIN_FD, out result) == 0;
    }

    /// <summary>
    /// Apply raw mode to the saved attributes and set them on stdin.
    /// </summary>
    internal static bool MakeRaw(ref Termios_t termios)
    {
        cfmakeraw(ref termios);
        return tcsetattr(STDIN_FD, TCSANOW, ref termios) == 0;
    }

    /// <summary>
    /// Restore previously saved attributes to stdin.
    /// </summary>
    internal static bool Restore(ref Termios_t termios)
    {
        return tcsetattr(STDIN_FD, TCSANOW, ref termios) == 0;
    }
}
#endif
