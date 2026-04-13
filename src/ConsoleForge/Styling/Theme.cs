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
