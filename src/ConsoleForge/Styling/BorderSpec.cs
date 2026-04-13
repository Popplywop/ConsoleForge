namespace ConsoleForge.Styling;

/// <summary>
/// Defines the character set for a border style.
/// Each field is a string (not a char) to support multi-rune Unicode glyphs.
/// </summary>
public readonly record struct BorderSpec
{
    /// <summary>Character used for the top horizontal edge.</summary>
    public string Top { get; init; }
    /// <summary>Character used for the bottom horizontal edge.</summary>
    public string Bottom { get; init; }
    /// <summary>Character used for the left vertical edge.</summary>
    public string Left { get; init; }
    /// <summary>Character used for the right vertical edge.</summary>
    public string Right { get; init; }
    /// <summary>Character used for the top-left corner.</summary>
    public string TopLeft { get; init; }
    /// <summary>Character used for the top-right corner.</summary>
    public string TopRight { get; init; }
    /// <summary>Character used for the bottom-left corner.</summary>
    public string BottomLeft { get; init; }
    /// <summary>Character used for the bottom-right corner.</summary>
    public string BottomRight { get; init; }
    /// <summary>Character used for a left-side T-junction (inner divider meets left edge).</summary>
    public string MiddleLeft { get; init; }
    /// <summary>Character used for a right-side T-junction (inner divider meets right edge).</summary>
    public string MiddleRight { get; init; }
    /// <summary>Character used for a cross-junction (inner horizontal and vertical dividers cross).</summary>
    public string Middle { get; init; }
    /// <summary>Character used for a top T-junction (inner divider meets the top edge).</summary>
    public string MiddleTop { get; init; }
    /// <summary>Character used for a bottom T-junction (inner divider meets the bottom edge).</summary>
    public string MiddleBottom { get; init; }
}
