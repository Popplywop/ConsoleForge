using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A single toggleable checkbox widget.
/// Renders as <c>[✓] Label</c> or <c>[ ] Label</c>.
/// Dispatches <see cref="CheckboxToggledMsg"/> when the user presses Space or Enter.
/// </summary>
public sealed class Checkbox : IFocusable
{
    // ── IFocusable ───────────────────────────────────────────────────────────
    /// <inheritdoc/>
    public bool HasFocus { get; set; }

    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Fixed(1);

    // ── Checkbox-specific ────────────────────────────────────────────────────
    /// <summary>Visual style for the widget. Inherits theme base style when unset.</summary>
    public Style Style { get; init; } = Style.Default;

    /// <summary>Text label displayed after the checkbox indicator.</summary>
    public string Label { get; init; } = "";

    /// <summary>Whether the checkbox is currently checked.</summary>
    public bool IsChecked { get; init; }

    /// <summary>Character rendered inside the brackets when checked. Default <c>'✓'</c>.</summary>
    public char CheckedChar { get; init; } = '✓';

    /// <summary>Character rendered inside the brackets when unchecked. Default <c>' '</c>.</summary>
    public char UncheckedChar { get; init; } = ' ';

    /// <summary>Object-initializer constructor; all properties default.</summary>
    public Checkbox() { }

    /// <summary>Positional constructor for inline usage.</summary>
    /// <param name="label">Text displayed next to the checkbox.</param>
    /// <param name="isChecked">Initial checked state.</param>
    /// <param name="checkedChar">Indicator character when checked.</param>
    /// <param name="uncheckedChar">Indicator character when unchecked.</param>
    /// <param name="style">Optional visual style override.</param>
    public Checkbox(
        string label = "",
        bool isChecked = false,
        char checkedChar = '✓',
        char uncheckedChar = ' ',
        Style? style = null)
    {
        Label         = label;
        IsChecked     = isChecked;
        CheckedChar   = checkedChar;
        UncheckedChar = uncheckedChar;
        if (style is not null) Style = style.Value;
    }

    // ── Key handling ─────────────────────────────────────────────────────────

    /// <summary>
    /// Toggle the checkbox state when Space or Enter is pressed.
    /// </summary>
    public void OnKeyEvent(KeyMsg key, Action<IMsg> dispatch)
    {
        if (key.Key is ConsoleKey.Spacebar or ConsoleKey.Enter)
            dispatch(new CheckboxToggledMsg(this, !IsChecked));
    }

    // ── Render ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the checkbox as <c>[✓] Label</c> or <c>[ ] Label</c>.
    /// The indicator brackets and label are styled together.
    /// When focused, the active theme's <see cref="Theme.FocusedStyle"/> is blended in.
    /// </summary>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        var baseStyle = Style.Inherit(HasFocus ? ctx.Theme.FocusedStyle : ctx.Theme.BaseStyle);

        var indicator = IsChecked ? CheckedChar : UncheckedChar;
        // Format: "[X] Label" — 4 chars for "[X] " then label
        var text = $"[{indicator}] {Label}";
        text = TextUtils.TruncateToWidth(text, region.Width);
        var textVisualWidth = TextUtils.VisualWidth(text);

        ctx.Write(region.Col, region.Row, text, baseStyle);

        // Pad remainder of row with spaces so background fills edge-to-edge
        if (textVisualWidth < region.Width)
        {
            var pad = new string(' ', region.Width - textVisualWidth);
            ctx.Write(region.Col + textVisualWidth, region.Row, pad, baseStyle);
        }
    }
}

/// <summary>
/// Dispatched when a <see cref="Checkbox"/> is toggled by the user.
/// The model should replace its Checkbox reference with
/// <c>checkbox with { IsChecked = msg.NewValue }</c>.
/// </summary>
public sealed record CheckboxToggledMsg(Checkbox Source, bool NewValue) : IMsg;
