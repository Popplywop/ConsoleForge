using System.Runtime.InteropServices;

namespace ConsoleForge.Terminal;

#if !WINDOWS
/// <summary>
/// P/Invoke shim for Unix terminal control (tcgetattr / tcsetattr / cfmakeraw).
/// Only compiled on non-Windows platforms.
/// Used by <see cref="AnsiTerminal.EnterRawMode"/>, <see cref="AnsiTerminal.ExitRawMode"/>
/// and the capability probe.
/// </summary>
/// <remarks>
/// <c>struct termios</c> is treated as an opaque block of bytes. Its layout differs by
/// platform — 60 bytes on Linux, 72 on macOS, where the flag fields are 64-bit and
/// <c>c_cc</c> has 20 entries — and a managed mirror of the Linux layout let
/// <c>tcgetattr</c> write past the end of its buffer on macOS. Nothing here needs to read
/// a field: libc fills the block, <c>cfmakeraw</c> edits it, libc reads it back. So the
/// block only has to be large enough, never laid out correctly.
/// </remarks>
internal static class Termios
{
    // Comfortably larger than struct termios on every Unix .NET runs on.
    private const int BufferSize = 256;

    // TCSANOW is 0 on Linux, macOS and the BSDs.
    private const int TCSANOW = 0;

    private const int STDIN_FD = 0;

    [DllImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
    private static extern int tcgetattr(int fd, [Out] byte[] termios);

    [DllImport("libc", EntryPoint = "tcsetattr", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optional_actions, [In] byte[] termios);

    [DllImport("libc", EntryPoint = "cfmakeraw", SetLastError = false)]
    private static extern void cfmakeraw([In, Out] byte[] termios);

    /// <summary>
    /// Read the current attributes of stdin, or null when stdin is not a terminal.
    /// </summary>
    internal static byte[]? GetAttr()
    {
        var termios = new byte[BufferSize];
        return tcgetattr(STDIN_FD, termios) == 0 ? termios : null;
    }

    /// <summary>
    /// Apply raw mode, derived from <paramref name="saved"/>, to stdin.
    /// <paramref name="saved"/> itself is left untouched so it can be restored.
    /// </summary>
    internal static bool MakeRaw(byte[] saved)
    {
        var raw = (byte[])saved.Clone();
        cfmakeraw(raw);
        return tcsetattr(STDIN_FD, TCSANOW, raw) == 0;
    }

    /// <summary>
    /// True when <paramref name="attrs"/> are already raw mode. Read without knowing the
    /// layout: <c>cfmakeraw</c> of raw attributes changes nothing, so a copy put through it
    /// comes back identical exactly when the original was raw.
    /// </summary>
    internal static bool IsRaw(byte[] attrs)
    {
        var raw = (byte[])attrs.Clone();
        cfmakeraw(raw);
        return raw.AsSpan().SequenceEqual(attrs);
    }

    /// <summary>Restore previously saved attributes to stdin.</summary>
    internal static bool Restore(byte[] saved) =>
        tcsetattr(STDIN_FD, TCSANOW, saved) == 0;
}
#endif
