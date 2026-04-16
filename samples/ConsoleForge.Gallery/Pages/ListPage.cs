using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Widgets;

namespace ConsoleForge.Gallery;

/// <summary>List page component. Signals the last picked item as its result.</summary>
sealed record ListPageComponent(
    int     SelectedIndex = 0,
    int     ScrollOffset  = 0,
    string  LastPicked    = "",
    string? Result        = null) : IComponent<string>
{
    string IComponent<string>.Result => Result!;

    internal static readonly string[] Items = [
        "Apple", "Banana", "Cherry", "Date", "Elderberry",
        "Fig", "Grape", "Honeydew", "Kiwi", "Lemon",
    ];

    static readonly KeyMap Keys = new KeyMap()
        .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
        .On(ConsoleKey.DownArrow, () => new NavDownMsg())
        .On(ConsoleKey.Enter,     () => new NavSelectMsg());

    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg)
    {
        if (Keys.Handle(msg) is { } action) msg = action;
        return msg switch
        {
            NavUpMsg     => (this with { SelectedIndex = Math.Max(0, SelectedIndex - 1),
                                         ScrollOffset  = List.ComputeScrollOffset(Math.Max(0, SelectedIndex - 1), 8, ScrollOffset) }, null),
            NavDownMsg   => (this with { SelectedIndex = Math.Min(Items.Length - 1, SelectedIndex + 1),
                                         ScrollOffset  = List.ComputeScrollOffset(Math.Min(Items.Length - 1, SelectedIndex + 1), 8, ScrollOffset) }, null),
            NavSelectMsg => (this with { LastPicked = Items[SelectedIndex], Result = Items[SelectedIndex] }, null),
            _            => (this, null),
        };
    }

    public IWidget View()
    {
        var pickedText = string.IsNullOrEmpty(LastPicked)
            ? "Press Enter to select an item"
            : $"Last selected: \"{LastPicked}\"";
        return new Container(Axis.Vertical, [
            new Container(Axis.Vertical, height: SizeConstraint.Fixed(Items.Length + 1), children: [
                new List(Items, SelectedIndex,
                    scrollOffset: ScrollOffset) { HasFocus = true }
            ]),
            new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [
                new TextBlock(pickedText),
            ]),
        ]);
    }
}