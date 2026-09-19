# Getting Started

## Requirements

- .NET 8 SDK or later
- A terminal that supports ANSI escape sequences (Windows Terminal, iTerm2, Kitty, Alacritty, GNOME Terminal, …)

## Install

```bash
dotnet new console -o MyTui
cd MyTui
dotnet add package ConsoleForge
dotnet add package ConsoleForge.SourceGen   # optional, see below
```

## A first app

Replace `Program.cs`:

<!-- doccheck: program -->
```csharp
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

await App.Run(new HelloModel(), theme: Theme.Dark);

sealed record HelloModel(int Count = 0) : IModel
{
    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
    {
        KeyMsg { Key: ConsoleKey.UpArrow }   => (this with { Count = Count + 1 }, null),
        KeyMsg { Key: ConsoleKey.DownArrow } => (this with { Count = Count - 1 }, null),
        KeyMsg { Key: ConsoleKey.Q }         => (this, Cmd.Quit()),
        _ => (this, null),
    };

    public IWidget View() =>
        new BorderBox("ConsoleForge",
            body: new Container(Axis.Vertical, [
                new TextBlock($"Count: {Count}"),
                new TextBlock("↑↓ to change, Q to quit",
                    style: Style.Default.Faint(true)),
            ]));
}
```

```bash
dotnet run
```

Arrow keys change the count; `Q` exits. The terminal is restored on exit even if the app throws.

## What just happened

`App.Run` put the terminal in raw mode, called `Init()`, then began the loop: each keystroke arrived as a
`KeyMsg`, `Update` folded it into a new `HelloModel`, and `View` rebuilt the widget tree. The renderer
diffed that tree against the previous frame and wrote only the cells that changed.

Note that `HelloModel` is a `record` and `Update` returns `this with { … }`. Nothing is mutated, including
the widgets — they are rebuilt every frame and thrown away.

## Adding options

`App.Run` takes a few more arguments worth knowing:

```csharp
await App.Run(
    new HelloModel(),
    theme: Theme.Dracula,   // Dark, Light, Dracula, Nord, Monokai, TokyoNight
    targetFps: 30,          // 1–60
    enableMouse: true);     // SGR 1006 tracking: click-to-focus, scroll wheel
```

## Dropping the switch statement

Once `Update` grows past a handful of cases, `ConsoleForge.SourceGen` can generate the dispatch. Mark the
record `partial`, add `[DispatchUpdate]`, and write one `On…` method per message type:

<!-- doccheck: snippet -->
```csharp
// Your own messages — one per thing that can happen.
sealed record NavUpMsg   : IMsg;
sealed record NavDownMsg : IMsg;

[DispatchUpdate]
sealed partial record HelloModel(int Count = 0) : IModel
{
    public ICmd? Init() => null;

    private (IModel, ICmd?) OnNavUp()   => (this with { Count = Count + 1 }, null);
    private (IModel, ICmd?) OnNavDown() => (this with { Count = Count - 1 }, null);

    public IWidget View() => new TextBlock($"Count: {Count}");
}
```

The generator writes `Update(IMsg)` as a type switch: `NavUpMsg` routes to `OnNavUp`, `SwitchTabMsg` would
route to `OnSwitchTab`. Strip the `Msg` suffix, prefix `On`. The handler may take the message as a
parameter (`OnSwitchTab(SwitchTabMsg msg)`) or omit it when it carries nothing you need. Messages you
dispatch with a `KeyMap`:

```csharp
private static readonly KeyMap Keys = new KeyMap()
    .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
    .On(ConsoleKey.DownArrow, () => new NavDownMsg());
```

See
[Source Generators](../src/ConsoleForge.SourceGen/README.md) for the full conventions and diagnostics.

## Where to next

- **[Introduction](introduction.md)** — the architecture in more depth.
- **[Widgets](widgets.md)** — the sixteen built-ins and what each is for.
- **[Layout](layout.md)** — sizing a tree that has to fit a terminal.
- **[API Reference](../api/ConsoleForge.Core.App.yml)** — every public type.
- The `samples/` directory in the repository: `ConsoleForge.Gallery` (all widgets, all themes),
  `ConsoleForge.TodoApp`, and `ConsoleForge.SysMonitor` (live data via subscriptions).

```bash
dotnet run --project samples/ConsoleForge.Gallery
```
