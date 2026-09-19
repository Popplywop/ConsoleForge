# Layout

Layout runs in two phases, and they are strictly separated. First `LayoutEngine` resolves
every widget's `SizeConstraint` into a concrete `Region`. Only then does each widget write
cells into the region it was given. Nothing measures during rendering, and rendering never
changes a size.

## Constraints

A widget's `Width` and `Height` are each a <xref:ConsoleForge.Layout.SizeConstraint>. There
are five:

| Constraint | Meaning |
|---|---|
| `SizeConstraint.Fixed(n)` | Exactly `n` columns or rows. |
| `SizeConstraint.Flex(weight)` | A share of what is left over, in proportion to `weight`. |
| `SizeConstraint.Auto` | As much as the content wants. |
| `SizeConstraint.Min(n, inner)` | `inner`, but never below `n`. |
| `SizeConstraint.Max(n, inner)` | `inner`, but never above `n`. |

`Min` and `Max` wrap another constraint rather than replacing it, so they compose:
`SizeConstraint.Max(40, SizeConstraint.Auto)` is a content size capped at 40 columns.

Fixed sizes are taken out first, then what remains is divided among the flex weights. Two
children at `Flex(1)` and `Flex(3)` split the leftover one quarter to three quarters.

```csharp
new Container(Axis.Horizontal, [
    new Container(Axis.Vertical, width: SizeConstraint.Fixed(24), children: [sidebar]),
    new Container(Axis.Vertical, width: SizeConstraint.Flex(1),   children: [main]),
])
```

## Auto needs IMeasurable

`Auto` asks the widget how big it wants to be, which only works if the widget can answer.
That is what <xref:ConsoleForge.Layout.IMeasurable> is for: one `Measure(availableWidth,
availableHeight)` method returning the size the content wants.

Widgets that implement it are marked in the [widget catalogue](widgets.md). **A widget that
does not implement `IMeasurable` treats `Auto` as `Flex(1)`** — it is not an error, and it
will not warn you, so if an `Auto` size behaves like a flex share, that is why.

### The flex-inside-auto trap

A flex child contributes nothing along the stacking axis. Flex means "fill what is left
over", which is not a content size — so an `Auto` container whose children are all flex
measures to **zero** along that axis and disappears:

```csharp
// Wrong: Auto height, but the child only knows how to fill.
new Container(Axis.Vertical, height: SizeConstraint.Auto, children: [
    new Container(Axis.Vertical, height: SizeConstraint.Flex(1), children: [body]),
])
```

Give the outer container a `Fixed` or `Flex` height instead, or give the child a size it can
actually report. Across the stacking axis the rule reverses: a flex child does take the full
width on offer, because that is what it will fill.

## Margin and padding

Padding is space inside a widget's region; margin is space outside it. Both are enforced by
the layout engine, not drawn by the widget:

```csharp
Style.Default.Padding(1)          // all four sides
Style.Default.Padding(0, 2)       // vertical, horizontal
Style.Default.Margin(1, 2, 1, 2)  // top, right, bottom, left
```

A `BorderBox` insets its body by its border and padding together, and measures to at least
the width its title needs between the corners — so an `Auto` box never clips its own title.

## Regions and rendering

Once sizes resolve, each widget gets an `IRenderContext` scoped to its `Region` and writes
cells into it. Two rules hold there:

- **Render must be free of side effects.** No writing to stdout or stderr, no mutating the
  model, no I/O. The renderer may skip a widget whose region and inputs have not changed.
- **A widget cannot draw outside its region.** `SubRenderContext` clips children, which is
  what makes `Container` scrolling and `Modal` layering work.

The renderer double-buffers and diffs per cell, with per-widget dirty tracking, so a frame
in which one character changed writes one character to the terminal.
