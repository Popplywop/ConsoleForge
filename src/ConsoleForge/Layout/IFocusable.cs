using ConsoleForge.Core;
using ConsoleForge.Styling;

namespace ConsoleForge.Layout;

/// <summary>
/// Extended interface for interactive widgets that can receive keyboard focus.
/// </summary>
public interface IFocusable : IWidget
{
    /// <summary>True when this widget holds keyboard focus.</summary>
    bool HasFocus { get; set; }

    /// <summary>
    /// Called by the runtime when a key is pressed and this widget has focus.
    /// Call dispatch to inject a custom IMsg into the event loop.
    /// </summary>
    void OnKeyEvent(KeyMsg key, Action<IMsg> dispatch);
}
