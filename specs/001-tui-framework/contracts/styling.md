# Contract: Styling & Theming Interfaces

**Feature**: specs/001-tui-framework
**Date**: 2026-04-12

---

## `Style` — Immutable Visual Style

```csharp
namespace ConsoleForge.Styling;

/// <summary>
/// Immutable value type carrying visual style properties.
/// Uses a bitmask to distinguish "unset" from "set to default".
/// All mutating methods return a new Style value (fluent builder pattern).
/// </summary>
public readonly struct Style
{
    /// <summary>The empty style (no properties set). Fast path: Render returns text unchanged.</summary>
    public static readonly Style Default;

    // ── Color ────────────────────────────────────────────────────────
    public Style Foreground(IColor color);
    public Style Background(IColor color);

    // ── Text decoration ──────────────────────────────────────────────
    public Style Bold(bool value = true);
    public Style Italic(bool value = true);
    public Style Underline(bool value = true);
    public Style Strikethrough(bool value = true);
    public Style Faint(bool value = true);
    public Style Blink(bool value = true);
    public Style Reverse(bool value = true);

    // ── Spacing ──────────────────────────────────────────────────────
    public Style Padding(int all);
    public Style Padding(int vertical, int horizontal);
    public Style Padding(int top, int right, int bottom, int left);
    public Style Margin(int all);
    public Style Margin(int vertical, int horizontal);
    public Style Margin(int top, int right, int bottom, int left);

    // ── Size ─────────────────────────────────────────────────────────
    /// <summary>Clamp rendered output to exactly width columns (pad or truncate).</summary>
    public Style Width(int columns);
    /// <summary>Clamp rendered output to exactly height rows (pad or truncate).</summary>
    public Style Height(int rows);

    // ── Alignment ────────────────────────────────────────────────────
    public Style Align(HorizontalAlign align);

    // ── Borders ──────────────────────────────────────────────────────
    public Style Border(BorderSpec border);
    public Style BorderTop(bool enabled = true);
    public Style BorderRight(bool enabled = true);
    public Style BorderBottom(bool enabled = true);
    public Style BorderLeft(bool enabled = true);
    public Style BorderForeground(IColor color);
    public Style BorderBackground(IColor color);

    // ── Unset ────────────────────────────────────────────────────────
    public Style UnsetForeground();
    public Style UnsetBackground();
    public Style UnsetBold();
    // ... (one Unset method per property)

    // ── Inheritance ──────────────────────────────────────────────────
    /// <summary>
    /// Copy properties from parent that are set in parent but not yet set in this.
    /// Margins and padding are NOT inherited (they are local properties).
    /// </summary>
    public Style Inherit(Style parent);

    // ── Rendering ────────────────────────────────────────────────────
    /// <summary>
    /// Apply all set style properties to text and return the styled ANSI string.
    /// If no properties are set (_props == 0), returns text unchanged (fast path).
    /// Color sequences are downsampled to colorProfile capability.
    /// </summary>
    public string Render(string text, ColorProfile colorProfile = ColorProfile.TrueColor);
}

public enum HorizontalAlign { Left, Center, Right }
```

---

## `IColor` — Color Abstraction

```csharp
namespace ConsoleForge.Styling;

/// <summary>
/// Abstraction over ANSI/256/TrueColor values.
/// Implementations are downsampled at render time via ColorProfile.
/// </summary>
public interface IColor
{
    /// <summary>
    /// Produce an ANSI escape sequence fragment for this color,
    /// downsampled to the given profile (never upgrades).
    /// </summary>
    string ToAnsiSequence(bool isForeground, ColorProfile profile);
}
```

**Built-in implementations**:

```csharp
namespace ConsoleForge.Styling;

/// <summary>No color set (transparent / inherit).</summary>
public sealed record NoColor : IColor;

/// <summary>One of the 8 or 16 standard ANSI colors.</summary>
public sealed record AnsiColor(int Index) : IColor;  // Index 0–15

/// <summary>One of the 256 xterm palette colors.</summary>
public sealed record Ansi256Color(int Index) : IColor;  // Index 0–255

/// <summary>24-bit RGB true color.</summary>
public sealed record TrueColor(byte R, byte G, byte B) : IColor;

/// <summary>
/// Factory for common named colors and hex parsing.
/// Color.FromHex("#FF5733") → TrueColor(255, 87, 51)
/// Color.Red → AnsiColor(1)
/// </summary>
public static class Color
{
    public static IColor FromHex(string hex);
    public static IColor FromRgb(byte r, byte g, byte b);
    public static IColor FromAnsi(int index);   // 0–255
    public static readonly IColor Black, Red, Green, Yellow, Blue,
        Magenta, Cyan, White,
        BrightBlack, BrightRed, BrightGreen, BrightYellow,
        BrightBlue, BrightMagenta, BrightCyan, BrightWhite;
}

/// <summary>Terminal color capability level.</summary>
public enum ColorProfile
{
    NoColor  = 0,  // no color support
    Ansi     = 1,  // 16 colors
    Ansi256  = 2,  // 256-color palette
    TrueColor = 3  // 24-bit RGB
}
```

---

## `BorderSpec` — Border Character Set

```csharp
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

/// <summary>Pre-defined border character sets.</summary>
public static class Borders
{
    public static readonly BorderSpec Normal;   // ┌─┐│└┘├┤┬┴┼
    public static readonly BorderSpec Rounded;  // ╭─╮│╰╯├┤┬┴┼
    public static readonly BorderSpec Thick;    // ┏━┓┃┗┛┣┫┳┻╋
    public static readonly BorderSpec Double;   // ╔═╗║╚╝╠╣╦╩╬
    public static readonly BorderSpec ASCII;    // +-+|+-+  (safe fallback)
    public static readonly BorderSpec Hidden;   // all spaces
}
```

---

## `Theme` — Global Style Defaults

```csharp
namespace ConsoleForge.Styling;

/// <summary>
/// Immutable named collection of base styles applied as defaults across
/// all widgets via Style.Inherit().
/// </summary>
public sealed record Theme
{
    /// <summary>The default theme (no colors, no borders).</summary>
    public static readonly Theme Default;

    public required string Name { get; init; }

    /// <summary>Default text style. All widgets inherit from this.</summary>
    public Style BaseStyle { get; init; } = Style.Default;

    /// <summary>Default border style.</summary>
    public Style BorderStyle { get; init; } = Style.Default;

    /// <summary>Additional style applied on top of the widget's own style when focused.</summary>
    public Style FocusedStyle { get; init; } = Style.Default;

    /// <summary>Applied to widgets that are disabled / non-interactive.</summary>
    public Style DisabledStyle { get; init; } = Style.Default.Faint(true);

    /// <summary>
    /// Named style slots for custom or third-party widget types.
    /// Widgets look themselves up by a string key they define.
    /// </summary>
    public IReadOnlyDictionary<string, Style> Named { get; init; }
        = ImmutableDictionary<string, Style>.Empty;
}
```
