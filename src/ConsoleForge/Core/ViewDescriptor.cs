using ConsoleForge.Layout;
using ConsoleForge.Styling;
// Forward references to Layout and Styling are resolved by the project — no extra usings needed.

namespace ConsoleForge.Core;

/// <summary>
/// Immutable descriptor for a single rendered frame.
/// Produced by IModel.View() and diffed by the renderer.
/// </summary>
public readonly record struct ViewDescriptor
{
    /// <summary>Pre-rendered ANSI string for the full terminal frame.</summary>
    public required string Content { get; init; }

    /// <summary>Optional terminal window title. Null = no change.</summary>
    public string? Title { get; init; }

    /// <summary>Cursor state for this frame.</summary>
    public CursorDescriptor Cursor { get; init; }

    /// <summary>
    /// The root widget that was rendered. Used by the framework for
    /// focus traversal via <see cref="FocusManager"/>.
    /// </summary>
    public IWidget? RootWidget { get; init; }

    /// <summary>
    /// Convenience factory: render an IWidget into a ViewDescriptor
    /// at the given terminal dimensions.
    /// </summary>
    /// <param name="root">Root widget to render.</param>
    /// <param name="existingCtx">
    /// Optional persistent <see cref="RenderContext"/> for double-buffered diff rendering.
    /// If provided, <see cref="RenderContext.Reset"/> is called to reuse it for this frame.
    /// If null, a fresh context is created (stateless, full redraw).
    /// </param>
    /// <param name="width">Terminal width. Defaults to <see cref="Console.WindowWidth"/>.</param>
    /// <param name="height">Terminal height. Defaults to <see cref="Console.WindowHeight"/>.</param>
    /// <param name="theme">Theme. Defaults to <see cref="Theme.Default"/>.</param>
    /// <param name="colorProfile">Color profile for ANSI output.</param>
    public static ViewDescriptor From(
        IWidget root,
        RenderContext? existingCtx = null,
        int? width = null,
        int? height = null,
        Theme? theme = null,
        ColorProfile colorProfile = ColorProfile.TrueColor)
    {
        var w = width ?? Console.WindowWidth;
        var h = height ?? Console.WindowHeight;
        var resolvedTheme = theme ?? Theme.Default;

        var layout = LayoutEngine.Resolve(root, w, h);
        var rootRegion = layout.GetRegion(root) ?? new Region(0, 0, w, h);

        RenderContext ctx;
        if (existingCtx is not null)
        {
            existingCtx.Reset(rootRegion, resolvedTheme, colorProfile, layout);
            ctx = existingCtx;
        }
        else
        {
            ctx = new RenderContext(rootRegion, resolvedTheme, colorProfile, layout);
        }

        root.Render(ctx);

        return new ViewDescriptor
        {
            Content    = ctx.ToAnsiFrame(),
            Cursor     = new CursorDescriptor(Visible: false),
            RootWidget = root
        };
    }
}

/// <summary>
/// Cursor state for a rendered frame.
/// </summary>
/// <param name="Visible">Whether the hardware cursor should be shown.</param>
/// <param name="Col">Zero-based column of the cursor.</param>
/// <param name="Row">Zero-based row of the cursor.</param>
public readonly record struct CursorDescriptor(bool Visible = true, int Col = 0, int Row = 0);
