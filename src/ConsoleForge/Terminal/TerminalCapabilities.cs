using System.Diagnostics;

namespace ConsoleForge.Terminal;

/// <summary>
/// Detected capabilities of the host terminal. Injected into widgets that need to
/// adapt their rendering strategy at runtime (e.g. <c>ImageWidget</c> choosing between
/// Kitty graphics and half-block Unicode fallback).
/// <para>
/// Obtain an instance via <see cref="Detect"/> at application startup (before entering
/// raw mode and the alternate screen), or construct a known configuration with
/// <see cref="None"/> / <see cref="WithKitty"/> for tests or explicit overrides.
/// </para>
/// </summary>
public sealed class TerminalCapabilities
{
    /// <summary>
    /// True when the terminal supports the Kitty graphics protocol
    /// (<c>ESC _ G … ESC \</c> APC sequences for pixel image rendering).
    /// </summary>
    public bool SupportsKittyGraphics { get; init; }

    /// <summary>
    /// When running inside tmux, the number of terminal rows above the active pane
    /// (i.e. the height of status bars positioned at the top of the window).
    /// 0 when not in tmux or when the status bar is at the bottom.
    /// <para>
    /// Used to convert pane-relative row coordinates to absolute terminal coordinates
    /// when embedding cursor-positioning escapes inside DCS passthrough blocks for
    /// Kitty graphics placement.
    /// </para>
    /// </summary>
    public int TmuxPaneRowOffset { get; init; }

    /// <summary>
    /// When running inside tmux, the number of terminal columns to the left of the
    /// active pane (non-zero when panes are arranged side-by-side).
    /// 0 when not in tmux or pane starts at the left edge.
    /// </summary>
    public int TmuxPaneColOffset { get; init; }

    // ── Well-known instances ──────────────────────────────────────────────────

    /// <summary>No optional capabilities — all flags false. Suitable as a safe default.</summary>
    public static readonly TerminalCapabilities None = new();

    /// <summary>Kitty graphics supported, no tmux offsets. Useful for tests and explicit overrides.</summary>
    public static readonly TerminalCapabilities WithKitty = new() { SupportsKittyGraphics = true };

    // ── Detection ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Probe the environment and return a <see cref="TerminalCapabilities"/> instance
    /// reflecting what the current terminal supports.
    /// <para>
    /// <b>Kitty detection</b> checks a prioritised set of well-known environment
    /// variables. Variables that propagate through tmux sessions are tried first
    /// (e.g. <c>WEZTERM_PANE</c> is set by WezTerm and survives inside tmux).
    /// </para>
    /// <list type="bullet">
    ///   <item>Kitty: <c>TERM=xterm-kitty</c> or <c>KITTY_WINDOW_ID</c></item>
    ///   <item>WezTerm: <c>WEZTERM_PANE</c>, <c>WEZTERM_UNIX_SOCKET</c>, or <c>TERM_PROGRAM=WezTerm</c></item>
    ///   <item>Ghostty: <c>TERM=xterm-ghostty</c> or <c>GHOSTTY_RESOURCES_DIR</c></item>
    ///   <item>foot: <c>TERM=foot</c> or <c>TERM=foot-extra</c></item>
    /// </list>
    /// <para>
    /// <b>tmux pane offsets:</b> When inside tmux the method runs <c>tmux display</c>
    /// once to determine the pane's absolute position in the terminal window, including
    /// status-bar height. These offsets correct Kitty image placement when the status
    /// bar is positioned above the pane.
    /// </para>
    /// <para>
    /// Call before <see cref="ITerminal.EnterRawMode"/> and
    /// <see cref="ITerminal.EnterAlternateScreen"/>.
    /// </para>
    /// </summary>
    public static TerminalCapabilities Detect()
    {
        var term        = Environment.GetEnvironmentVariable("TERM")        ?? "";
        var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM") ?? "";

        bool kitty =
            term == "xterm-kitty"
            || Environment.GetEnvironmentVariable("KITTY_WINDOW_ID")     is not null
            || Environment.GetEnvironmentVariable("WEZTERM_PANE")         is not null
            || Environment.GetEnvironmentVariable("WEZTERM_UNIX_SOCKET")  is not null
            || string.Equals(termProgram, "WezTerm", StringComparison.OrdinalIgnoreCase)
            || term == "xterm-ghostty"
            || Environment.GetEnvironmentVariable("GHOSTTY_RESOURCES_DIR") is not null
            || term == "foot"
            || term == "foot-extra";

        bool insideTmux = Environment.GetEnvironmentVariable("TMUX") is not null
                       || term.StartsWith("tmux", StringComparison.Ordinal);

        int rowOffset = 0;
        int colOffset = 0;
        if (insideTmux)
            (rowOffset, colOffset) = QueryTmuxPaneAbsoluteOffset();

        return new TerminalCapabilities
        {
            SupportsKittyGraphics = kitty,
            TmuxPaneRowOffset     = rowOffset,
            TmuxPaneColOffset     = colOffset,
        };
    }

    /// <summary>
    /// Query tmux for the pane's absolute position in the outer terminal window.
    /// <para>
    /// tmux's <c>#{pane_top}</c> and <c>#{pane_left}</c> are relative to the window
    /// content area and do not include status bar rows. We add the status bar height
    /// (when positioned at the top) to get the true terminal-absolute row coordinate.
    /// </para>
    /// </summary>
    private static (int rowOffset, int colOffset) QueryTmuxPaneAbsoluteOffset()
    {
        try
        {
            var paneId = Environment.GetEnvironmentVariable("TMUX_PANE") ?? "";

            var psi = new ProcessStartInfo
            {
                FileName               = "tmux",
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            // Target the specific pane when TMUX_PANE is available so nested
            // tmux sessions or split-pane scenarios return the right values.
            psi.ArgumentList.Add("display");
            psi.ArgumentList.Add("-p");
            if (!string.IsNullOrEmpty(paneId))
            {
                psi.ArgumentList.Add("-t");
                psi.ArgumentList.Add(paneId);
            }
            // Returns e.g. "0 0 2 top" for pane_top=0, pane_left=0, status=2, status-position=top
            psi.ArgumentList.Add("#{pane_top} #{pane_left} #{status} #{status-position}");

            using var proc = Process.Start(psi);
            if (proc is null) return (0, 0);

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(2000);

            var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return (0, 0);

            int paneTop      = int.TryParse(parts[0], out var pt) ? pt : 0;
            int paneLeft     = int.TryParse(parts[1], out var pl) ? pl : 0;
            int statusRows   = int.TryParse(parts[2], out var sr) ? sr : 0;
            bool statusAtTop = parts[3].Equals("top", StringComparison.OrdinalIgnoreCase);

            // Absolute terminal row = pane's window-row + status bar height (if at top).
            // pane_left is already in terminal-absolute column coordinates.
            int rowOffset = paneTop + (statusAtTop ? statusRows : 0);
            int colOffset = paneLeft;

            return (rowOffset, colOffset);
        }
        catch
        {
            // tmux not found, permission error, unexpected output, etc.
            // Safe fallback — images may be slightly mispositioned but won't crash.
            return (0, 0);
        }
    }
}
