namespace ConsoleForge.Styling;

/// <summary>
/// Defines the character set for a border style.
/// Each field is a string (not a char) to support multi-rune Unicode glyphs.
/// </summary>
public readonly record struct BorderSpec
{
    public string Top { get; init; }
    public string Bottom { get; init; }
    public string Left { get; init; }
    public string Right { get; init; }
    public string TopLeft { get; init; }
    public string TopRight { get; init; }
    public string BottomLeft { get; init; }
    public string BottomRight { get; init; }
    public string MiddleLeft { get; init; }
    public string MiddleRight { get; init; }
    public string Middle { get; init; }
    public string MiddleTop { get; init; }
    public string MiddleBottom { get; init; }
}
