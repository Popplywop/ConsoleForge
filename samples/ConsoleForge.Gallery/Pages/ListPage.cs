using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Widgets;

namespace ConsoleForge.Gallery;

/// <summary>List page component. Signals the last picked item as its result.</summary>
[DispatchUpdate, Component]
public sealed partial class ListPageComponent : IComponent<string>
{
    public int SelectedIndex { get; init; } = 0;
    public int ScrollOffset { get; init; } = 0;
    public string LastPicked { get; init; } = "";
    public string? Result { get; init; } = null;

    internal static readonly string[] Items = [
        "Apple", "Banana", "Cherry", "Date", "Elderberry",
        "Fig", "Grape", "Honeydew", "Kiwi", "Lemon",
    ];

    static readonly KeyMap Keys = new KeyMap()
        .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
        .On(ConsoleKey.DownArrow, () => new NavDownMsg())
        .On(ConsoleKey.Enter,     () => new NavSelectMsg());

    public (IModel Model, ICmd? Cmd) OnNavUp() => (new ListPageComponent()
    {
        SelectedIndex = Math.Max(0, SelectedIndex - 1),
        ScrollOffset = List.ComputeScrollOffset(Math.Max(0, SelectedIndex - 1), 8, ScrollOffset),
        LastPicked = LastPicked,
        Result = Result
    }, null);

    public (IModel Model, ICmd? Cmd) OnNavDown() => (new ListPageComponent()
    {
        SelectedIndex = Math.Min(Items.Length - 1, SelectedIndex + 1),
        ScrollOffset = List.ComputeScrollOffset(Math.Min(Items.Length - 1, SelectedIndex + 1), 8, ScrollOffset),
        LastPicked = LastPicked,
        Result = Result
    }, null);

    public (IModel Model, ICmd? Cmd) OnNavSelect() => (new ListPageComponent() { SelectedIndex = SelectedIndex, ScrollOffset = ScrollOffset, LastPicked = Items[SelectedIndex], Result = Items[SelectedIndex] }, null);

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