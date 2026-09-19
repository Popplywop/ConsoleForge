# Introduction

ConsoleForge is a terminal UI framework for .NET 8 built on the
[Elm architecture](https://guide.elm-lang.org/architecture/). Your application is an
immutable record; a pure function turns messages into the next record; a second pure function turns that
record into a widget tree. The framework owns everything else.

If you have used Bubble Tea in Go, the shape will be familiar.

## The loop

Three members, defined by <xref:ConsoleForge.Core.IModel>:

| Member | Signature | Role |
|---|---|---|
| `Init()` | `ICmd?` | Optional startup work, run once before the first frame. |
| `Update(IMsg)` | `(IModel, ICmd?)` | Fold a message into the next model. Pure. |
| `View()` | `IWidget` | Describe the frame. Pure. |

<xref:ConsoleForge.Core.App> drives it: input events and command results arrive as `IMsg` on an unbounded
channel, `Update` folds each one into a new model, and `View` is re-rendered at the target frame rate.

`Update` returns a *new* model — never a mutated one. Records and `with` expressions make that cheap:

```csharp
public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
{
    KeyMsg { Key: ConsoleKey.UpArrow } => (this with { Count = Count + 1 }, null),
    KeyMsg { Key: ConsoleKey.Q }       => (this, Cmd.Quit()),
    _ => (this, null),
};
```

## Widgets hold no state

This is the part that differs most from other TUI toolkits. A ConsoleForge widget is a value describing
one frame — it is constructed in `View()`, rendered, and discarded. It has no fields you mutate and no
lifecycle you hook.

Anything that persists lives in your model. For editing and selection, that means the pure reducers:
<xref:ConsoleForge.Core.TextInputState>, `TextAreaState`, and `ListState` hold cursor position,
selection and scroll as plain values you store and update yourself. The widgets render those values and
delegate back to them, so widget and state cannot drift apart.

Focus works the same way. The framework tracks the focus index and pushes it into your model as a
`FocusIndexChangedMsg`; widgets never set their own `HasFocus`.

## Side effects leave the loop

`Update` is pure, so anything async is described rather than performed. You return an
<xref:ConsoleForge.Core.ICmd> and the dispatcher runs it off the loop, feeding the result back as another
message:

```csharp
KeyMsg { Key: ConsoleKey.R } => (
    this with { Loading = true },
    Cmd.Run(async ct => new DataLoadedMsg(await Fetch(ct)))),
```

`Cmd.Batch`, `Cmd.Sequence`, `Cmd.Tick`, `Cmd.Debounce` and `Cmd.Throttle` compose from there. For
continuous streams — a timer, a file watcher, an `IAsyncEnumerable` — use `Sub` instead, which stays
subscribed for the life of the model.

## Layout is resolved before rendering

Layout runs in two phases. `LayoutEngine` resolves each widget's `SizeConstraint` (`Fixed`, `Flex`,
`Auto`, `Min`, `Max`) into concrete `Region`s; only then does each widget write cells into its own region.
`Auto` sizes to content by asking the widget to measure itself.

Rendering is double-buffered and diffed per cell, with per-widget dirty tracking, so a frame where one
character changed writes one character to the terminal.

## Next

- **[Getting Started](getting-started.md)** — install and run something.
- **[Widgets](widgets.md)** — the sixteen built-ins and what each is for.
- **[Layout](layout.md)** — constraints, measuring, and the flex-inside-auto trap.
- **[State Reducers](state-reducers.md)** — where the cursor and the scroll offset live.
- **[Commands & Subscriptions](commands-and-subscriptions.md)** — doing work off the loop.
- **[Source Generators](../src/ConsoleForge.SourceGen/README.md)** — replace the `Update` switch with generated dispatch.
