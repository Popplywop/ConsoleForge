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
/// <para>
/// Layers composite by painting, not by blending: a layer that writes a cell replaces
/// what was there, and one that fills its whole region hides every layer beneath it.
/// <see cref="Modal.ShowBackdrop"/> is the case to know about — it blanks the full region
/// before drawing its dialog, which erases the layout you stacked it over.
/// </para>
/// </remarks>
public sealed record ZStack : IWidget, ILayeredContainer, IMeasurable
{
    // ── IWidget ─────────────────────────────────────────────────────────────
    /// <inheritdoc/>
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    /// <inheritdoc/>
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

    /// <inheritdoc/>
    /// <remarks>Large enough for every layer: the maximum desired size across them.</remarks>
    public Size Measure(int availableWidth, int availableHeight)
    {
        int width = 0, height = 0;
        for (var i = 0; i < Layers.Count; i++)
        {
            var desired = LayoutSolver.DesiredSize(
                Layers[i], availableWidth, availableHeight,
                flexWidth: availableWidth, flexHeight: availableHeight);
            width  = Math.Max(width,  desired.Width);
            height = Math.Max(height, desired.Height);
        }
        return new Size(Math.Min(availableWidth, width), Math.Min(availableHeight, height));
    }

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