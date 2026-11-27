using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A one-line summary of key bindings — <c>q quit · esc back · ? help</c> — rendered from
/// the same <see cref="KeyBinding"/> values the application's <see cref="KeyMap"/> handles,
/// so the help cannot list a key the map does not, or miss one it does.
/// </summary>
/// <remarks>
/// <para>
/// Disabled bindings are skipped. Bindings that do not fit the width are dropped from the
/// end and replaced by <see cref="Ellipsis"/>, so the most important ones go first.
/// </para>
/// <code>
/// public IWidget View() => new Container(Axis.Vertical, [
///     body,
///     new HelpBar(Keys.Bindings),   // Keys: the KeyMap Update handles input with
/// ]);
/// </code>
/// <para>
/// Keys render in the widget's style, descriptions and separators in the theme's
/// <see cref="Theme.DisabledStyle"/> (faint by default); override either with
/// <see cref="KeyStyle"/> and <see cref="DescriptionStyle"/>.
/// </para>
/// </remarks>
public sealed record HelpBar : IWidget, IMeasurable
{
    // ── IWidget ─────────────────────────────────────────────────────────────

    /// <summary>Horizontal size constraint. Defaults to <see cref="SizeConstraint.Flex(int)"/> weight 1.</summary>
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);

    /// <summary>Vertical size constraint. Defaults to <see cref="SizeConstraint.Fixed(int)"/> 1 (one row).</summary>
    public SizeConstraint Height { get; init; } = SizeConstraint.Fixed(1);

    /// <summary>Base style. Inherits from the theme's <see cref="Theme.BaseStyle"/> when unset.</summary>
    public Style Style { get; init; } = Style.Default;

    // ── HelpBar-specific ─────────────────────────────────────────────────────

    /// <summary>The bindings to list, in order. Disabled ones are skipped.</summary>
    public IReadOnlyList<KeyBinding> Bindings { get; init; } = [];

    /// <summary>Text between entries. Default <c>" · "</c>.</summary>
    public string Separator { get; init; } = " · ";

    /// <summary>Shown in place of the entries that do not fit. Default <c>"…"</c>.</summary>
    public string Ellipsis { get; init; } = "…";

    /// <summary>Style of each <see cref="KeyBinding.HelpKey"/>. Inherits <see cref="Style"/> when unset.</summary>
    public Style KeyStyle { get; init; } = Style.Default;

    /// <summary>
    /// Style of descriptions, separators and the ellipsis. Inherits the theme's
    /// <see cref="Theme.DisabledStyle"/> when unset.
    /// </summary>
    public Style DescriptionStyle { get; init; } = Style.Default;

    /// <summary>Object-initializer constructor.</summary>
    public HelpBar() { }

    /// <summary>Positional constructor.</summary>
    /// <param name="bindings">The bindings to list, in order — typically <see cref="KeyMap.Bindings"/>.</param>
    public HelpBar(IReadOnlyList<KeyBinding> bindings) => Bindings = bindings;

    /// <summary>Positional constructor.</summary>
    /// <param name="bindings">The bindings to list, in order.</param>
    public HelpBar(params KeyBinding[] bindings) => Bindings = bindings;

    // ── Layout ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The width of the full line, capped at <paramref name="availableWidth"/>, and one row —
    /// or nothing at all when no binding is enabled.
    /// </summary>
    public Size Measure(int availableWidth, int availableHeight)
    {
        int width = 0;
        bool first = true;
        foreach (var b in Bindings)
        {
            if (!b.Enabled) continue;
            width += EntryWidth(b, first);
            first = false;
        }

        return first
            ? new Size(0, 0)
            : new Size(Math.Min(width, Math.Max(availableWidth, 0)), Math.Min(1, Math.Max(availableHeight, 0)));
    }

    // ── Render ───────────────────────────────────────────────────────────────

    /// <summary>Renders as many entries as fit on the region's first row.</summary>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        var baseStyle = Style.Inherit(ctx.Theme.BaseStyle);
        var keyStyle  = KeyStyle.Inherit(baseStyle);
        var descStyle = DescriptionStyle.Inherit(ctx.Theme.DisabledStyle.Inherit(baseStyle));

        int col = region.Col, row = region.Row, end = region.Col + region.Width;
        bool first = true;

        foreach (var b in Bindings)
        {
            if (!b.Enabled) continue;

            if (col + EntryWidth(b, first) > end)
            {
                // What does not fit is summarised rather than cut mid-word.
                string tail = first ? Ellipsis : " " + Ellipsis;
                if (col + TextUtils.VisualWidth(tail) <= end)
                    ctx.Write(col, row, tail, descStyle);
                return;
            }

            if (!first) col = Put(ctx, col, row, Separator, descStyle);
            col = Put(ctx, col, row, b.HelpKey, keyStyle);
            if (b.HelpDescription.Length > 0)
                col = Put(ctx, col, row, " " + b.HelpDescription, descStyle);
            first = false;
        }
    }

    private int EntryWidth(KeyBinding b, bool first) =>
        (first ? 0 : TextUtils.VisualWidth(Separator))
        + TextUtils.VisualWidth(b.HelpKey)
        + (b.HelpDescription.Length > 0 ? 1 + TextUtils.VisualWidth(b.HelpDescription) : 0);

    private static int Put(IRenderContext ctx, int col, int row, string text, Style style)
    {
        if (text.Length == 0) return col;
        ctx.Write(col, row, text, style);
        return col + TextUtils.VisualWidth(text);
    }
}
