using System.Text.RegularExpressions;

namespace ConsoleForge.Tests;

/// <summary>
/// Shared test utilities: ANSI escape stripping and plain-text extraction.
/// </summary>
internal static partial class TestHelpers
{
    [GeneratedRegex(@"\x1b\[[^a-zA-Z]*[a-zA-Z]")]
    private static partial Regex AnsiEscapeRegex();

    // Matches Kitty / Sixel APC sequences: ESC _ <anything> ESC \
    // The inner content may contain any bytes except ESC, hence [^\x1b]*.
    [GeneratedRegex(@"\x1b_[^\x1b]*\x1b\\\\")]
    private static partial Regex ApcSequenceRegex();

    /// <summary>
    /// Strip all ANSI CSI escape sequences from <paramref name="s"/>
    /// and return the plain printable text.
    /// Useful for asserting content in styled ANSI frames where individual
    /// characters are wrapped in escape codes (bold, reverse, etc.).
    /// </summary>
    public static string StripAnsi(string s) =>
        AnsiEscapeRegex().Replace(s, string.Empty);

    /// <summary>
    /// Strip all APC escape sequences (e.g. Kitty graphics blobs) from
    /// <paramref name="s"/> and return the remaining text.
    /// </summary>
    public static string StripApc(string s) =>
        ApcSequenceRegex().Replace(s, string.Empty);

    /// <summary>
    /// Returns true when <paramref name="s"/> contains at least one APC sequence
    /// (i.e. a raw escape payload such as a Kitty graphics chunk).
    /// </summary>
    public static bool ContainsApc(string s) =>
        ApcSequenceRegex().IsMatch(s);
}
