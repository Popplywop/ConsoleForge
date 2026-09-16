namespace ConsoleForge.Layout;

/// <summary>A widget's desired size in terminal cells.</summary>
/// <param name="Width">Desired columns.</param>
/// <param name="Height">Desired rows.</param>
public readonly record struct Size(int Width, int Height);

/// <summary>
/// Implemented by widgets that can report how much space their content wants, which is
/// what makes <see cref="SizeConstraint.Auto"/> size to content.
/// </summary>
/// <remarks>
/// <para>
/// Opting in is what turns <c>Auto</c> from a promise into behaviour. A widget that does
/// not implement this interface keeps the older meaning — <c>Auto</c> resolves as flex
/// weight 1 — so adding this to the framework moved no existing third-party layout.
/// </para>
/// <para>
/// <see cref="Measure"/> is called during layout, before any region is assigned, and must
/// be pure: same inputs, same answer, no side effects, no writing to a render context.
/// It is called once per <c>Auto</c> axis per layout pass, and layout runs per frame, so
/// it should stay cheap.
/// </para>
/// <para>
/// Implementations must not report more than they were offered. Clamping to the available
/// size keeps a container's children from over-subscribing it in the common case, and the
/// available size is the only bound the solver can offer for content that could otherwise
/// grow without limit.
/// </para>
/// </remarks>
public interface IMeasurable : IWidget
{
    /// <summary>
    /// The size this widget's content wants, given what is on offer. Both components must
    /// be within <c>[0, available]</c> — see the remarks on <see cref="IMeasurable"/>.
    /// </summary>
    /// <param name="availableWidth">Columns available.</param>
    /// <param name="availableHeight">Rows available.</param>
    Size Measure(int availableWidth, int availableHeight);
}
