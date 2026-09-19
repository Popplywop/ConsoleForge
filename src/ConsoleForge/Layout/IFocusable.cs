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

    /// <summary>
    /// Handle a key press while this widget holds focus. Returns the widget's next
    /// state and an optional command; the widget is immutable, so the result is a new
    /// instance rather than a mutation of this one. Return <c>this</c> to ignore the key.
    /// </summary>
    /// <param name="key">The key event delivered to the focused widget.</param>
    (IFocusable Next, ICmd? Cmd) Update(KeyMsg key);
}