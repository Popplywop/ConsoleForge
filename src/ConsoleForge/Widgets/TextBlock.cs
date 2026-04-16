using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A widget that renders a single string, wrapping at region width.
/// Inherits style from the active theme's BaseStyle when widget style has no properties set.
/// </summary>
public sealed class TextBlock : IWidget
{
    /// <summary>Positional constructor for inline usage.</summary>
    public TextBlock(string text, Style? style = null)
    {
        Text = text;
        if (style is not null) Style = style.Value;
    }

    /// <summary>Object-initializer constructor.</summary>
    public TextBlock() { }

    /// <summary>The text content to display, wrapped at the region width.</summary>
    public string Text { get; init; } = "";
    /// <summary>Visual style applied to the rendered text. Inherits <see cref="Theme.BaseStyle"/> when no properties are set.</summary>
    public Style Style { get; init; } = Style.Default;
    public SizeConstraint Width { get; init; } = SizeConstraint.Auto;
    public SizeConstraint Height { get; init; } = SizeConstraint.Auto;

    /// <inheritdoc/>
    public void Render(IRenderContext ctx)
    {
        var effectiveStyle = Style.Inherit(ctx.Theme.BaseStyle);
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        // Apply widget's own padding (not inherited — padding is a local property)
        int padT = Style.HasPadding ? Style.PaddingTop    : 0;
        int padR = Style.HasPadding ? Style.PaddingRight  : 0;
        int padB = Style.HasPadding ? Style.PaddingBottom : 0;
        int padL = Style.HasPadding ? Style.PaddingLeft   : 0;

        int textCol   = region.Col  + padL;
        int textRow   = region.Row  + padT;
        int textWidth = Math.Max(0, region.Width  - padL - padR);
        int textHeight= Math.Max(0, region.Height - padT - padB);
        if (textWidth <= 0 || textHeight <= 0) return;

        var lines = WrapText(Text, textWidth);
        var maxRows = Math.Min(lines.Count, textHeight);
        for (var i = 0; i < maxRows; i++)
            ctx.Write(textCol, textRow + i, lines[i], effectiveStyle);
    }

    internal static List<string> WrapText(string text, int width) =>
        TextUtils.WrapToWidth(text, width);
}