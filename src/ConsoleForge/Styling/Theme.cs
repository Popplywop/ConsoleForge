using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace ConsoleForge.Styling;

/// <summary>
/// Immutable named collection of base styles applied as defaults across
/// all widgets via Style.Inherit().
/// </summary>
public sealed record Theme
{
    /// <summary>The default theme (no colors, no borders).</summary>
    public static readonly Theme Default = new() { Name = "default" };

    // ── Built-in named themes ────────────────────────────────────────────────────

    /// <summary>
    /// Dark terminal theme. Bright text on a near-black background, cyan borders,
    /// gold focus ring. Suitable as a drop-in for any ConsoleForge application.
    /// </summary>
    public static readonly Theme Dark = new(
        name: "Dark",
        baseStyle:    Style.Default.Foreground(Color.FromHex("#EBEBEB")).Background(Color.FromHex("#1C1C1C")),
        borderStyle:  Style.Default.BorderForeground(Color.FromHex("#4EC9B0")).Border(Borders.Rounded),
        focusedStyle: Style.Default.BorderForeground(Color.FromHex("#FFD700")).Bold(true),
        disabledStyle:Style.Default.Foreground(Color.FromHex("#6B6B6B")).Faint(true),
        named: new Dictionary<string, Style>
        {
            ["accent"]    = Style.Default.Foreground(Color.FromHex("#4EC9B0")),
            ["secondary"] = Style.Default.Foreground(Color.FromHex("#9B9B9B")),
            ["success"]   = Style.Default.Foreground(Color.FromHex("#4EC9B0")),
            ["warning"]   = Style.Default.Foreground(Color.FromHex("#FFD700")),
            ["error"]     = Style.Default.Foreground(Color.FromHex("#FF6B6B")),
            ["muted"]     = Style.Default.Foreground(Color.FromHex("#6B6B6B")),
        });

    /// <summary>
    /// Light terminal theme. Dark text on a near-white background, blue borders,
    /// red focus ring. Best on terminals with light default backgrounds.
    /// </summary>
    public static readonly Theme Light = new(
        name: "Light",
        baseStyle:    Style.Default.Foreground(Color.FromHex("#1C1C1C")).Background(Color.FromHex("#F0F0F0")),
        borderStyle:  Style.Default.BorderForeground(Color.FromHex("#0066CC")).Border(Borders.Rounded),
        focusedStyle: Style.Default.BorderForeground(Color.FromHex("#CC0000")).Bold(true),
        disabledStyle:Style.Default.Foreground(Color.FromHex("#999999")).Faint(true),
        named: new Dictionary<string, Style>
        {
            ["accent"]    = Style.Default.Foreground(Color.FromHex("#0066CC")),
            ["secondary"] = Style.Default.Foreground(Color.FromHex("#888888")),
            ["success"]   = Style.Default.Foreground(Color.FromHex("#007700")),
            ["warning"]   = Style.Default.Foreground(Color.FromHex("#CC6600")),
            ["error"]     = Style.Default.Foreground(Color.FromHex("#CC0000")),
            ["muted"]     = Style.Default.Foreground(Color.FromHex("#999999")),
        });

    /// <summary>
    /// Dracula color scheme. A well-known dark theme with purple borders and
    /// cyan focus highlights.
    /// </summary>
    public static readonly Theme Dracula = new(
        name: "Dracula",
        baseStyle:    Style.Default.Foreground(Color.FromHex("#F8F8F2")).Background(Color.FromHex("#282A36")),
        borderStyle:  Style.Default.BorderForeground(Color.FromHex("#BD93F9")).Border(Borders.Rounded),
        focusedStyle: Style.Default.BorderForeground(Color.FromHex("#8BE9FD")).Bold(true),
        disabledStyle:Style.Default.Foreground(Color.FromHex("#6272A4")).Faint(true),
        named: new Dictionary<string, Style>
        {
            ["accent"]    = Style.Default.Foreground(Color.FromHex("#BD93F9")),
            ["secondary"] = Style.Default.Foreground(Color.FromHex("#6272A4")),
            ["success"]   = Style.Default.Foreground(Color.FromHex("#50FA7B")),
            ["warning"]   = Style.Default.Foreground(Color.FromHex("#FFB86C")),
            ["error"]     = Style.Default.Foreground(Color.FromHex("#FF5555")),
            ["muted"]     = Style.Default.Foreground(Color.FromHex("#6272A4")),
        });

    /// <summary>
    /// Nord color palette. A cool, arctic-blue dark theme with teal borders
    /// and soft green focus highlights.
    /// </summary>
    public static readonly Theme Nord = new(
        name: "Nord",
        baseStyle:    Style.Default.Foreground(Color.FromHex("#D8DEE9")).Background(Color.FromHex("#2E3440")),
        borderStyle:  Style.Default.BorderForeground(Color.FromHex("#88C0D0")).Border(Borders.Rounded),
        focusedStyle: Style.Default.BorderForeground(Color.FromHex("#A3BE8C")).Bold(true),
        disabledStyle:Style.Default.Foreground(Color.FromHex("#4C566A")).Faint(true),
        named: new Dictionary<string, Style>
        {
            ["accent"]    = Style.Default.Foreground(Color.FromHex("#88C0D0")),
            ["secondary"] = Style.Default.Foreground(Color.FromHex("#4C566A")),
            ["success"]   = Style.Default.Foreground(Color.FromHex("#A3BE8C")),
            ["warning"]   = Style.Default.Foreground(Color.FromHex("#EBCB8B")),
            ["error"]     = Style.Default.Foreground(Color.FromHex("#BF616A")),
            ["muted"]     = Style.Default.Foreground(Color.FromHex("#4C566A")),
        });

    /// <summary>
    /// Monokai color scheme. A warm dark theme with green borders and
    /// yellow focus highlights.
    /// </summary>
    public static readonly Theme Monokai = new(
        name: "Monokai",
        baseStyle:    Style.Default.Foreground(Color.FromHex("#F8F8F2")).Background(Color.FromHex("#272822")),
        borderStyle:  Style.Default.BorderForeground(Color.FromHex("#A6E22E")).Border(Borders.Rounded),
        focusedStyle: Style.Default.BorderForeground(Color.FromHex("#E6DB74")).Bold(true),
        disabledStyle:Style.Default.Foreground(Color.FromHex("#75715E")).Faint(true),
        named: new Dictionary<string, Style>
        {
            ["accent"]    = Style.Default.Foreground(Color.FromHex("#A6E22E")),
            ["secondary"] = Style.Default.Foreground(Color.FromHex("#75715E")),
            ["success"]   = Style.Default.Foreground(Color.FromHex("#A6E22E")),
            ["warning"]   = Style.Default.Foreground(Color.FromHex("#E6DB74")),
            ["error"]     = Style.Default.Foreground(Color.FromHex("#F92672")),
            ["muted"]     = Style.Default.Foreground(Color.FromHex("#75715E")),
        });

    /// <summary>
    /// Tokyo Night color scheme. A cool blue-purple dark theme with
    /// soft blue borders and warm orange focus highlights.
    /// </summary>
    public static readonly Theme TokyoNight = new(
        name: "Tokyo Night",
        baseStyle:    Style.Default.Foreground(Color.FromHex("#A9B1D6")).Background(Color.FromHex("#1A1B26")),
        borderStyle:  Style.Default.BorderForeground(Color.FromHex("#7AA2F7")).Border(Borders.Rounded),
        focusedStyle: Style.Default.BorderForeground(Color.FromHex("#FF9E64")).Bold(true),
        disabledStyle:Style.Default.Foreground(Color.FromHex("#565F89")).Faint(true),
        named: new Dictionary<string, Style>
        {
            ["accent"]    = Style.Default.Foreground(Color.FromHex("#7AA2F7")),
            ["secondary"] = Style.Default.Foreground(Color.FromHex("#565F89")),
            ["success"]   = Style.Default.Foreground(Color.FromHex("#9ECE6A")),
            ["warning"]   = Style.Default.Foreground(Color.FromHex("#E0AF68")),
            ["error"]     = Style.Default.Foreground(Color.FromHex("#F7768E")),
            ["muted"]     = Style.Default.Foreground(Color.FromHex("#565F89")),
        });

    /// <summary>
    /// All built-in themes in declaration order. Useful for cycling.
    /// Does not include <see cref="Default"/> (the no-color fallback).
    /// </summary>
    public static readonly IReadOnlyList<Theme> BuiltIn =
        [Dark, Light, Dracula, Nord, Monokai, TokyoNight];

    /// <summary>
    /// Convenience constructor matching quickstart named-argument usage:
    /// <c>new Theme(name: "Dark", baseStyle: ..., borderStyle: ..., focusedStyle: ...)</c>
    /// </summary>
    [SetsRequiredMembers]
    public Theme(
        string name,
        Style? baseStyle = null,
        Style? borderStyle = null,
        Style? focusedStyle = null,
        Style? disabledStyle = null,
        IReadOnlyDictionary<string, Style>? named = null)
    {
        Name = name;
        if (baseStyle is not null)     BaseStyle     = baseStyle.Value;
        if (borderStyle is not null)   BorderStyle   = borderStyle.Value;
        if (focusedStyle is not null)  FocusedStyle  = focusedStyle.Value;
        if (disabledStyle is not null) DisabledStyle = disabledStyle.Value;
        if (named is not null)         Named         = named;
    }

    /// <summary>Object-initializer constructor (required for <see cref="Default"/>).</summary>
    public Theme() { }

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
