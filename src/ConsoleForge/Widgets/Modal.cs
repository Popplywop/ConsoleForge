using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A centered dialog overlay widget. Renders a bordered box on top of whatever
/// is already in the cell buffer (paint-over, no viewport capture).
/// <para>
/// Compose with <see cref="ZStack"/> to show a modal on top of existing content:
/// <code>
/// new ZStack([
///     mainLayout,
///     isOpen ? new Modal("Confirm", body: confirmBody) : new TextBlock(""),
/// ])
/// </code>
/// </para>
/// </summary>
/// <remarks>
/// <b>Focus</b> — <see cref="FocusManager"/> traverses into <see cref="Body"/>
/// automatically (Modal implements <see cref="ISingleBodyWidget"/>). The model is responsible
/// for routing keyboard input to the modal when it is open.
/// <para>
/// <b>Backdrop</b> — When <see cref="ShowBackdrop"/> is true, the entire region is filled with
/// <see cref="BackdropStyle"/> spaces before drawing the dialog box. This replaces background
/// content with a dark overlay. When false (default), content behind the modal remains visible.
/// </para>
/// </remarks>
public sealed class Modal : IWidget, ISingleBodyWidget
{
    // ── IWidget ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Width constraint of the <em>outer</em> region (not the dialog box).
    /// Defaults to <see cref="SizeConstraint.Flex"/> so the widget fills the terminal.
    /// </summary>
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    /// <summary>
    /// Height constraint of the <em>outer</em> region (not the dialog box).
    /// Defaults to <see cref="SizeConstraint.Flex"/> so the widget fills the terminal.
    /// </summary>
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);

    // ── Modal-specific ───────────────────────────────────────────────────────
    /// <summary>Visual style for the dialog border and title. Inherits theme border style when unset.</summary>
    public Style Style { get; init; } = Style.Default.Border(Borders.Rounded);

    /// <summary>Title text rendered in the dialog's top border edge.</summary>
    public string Title { get; init; } = "";

    /// <summary>Content widget rendered inside the dialog box.</summary>
    public IWidget? Body { get; init; }

    /// <summary>
    /// Width of the dialog box in columns. Clamped to the available terminal width.
    /// Default 60.
    /// </summary>
    public int DialogWidth { get; init; } = 60;

    /// <summary>
    /// Height of the dialog box in rows. Clamped to the available terminal height.
    /// Default 16.
    /// </summary>
    public int DialogHeight { get; init; } = 16;

    /// <summary>
    /// When true, fills the entire region with <see cref="BackdropStyle"/> spaces before
    /// rendering the dialog box, creating a dark-overlay effect.
    /// Default false (background content remains visible).
    /// </summary>
    public bool ShowBackdrop { get; init; } = false;

    /// <summary>
    /// Style used for the backdrop fill when <see cref="ShowBackdrop"/> is true.
    /// Defaults to a dark background with faint text.
    /// </summary>
    public Style BackdropStyle { get; init; } =
        Style.Default.Background(Color.FromRgb(20, 20, 20)).Faint(true);

    /// <summary>Object-initializer constructor; all properties default.</summary>
    public Modal() { }

    /// <summary>Positional constructor for inline usage.</summary>
    /// <param name="title">Dialog title in the top border.</param>
    /// <param name="body">Content widget inside the dialog.</param>
    /// <param name="dialogWidth">Dialog box width (columns). Default 60.</param>
    /// <param name="dialogHeight">Dialog box height (rows). Default 16.</param>
    /// <param name="showBackdrop">Fill background with <see cref="BackdropStyle"/> before drawing.</param>
    /// <param name="style">Optional border/title style override.</param>
    public Modal(
        string title = "",
        IWidget? body = null,
        int dialogWidth = 60,
        int dialogHeight = 16,
        bool showBackdrop = false,
        Style? style = null)
    {
        Title        = title;
        Body         = body;
        DialogWidth  = dialogWidth;
        DialogHeight = dialogHeight;
        ShowBackdrop = showBackdrop;
        if (style is not null) Style = style.Value;
    }

    // ── ISingleBodyWidget ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the region allocated to <see cref="Body"/> within the centered dialog box.
    /// </summary>
    public Region ComputeBodyRegion(Region outer)
    {
        var dw = Math.Clamp(DialogWidth,  2, outer.Width);
        var dh = Math.Clamp(DialogHeight, 2, outer.Height);
        var dc = outer.Col + (outer.Width  - dw) / 2;
        var dr = outer.Row + (outer.Height - dh) / 2;
        var s  = Style;
        int t  = 1 + (s.HasPadding ? s.PaddingTop    : 0);
        int r  = 1 + (s.HasPadding ? s.PaddingRight  : 0);
        int b  = 1 + (s.HasPadding ? s.PaddingBottom : 0);
        int l  = 1 + (s.HasPadding ? s.PaddingLeft   : 0);
        return new Region(dc + l, dr + t,
            Math.Max(0, dw - l - r), Math.Max(0, dh - t - b));
    }

    // ── Render ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Optionally fills the region with a backdrop, then renders a centered bordered
    /// dialog box containing <see cref="Body"/>.
    /// </summary>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0) return;

        // ── Backdrop ─────────────────────────────────────────────────────────
        if (ShowBackdrop)
        {
            var bdStyle = BackdropStyle.Inherit(ctx.Theme.BaseStyle);
            var fill = new string(' ', region.Width);
            for (var r = 0; r < region.Height; r++)
                ctx.Write(region.Col, region.Row + r, fill, bdStyle);
        }

        // ── Center the dialog box ─────────────────────────────────────────────
        var dw = Math.Clamp(DialogWidth,  2, region.Width);
        var dh = Math.Clamp(DialogHeight, 2, region.Height);

        var dialogCol = region.Col + (region.Width  - dw) / 2;
        var dialogRow = region.Row + (region.Height - dh) / 2;

        var dialogRegion = new Region(dialogCol, dialogRow, dw, dh);
        var dialogCtx    = new SubRenderContext(ctx, dialogRegion);

        // ── Dialog box ────────────────────────────────────────────────────────
        var effectiveStyle = Style.Inherit(ctx.Theme.BorderStyle);
        var box = new BorderBox(Title, Body, effectiveStyle);
        box.Render(dialogCtx);
    }
}

/// <summary>
/// Dispatched by convention when the user dismisses a modal (e.g. presses Escape).
/// The model should set its open-flag to false and remove the modal from the view tree.
/// </summary>
public sealed record ModalDismissedMsg : IMsg;