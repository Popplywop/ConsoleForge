using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// An animated spinner widget. Cycles through a sequence of frames on each render.
/// Models should advance <see cref="Frame"/> via a <c>Cmd.Tick</c> or <c>Sub.Interval</c>.
/// </summary>
/// <remarks>
/// The spinner does not self-animate — it renders the frame at index
/// <c>Frame % Frames.Length</c>. Advance <see cref="Frame"/> in your model's
/// Update handler to produce motion.
/// </remarks>
public sealed class Spinner : IWidget
{
    /// <summary>Default braille dot spinner frames.</summary>
    public static readonly IReadOnlyList<string> BrailleFrames =
        ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    /// <summary>Classic ASCII spinner frames.</summary>
    public static readonly IReadOnlyList<string> AsciiFrames =
        ["-", "\\", "|", "/"];

    /// <summary>Arc spinner frames.</summary>
    public static readonly IReadOnlyList<string> ArcFrames =
        ["◜", "◠", "◝", "◞", "◡", "◟"];

    // ── IWidget ─────────────────────────────────────────────────────────────
    /// <summary>Horizontal size constraint. Defaults to <see cref="SizeConstraint.Auto"/> (sized to content).</summary>
    public SizeConstraint Width  { get; init; } = SizeConstraint.Auto;
    /// <summary>Vertical size constraint. Defaults to <see cref="SizeConstraint.Auto"/> (single row).</summary>
    public SizeConstraint Height { get; init; } = SizeConstraint.Auto;
    /// <summary>Base style applied to the spinner text. Inherits from the active theme when unset.</summary>
    public Style Style { get; init; } = Style.Default;

    // ── Spinner-specific ─────────────────────────────────────────────────────
    /// <summary>The animation frames to cycle through. Defaults to <see cref="BrailleFrames"/>.</summary>
    public IReadOnlyList<string> Frames { get; init; } = BrailleFrames;

    /// <summary>
    /// Current animation frame index. The spinner renders <c>Frames[Frame % Frames.Length]</c>.
    /// Increment this in your model to advance the animation.
    /// </summary>
    public int Frame { get; init; }

    /// <summary>
    /// Optional label displayed after the spinner frame (e.g. "Loading…").
    /// A single space is inserted between the frame and the label.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>Object-initializer constructor.</summary>
    public Spinner() { }

    /// <summary>Positional constructor for inline usage.</summary>
    public Spinner(int frame, string? label = null, IReadOnlyList<string>? frames = null, Style? style = null)
    {
        Frame = frame;
        Label = label;
        if (frames is not null) Frames = frames;
        if (style is not null) Style = style.Value;
    }

    // ── Render ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Renders the current animation frame (and optional label) into <paramref name="ctx"/>'s
    /// allocated region. Content is truncated to fit the available width.
    /// </summary>
    /// <param name="ctx">The render context providing the target region, theme, and write methods.</param>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0 || Frames.Count == 0) return;

        var effectiveStyle = Style.Inherit(ctx.Theme.BaseStyle);
        var frameText = Frames[((Frame % Frames.Count) + Frames.Count) % Frames.Count];
        var text = Label is null ? frameText : $"{frameText} {Label}";

        if (text.Length > region.Width) text = text[..region.Width];
        ctx.Write(region.Col, region.Row, text, effectiveStyle);
    }
}
