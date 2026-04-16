namespace ConsoleForge.Styling;

/// <summary>
/// Extension methods for <see cref="Theme"/> that give convenient, typed access to
/// the colours and styles embedded in a theme's <see cref="Style"/> properties.
/// </summary>
/// <example>
/// <code>
/// var t = Theme.Dracula;
///
/// // Pull raw colours for building explicit widget styles
/// IColor? bg  = t.Bg();          // background from BaseStyle
/// IColor? fg  = t.Fg();          // foreground from BaseStyle
/// IColor? acc = t.Accent();      // border-fg from BorderStyle (the brand/accent colour)
/// IColor? foc = t.FocusColor();  // border-fg from FocusedStyle
///
/// // Ready-made styles for common UI roles
/// Style heading = t.AccentStyle().Bold(true);   // accent colour, bold
/// Style hint    = t.MutedStyle();               // dim secondary text
/// Style ok      = t.Success();                  // green / equivalent per theme
/// Style bad     = t.Error();                    // red / equivalent per theme
///
/// // Arbitrary named slot with fallback
/// Style custom = t.GetStyle("my-widget", Style.Default.Foreground(Color.White));
/// </code>
/// </example>
public static class ThemeExtensions
{
    // ── Raw colour extraction ─────────────────────────────────────────────────

    /// <summary>
    /// Background colour from <see cref="Theme.BaseStyle"/>,
    /// or <see langword="null"/> if the theme does not specify one.
    /// </summary>
    public static IColor? Bg(this Theme theme) => theme.BaseStyle.Bg;

    /// <summary>
    /// Foreground colour from <see cref="Theme.BaseStyle"/>,
    /// or <see langword="null"/> if the theme does not specify one.
    /// </summary>
    public static IColor? Fg(this Theme theme) => theme.BaseStyle.Fg;

    /// <summary>
    /// Accent colour — the border-foreground colour from <see cref="Theme.BorderStyle"/>.
    /// This is the theme's primary brand / highlight colour used for borders and headings.
    /// Returns <see langword="null"/> if the theme does not specify one.
    /// </summary>
    public static IColor? Accent(this Theme theme) => theme.BorderStyle.BorderColor;

    /// <summary>
    /// Focus-ring colour — the border-foreground colour from <see cref="Theme.FocusedStyle"/>.
    /// Returns <see langword="null"/> if the theme does not specify one.
    /// </summary>
    public static IColor? FocusColor(this Theme theme) => theme.FocusedStyle.BorderColor;

    // ── Ready-made styles ─────────────────────────────────────────────────────

    /// <summary>
    /// Accent style suitable for headings and highlights.
    /// Returns <c>Named["accent"]</c> if present; otherwise constructs a style
    /// with the <see cref="Accent"/> colour as foreground.
    /// Falls back to <see cref="Style.Default"/> when the theme has no accent.
    /// </summary>
    public static Style AccentStyle(this Theme theme)
    {
        if (theme.Named.TryGetValue("accent", out var named)) return named;
        var c = theme.Accent();
        return c is not null ? Style.Default.Foreground(c) : Style.Default;
    }

    /// <summary>
    /// Muted / secondary-text style for hints, descriptions, and status text.
    /// Returns <c>Named["muted"]</c> if present; otherwise falls back to
    /// <see cref="Theme.DisabledStyle"/>.
    /// </summary>
    public static Style MutedStyle(this Theme theme) =>
        theme.Named.TryGetValue("muted", out var s) ? s : theme.DisabledStyle;

    /// <summary>
    /// Secondary style — slightly less prominent than muted.
    /// Returns <c>Named["secondary"]</c> if present; otherwise falls back to
    /// <see cref="MutedStyle"/>.
    /// </summary>
    public static Style SecondaryStyle(this Theme theme) =>
        theme.Named.TryGetValue("secondary", out var s) ? s : theme.MutedStyle();

    /// <summary>
    /// Success / positive-state style (green or theme equivalent).
    /// Returns <c>Named["success"]</c> if present; otherwise <see cref="Style.Default"/>.
    /// </summary>
    public static Style Success(this Theme theme) =>
        theme.Named.TryGetValue("success", out var s) ? s : Style.Default;

    /// <summary>
    /// Warning / caution style (yellow/orange or theme equivalent).
    /// Returns <c>Named["warning"]</c> if present; otherwise <see cref="Style.Default"/>.
    /// </summary>
    public static Style Warning(this Theme theme) =>
        theme.Named.TryGetValue("warning", out var s) ? s : Style.Default;

    /// <summary>
    /// Error / danger style (red or theme equivalent).
    /// Returns <c>Named["error"]</c> if present; otherwise <see cref="Style.Default"/>.
    /// </summary>
    public static Style Error(this Theme theme) =>
        theme.Named.TryGetValue("error", out var s) ? s : Style.Default;

    /// <summary>
    /// Retrieve an arbitrary named style slot with a fallback.
    /// </summary>
    /// <param name="theme">The theme to query.</param>
    /// <param name="key">Named style key (e.g. <c>"muted"</c>, <c>"accent"</c>).</param>
    /// <param name="fallback">Style to return when the key is absent. Defaults to <see cref="Style.Default"/>.</param>
    public static Style GetStyle(this Theme theme, string key, Style fallback = default) =>
        theme.Named.TryGetValue(key, out var s) ? s : fallback;

    // ── Background fill helper ────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="Style"/> that applies the theme's background colour.
    /// When the theme has no background, returns <see cref="Style.Default"/>.
    /// Useful for setting a full-terminal background on the root container.
    /// </summary>
    public static Style BgStyle(this Theme theme)
    {
        var c = theme.Bg();
        return c is not null ? Style.Default.Background(c) : Style.Default;
    }

    /// <summary>
    /// Returns a <see cref="Style"/> combining both the theme's foreground and
    /// background colours. All widgets placed inside a container that uses this
    /// style will inherit the full base palette.
    /// </summary>
    public static Style BaseColorStyle(this Theme theme)
    {
        var s  = Style.Default;
        var fg = theme.Fg();
        var bg = theme.Bg();
        if (fg is not null) s = s.Foreground(fg);
        if (bg is not null) s = s.Background(bg);
        return s;
    }
}
