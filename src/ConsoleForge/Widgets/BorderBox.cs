using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A widget that renders a bordered box with an optional title and a body child widget.
/// Default border style is <see cref="Borders.Normal"/> unless overridden by the widget
/// style or theme.
/// </summary>
public sealed class BorderBox : IWidget
{
    /// <summary>Positional/named constructor for inline usage.</summary>
    public BorderBox(string title = "", IWidget? body = null, Style? style = null)
    {
        Title = title;
        Body = body;
        if (style is not null) Style = style.Value;
    }

    /// <summary>Optional title text rendered in the top border edge.</summary>
    public string Title { get; init; } = "";
    /// <summary>Optional child widget rendered inside the border, in the inner region.</summary>
    public IWidget? Body { get; init; }
    public Style Style { get; init; } = Style.Default.Border(Borders.Normal);
    public SizeConstraint Width { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);

    public void Render(IRenderContext ctx)
    {
        var effectiveStyle = Style.Inherit(ctx.Theme.BorderStyle);
        var region = ctx.Region;
        if (region.Width < 2 || region.Height < 2) return;

        var border = effectiveStyle.HasBorder ? effectiveStyle.BorderChars : Borders.Normal;
        var borderStyle = Style.Default;
        if (effectiveStyle.BorderFg is { } bfg)
            borderStyle = borderStyle.Foreground(bfg);
        if (effectiveStyle.BorderBg is { } bbg)
            borderStyle = borderStyle.Background(bbg);

        DrawBorder(ctx, region, border, borderStyle);

        // Render title in top edge if provided
        if (!string.IsNullOrEmpty(Title))
            RenderTitle(ctx, region, border, borderStyle);

        // Delegate body render to a sub-region inside the border
        if (Body is not null)
        {
            var innerRegion = new Region(
                region.Col + 1,
                region.Row + 1,
                Math.Max(0, region.Width - 2),
                Math.Max(0, region.Height - 2));

            var innerCtx = new SubRenderContext(ctx, innerRegion);
            Body.Render(innerCtx);
        }
    }

    private static void DrawBorder(IRenderContext ctx, Region r, BorderSpec b, Style s)
    {
        int right = r.Col + r.Width - 1;
        int bottom = r.Row + r.Height - 1;

        // Corners
        ctx.Write(r.Col, r.Row, b.TopLeft, s);
        ctx.Write(right, r.Row, b.TopRight, s);
        ctx.Write(r.Col, bottom, b.BottomLeft, s);
        ctx.Write(right, bottom, b.BottomRight, s);

        // Top and bottom edges
        for (var c = r.Col + 1; c < right; c++)
        {
            ctx.Write(c, r.Row, b.Top, s);
            ctx.Write(c, bottom, b.Bottom, s);
        }

        // Left and right edges
        for (var row = r.Row + 1; row < bottom; row++)
        {
            ctx.Write(r.Col, row, b.Left, s);
            ctx.Write(right, row, b.Right, s);
        }
    }

    private void RenderTitle(IRenderContext ctx, Region r, BorderSpec b, Style borderStyle)
    {
        // Title fits between corners: available = width - 2 (corners) - 2 (spaces)
        var available = r.Width - 4;
        if (available <= 0) return;

        var text = Title.Length > available ? Title[..available] : Title;
        // Position title starting at col+2 (corner + space)
        ctx.Write(r.Col + 2, r.Row, text, borderStyle);
    }
}
