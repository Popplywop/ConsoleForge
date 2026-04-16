using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Widgets;

namespace ConsoleForge.Gallery;

/// <summary>Checkbox page component.</summary>
sealed record CheckboxComponent(
    bool[] States   = null!,
    int    FocusIdx = 0) : IComponent
{
    internal static readonly string[] Labels = ["Enable dark mode", "Show line numbers", "Auto-save on exit"];

    /// <summary>The actual checkbox states, with a default when null.</summary>
    public bool[] ActualStates => States ?? [false, true, false];

    static readonly KeyMap Keys = new KeyMap()
        .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
        .On(ConsoleKey.DownArrow, () => new NavDownMsg())
        .On(ConsoleKey.Spacebar,  () => new ToggleCheckboxMsg())
        .On(ConsoleKey.Enter,     () => new ToggleCheckboxMsg());

    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg)
    {
        if (Keys.Handle(msg) is { } action) msg = action;
        if (msg is ToggleCheckboxMsg)
        {
            var s = (bool[])ActualStates.Clone();
            s[FocusIdx] = !s[FocusIdx];
            return (this with { States = s }, null);
        }
        return msg switch
        {
            NavUpMsg   => (this with { FocusIdx = Math.Max(0, FocusIdx - 1) }, null),
            NavDownMsg => (this with { FocusIdx = Math.Min(Labels.Length - 1, FocusIdx + 1) }, null),
            _          => (this, null),
        };
    }

    public IWidget View()
    {
        var states = ActualStates;
        var boxes = Labels.Select((lbl, i) =>
            (IWidget)new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [
                new Checkbox(lbl, states[i]) { HasFocus = i == FocusIdx }
            ])).ToArray();
        return new Container(Axis.Vertical, [.. boxes]);
    }
}
