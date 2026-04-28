using ConsoleForge.Core;

namespace ConsoleForge.Layout;

/// <summary>
/// Extended interface for interactive widgets that can receive keyboard focus.
/// </summary>
public interface IFocusable : IWidget
{
    /// <summary>True when this widget holds keyboard focus.</summary>
    bool HasFocus { get; set; }

    (IFocusable Next, ICmd? Cmd) Update(KeyMsg key);
}