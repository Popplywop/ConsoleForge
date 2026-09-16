# ConsoleForge

An Elm-architecture TUI framework for .NET 8. Immutable model → pure update → declarative view.

**Docs:** [popplywop.github.io/ConsoleForge](https://popplywop.github.io/ConsoleForge) &nbsp;|
**NuGet:** [ConsoleForge](https://www.nuget.org/packages/ConsoleForge) &nbsp;|
**SourceGen:** [ConsoleForge.SourceGen](https://www.nuget.org/packages/ConsoleForge.SourceGen)

Built for developers who want the predictability of [Bubble Tea](https://github.com/charmbracelet/bubbletea) in C#: no mutable widget state, no hidden side effects, and a render pipeline that only touches the cells that actually changed.

## Features

- **Elm loop** — `Init` → `Update` → `View`. Your model is an immutable record. `Update` returns a new copy. `View` is pure.
- **15 built-in widgets** — TextBlock, TextInput, TextArea, List, Table, Checkbox, Tabs, ProgressBar, Spinner, BorderBox, Container, Modal, ZStack, ImageWidget, HorizontalShelf
- **6 named themes** — Dark, Light, Dracula, Nord, Monokai, Tokyo Night. Switch at runtime with one message.
- **Mouse support** — SGR 1006 extended mouse tracking. Click-to-focus, scroll wheel, button/motion events.
- **Unicode-aware layout** — CJK, emoji, and full-width characters render at correct column widths, from a table generated out of the Unicode Character Database.
- **Inline images** — `ImageWidget` draws PNGs through the Kitty graphics protocol where the terminal supports it, and falls back to half-block cells with 24-bit colour everywhere else.
- **Composable sub-programs** — `IComponent` / `IComponent<TResult>` for self-contained pages with own state, keymaps, and lifecycle.
- **Declarative keybindings** — `KeyMap` + `KeyPattern` replace giant switch statements. Composable, context-aware, and layout-independent for printable keys via `KeyPattern.OfChar`.
- **Virtualized scrolling** — List and Table render only visible rows. 1,000 items costs the same as 20.
- **Double-buffered renderer** — Cell-level diff with per-widget dirty tracking. Only changed cells hit the terminal.
- **Margin & padding** — `Style.Padding(1)` and `Style.Margin(1)` enforced by the layout engine.
- **Content-aware layout** — `SizeConstraint.Auto` sizes to content through `IMeasurable`; `Fixed` and `Flex` cover the rest.
- **Pure state reducers** — `TextInputState`, `TextAreaState`, `ListState`: editing, cursor movement and selection as values in your model, not stateful sub-widgets. The widgets delegate to them, so the two cannot drift.
- **Async commands** — `Cmd.Run`, `Cmd.Batch`, `Cmd.Sequence`, `Cmd.Tick`, and `Cmd.Debounce` / `Cmd.Throttle`, which rate-limit by key so a cmd built fresh in `Update` still coalesces.
- **Subscriptions** — `Sub.Interval`, `Sub.FromAsyncEnumerable`, `Sub.FromObservable` for continuous data streams.

## Quick Start

```bash
dotnet add package ConsoleForge
```

```csharp
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

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

await App.Run(new HelloModel(), theme: Theme.Dark);
```

## Widgets

| Widget | Description |
|--------|-------------|
| `TextBlock` | Text display with word-wrap. Supports `\n`, padding, and alignment. |
| `TextInput` | Single-line input with cursor, word jumps, and grapheme-aware editing. Delegates to `TextInputState`. |
| `TextArea` | Multi-line editor with cursor navigation, line splitting, and scroll. Delegates to `TextAreaState`. |
| `List` | Scrollable list with selection highlight. Virtualized — only visible rows render. Delegates to `ListState`. |
| `Table` | Columnar data with headers, selection, separators. Virtualized scrolling. |
| `Checkbox` | Toggle `[✓] Label` / `[ ] Label`. Customizable indicator characters. |
| `Tabs` | Tab bar + body content. Left/Right arrows, number keys 1–9. |
| `ProgressBar` | Horizontal fill bar with optional percentage label. |
| `Spinner` | Animated spinner with braille, ASCII, and arc frame sets. |
| `BorderBox` | Bordered box with title. 6 border styles: Normal, Rounded, Thick, Double, ASCII, Hidden. |
| `Container` | Flex layout along horizontal or vertical axis. Supports scrolling. |
| `Modal` | Centered dialog overlay. Compose with `ZStack` for layered UIs. |
| `ZStack` | Renders layers back-to-front. The foundation for overlays and modals. |
| `ImageWidget` | Inline image. Kitty graphics protocol when available, half-block cells otherwise. |
| `HorizontalShelf` | Paged, virtualized strip of cover art for rows wider than the terminal. |

## Themes

Six built-in themes with background colours, accent styles, and semantic colour slots:

```csharp
await App.Run(model, theme: Theme.Dracula);
```

| Theme | Background | Accent | Focus |
|-------|-----------|--------|-------|
| `Theme.Dark` | `#1C1C1C` | Teal | Gold |
| `Theme.Light` | `#F0F0F0` | Blue | Red |
| `Theme.Dracula` | `#282A36` | Purple | Cyan |
| `Theme.Nord` | `#2E3440` | Teal | Green |
| `Theme.Monokai` | `#272822` | Green | Yellow |
| `Theme.TokyoNight` | `#1A1B26` | Blue | Orange |

Switch at runtime:

```csharp
// In Update:
return (this with { ThemeIdx = next }, Cmd.Msg(new ThemeChangedMsg(newTheme)));
```

Access theme colours with extension methods:

```csharp
theme.Accent()       // IColor? — border brand colour
theme.AccentStyle()  // Style — accent as foreground, bold-ready
theme.MutedStyle()   // Style — dim secondary text
theme.Success()      // Style — green/equivalent
theme.Warning()      // Style — yellow/equivalent
theme.Error()        // Style — red/equivalent
theme.Bg()           // IColor? — background from BaseStyle
theme.BgStyle()      // Style — background only
```

## Layout

Children declare `Width` and `Height` as `SizeConstraint`:

```csharp
SizeConstraint.Fixed(24)        // exact columns/rows
SizeConstraint.Flex(1)          // proportional share of free space
SizeConstraint.Auto             // shrink to content
SizeConstraint.Min(10, inner)   // minimum bound
SizeConstraint.Max(40, inner)   // maximum bound
```

`Auto` asks the widget how much room its content wants, via `IMeasurable`:

```csharp
public interface IMeasurable : IWidget
{
    Size Measure(int availableWidth, int availableHeight);
}
```

`TextBlock`, `Spinner`, `Container`, `BorderBox`, and `ZStack` implement it — a one-line
`TextBlock` is one row, a `Container` sums its children along its axis. Implement it on your
own widgets to make `Auto` work for them; a widget that doesn't gets flex weight 1, the older
behaviour. `Measure` runs during layout, so it must be pure and cheap, and must never ask for
more than it was offered.

Two edges worth knowing: a flex child contributes nothing along the stacking axis (flex fills
leftovers, which is not a content size), so an `Auto` container of only flex children collapses
— give that container a `Fixed` or `Flex` size. And `Min`/`Max` fold over the measurement, so
`Max(10, Auto)` is a content size capped at 10.

`Container` runs a two-pass layout: fixed children first, then flex children share the remainder. Supports padding and margin:

```csharp
new Container(Axis.Horizontal,
    style: Style.Default.Padding(1),          // insets child region
    children: [
        new TextBlock("A") { Style = Style.Default.Margin(0, 1, 0, 1) },  // spacing around widget
        new TextBlock("B"),
    ]);
```

## Styling

`Style` is an immutable value type. All methods return a new `Style`:

```csharp
Style.Default
    .Foreground(Color.FromHex("#FF5733"))
    .Background(Color.Blue)
    .Bold()
    .Italic()
    .Underline()
    .Padding(1, 2)
    .Border(Borders.Rounded)
    .BorderForeground(Color.Cyan)
```

Styles inherit from the active theme when properties are unset — set only what you need.

## Mouse Support

```csharp
await App.Run(model, theme: Theme.Dark, enableMouse: true);
```

- **Click-to-focus** — give a widget a `FocusKey`, and a left-click on it sends
  `FocusRequestedMsg(key)` to your model. The framework hit-tests; your model decides
  what focus means. A widget without a key is not a click-focus target.
- **Scroll wheel** — `MouseMsg` with `MouseButton.ScrollUp` / `ScrollDown`
- **Button events** — press, release, motion tracking via `MouseMsg`

Handle in your model:

```csharp
case MouseMsg { Button: MouseButton.ScrollDown } => // scroll handler
```

## KeyMap — Declarative Keybindings

Replace switch statements with composable, context-aware binding maps:

```csharp
static readonly KeyMap SidebarKeys = new KeyMap()
    .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
    .On(ConsoleKey.DownArrow, () => new NavDownMsg())
    .On(ConsoleKey.Enter,     () => new SelectMsg())
    .On(ConsoleKey.Escape,    () => new QuitMsg())
    .On('?',                  () => new ShowHelpMsg())
    .On(KeyPattern.WithCtrl(ConsoleKey.C), () => new QuitMsg())
    .OnScroll(m => m.Button == MouseButton.ScrollUp
        ? new NavUpMsg() : new NavDownMsg());

// In Update:
if (SidebarKeys.Handle(msg) is { } action) msg = action;
```

`KeyPattern` matches a key two ways. `Of(key)`, `WithCtrl(key)`, `WithAlt(key)`, `WithShift(key)` and `Plain(key)` match a `ConsoleKey` with the named modifiers — the right choice for keys that produce no character, such as arrows, Enter, Tab and function keys.

`OfChar(char)` matches the character the terminal produced, which is what printable bindings want, because it does not assume a keyboard layout. `?` sits on `Shift+Oem2` on a US layout and somewhere else entirely on a German or French one, but it is `'?'` on all of them:

```csharp
.On('?', () => new ShowHelpMsg())          // shorthand for KeyPattern.OfChar('?')
.On(KeyPattern.OfChar('N'), () => ...)     // capital N only — case-sensitive
```

Shift stays a wildcard for a character pattern, since it has already been consumed producing the glyph; Ctrl and Alt must be absent, because `Ctrl+letter` arrives as a control character and `Alt+key` is a separate binding. Every `KeyPattern` field is optional and null means "don't care", so `default(KeyPattern)` matches every key — useful as a trailing catch-all, and worth avoiding by accident.

Compose maps: `globalKeys.Merge(pageKeys)` — first map takes priority.

## IComponent — Sub-Programs

Self-contained pages with own state, keybindings, and view. The [Bubble Tea](https://github.com/charmbracelet/bubbletea) `tea.Model`-per-page pattern:

```csharp
// Pages/CounterPage.cs
sealed record CounterPage(int Count = 0) : IComponent
{
    static readonly KeyMap Keys = new KeyMap()
        .On(ConsoleKey.UpArrow,   () => new IncrMsg())
        .On(ConsoleKey.DownArrow, () => new DecrMsg());

    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg)
    {
        if (Keys.Handle(msg) is { } action) msg = action;
        return msg switch
        {
            IncrMsg => (this with { Count = Count + 1 }, null),
            DecrMsg => (this with { Count = Count - 1 }, null),
            _       => (this, null),
        };
    }

    public IWidget View() => new TextBlock($"Count: {Count}");
}
```

Embed in a parent model:

```csharp
record AppModel(CounterPage Counter) : IModel
{
    public (IModel, ICmd?) Update(IMsg msg)
    {
        var (next, cmd) = Component.Delegate(Counter, msg);
        return (this with { Counter = next! }, cmd);
    }
    public IWidget View() => Counter.View();
}
```

For components that return results (file pickers, confirm dialogs):

```csharp
sealed record FilePicker(string? Result = null) : IComponent<string>
{
    string IComponent<string>.Result => Result!;
    // ... Update sets Result when user picks a file
}

// Parent checks completion:
var (next, cmd) = Component.Delegate(picker, msg);
if (next.IsCompleted())
    return (this with { Picker = null, ChosenFile = next.Result }, cmd);
```

## State Reducers — `TextInputState`, `TextAreaState`, `ListState`

Editing and selection rules as pure values, so a model owns its state and never needs a
stateful sub-program to manage it. Each widget delegates to its reducer, so storing the
widget and storing the state behave identically — they cannot drift:

### `TextInputState` — single-line

```csharp
sealed record Model(TextInputState Filter) : IModel
{
    public (IModel, ICmd?) Update(IMsg msg) => msg switch
    {
        KeyMsg k => (this with { Filter = Filter.HandleKey(k) }, null),
        _        => (this, null),
    };

    public IWidget View() => new TextInput(Filter.Value, cursorPosition: Filter.Cursor);
}
```

`HandleKey` covers insert, Backspace/Delete, Left/Right, `Ctrl+←`/`Ctrl+→` word jumps,
`Home`/`End` (and `Ctrl+A`/`Ctrl+E`), `Ctrl+W` delete-word, and `Ctrl+U`/`Ctrl+K`
kill-to-edge. `Insert(text)` handles paste. It returns the same instance when a key
changes nothing, so `ReferenceEquals` tells you whether to do more work.

`Cursor` is an index into `Value`, always on a grapheme cluster boundary — an emoji or
an accented letter moves and deletes as one unit, and a `with` expression that would
leave the cursor mid-cluster is normalised instead. It is not a column: a wide glyph is
two columns but one cursor step.

### `TextAreaState` — multi-line

Same editing rules, because every key that stays on one line is handed to
`TextInputState`. `TextAreaState` owns only what crosses lines: `Up`/`Down`, `Enter`
splitting a line, `Backspace` at column 0 joining onto the previous line, and `Delete`
at end-of-line pulling the next one up. `Insert(text)` splits a paste on newlines.

```csharp
var next = Body.HandleKey(key);          // Lines, CursorRow, CursorCol
string doc = next.Text();                 // joined with \n
```

`Lines` is never empty — an empty document is one empty line — and the cursor is
re-normalised on every `with`, so no copy lands off the end or inside a cluster.

### `ListState` — selection and scroll

Holds the item **count**, not the items, so it serves an array, a filtered view, or
virtualized pages equally. The model keeps the data and indexes it:

```csharp
sealed record Model(ListState Rows, string[] Items) : IModel
{
    public (IModel, ICmd?) Update(IMsg msg) => msg switch
    {
        WindowResizeMsg r => (this with { Rows = Rows.WithViewport(r.Height - Chrome) }, null),
        KeyMsg k          => (this with { Rows = Rows.HandleKey(k) }, null),
        _                 => (this, null),
    };
}
```

`HandleKey` covers `Up`/`Down`, `Home`/`End`, and `PageUp`/`PageDown`. Because
`ViewportHeight` lives in the state, `ScrollOffset` is maintained on every move rather
than being something you remember to recompute — a selection cannot scroll out of sight,
and an offset that would hide it is overridden. Set the viewport to 0 when nothing knows
it yet: scroll is then left alone and paging does nothing, since a page has no size.

`VisibleRange` gives the `(offset, length)` slice a view should draw.

> **Note:** the `List` widget cannot use the viewport half — a widget learns its region
> only at render time — so it leaves `ScrollOffset` to the model via
> `List.ComputeScrollOffset`. A model holding a `ListState` gets both.

## Commands

```csharp
Cmd.Quit()                                    // exit the program
Cmd.Msg(new SomeMsg())                        // synchronous follow-up message
Cmd.Run(async ct => { ... return msg; })      // async work → message
Cmd.Batch(cmd1, cmd2, cmd3)                   // run concurrently
Cmd.Sequence(cmd1, cmd2, cmd3)                // run serially
Cmd.Tick(TimeSpan, ts => new TickMsg(ts))     // delayed single fire
Cmd.Debounce(key, TimeSpan, ts => msg)        // debounced by key (last wins)
Cmd.Throttle(key, TimeSpan, ts => msg)        // throttled by key (first wins)
```

`Debounce` and `Throttle` are keyed because `Update` builds a new command on every
call. The rate-limit state belongs to the key, held by the running program, so
re-dispatching under the same key supersedes the pending window no matter how many
command instances were built along the way:

```csharp
public (IModel, ICmd?) Update(IMsg msg) => msg switch
{
    // One fetch after the selection settles, not one per keypress. The URL differs
    // per row, so the closure differs per call — only the key is stable.
    SelectionChangedMsg m => (this with { Index = m.Index },
        Cmd.Debounce("poster", TimeSpan.FromMilliseconds(150),
            _ => new LoadPosterMsg(Items[m.Index].PosterUrl))),
    _ => (this, null),
};
```

Keys are plain strings, namespaced by the application the same way subscription keys
are (`"shelf/poster"`). A superseded or throttled call produces no message at all.

## Subscriptions

For continuous data streams, implement `IHasSubscriptions`:

```csharp
public IReadOnlyList<(string Key, ISub Sub)> Subscriptions() =>
[
    ("timer", Sub.Interval(TimeSpan.FromSeconds(1), ts => new TickMsg(ts))),
    ("data",  Sub.FromAsyncEnumerable(ct => GetDataStream(ct))),
];
```

## Samples

| Sample | Description |
|--------|-------------|
| [`ConsoleForge.Gallery`](samples/ConsoleForge.Gallery/) | Widget showcase — widgets, 6 themes, mouse support, IComponent pages |
| [`ConsoleForge.TodoApp`](samples/ConsoleForge.TodoApp/) | Todo list — browse, add, toggle, delete |
| [`ConsoleForge.SysMonitor`](samples/ConsoleForge.SysMonitor/) | Live system stats via async subscriptions |

```bash
dotnet run --project samples/ConsoleForge.Gallery
```

## Project Structure

```
src/ConsoleForge/
  Core/       App, IModel, IMsg, ICmd, IComponent, KeyMap, KeyPattern, Component,
              Cmd, CmdDispatcher, Sub, FocusManager, Renderer, ViewDescriptor,
              Messages, TextInputState, TextAreaState, ListState, Attributes
  Layout/     IWidget, IContainer, IFocusable, IMeasurable, ISingleBodyWidget,
              ILayeredContainer, IRawEscapePayload, LayoutEngine, LayoutSolver,
              RenderContext, SubRenderContext, TextUtils, SizeConstraint, Region
  Styling/    Style, Theme, ThemeExtensions, Color, Borders, BorderSpec, ColorProfile
  Terminal/   ITerminal, AnsiTerminal, TerminalCapabilities, KittyProtocol,
              Termios (Unix), WindowsConsole
  Widgets/    TextBlock, TextInput, TextArea, List, Table, Checkbox, Tabs,
              ProgressBar, Spinner, BorderBox, Container, Modal, ZStack,
              ImageWidget, HorizontalShelf

src/ConsoleForge.SourceGen/      Roslyn incremental generator (netstandard2.0)

tests/ConsoleForge.Tests/          662 unit tests (xUnit v3)
tests/ConsoleForge.SourceGen.Tests/ 12 generator snapshot tests
tests/ConsoleForge.Benchmarks/     BenchmarkDotNet render + cmd benchmarks

samples/
  ConsoleForge.Gallery/          Widget browser with IComponent page architecture
  ConsoleForge.SysMonitor/       System monitor dashboard
  ConsoleForge.TodoApp/          Todo list app
```

## Building

```bash
dotnet build ConsoleForge.slnx
dotnet test  ConsoleForge.slnx
dotnet run --project samples/ConsoleForge.Gallery
```

Requires **.NET 8 SDK**. Single dependency: `System.Reactive`.

## Performance

Double-buffered cell diff + per-widget render cache. Benchmarks (80×24 terminal):

| Scenario | Time | Allocations |
|----------|------|-------------|
| 20-widget cold render | ~20 µs | 68 KB |
| 20-widget warm (no changes) | ~21 µs | 14 KB |
| 1,000-row Table (24 visible) | ~30 µs | 67 KB |
| Dirty-skip (model unchanged) | 3 ns | 0 |

## License

MIT — see [LICENSE](LICENSE).
