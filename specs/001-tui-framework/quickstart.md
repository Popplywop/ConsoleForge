# Quick Start: ConsoleForge

**Feature**: specs/001-tui-framework
**Date**: 2026-04-12

---

## Prerequisites

- .NET 8 SDK
- Unix-compatible terminal (VT100 minimum)

---

## Install

```bash
dotnet add package ConsoleForge
```

---

## Hello World: Render a Bordered Box

```csharp
using ConsoleForge;
using ConsoleForge.Widgets;
using ConsoleForge.Styling;

// 1. Define your model (immutable record)
record HelloModel(string Message) : IModel
{
    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
    {
        KeyMsg { Key: ConsoleKey.Q } => (this, Cmd.Quit()),
        _ => (this, null)
    };

    public ViewDescriptor View()
    {
        var box = new BorderBox(
            title: "ConsoleForge",
            body: new TextBlock(Message),
            style: Style.Default.BorderForeground(Color.Green)
        );
        return ViewDescriptor.From(box);
    }
}

// 2. Run
Program.Run(new HelloModel("Press Q to quit."));
```

Run it:

```bash
dotnet run
```

You see a green-bordered box in the terminal. Press **Q** to exit. The terminal
is fully restored — no artifacts left behind.

---

## Two-Pane Layout with a Status Bar

```csharp
record AppModel(string[] Items, int Selected) : IModel
{
    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
    {
        KeyMsg { Key: ConsoleKey.UpArrow } =>
            (this with { Selected = Math.Max(0, Selected - 1) }, null),
        KeyMsg { Key: ConsoleKey.DownArrow } =>
            (this with { Selected = Math.Min(Items.Length - 1, Selected + 1) }, null),
        KeyMsg { Key: ConsoleKey.Q } => (this, Cmd.Quit()),
        _ => (this, null)
    };

    public ViewDescriptor View()
    {
        var sidebar = new Container(Axis.Vertical,
            width: SizeConstraint.Fixed(24),
            children: Items.Select((item, i) =>
                (IWidget)new TextBlock(item,
                    style: i == Selected
                        ? Style.Default.Background(Color.Blue).Foreground(Color.White)
                        : Style.Default)
            ).ToArray()
        );

        var main = new Container(Axis.Vertical,
            width: SizeConstraint.Flex(1),
            children: [
                new TextBlock($"Selected: {Items[Selected]}")
            ]
        );

        var statusBar = new TextBlock(
            "↑↓ navigate  Q quit",
            style: Style.Default.Background(Color.DarkGray).Foreground(Color.White)
        );

        var root = new Container(Axis.Vertical, [
            new Container(Axis.Horizontal,
                height: SizeConstraint.Flex(1),
                children: [sidebar, main]
            ),
            new Container(Axis.Vertical,
                height: SizeConstraint.Fixed(1),
                children: [statusBar]
            )
        ]);

        return ViewDescriptor.From(root);
    }
}

Program.Run(new AppModel(
    Items: ["Alpha", "Beta", "Gamma", "Delta"],
    Selected: 0
));
```

---

## Apply a Global Theme

```csharp
var theme = new Theme(
    name: "Dark",
    baseStyle: Style.Default.Foreground(Color.White).Background(Color.Black),
    borderStyle: Style.Default.BorderForeground(Color.Cyan),
    focusedStyle: Style.Default.BorderForeground(Color.Yellow)
);

Program.Run(new AppModel(...), theme: theme);
```

All widgets inherit from the theme. Per-widget styles override only the
properties they explicitly set.

---

## Async Command (Data Fetch)

```csharp
// Message types
record DataLoadedMsg(string[] Lines) : IMsg;
record LoadErrorMsg(string Error) : IMsg;

// Command factory
static ICmd FetchData(string path) => () =>
{
    try
    {
        var lines = File.ReadAllLines(path);
        return new DataLoadedMsg(lines);
    }
    catch (Exception ex)
    {
        return new LoadErrorMsg(ex.Message);
    }
};

// Model using it
record FileModel(string? Error, string[] Lines) : IModel
{
    public ICmd? Init() => FetchData("/etc/hosts");

    public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
    {
        DataLoadedMsg m => (this with { Lines = m.Lines }, null),
        LoadErrorMsg m  => (this with { Error = m.Error }, null),
        KeyMsg { Key: ConsoleKey.Q } => (this, Cmd.Quit()),
        _ => (this, null)
    };

    public ViewDescriptor View()
    {
        var content = Error is not null
            ? (IWidget)new TextBlock($"Error: {Error}",
                style: Style.Default.Foreground(Color.Red))
            : new Container(Axis.Vertical,
                scrollable: true,
                children: Lines.Select(l => (IWidget)new TextBlock(l)).ToArray()
            );
        return ViewDescriptor.From(content);
    }
}
```

---

## Composing Reusable Components

```csharp
// A reusable spinner component
class Spinner : IComponent<Spinner>
{
    static readonly string[] Frames = ["|", "/", "-", "\\"];
    public int Frame { get; private init; }
    public Style Style { get; init; } = Style.Default;

    public ICmd? Tick() => Cmd.Tick(
        TimeSpan.FromMilliseconds(100),
        _ => new SpinnerTickMsg(this));

    public (Spinner Model, ICmd? Cmd) Update(IMsg msg) => msg switch
    {
        SpinnerTickMsg m when ReferenceEquals(m.Spinner, this) =>
            (this with { Frame = (Frame + 1) % Frames.Length }, Tick()),
        _ => (this, null)
    };

    public string View() => Style.Render(Frames[Frame]);
}

record SpinnerTickMsg(Spinner Spinner) : IMsg;
```

Parent model holds the spinner as a field, delegates Update calls, and
incorporates `spinner.View()` into its own `View()`.

---

## Testing Your TUI with VirtualTerminal

```csharp
using ConsoleForge.Testing;

[Fact]
public void QuitKey_ExitsLoop()
{
    var terminal = new VirtualTerminal(width: 80, height: 24);
    var model = new HelloModel("Hello");
    var program = new Program(terminal);

    terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q));
    program.Run(model);  // returns because QuitMsg was processed

    Assert.True(terminal.ExitedCleanly);
    Assert.False(terminal.HasArtifacts);
}
```
