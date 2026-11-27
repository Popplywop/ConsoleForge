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
    /// <inheritdoc/>
    public SizeConstraint Width { get; init; } = SizeConstraint.Flex(1);
    /// <inheritdoc/>
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

        // Scroll sideways to keep the cursor in view. CursorPosition indexes UTF-16 units,
        // but the terminal cursor is placed in columns, so the offset is the visual width
        // of the text before it — a wide glyph or an emoji is one index step and two
        // columns. The text used to be cut at the box's width and the cursor pinned to its
        // edge, so typing past the width went on invisibly.
        int cursorCol = 0, scroll = 0;
        if (HasFocus && Value.Length > 0)
        {
            int pos = Math.Clamp(CursorPosition, 0, Value.Length);
            cursorCol = TextUtils.VisualWidth(Value.AsSpan(0, pos));
            scroll    = HorizontalScroll(cursorCol, textWidth);
        }

        var display = Value.Length > 0 ? Value : Placeholder;
        if (scroll == 0)
        {
            // Clip to region width (visual-width-aware for wide chars)
            ctx.Write(textCol, region.Row, TextUtils.TruncateToWidth(display, textWidth), effectiveStyle);
        }
        else
        {
            // Shifted left past the box's edge; the clip cuts it (a wide glyph split by
            // the edge is drawn as a blank) and stops it at the right.
            var textRegion = new Region(textCol, region.Row, textWidth, 1);
            new SubRenderContext(ctx, textRegion).Write(textCol - scroll, region.Row, display, effectiveStyle);
        }

        if (HasFocus)
            ctx.SetCursorDescriptor(new(true, textCol + cursorCol - scroll, region.Row));
    }

    /// <summary>
    /// Columns to scroll so a cursor at <paramref name="cursorCol"/> is inside a box
    /// <paramref name="width"/> columns wide: none while it fits, otherwise just enough to
    /// put it in the last column. A pure function of the two, so it needs no state — the
    /// text simply slides as the cursor passes the right edge.
    /// </summary>
    internal static int HorizontalScroll(int cursorCol, int width) =>
        width <= 0 ? 0 : Math.Max(0, cursorCol - (width - 1));
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