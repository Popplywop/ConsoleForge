using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A single-line text input widget that accepts keyboard input when focused.
/// </summary>
public sealed class TextInput : IFocusable
{
    // ── IFocusable ───────────────────────────────────────────────────────────
    public bool HasFocus { get; set; }

    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Fixed(1);

    // ── TextInput-specific ───────────────────────────────────────────────────
    public string Value          { get; init; } = "";
    public string Placeholder    { get; init; } = "";
    public int    CursorPosition { get; init; }
    public Style  Style          { get; init; } = Style.Default;

    public TextInput() { }

    public TextInput(
        string value = "",
        string placeholder = "",
        int cursorPosition = 0,
        Style? style = null)
    {
        Value          = value;
        Placeholder    = placeholder;
        CursorPosition = Math.Clamp(cursorPosition, 0, value.Length);
        if (style is not null) Style = style.Value;
    }

    /// <summary>
    /// Handle printable char input, Backspace, and Left/Right cursor movement.
    /// NOTE: <see cref="TextInput"/> is immutable — this method mutates state via
    /// <see cref="HasFocus"/> but keyboard changes are returned as a model message.
    /// Callers must update the model's reference to this widget.
    /// </summary>
    public void OnKeyEvent(KeyMsg key, Action<IMsg> dispatch)
    {
        // Mutate via replacement — callers should replace this instance
        // by dispatching a TextInputChangedMsg carrying the new value/cursor.
        switch (key.Key)
        {
            case ConsoleKey.Backspace when Value.Length > 0 && CursorPosition > 0:
            {
                var newValue = Value[..(CursorPosition - 1)] + Value[CursorPosition..];
                dispatch(new TextInputChangedMsg(this, newValue, CursorPosition - 1));
                break;
            }
            case ConsoleKey.Delete when CursorPosition < Value.Length:
            {
                var newValue = Value[..CursorPosition] + Value[(CursorPosition + 1)..];
                dispatch(new TextInputChangedMsg(this, newValue, CursorPosition));
                break;
            }
            case ConsoleKey.LeftArrow:
                dispatch(new TextInputChangedMsg(this, Value, Math.Max(0, CursorPosition - 1)));
                break;
            case ConsoleKey.RightArrow:
                dispatch(new TextInputChangedMsg(this, Value, Math.Min(Value.Length, CursorPosition + 1)));
                break;
            default:
            {
                // Printable characters
                if (key.Character is char c && !char.IsControl(c))
                {
                    var newValue = Value[..CursorPosition] + c + Value[CursorPosition..];
                    dispatch(new TextInputChangedMsg(this, newValue, CursorPosition + 1));
                }
                break;
            }
        }
    }

    // ── Render ───────────────────────────────────────────────────────────────
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        var effectiveStyle = Style.Inherit(ctx.Theme.BaseStyle);

        var display = Value.Length > 0 ? Value : Placeholder;
        // Clip to region width
        if (display.Length > region.Width)
            display = display[..region.Width];

        ctx.Write(region.Col, region.Row, display, effectiveStyle);

        // Draw cursor when focused
        if (HasFocus)
        {
            int cursorCol = region.Col + Math.Min(CursorPosition, region.Width - 1);
            var cursorChar = CursorPosition < display.Length
                ? display[CursorPosition].ToString()
                : " ";
            var cursorStyle = effectiveStyle.Reverse(true);
            ctx.Write(cursorCol, region.Row, cursorChar, cursorStyle);
        }
    }
}

/// <summary>
/// Dispatched when a <see cref="TextInput"/> value or cursor position changes.
/// The model should replace its reference to the input with a new instance
/// having <see cref="NewValue"/> and <see cref="NewCursorPosition"/>.
/// </summary>
public sealed record TextInputChangedMsg(
    TextInput Source,
    string NewValue,
    int NewCursorPosition) : IMsg;
