# ConsoleForge

A C# 12 / .NET 8 terminal UI framework. Simple to use, expressive enough for real apps.

**Architecture**: Elm-loop — immutable model, pure `Update`, pure `View`. No mutable state in widgets.  
**Renderer**: Double-buffered, cell-level diff. Only changed cells hit the terminal per frame.  
**Docs**: [popplywop.github.io/ConsoleForge](https://popplywop.github.io/ConsoleForge/)

---

## Quick Start

```csharp
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

record HelloModel(string Message) : IModel
{
    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
    {
        KeyMsg { Key: ConsoleKey.Q } => (this, Cmd.Quit()),
        _ => (this, null)
    };

    public IWidget View() =>
        new BorderBox(
            title: "ConsoleForge",
            body: new TextBlock(Message),
            style: Style.Default.BorderForeground(Colors.Green)
        );
}

Program.Run(new HelloModel("Press Q to quit."));
```

```bash
dotnet run
```

---

## Core Concepts

### The Elm Loop

```
IModel.Init() → optional startup command
     ↓
  input event → IModel.Update(msg) → (newModel, cmd?)
                                           ↓
                                     IModel.View() → IWidget tree → render
```

Your model is an immutable record. `Update` returns a new copy. `View` is pure — no side effects.

### Widgets

| Widget | Description |
|--------|-------------|
| `TextBlock` | Renders text, word-wraps at region width |
| `Container` | Lays out children along horizontal or vertical axis |
| `BorderBox` | Bordered box with optional title and body widget |
| `List` | Scrollable list, keyboard navigable, dispatches selection messages |
| `TextInput` | Single-line text input with cursor, dispatches change messages |

### Layout

Children declare `Width` and `Height` as `SizeConstraint`:

```csharp
SizeConstraint.Fixed(24)    // exact column/row count
SizeConstraint.Flex(1)      // proportional share of remaining space
SizeConstraint.Auto         // shrink to content
SizeConstraint.Min(n, inner)
SizeConstraint.Max(n, inner)
```

`Container` does two-pass layout: fixed children first, then flex children share the remainder.

### Styling

`Style` is an immutable value type. All methods return a new `Style` — fluent builder pattern:

```csharp
var style = Style.Default
    .Foreground(Colors.Cyan)
    .Bold()
    .Padding(1, 2)
    .Border(Borders.Rounded);
```

Styles inherit from parent theme when properties are unset — no redundant overrides needed.

### Commands

`ICmd` represents async side effects returned alongside the new model:

```csharp
Cmd.Quit()                        // exit the program
Cmd.From(async ct => someMsg)     // run async work, dispatch result as message
Cmd.Batch(cmd1, cmd2)             // run multiple commands
```

### Focus

`IFocusable` widgets (`TextInput`, `List`) receive keyboard events when focused.  
`FocusManager.CycleNext` / `CyclePrev` traverse focusable widgets in render order.

---

## Samples

| Sample | Description |
|--------|-------------|
| [`ConsoleForge.TodoApp`](samples/ConsoleForge.TodoApp/) | Todo list — browse, add, toggle, delete |
| [`ConsoleForge.Gallery`](samples/ConsoleForge.Gallery/) | Widget showcase — all widgets, styles, layouts |
| [`ConsoleForge.SysMonitor`](samples/ConsoleForge.SysMonitor/) | Live system stats via async commands |

Run a sample:

```bash
dotnet run --project samples/ConsoleForge.TodoApp
```

---

## Project Structure

```
src/
  ConsoleForge/
    Core/         IModel, IMsg, ICmd, Program (runtime loop), Renderer, FocusManager
    Layout/       IWidget, Container layout engine, RenderContext (double-buffer), Region
    Styling/      Style, Theme, BorderSpec, Colors, ColorProfile
    Terminal/     ITerminal, AnsiTerminal, Termios (Unix raw mode)
    Widgets/      TextBlock, TextInput, List, BorderBox, Container
tests/
  ConsoleForge.Tests/       Unit + integration tests, VirtualTerminal test double
  ConsoleForge.Benchmarks/  BenchmarkDotNet render benchmarks
samples/
  ConsoleForge.Gallery/
  ConsoleForge.SysMonitor/
  ConsoleForge.TodoApp/
```

---

## Building

```bash
dotnet build
dotnet test
```

Requires **.NET 8 SDK**. No external NuGet dependencies beyond `System.Reactive`.

---

## License

MIT — see [LICENSE](LICENSE).
