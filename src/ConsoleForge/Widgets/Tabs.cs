using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A tabbed navigation widget. Renders a tab bar on the first row, and the
/// <see cref="Body"/> widget in the remaining area.
/// <para>
/// The caller is responsible for supplying the correct <see cref="Body"/> for the
/// <see cref="ActiveIndex"/> (swap it in the model's Update handler when
/// <see cref="TabChangedMsg"/> arrives).
/// </para>
/// </summary>
/// <remarks>
/// Key handling when focused:
/// <list type="bullet">
/// <item>Left/Right arrows — cycle tabs, wrap-around.</item>
/// <item>Number keys 1–9 — jump directly to tab N-1.</item>
/// </list>
/// Tab switching does <em>not</em> move keyboard focus to the body — the model
/// controls focus assignment independently.
/// </remarks>
public sealed class Tabs : IFocusable
{
    // ── IFocusable ───────────────────────────────────────────────────────────
    /// <inheritdoc/>
    public bool HasFocus { get; set; }

    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);

    // ── Tabs-specific ─────────────────────────────────────────────────────────
    /// <summary>Visual style for the tab bar row. Inherits theme base style when unset.</summary>
    public Style Style { get; init; } = Style.Default;

    /// <summary>Style applied to the active tab label.</summary>
    public Style ActiveTabStyle { get; init; } = Style.Default.Bold(true).Underline(true);

    /// <summary>Style applied to inactive tab labels.</summary>
    public Style InactiveTabStyle { get; init; } = Style.Default;

    /// <summary>Tab label strings in declaration order.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>Zero-based index of the currently active tab.</summary>
    public int ActiveIndex { get; init; }

    /// <summary>
    /// Content widget to render below the tab bar.
    /// Typically the widget associated with <see cref="ActiveIndex"/>.
    /// May be null (only the tab bar is drawn).
    /// </summary>
    public IWidget? Body { get; init; }

    /// <summary>
    /// Character used to separate tab labels in the bar. Default <c>'│'</c>.
    /// Set to <c>'\0'</c> to disable separators.
    /// </summary>
    public char Separator { get; init; } = '│';

    /// <summary>Object-initializer constructor; all properties default.</summary>
    public Tabs() { }

    /// <summary>Positional constructor for inline usage.</summary>
    /// <param name="labels">Tab label strings.</param>
    /// <param name="activeIndex">Initially active tab (clamped to valid range).</param>
    /// <param name="body">Content widget for the active tab.</param>
    /// <param name="style">Optional tab bar style override.</param>
    /// <param name="activeTabStyle">Optional style for the active tab label.</param>
    /// <param name="inactiveTabStyle">Optional style for inactive tab labels.</param>
    public Tabs(
        IReadOnlyList<string> labels,
        int activeIndex = 0,
        IWidget? body = null,
        Style? style = null,
        Style? activeTabStyle = null,
        Style? inactiveTabStyle = null)
    {
        Labels         = labels;
        ActiveIndex    = labels.Count > 0 ? Math.Clamp(activeIndex, 0, labels.Count - 1) : 0;
        Body           = body;
        if (style           is not null) Style           = style.Value;
        if (activeTabStyle   is not null) ActiveTabStyle  = activeTabStyle.Value;
        if (inactiveTabStyle is not null) InactiveTabStyle = inactiveTabStyle.Value;
    }

    // ── Key handling ─────────────────────────────────────────────────────────

    /// <summary>
    /// Left/Right arrows cycle tabs. Number keys 1–9 jump to a specific tab.
    /// </summary>
    public void OnKeyEvent(KeyMsg key, Action<IMsg> dispatch)
    {
        if (Labels.Count == 0) return;

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            {
                var next = ActiveIndex <= 0 ? Labels.Count - 1 : ActiveIndex - 1;
                dispatch(new TabChangedMsg(this, next));
                break;
            }
            case ConsoleKey.RightArrow:
            {
                var next = (ActiveIndex + 1) % Labels.Count;
                dispatch(new TabChangedMsg(this, next));
                break;
            }
            default:
            {
                // Number keys 1–9 jump directly
                if (key.Character is >= '1' and <= '9')
                {
                    int idx = key.Character.Value - '1';
                    if (idx < Labels.Count)
                        dispatch(new TabChangedMsg(this, idx));
                }
                break;
            }
        }
    }

    // ── Render ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the tab bar on row 0 of the allocated region, then delegates
    /// <see cref="Body"/> into the remaining rows.
    /// </summary>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        var barStyle = Style.Inherit(ctx.Theme.BaseStyle);

        // ── Tab bar row ───────────────────────────────────────────────────────
        // Fill entire bar row with base style first
        ctx.Write(region.Col, region.Row, new string(' ', region.Width), barStyle);

        int col = region.Col;
        for (var i = 0; i < Labels.Count; i++)
        {
            if (col >= region.Col + region.Width) break;

            // Separator between tabs
            if (i > 0 && Separator != '\0')
            {
                ctx.Write(col, region.Row, Separator.ToString(), barStyle);
                col++;
                if (col >= region.Col + region.Width) break;
            }

            var isActive  = i == ActiveIndex;
            var tabStyle  = isActive
                ? ActiveTabStyle.Inherit(HasFocus ? ctx.Theme.FocusedStyle : ctx.Theme.BaseStyle)
                : InactiveTabStyle.Inherit(ctx.Theme.BaseStyle);

            var label    = $" {Labels[i]} ";
            var maxChars = region.Col + region.Width - col;
            label = TextUtils.TruncateToWidth(label, maxChars);

            ctx.Write(col, region.Row, label, tabStyle);
            col += TextUtils.VisualWidth(label);
        }

        // ── Body area ─────────────────────────────────────────────────────────
        if (Body is not null && region.Height > 1)
        {
            var bodyRegion = new Region(
                region.Col,
                region.Row + 1,
                region.Width,
                region.Height - 1);

            var sub = new SubRenderContext(ctx, bodyRegion);
            Body.Render(sub);
        }
    }
}

/// <summary>
/// Dispatched when the user navigates to a different tab.
/// The model should replace its Tabs reference with
/// <c>tabs with { ActiveIndex = msg.NewIndex, Body = ... }</c>.
/// </summary>
public sealed record TabChangedMsg(Tabs Source, int NewIndex) : IMsg;
