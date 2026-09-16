using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A single-line text input widget that accepts keyboard input when focused.
/// </summary>
public sealed record TextInput : IFocusable
{
    // ── IFocusable ───────────────────────────────────────────────────────────
    /// <inheritdoc/>
    public bool HasFocus { get; init; }

    /// <inheritdoc/>
    public string? FocusKey { get; init; }

    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Fixed(1);

    // ── TextInput-specific ───────────────────────────────────────────────────
    /// <summary>Current text value in the input field.</summary>
    public string Value { get; init; } = "";
    /// <summary>Placeholder text shown when <see cref="Value"/> is empty.</summary>
    public string Placeholder { get; init; } = "";
    /// <summary>Zero-based index of the cursor within <see cref="Value"/>.</summary>
    public int CursorPosition { get; init; }
    /// <summary>Visual style for the input text. Inherits theme base style when no properties are set.</summary>
    public Style Style { get; init; } = Style.Default;

    /// <summary>Object-initializer constructor; all properties default.</summary>
    public TextInput() { }

    /// <summary>
    /// Positional constructor for inline usage.
    /// </summary>
    /// <param name="value">Initial text value.</param>
    /// <param name="placeholder">Placeholder shown when value is empty.</param>
    /// <param name="cursorPosition">Initial cursor position (clamped to value length).</param>
    /// <param name="style">Optional visual style override.</param>
    public TextInput(
        string value = "",
        string placeholder = "",
        int cursorPosition = 0,
        Style? style = null)
    {
        Value = value;
        Placeholder = placeholder;
        CursorPosition = Math.Clamp(cursorPosition, 0, value.Length);
        if (style is not null) Style = style.Value;
    }

    /// <summary>
    /// Apply one key press to <see cref="Value"/> and <see cref="CursorPosition"/> and
    /// return the edited widget — this instance is never mutated, so the model must store
    /// what comes back.
    /// </summary>
    /// <remarks>
    /// The editing rules live in <see cref="TextInputState"/>; this only carries them
    /// across, so a model that keeps its own <see cref="TextInputState"/> and one that
    /// stores the widget behave identically.
    /// </remarks>
    public (IFocusable Next, ICmd? Cmd) Update(KeyMsg key)
    {
        var before = new TextInputState(Value, CursorPosition);
        var after = before.HandleKey(key);
        if (ReferenceEquals(after, before)) return (this, null);

        return (this with { Value = after.Value, CursorPosition = after.Cursor }, null);
    }

    // ── Render ───────────────────────────────────────────────────────────────
    /// <inheritdoc/>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        var effectiveStyle = Style.Inherit(ctx.Theme.BaseStyle);

        // Apply widget's own padding (not inherited — local property)
        int padL = Style.HasPadding ? Style.PaddingLeft : 0;
        int padR = Style.HasPadding ? Style.PaddingRight : 0;
        int textCol = region.Col + padL;
        int textWidth = Math.Max(0, region.Width - padL - padR);
        if (textWidth <= 0) return;

        var display = Value.Length > 0 ? Value : Placeholder;
        // Clip to region width (visual-width-aware for wide chars)
        display = TextUtils.TruncateToWidth(display, textWidth);

        ctx.Write(textCol, region.Row, display, effectiveStyle);

        // Draw cursor when focused. CursorPosition indexes UTF-16 units, but the terminal
        // cursor is placed in columns, so the offset is the visual width of the text
        // before it — a wide glyph or an emoji is one index step and two columns. It
        // measures Value, not display, which may be the placeholder or truncated.
        if (HasFocus)
        {
            int pos = Math.Clamp(CursorPosition, 0, Value.Length);
            int cursorCol = TextUtils.VisualWidth(Value.AsSpan(0, pos));
            ctx.SetCursorDescriptor(
                new(true, textCol + Math.Min(cursorCol, textWidth), region.Row));
        }
    }
}

/// <summary>
/// Was dispatched when a <see cref="TextInput"/> value or cursor position changed, back
/// when widgets emitted messages through a callback. Nothing raises it now —
/// <see cref="TextInput.Update"/> returns the edited widget directly.
/// </summary>
[Obsolete("Unused since widgets stopped emitting messages. TextInput.Update returns the next widget; store that instead.")]
public sealed record TextInputChangedMsg(
    TextInput Source,
    string NewValue,
    int NewCursorPosition) : IMsg;