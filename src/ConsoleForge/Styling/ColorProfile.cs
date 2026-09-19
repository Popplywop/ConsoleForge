namespace ConsoleForge.Styling;

/// <summary>Terminal color capability level.</summary>
public enum ColorProfile
{
    /// <summary>No colour support. Styles render as plain text.</summary>
    NoColor   = 0,

    /// <summary>The 16 standard ANSI colours.</summary>
    Ansi      = 1,

    /// <summary>The 256-colour indexed palette.</summary>
    Ansi256   = 2,

    /// <summary>24-bit RGB.</summary>
    TrueColor = 3
}
