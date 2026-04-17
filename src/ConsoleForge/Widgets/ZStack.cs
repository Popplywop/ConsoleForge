using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A layered widget that renders its children back-to-front over the same region.
/// Each layer is given the full allocated region; later layers paint over earlier ones.
/// <para>
/// Typical usage — base layout + optional overlay:
/// <code>
/// new ZStack([
///     mainLayout,
///     isModalOpen ? new Modal(...) : new TextBlock(""),
/// ])
/// </code>
/// </para>
/// </summary>
/// <remarks>
/// <see cref="Core.FocusManager"/> traverses all layers for focus collection,
/// so interactive widgets in any layer participate in Tab-order traversal.
/// </remarks>
public sealed class ZStack : IWidget, ILayeredContainer
{
    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);
    /// <summary>Visual style for the stack. Not rendered directly — ZStack has no visual output of its own.</summary>
    public Style Style { get; init; } = Style.Default;

    // ── ILayeredContainer ────────────────────────────────────────────────────
    /// <summary>Layers in back-to-front order. The last layer renders on top.</summary>
    public IReadOnlyList<IWidget> Layers { get; init; } = [];

    /// <summary>Object-initializer constructor; all properties default.</summary>
    public ZStack() { }

    /// <summary>Positional constructor for inline usage.</summary>
    /// <param name="layers">Layers in back-to-front render order.</param>
    public ZStack(IReadOnlyList<IWidget> layers) => Layers = layers;

    // ── Render ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders each layer over the same region in declaration order.
    /// Later layers paint over earlier ones, producing a stacked visual effect.
    /// </summary>
    public void Render(IRenderContext ctx)
    {
        if (ctx.Region.Width <= 0 || ctx.Region.Height <= 0) return;

        foreach (var layer in Layers)
            layer.Render(ctx);
    }
}