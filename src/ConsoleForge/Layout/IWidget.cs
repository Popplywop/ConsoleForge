using ConsoleForge.Styling;

namespace ConsoleForge.Layout;

/// <summary>
/// Base interface for all visual elements in the widget tree.
/// </summary>
public interface IWidget
{
    /// <summary>Visual style for this widget. Inherits from Theme if unset.</summary>
    Style Style { get; }

    /// <summary>Width constraint used by the layout engine.</summary>
    SizeConstraint Width { get; }

    /// <summary>Height constraint used by the layout engine.</summary>
    SizeConstraint Height { get; }

    /// <summary>
    /// Render this widget into the provided context.
    /// The context carries the allocated region, theme, and color profile.
    /// Implementations MUST NOT write outside ctx.Region.
    /// </summary>
    void Render(IRenderContext ctx);
}
