# State reducers

Editing and selection are the two places a terminal UI usually grows hidden state: a text
box that owns its cursor, a list that owns its scroll position. ConsoleForge puts both in
your model instead, as plain values.

Three reducers live in `ConsoleForge.Core`:

| Reducer | Owns |
|---|---|
| <xref:ConsoleForge.Core.TextInputState> | One line of text and a cursor. |
| <xref:ConsoleForge.Core.TextAreaState> | Many lines, a cursor row and column. |
| <xref:ConsoleForge.Core.ListState> | A count, a selected index, a scroll offset, a viewport. |

Each is an immutable record. Every method returns a new one. The matching widget renders
what you pass it and delegates its key handling to the same reducer, so the widget and the
state cannot drift apart.

## TextInputState

Store it in your model, fold keys into it, and read it back out in `View()`:

```csharp
sealed record Model(TextInputState Filter) : IModel
{
    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
    {
        KeyMsg k => (this with { Filter = Filter.HandleKey(k) }, null),
        _        => (this, null),
    };

    public IWidget View() => new TextInput(Filter.Value, cursorPosition: Filter.Cursor);
}
```

`HandleKey` covers the usual bindings: arrows, Home and End, Backspace and Delete, and word-
wise deletion. `Insert`, `DeleteWordBackward`, `DeleteWordForward` and `Clear` are there when
you want to drive it yourself.

`Cursor` is an index into `Value` in UTF-16 code units, always kept on a grapheme cluster
boundary. An emoji or an accented letter moves and deletes as one unit, never leaving half a
surrogate pair behind. It is **not** a column: a wide glyph occupies two columns but one
cursor step.

Out-of-range or mid-cluster values are normalised on construction, so a `with` expression
cannot produce a broken state — `Filter with { Value = shorterText }` pulls the cursor back
into range for you rather than leaving it dangling.

## TextAreaState

The same shape across `Lines`, with `CursorRow` and `CursorCol`. `Text()` joins the lines
back together, taking the newline you want:

```csharp
var body = Draft.Text();            // "\n"
var crlf = Draft.Text("\r\n");
```

`SplitLine`, `JoinWithPrevious` and `JoinWithNext` are the Enter and Backspace cases spelled
out, and `MaxLines` caps growth. `CurrentLine` and `IsEmpty` save you indexing by hand.

## ListState

`ListState` is the one that earns its keep, because selection and scrolling have to agree.
It holds `Count`, `SelectedIndex`, `ScrollOffset` and `ViewportHeight`, and keeps the
selection visible whenever any of them change:

```csharp
sealed record Model(ListState Rows, string[] Items) : IModel
{
    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
    {
        // The viewport is a layout result, so feed it back in on resize.
        WindowResizeMsg r => (this with { Rows = Rows.WithViewport(r.Height - 2) }, null),
        KeyMsg k          => (this with { Rows = Rows.HandleKey(k) }, null),
        _                 => (this, null),
    };

    public IWidget View() =>
        new List(Items, selectedIndex: Rows.SelectedIndex, scrollOffset: Rows.ScrollOffset);
}
```

`MoveUp`, `MoveDown` and `MoveTo` move the selection; `WithCount` tells it the list changed
length; `WithViewport` tells it how many rows are on screen. `ViewportHeight` is a layout
result rather than something you decide, which is why it arrives from a resize message.

Keep `Count` in step with the collection you are actually rendering. `WithCount` clamps the
selection for you when the list shrinks.
