using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A horizontal progress bar widget. Fills a region proportionally based on
/// <see cref="Value"/> relative to <see cref="Maximum"/>.
/// </summary>
public sealed class ProgressBar : IWidget
{
    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Fixed(1);
    public Style Style { get; init; } = Style.Default;

    // ── ProgressBar-specific ─────────────────────────────────────────────────
    /// <summary>Current progress value. Clamped to [0, <see cref="Maximum"/>].</summary>
    public double Value { get; init; }

    /// <summary>Maximum value. Must be &gt; 0. Default 100.</summary>
    public double Maximum { get; init; } = 100;

    /// <summary>Character used for the filled portion. Default '█'.</summary>
    public char FillChar { get; init; } = '█';

    /// <summary>Character used for the empty portion. Default '░'.</summary>
    public char EmptyChar { get; init; } = '░';

    /// <summary>Style applied to the filled portion. Inherits widget style when unset.</summary>
    public Style FillStyle { get; init; } = Style.Default;

    /// <summary>Style applied to the empty portion. Inherits widget style when unset.</summary>
    public Style EmptyStyle { get; init; } = Style.Default;

    /// <summary>
    /// When true, a percentage label (e.g. " 42%") is rendered at the right edge of the bar.
    /// </summary>
    public bool ShowPercent { get; init; } = true;

    /// <summary>Object-initializer constructor.</summary>
    public ProgressBar() { }

    /// <summary>Positional constructor for inline usage.</summary>
    public ProgressBar(double value, double maximum = 100, bool showPercent = true,
        char fillChar = '█', char emptyChar = '░', Style? style = null)
    {
        Value = value;
        Maximum = maximum;
        ShowPercent = showPercent;
        FillChar = fillChar;
        EmptyChar = emptyChar;
        if (style is not null) Style = style.Value;
    }

    // ── Render ───────────────────────────────────────────────────────────────
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        var effectiveStyle = Style.Inherit(ctx.Theme.BaseStyle);
        var effectiveFill  = FillStyle.Inherit(effectiveStyle);
        var effectiveEmpty = EmptyStyle.Inherit(effectiveStyle);

        double max   = Maximum > 0 ? Maximum : 1;
        double ratio = Math.Clamp(Value / max, 0.0, 1.0);

        var barWidth = region.Width;
        string? percentLabel = null;

        if (ShowPercent)
        {
            percentLabel = $" {(int)(ratio * 100),3}%";
            barWidth = Math.Max(1, barWidth - percentLabel.Length);
        }

        var fillCount  = (int)Math.Round(ratio * barWidth);
        var emptyCount = barWidth - fillCount;

        var col = region.Col;
        var row = region.Row;

        if (fillCount > 0)
        {
            ctx.Write(col, row, new string(FillChar, fillCount), effectiveFill);
            col += fillCount;
        }
        if (emptyCount > 0)
        {
            ctx.Write(col, row, new string(EmptyChar, emptyCount), effectiveEmpty);
            col += emptyCount;
        }
        if (percentLabel is not null)
        {
            ctx.Write(col, row, percentLabel, effectiveStyle);
        }
    }
}
