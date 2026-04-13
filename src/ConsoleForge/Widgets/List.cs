using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A scrollable list widget that displays items and highlights the selected one.
/// Dispatches <see cref="ListItemSelectedMsg"/> when the user presses Enter.
/// </summary>
public sealed class List : IFocusable
{
    // ── IFocusable ───────────────────────────────────────────────────────────
    public bool HasFocus { get; set; }

    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);

    // ── List-specific ────────────────────────────────────────────────────────
    /// <summary>The display strings shown in the list.</summary>
    public IReadOnlyList<string> Items         { get; init; } = [];
    /// <summary>Zero-based index of the currently highlighted item.</summary>
    public int                   SelectedIndex { get; init; }
    /// <summary>Visual style for unselected rows. Inherits theme base style when no properties set.</summary>
    public Style                 Style         { get; init; } = Style.Default;
    /// <summary>Visual style applied to the highlighted row. Defaults to reverse-video.</summary>
    public Style                 SelectedItemStyle { get; init; } = Style.Default.Reverse(true);

    /// <summary>Object-initializer constructor; all properties default.</summary>
    public List() { }

    /// <summary>
    /// Positional constructor for inline usage.
    /// </summary>
    /// <param name="items">Display strings to show.</param>
    /// <param name="selectedIndex">Initially highlighted row index (clamped).</param>
    /// <param name="style">Optional style for unselected rows.</param>
    /// <param name="selectedItemStyle">Optional style for the highlighted row.</param>
    public List(
        IReadOnlyList<string> items,
        int selectedIndex = 0,
        Style? style = null,
        Style? selectedItemStyle = null)
    {
        Items = items;
        SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, items.Count - 1));
        if (style is not null) Style = style.Value;
        if (selectedItemStyle is not null) SelectedItemStyle = selectedItemStyle.Value;
    }

    // ── Key handling ─────────────────────────────────────────────────────────
    public void OnKeyEvent(KeyMsg key, Action<IMsg> dispatch)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                dispatch(new ListSelectionChangedMsg(this, Math.Max(0, SelectedIndex - 1)));
                break;
            case ConsoleKey.DownArrow:
                dispatch(new ListSelectionChangedMsg(this, Math.Min(Items.Count - 1, SelectedIndex + 1)));
                break;
            case ConsoleKey.Enter when Items.Count > 0:
                dispatch(new ListItemSelectedMsg(SelectedIndex, Items[SelectedIndex]));
                break;
        }
    }

    // ── Render ───────────────────────────────────────────────────────────────
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        var baseStyle = Style.Inherit(ctx.Theme.BaseStyle);
        var selectedStyle = HasFocus
            ? SelectedItemStyle.Inherit(ctx.Theme.FocusedStyle)
            : SelectedItemStyle.Inherit(ctx.Theme.BaseStyle);

        var maxRows = Math.Min(Items.Count, region.Height);
        for (var i = 0; i < maxRows; i++)
        {
            var text = Items[i];
            if (text.Length > region.Width)
                text = text[..region.Width];

            var rowStyle = i == SelectedIndex ? selectedStyle : baseStyle;
            ctx.Write(region.Col, region.Row + i, text, rowStyle);
        }
    }
}

/// <summary>
/// Dispatched when the highlighted row in a <see cref="List"/> changes.
/// The model should replace its List reference with <c>list with { SelectedIndex = msg.NewIndex }</c>.
/// </summary>
public sealed record ListSelectionChangedMsg(List Source, int NewIndex) : IMsg;
