using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ConsoleForge.Terminal;

/// <summary>
/// Windows-only helper that P/Invokes into kernel32.dll to manage console mode:
/// raw input mode (disable echo/line-input/processed-input) and Virtual Terminal
/// Processing (ENABLE_VIRTUAL_TERMINAL_PROCESSING) for ANSI escape sequences.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsConsole
{
    // ── kernel32 constants ────────────────────────────────────────────

    private const int STD_INPUT_HANDLE  = -10;
    private const int STD_OUTPUT_HANDLE = -11;

    // Input mode flags
    private const uint ENABLE_ECHO_INPUT      = 0x0004;
    private const uint ENABLE_LINE_INPUT      = 0x0002;
    private const uint ENABLE_PROCESSED_INPUT = 0x0001;

    // Output mode flags
    private const uint ENABLE_PROCESSED_OUTPUT            = 0x0001;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    // ── P/Invokes ─────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    // ── Saved state ───────────────────────────────────────────────────

    private static uint _savedInputMode;
    private static uint _savedOutputMode;
    private static bool _rawModeActive;

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to enable ENABLE_VIRTUAL_TERMINAL_PROCESSING on stdout.
    /// Safe to call on any Windows version; returns false silently on failure.
    /// </summary>
    internal static bool TryEnableVtp()
    {
        var hOut = GetStdHandle(STD_OUTPUT_HANDLE);
        if (hOut == INVALID_HANDLE_VALUE || hOut == IntPtr.Zero) return false;

        if (!GetConsoleMode(hOut, out var mode)) return false;

        mode |= ENABLE_PROCESSED_OUTPUT | ENABLE_VIRTUAL_TERMINAL_PROCESSING;
        return SetConsoleMode(hOut, mode);
    }

    /// <summary>
    /// Saves current console modes and enables raw input mode (disables echo,
    /// line buffering, and processed input) on stdin.
    /// Also enables VTP on stdout as a side-effect.
    /// Returns false if unable to retrieve or set modes.
    /// </summary>
    internal static bool TryEnableRawMode()
    {
        if (_rawModeActive) return true;

        var hIn  = GetStdHandle(STD_INPUT_HANDLE);
        var hOut = GetStdHandle(STD_OUTPUT_HANDLE);

        if (hIn  == INVALID_HANDLE_VALUE || hIn  == IntPtr.Zero) return false;
        if (hOut == INVALID_HANDLE_VALUE || hOut == IntPtr.Zero) return false;

        if (!GetConsoleMode(hIn,  out _savedInputMode))  return false;
        if (!GetConsoleMode(hOut, out _savedOutputMode)) return false;

        // Raw input: remove echo, line-input, and processed-input.
        // Do NOT set ENABLE_VIRTUAL_TERMINAL_INPUT — that switches the input
        // pipe to raw VT byte sequences, which breaks Console.ReadKey (it reads
        // via ReadConsoleInput KEY_EVENT records and will block/return garbage).
        var rawInput = _savedInputMode
            & ~ENABLE_ECHO_INPUT
            & ~ENABLE_LINE_INPUT
            & ~ENABLE_PROCESSED_INPUT;

        if (!SetConsoleMode(hIn, rawInput)) return false;

        // Output: ensure VTP is enabled
        var rawOutput = _savedOutputMode
            | ENABLE_PROCESSED_OUTPUT
            | ENABLE_VIRTUAL_TERMINAL_PROCESSING;

        if (!SetConsoleMode(hOut, rawOutput)) return false;

        _rawModeActive = true;
        return true;
    }

    /// <summary>
    /// Restores previously saved console modes for both stdin and stdout.
    /// No-op if <see cref="TryEnableRawMode"/> was never called successfully.
    /// </summary>
    internal static void TryRestoreMode()
    {
        if (!_rawModeActive) return;

        var hIn  = GetStdHandle(STD_INPUT_HANDLE);
        var hOut = GetStdHandle(STD_OUTPUT_HANDLE);

        if (hIn  != INVALID_HANDLE_VALUE && hIn  != IntPtr.Zero)
            SetConsoleMode(hIn,  _savedInputMode);

        if (hOut != INVALID_HANDLE_VALUE && hOut != IntPtr.Zero)
            SetConsoleMode(hOut, _savedOutputMode);

        _rawModeActive = false;
    }
}
