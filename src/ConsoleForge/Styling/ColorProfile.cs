namespace ConsoleForge.Styling;

/// <summary>Terminal color capability level.</summary>
public enum ColorProfile
{
    NoColor   = 0,  // no color support
    Ansi      = 1,  // 16 colors
    Ansi256   = 2,  // 256-color palette
    TrueColor = 3   // 24-bit RGB
}
