using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Widgets;

namespace ConsoleForge.Gallery;

/// <summary>Checkbox page component.</summary>
[DispatchUpdate, Component]
public sealed partial class CheckboxComponent : IComponent
{
    public bool[] States { get; init; } = null!;
    public int FocusIdx { get; init; } = 0;
    
    internal static readonly string[] Labels = ["Enable dark mode", "Show line numbers", "Auto-save on exit"];

    /// <summary>The actual checkbox states, with a default when null.</summary>
    public bool[] ActualStates => States ?? [false, true, false];

    static readonly KeyMap Keys = new KeyMap()
        .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
        .On(ConsoleKey.DownArrow, () => new NavDownMsg())
        .On(ConsoleKey.Spacebar,  () => new ToggleCheckboxMsg())
        .On(ConsoleKey.Enter,     () => new ToggleCheckboxMsg());
        
    public (IModel Model, ICmd? Cmd) OnToggleCheckbox()
    {
        var s = (bool[])ActualStates.Clone();
        s[FocusIdx] = !s[FocusIdx];
        return (new CheckboxComponent() { States = s, FocusIdx = FocusIdx }, null);
    }

    public (IModel Model, ICmd? Cmd) OnNavUp() => (new CheckboxComponent() { States = States, FocusIdx = Math.Max(0, FocusIdx - 1) }, null);

    public (IModel Model, ICmd? Cmd) OnNavDown() => (new CheckboxComponent() { States = States, FocusIdx = Math.Min(Labels.Length - 1, FocusIdx + 1) }, null);

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