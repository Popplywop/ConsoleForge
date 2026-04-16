using System.Text.RegularExpressions;

namespace ConsoleForge.Tests;

/// <summary>
/// Shared test utilities: ANSI escape stripping and plain-text extraction.
/// </summary>
internal static partial class TestHelpers
{
    [GeneratedRegex(@"\x1b\[[^a-zA-Z]*[a-zA-Z]")]
    private static partial Regex AnsiEscapeRegex();

    /// <summary>
    /// Strip all ANSI escape sequences from <paramref name="s"/>
    /// and return the plain printable text.
    /// Useful for asserting content in styled ANSI frames where individual
    /// characters are wrapped in escape codes (bold, reverse, etc.).
    /// </summary>
    public static string StripAnsi(string s) =>
        AnsiEscapeRegex().Replace(s, string.Empty);
}
