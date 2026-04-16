using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Widgets;

namespace ConsoleForge.Gallery;

/// <summary>TextArea page component. Routes raw key events to the TextArea widget.</summary>
sealed record TextAreaComponent(
    IReadOnlyList<string>? Lines     = null,
    int                    CursorRow = 0,
    int                    CursorCol = 0,
    int                    ScrollRow = 0) : IComponent
{
    /// <summary>The actual lines, with a default when null.</summary>
    public IReadOnlyList<string> ActualLines => Lines ?? ["Hello, ConsoleForge!", "Edit this text\u2026", ""];

    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg)
    {
        if (msg is not KeyMsg key) return (this, null);
        var ta = new TextArea(ActualLines, CursorRow, CursorCol, ScrollRow);
        TextAreaChangedMsg? changed = null;
        ta.OnKeyEvent(key, m => changed = m as TextAreaChangedMsg);
        if (changed is null) return (this, null);
        var scroll = ConsoleForge.Widgets.TextArea.ComputeScrollRow(
            changed.NewCursorRow, viewportHeight: 12, ScrollRow);
        return (this with {
            Lines     = changed.NewLines,
            CursorRow = changed.NewCursorRow,
            CursorCol = changed.NewCursorCol,
            ScrollRow = scroll,
        }, null);
    }

    public IWidget View() =>
        new TextArea(ActualLines, CursorRow, CursorCol, ScrollRow)
            { HasFocus = true };
}