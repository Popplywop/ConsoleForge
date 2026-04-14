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
    /// <summary>
    /// Number of blank columns inserted to the left of each item's text.
    /// Provides breathing room when the list is placed inside a <see cref="BorderBox"/>.
    /// Defaults to <c>1</c>.
    /// </summary>
    public int                   PaddingLeft  { get; init; } = 1;
    /// <summary>
    /// Number of blank columns reserved to the right of each item's text.
    /// Defaults to <c>0</c>.
    /// </summary>
    public int                   PaddingRight { get; init; } = 0;

    /// <summary>Object-initializer constructor; all properties default.</summary>
    public List() { }

    /// <summary>
    /// Positional constructor for inline usage.
    /// </summary>
    /// <param name="items">Display strings to show.</param>
    /// <param name="selectedIndex">Initially highlighted row index (clamped).</param>
    /// <param name="style">Optional style for unselected rows.</param>
    /// <param name="selectedItemStyle">Optional style for the highlighted row.</param>
    /// <param name="paddingLeft">
    ///   Blank columns inserted to the left of each item's text.
    ///   Provides breathing room when the list is placed inside a <see cref="BorderBox"/>.
    ///   Defaults to <c>1</c>.
    /// </param>
    /// <param name="paddingRight">
    ///   Blank columns reserved to the right of each item's text.
    ///   Defaults to <c>0</c>.
    /// </param>
    public List(
        IReadOnlyList<string> items,
        int selectedIndex = 0,
        Style? style = null,
        Style? selectedItemStyle = null,
        int paddingLeft = 1,
        int paddingRight = 0)
    {
        Items = items;
        SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, items.Count - 1));
        if (style is not null) Style = style.Value;
        if (selectedItemStyle is not null) SelectedItemStyle = selectedItemStyle.Value;
        PaddingLeft  = paddingLeft;
        PaddingRight = paddingRight;
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

        var fill     = new string(' ', region.Width);
        var padLeft  = Math.Max(0, PaddingLeft);
        var padRight = Math.Max(0, PaddingRight);
        // Width available for item text after subtracting horizontal padding
        var textWidth = Math.Max(0, region.Width - padLeft - padRight);
        var leftPad  = new string(' ', padLeft);
        var rightPad = new string(' ', padRight);

        var maxRows = Math.Min(Items.Count, region.Height);
        for (var i = 0; i < maxRows; i++)
        {
            var rowStyle = i == SelectedIndex ? selectedStyle : baseStyle;

            // 1. Fill the entire row so the background colour covers edge-to-edge
            ctx.Write(region.Col, region.Row + i, fill, rowStyle);

            // 2. Write the padded text on top (truncated or space-padded to textWidth)
            if (textWidth > 0)
            {
                var text = Items[i];
                if (text.Length > textWidth)
                    text = text[..textWidth];
                else if (text.Length < textWidth)
                    text = text.PadRight(textWidth);

                ctx.Write(region.Col + padLeft, region.Row + i, text, rowStyle);
            }
        }

        // Fill rows below items with base style so background is uniform
        for (var i = maxRows; i < region.Height; i++)
            ctx.Write(region.Col, region.Row + i, fill, baseStyle);
    }
}

/// <summary>
/// Dispatched when the highlighted row in a <see cref="List"/> changes.
/// The model should replace its List reference with <c>list with { SelectedIndex = msg.NewIndex }</c>.
/// </summary>
public sealed record ListSelectionChangedMsg(List Source, int NewIndex) : IMsg;
