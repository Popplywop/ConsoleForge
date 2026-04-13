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

    public string Text { get; init; } = "";
    public Style Style { get; init; } = Style.Default;
    public SizeConstraint Width { get; init; } = SizeConstraint.Auto;
    public SizeConstraint Height { get; init; } = SizeConstraint.Auto;

    public void Render(IRenderContext ctx)
    {
        var effectiveStyle = Style.Inherit(ctx.Theme.BaseStyle);
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        var lines = WrapText(Text, region.Width);
        var maxRows = Math.Min(lines.Count, region.Height);
        for (var i = 0; i < maxRows; i++)
            ctx.Write(region.Col, region.Row + i, lines[i], effectiveStyle);
    }

    internal static List<string> WrapText(string text, int width)
    {
        if (width <= 0) return [];

        var result = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            if (rawLine.Length == 0)
            {
                result.Add("");
                continue;
            }
            var remaining = rawLine.AsSpan();
            while (remaining.Length > width)
            {
                result.Add(remaining[..width].ToString());
                remaining = remaining[width..];
            }
            result.Add(remaining.ToString());
        }
        return result;
    }
}
