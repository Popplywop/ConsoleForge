using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Widgets;

namespace ConsoleForge.Gallery;

/// <summary>ProgressBar page component.</summary>
sealed record ProgressBarComponent(double Value = 42) : IComponent
{
    static readonly KeyMap Keys = new KeyMap()
        .On(ConsoleKey.LeftArrow,  () => new AdjustLeftMsg())
        .On(ConsoleKey.RightArrow, () => new AdjustRightMsg());

    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg)
    {
        if (Keys.Handle(msg) is { } action) msg = action;
        return msg switch
        {
            AdjustLeftMsg  => (this with { Value = Math.Max(0,   Value - 5) }, null),
            AdjustRightMsg => (this with { Value = Math.Min(100, Value + 5) }, null),
            _              => (this, null),
        };
    }

    public IWidget View() => new Container(Axis.Vertical, [
        new ProgressBar(Value),
        new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [
            new TextBlock($"Value: {Value:0}  (\u2190 / \u2192 to adjust by 5)"),
        ]),
    ]);
}
