using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ConsoleForge.Terminal;

/// <summary>
/// Windows-only helper that P/Invokes into kernel32.dll to manage console mode:
/// raw input mode (disable echo/line-input/processed-input, enable VT input) and Virtual
/// Terminal Processing (ENABLE_VIRTUAL_TERMINAL_PROCESSING) for ANSI escape sequences.
/// </summary>
/// <remarks>
/// With ENABLE_VIRTUAL_TERMINAL_INPUT the console delivers keys, mouse reports and replies
/// as the same VT sequences a Unix terminal sends, so one <see cref="InputDecoder"/> serves
/// both platforms. Without it, SGR mouse reports never arrive: the console turns mouse
/// activity into MOUSE_EVENT records that the old <c>Console.ReadKey</c> loop skipped.
/// </remarks>
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
    private const uint ENABLE_MOUSE_INPUT     = 0x0010;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS  = 0x0080;
    private const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

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

    /// <summary>True while raw mode is active with ENABLE_VIRTUAL_TERMINAL_INPUT accepted.</summary>
    internal static bool VtInputActive { get; private set; }

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

        // Raw input: remove echo, line-input, and processed-input (so Ctrl+C arrives as
        // a key), and switch input to VT sequences for InputDecoder. Input is read with
        // ReadConsoleInputW (WindowsStdinReader), never Console.ReadKey, which VT input
        // mode would break.
        var rawInput = (_savedInputMode
            & ~ENABLE_ECHO_INPUT
            & ~ENABLE_LINE_INPUT
            & ~ENABLE_PROCESSED_INPUT)
            | ENABLE_VIRTUAL_TERMINAL_INPUT;

        // VT input needs Windows 10 1809 or later. Where it is refused, raw mode still goes
        // ahead without it, and WindowsStdinReader encodes the keys that carry no character
        // (arrows, function keys, …) itself.
        VtInputActive = SetConsoleMode(hIn, rawInput);
        if (!VtInputActive && !SetConsoleMode(hIn, rawInput & ~ENABLE_VIRTUAL_TERMINAL_INPUT))
            return false;

        // Output: ensure VTP is enabled
        var rawOutput = _savedOutputMode
            | ENABLE_PROCESSED_OUTPUT
            | ENABLE_VIRTUAL_TERMINAL_PROCESSING;

        if (!SetConsoleMode(hOut, rawOutput)) return false;

        _rawModeActive = true;
        return true;
    }

    /// <summary>
    /// Turn mouse capture on or off. On, Quick Edit is suspended — otherwise a click in a
    /// classic console window starts a text selection instead of reaching the application.
    /// Off, Quick Edit goes back to what it was before raw mode.
    /// </summary>
    internal static void SetMouseCapture(bool capture)
    {
        // Restoring needs the mode saved by TryEnableRawMode; without it there is nothing
        // to go back to.
        if (!capture && !_rawModeActive) return;

        var hIn = GetStdHandle(STD_INPUT_HANDLE);
        if (hIn == INVALID_HANDLE_VALUE || hIn == IntPtr.Zero) return;
        if (!GetConsoleMode(hIn, out var mode)) return;

        mode |= ENABLE_EXTENDED_FLAGS; // required for the Quick Edit bit to take effect
        if (capture)
            mode = (mode | ENABLE_MOUSE_INPUT) & ~ENABLE_QUICK_EDIT_MODE;
        else
            mode = (mode & ~(ENABLE_QUICK_EDIT_MODE | ENABLE_MOUSE_INPUT))
                 | (_savedInputMode & (ENABLE_QUICK_EDIT_MODE | ENABLE_MOUSE_INPUT));

        SetConsoleMode(hIn, mode);
    }

    /// <summary>A reader for the console's input buffer, or null when stdin is not a console.</summary>
    internal static StdinReader? OpenInput()
    {
        var hIn = GetStdHandle(STD_INPUT_HANDLE);
        if (hIn == INVALID_HANDLE_VALUE || hIn == IntPtr.Zero) return null;
        return GetConsoleMode(hIn, out _) ? new WindowsStdinReader(hIn) : null;
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
        VtInputActive  = false;
    }
}

/// <summary>
/// Reads the console input buffer with <c>ReadConsoleInputW</c>. With VT input enabled every
/// key, mouse report and reply arrives as key-down records carrying the characters of its
/// VT sequence, which this concatenates for <see cref="InputDecoder"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsStdinReader(IntPtr input) : StdinReader
{
    private const ushort KEY_EVENT    = 0x0001;
    private const uint   WAIT_OBJECT_0 = 0x00000000;
    private const uint   WAIT_TIMEOUT  = 0x00000102;

    // INPUT_RECORD with its KEY_EVENT_RECORD arm; every field blittable.
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct InputRecord
    {
        [FieldOffset(0)]  public ushort EventType;
        [FieldOffset(4)]  public int    KeyDown;
        [FieldOffset(8)]  public ushort RepeatCount;
        [FieldOffset(10)] public ushort VirtualKeyCode;
        [FieldOffset(12)] public ushort VirtualScanCode;
        [FieldOffset(14)] public ushort UnicodeChar;
        [FieldOffset(16)] public uint   ControlKeyState;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", SetLastError = true)]
    private static extern bool ReadConsoleInput(IntPtr input, [Out] InputRecord[] buffer, uint length, out uint read);

    private readonly InputRecord[] _records = new InputRecord[512];

    // dwControlKeyState bits.
    private const uint AltPressed  = 0x0001 | 0x0002; // RIGHT_ALT | LEFT_ALT
    private const uint CtrlPressed = 0x0004 | 0x0008; // RIGHT_CTRL | LEFT_CTRL
    private const uint ShiftPressed = 0x0010;

    /// <summary>
    /// Without VT input, keys that produce no character arrive only as virtual-key codes.
    /// Encode them the way a VT terminal would, so <see cref="InputDecoder"/> sees one format.
    /// </summary>
    private static string? LegacyKeySequence(ushort vk, uint state)
    {
        int mods = 1
            + ((state & ShiftPressed) != 0 ? 1 : 0)
            + ((state & AltPressed)   != 0 ? 2 : 0)
            + ((state & CtrlPressed)  != 0 ? 4 : 0);

        static string Letter(char f, int m) => m > 1 ? $"\x1b[1;{m}{f}" : $"\x1b[{f}";
        static string Tilde(int code, int m) => m > 1 ? $"\x1b[{code};{m}~" : $"\x1b[{code}~";

        return vk switch
        {
            0x26 => Letter('A', mods), // VK_UP
            0x28 => Letter('B', mods), // VK_DOWN
            0x27 => Letter('C', mods), // VK_RIGHT
            0x25 => Letter('D', mods), // VK_LEFT
            0x24 => Letter('H', mods), // VK_HOME
            0x23 => Letter('F', mods), // VK_END
            0x2D => Tilde(2, mods),    // VK_INSERT
            0x2E => Tilde(3, mods),    // VK_DELETE
            0x21 => Tilde(5, mods),    // VK_PRIOR
            0x22 => Tilde(6, mods),    // VK_NEXT
            0x70 => Letter('P', mods), // VK_F1 … VK_F4
            0x71 => Letter('Q', mods),
            0x72 => Letter('R', mods),
            0x73 => Letter('S', mods),
            >= 0x74 and <= 0x7B => Tilde(new[] { 15, 17, 18, 19, 20, 21, 23, 24 }[vk - 0x74], mods),
            _ => null,
        };
    }

    internal override int Read(char[] buffer, int timeoutMs)
    {
        uint wait = WaitForSingleObject(input, (uint)Math.Max(0, timeoutMs));
        if (wait == WAIT_TIMEOUT) return 0;
        if (wait != WAIT_OBJECT_0) return Closed;

        if (!ReadConsoleInput(input, _records, (uint)_records.Length, out uint count))
            return Closed;

        bool vt = WindowsConsole.VtInputActive;
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            ref var r = ref _records[i];
            // Key-up records repeat the character of their key-down; focus, menu and
            // buffer-size records carry no text at all.
            if (r.EventType != KEY_EVENT || r.KeyDown == 0) continue;

            string? text = r.UnicodeChar != 0
                ? (!vt && (r.ControlKeyState & AltPressed) != 0 && (r.ControlKeyState & CtrlPressed) == 0
                    ? "\x1b" + (char)r.UnicodeChar          // Alt+key, as a VT terminal sends it
                    : null)
                : vt ? null : LegacyKeySequence(r.VirtualKeyCode, r.ControlKeyState);

            for (int rep = Math.Max((int)r.RepeatCount, 1); rep > 0; rep--)
            {
                if (text is null)
                {
                    if (r.UnicodeChar == 0 || n >= buffer.Length) break;
                    buffer[n++] = (char)r.UnicodeChar;
                }
                else
                {
                    if (n + text.Length > buffer.Length) break;
                    text.CopyTo(0, buffer, n, text.Length);
                    n += text.Length;
                }
            }
        }

        // Records that carried no text: a wake-up, not a quiet period.
        return n == 0 ? Nothing : n;
    }
}
