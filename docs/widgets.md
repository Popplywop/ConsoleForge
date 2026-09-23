# Widgets

A widget is a value describing one frame. You construct it in `View()`, the renderer draws
it, and it is discarded. It has no fields you mutate and no lifecycle you hook — everything
that persists lives in your model.

All sixteen ship in `ConsoleForge.Widgets`, except the state reducers, which live in
`ConsoleForge.Core`.

## The catalogue

| Widget | For | Sizes to content |
|---|---|:---:|
| `TextBlock` | A run of text, wrapped to its region. | yes |
| `TextInput` | One line of editable text. | |
| `TextArea` | Multiple lines of editable text. | |
| `List` | A vertical list of strings with a selection. | |
| `Table` | Columns and rows with a selected row. | |
| `Checkbox` | A labelled on/off box. | |
| `Tabs` | A row of labels with one active. | |
| `ProgressBar` | A proportion of a whole. | |
| `Spinner` | Work is happening, length unknown. | yes |
| `BorderBox` | A titled frame around one child. | yes |
| `Container` | Children stacked along an axis. | yes |
| `ZStack` | Children stacked front to back. | yes |
| `Modal` | A centred dialog over a backdrop. | |
| `ImageWidget` | A PNG, drawn inline where the terminal allows. | |
| `HorizontalShelf` | A paged strip of cover art. | yes |

"Sizes to content" means the widget implements <xref:ConsoleForge.Layout.IMeasurable>, so
`SizeConstraint.Auto` asks it how much room it wants. A widget that does not implement it
treats `Auto` as `Flex(1)`. See [Layout](layout.md).

## Focus

`TextInput`, `TextArea`, `List`, `Checkbox` and `Tabs` implement `IFocusable`. They carry a
`HasFocus` flag, but **you never set it**: the framework tracks which widget holds focus and
pushes the index into your model as a `FocusIndexChangedMsg`. Give a widget a stable
`FocusKey` when the tree changes shape between frames and you need focus to survive.

## Text

`TextBlock` takes its text positionally, and wraps to whatever region layout gives it:

```csharp
new TextBlock("Ready.", style: Style.Default.Faint(true))
```

`TextInput` and `TextArea` render editing state. They hold `Value`/`Lines`, a cursor, and
nothing else — the cursor arithmetic belongs to the reducers in
[State reducers](state-reducers.md), which the widgets delegate to so the two cannot drift:

```csharp
new TextInput(Filter.Value, cursorPosition: Filter.Cursor)
```

## Selection

`List` takes its items and the selected index. It renders only the visible rows, so a
thousand items cost what twenty cost:

```csharp
new List(Items, selectedIndex: Rows.SelectedIndex, scrollOffset: Rows.ScrollOffset)
```

> [!NOTE]
> `ConsoleForge.Widgets.List` collides with `System.Collections.Generic.List<T>`, which
> `ImplicitUsings` brings in. In a file that uses both, alias it:
> `using List = ConsoleForge.Widgets.List;`

`Table` is the same idea with columns. A `TableColumn` carries a header and an optional
fixed `Width`; leave the width at `0` and the column shares what is left.

`Tabs` takes `Labels` and an `ActiveIndex`. `Checkbox` takes a `Label` and `IsChecked`, and
lets you replace the `CheckedChar` / `UncheckedChar` glyphs.

## Progress

`ProgressBar` reads `Value` against `Maximum` and can `ShowPercent`. `Spinner` takes the
frame index — it does not animate itself, because nothing in a ConsoleForge app animates
itself. Drive it from a `Cmd.Tick` or a `Sub.Interval`, as in
[Commands and subscriptions](commands-and-subscriptions.md):

```csharp
new Spinner(Frame, label: "Loading")
```

## Structure

`Container` stacks children along an `Axis`, and is the widget you will reach for most:

```csharp
new Container(Axis.Vertical, [
    new TextBlock("Title"),
    new TextBlock("Body"),
])
```

`BorderBox` frames a single child and draws a title into the top edge. `ZStack` layers
children front to back, the last one on top. `Modal` is the dialog case of that — a centred
`DialogWidth` × `DialogHeight` panel over an optional backdrop.

`Container` also does scrolling: set `Scrollable` and drive `ScrollOffset` from your model.

## Images

`ImageWidget` draws a PNG through the Kitty graphics protocol where the terminal supports
it, and falls back to half-block cells with 24-bit colour everywhere else. It takes either
`PngData` or `RgbaData`, and you can pin the `RenderMode` rather than letting
`TerminalCapabilities` decide.

On a Kitty terminal the image is uploaded the first time it is drawn, which puts the upload's
latency on the frame it is meant to appear in. Under tmux that shows: the frame's cells arrive
before the image does. Return `Cmd.Preload` from the `Update` that receives the bytes to
upload them then instead — give it a payload built from the same bytes and capabilities the
widget gets:

```csharp
var cmd = caps.SupportsKittyGraphics
    ? Cmd.Preload(KittyProtocol.CreatePayload(png, caps))
    : Cmd.None;
```

`HorizontalShelf` is a paged, virtualised strip of `ShelfItem` cards — built for poster rows
wider than the terminal.

## Styling

Every widget takes a `Style`. Styles are values and compose by chaining:

```csharp
Style.Default.Foreground(Color.FromHex("#4EC9B0")).Bold(true).Padding(1)
```

Colours you do not set fall through to the active `Theme`, so a widget that sets no
foreground follows the theme when the user switches it at runtime.
