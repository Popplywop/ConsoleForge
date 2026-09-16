using ConsoleForge.Core;

namespace ConsoleForge.Layout;

/// <summary>
/// Extended interface for interactive widgets that can receive keyboard focus.
/// </summary>
public interface IFocusable : IWidget
{
    /// <summary>True when this widget holds keyboard focus.</summary>
    bool HasFocus { get; init; }

    /// <summary>null means widget is not click-focusable. </summary>
    string? FocusKey { get; init; }

    (IFocusable Next, ICmd? Cmd) Update(KeyMsg key);
}