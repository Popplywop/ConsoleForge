# Data Model: Terminal UI Framework (ConsoleForge)

**Feature**: specs/001-tui-framework
**Date**: 2026-04-12

---

## Core Entities

### 1. `IMsg` — Message

The base marker interface for all messages flowing through the event loop.

**Fields**: none (marker interface)

**Concrete built-in implementations**:
- `KeyMsg` — a keypress event: `ConsoleKey Key`, `char? Character`, `bool Shift`,
  `bool Alt`, `bool Ctrl`
- `WindowResizeMsg` — terminal dimensions changed: `int Width`, `int Height`
- `FocusChangedMsg` — focus moved: `IWidget? Previous`, `IWidget? Next`
- `QuitMsg` — signals the program loop to exit cleanly
- `BatchMsg` — wraps `ICmd[]`; runtime fans out all commands concurrently
- `SequenceMsg` — wraps `ICmd[]`; runtime runs commands serially

**State transitions**: none (immutable value)

**Validation rules**: none (marker interface)

---

### 2. `ICmd` — Command

A deferred async operation that produces exactly one `IMsg` when complete.

```
ICmd = Func<IMsg>
```

Built-in factory methods (on `Cmd` static class):
- `Cmd.Quit()` → returns `QuitMsg` immediately
- `Cmd.Batch(params ICmd[] cmds)` → null-filtered; if 0 returns null;
  if 1 returns that cmd; if 2+ wraps in `BatchMsg`
- `Cmd.Sequence(params ICmd[] cmds)` → same null-filtering, wraps in `SequenceMsg`
- `Cmd.Tick(TimeSpan interval, Func<DateTimeOffset, IMsg> fn)` → fires once after
  interval; returns `fn(timestamp)`
- `Cmd.None` → `null` constant; framework skips dispatch

**State transitions**: none (pure function value)

---

### 3. `IModel` — Application Model

The top-level interface every user-defined application model must implement.

```
interface IModel
{
    ICmd? Init();
    (IModel Model, ICmd? Cmd) Update(IMsg msg);
    ViewDescriptor View();
}
```

**Relationships**:
- Returns `ViewDescriptor` from `View()` (see below)
- Returns `ICmd?` from `Init()` and `Update()` — null means no-op

**Validation rules**:
- `Update` MUST return a non-null `IModel` (the updated model)
- Framework does not enforce immutability; convention is to return a new
  record/struct copy (using `with` expression on C# records)

---

### 4. `ViewDescriptor` — Rendered View

An immutable value type capturing the fully-rendered string output and terminal
mode flags for a single frame.

**Fields**:
- `string Content` — the rendered string to write to the terminal
- `bool AlternateScreen` — whether to use the alternate screen buffer
- `string? Title` — optional terminal window title to set this frame
- `CursorDescriptor Cursor` — cursor visibility and position

**`CursorDescriptor`** (nested record):
- `bool Visible` (default `true`)
- `int Col`, `int Row` — cursor position after render (default 0, 0)

**State transitions**: immutable; produced fresh each `View()` call

---

### 5. `Style` — Visual Style

An immutable value type (readonly struct) controlling the visual appearance of
any rendered string. Carries a bitmask tracking which properties are explicitly
set vs. absent.

**Properties** (all nullable / unset by default):
- `IColor? Foreground`
- `IColor? Background`
- `bool? Bold`, `bool? Italic`, `bool? Underline`, `bool? Strikethrough`,
  `bool? Faint`, `bool? Blink`, `bool? Reverse`
- `int? PaddingTop`, `int? PaddingRight`, `int? PaddingBottom`, `int? PaddingLeft`
- `int? MarginTop`, `int? MarginRight`, `int? MarginBottom`, `int? MarginLeft`
- `int? Width`, `int? Height` — explicit size override (clamps/pads rendered output)
- `HorizontalAlign? Align` — `Left | Center | Right`
- `BorderSpec? Border` — see BorderSpec entity
- `bool? BorderTop`, `bool? BorderRight`, `bool? BorderBottom`, `bool? BorderLeft`
  — per-side enable/disable
- `IColor? BorderForeground`, `IColor? BorderBackground`

**Bitmask**: `long _props` — one bit per property. `Style.Inherit(parent)` copies
only bits set in parent but absent in self (excluding margins + padding).

**Key methods**:
- `Style.Render(string text, ColorProfile profile)` → `string` — applies all set
  properties; returns `text` unchanged if `_props == 0` (fast path)
- `Style.Inherit(Style parent)` → `Style` — returns new Style with inherited values
- Per-property builder methods: `Style.Foreground(IColor c)` → returns new Style
  with that property set

**Relationships**: used by every Widget; composed into Theme

---

### 6. `Theme` — Global Style Defaults

A named, immutable collection of base styles applied as defaults across all
widgets. Applied via `Style.Inherit()` at render time.

**Fields**:
- `string Name`
- `Style BaseStyle` — default text style
- `Style BorderStyle` — default border style
- `Style FocusedStyle` — additional style applied to the focused widget
- `Style DisabledStyle` — applied to disabled/non-interactive widgets
- `Dictionary<string, Style> Named` — optional named style slots for custom widget types

**Relationships**: held by `Program`; injected into the render context; widgets
call `style.Inherit(theme.BaseStyle)` before rendering

---

### 7. `IWidget` — Widget (Base Abstraction)

The base abstraction for all visual elements. Both leaf widgets and containers
implement this interface.

```
interface IWidget
{
    Style Style { get; }
    SizeConstraint Width { get; }
    SizeConstraint Height { get; }
    void Render(IRenderContext ctx);
}
```

**Relationships**:
- `Render` is called by the layout engine after it resolves positions and sizes
- `IRenderContext` carries the allocated region (col, row, width, height) plus
  the `Theme` and `ColorProfile`

**Focusable variant**:
```
interface IFocusable : IWidget
{
    bool HasFocus { get; set; }
    void OnKeyEvent(KeyMsg key, Action<IMsg> dispatch);
}
```

**State transitions** for focusable widgets:
- `HasFocus = false` → `HasFocus = true` (tab traversal or explicit focus call)
- `HasFocus = true` → processes `KeyMsg` via `OnKeyEvent`

---

### 8. `Container` — Layout Container

A composite widget that arranges children along one axis.

**Fields**:
- `Axis Direction` — `Horizontal | Vertical`
- `IWidget[] Children`
- `Style Style`
- `SizeConstraint Width`, `SizeConstraint Height`
- `bool Scrollable` — if true, content overflowing the allocated height/width
  is clipped and a scroll offset is tracked internally
- `int ScrollOffset` — current scroll position (0-based row/col index into children)

**Relationships**:
- Contains `IWidget[]` children (recursive composition)
- Passed to `LayoutEngine.Resolve()` to produce `ResolvedLayout`

---

### 9. `SizeConstraint` — Size Specification

A discriminated union representing how a widget's dimension should be resolved.

**Cases**:
- `Fixed(int n)` — exact character count
- `Flex(int weight = 1)` — proportional share of free space
- `Min(int min, SizeConstraint inner)` — lower bound on an inner constraint
- `Max(int max, SizeConstraint inner)` — upper bound on an inner constraint
- `Auto` — size to content (width: longest child line; height: child count)

---

### 10. `ResolvedLayout` — Layout Resolution Output

The output of `LayoutEngine.Resolve()`. Maps each widget to its allocated region.

**Fields**:
- `Dictionary<IWidget, Region> Allocations`

**`Region`** (value record):
- `int Col`, `int Row` — top-left corner (absolute terminal coordinates)
- `int Width`, `int Height` — allocated dimensions

---

### 11. `LayoutEngine` — Constraint Resolver

Stateless service. Takes a root widget and terminal dimensions, returns a
`ResolvedLayout`.

**Entry point**:
```
ResolvedLayout Resolve(IWidget root, int terminalWidth, int terminalHeight)
```

**Algorithm** (two-pass per axis, applied recursively):
1. Assign fixed sizes; sum fixed sizes along the axis
2. Compute remaining free space
3. Distribute free space among flex children proportional to weight
4. Apply min/max bounds; redistribute clamped excess
5. Rounding: remainder goes to the last flex child
6. Recurse into child containers with their allocated dimensions

---

### 12. `Program` — Runtime

The top-level runtime that owns the event loop, renderer, and terminal.

**Fields**:
- `IModel _model` — current model (replaced on each Update)
- `ITerminal _terminal`
- `Theme _theme`
- `ColorProfile _colorProfile`
- Channel/queue for messages (`Channel<IMsg>`)
- Channel/queue for commands (`Channel<ICmd>`)
- `ViewDescriptor _lastView` — previous frame (for diff)
- `Timer _renderTimer` — drives flush at ≤60 fps

**Lifecycle**:
1. `Program.Run(IModel initialModel)` — synchronous; blocks until quit
2. On start: detect terminal, enter raw mode, detect color profile, send
   `WindowResizeMsg` + `ColorProfileMsg`, call `model.Init()`, dispatch Cmd
3. Event loop: dequeue `IMsg` → `model.Update(msg)` → new model + Cmd → render
4. Render timer: diff `ViewDescriptor` → if changed, flush to terminal
5. On quit: `ExitRawMode()`, restore cursor, exit alternate screen

---

### 13. `ITerminal` — Terminal Abstraction

Thin seam over the physical terminal. Two implementations:
`AnsiTerminal` (production) and `VirtualTerminal` (test).

**Interface**:
```
interface ITerminal : IDisposable
{
    int Width { get; }
    int Height { get; }
    void Write(string ansiText);       // write pre-rendered ANSI string
    void Clear();
    void Flush();
    void SetTitle(string title);
    void SetCursorVisible(bool visible);
    void SetCursorPosition(int col, int row);
    void EnterAlternateScreen();
    void ExitAlternateScreen();
    void EnterRawMode();
    void ExitRawMode();
    IObservable<InputEvent> Input { get; }
    event EventHandler<TerminalResizedEventArgs> Resized;
}
```

**`InputEvent`** union:
- `KeyInputEvent(KeyMsg Key)`
- `ResizeInputEvent(int Width, int Height)`

---

## Entity Relationships

```
Program
  ├── IModel (user-defined, implements Init/Update/View)
  ├── ITerminal (AnsiTerminal | VirtualTerminal)
  ├── Theme
  ├── LayoutEngine (stateless)
  └── Renderer (holds last ViewDescriptor for diff)

IModel.View() → ViewDescriptor
IModel.Update(IMsg) → (IModel, ICmd?)

IWidget tree:
  Container
    ├── Container (nested)
    └── IWidget leaves (TextBlock, BorderBox, List, TextInput, ...)

LayoutEngine.Resolve(root: IWidget) → ResolvedLayout
  (ResolvedLayout maps IWidget → Region)

Style
  ← Theme (base styles)
  ← IWidget (per-widget override)
  Style.Inherit(parent) for theme application
```
