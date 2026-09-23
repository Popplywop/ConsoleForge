---
name: add-widget
description: Adding a new built-in widget (or changing an existing widget's Render/Measure/Update) in src/ConsoleForge/Widgets.
triggers:
  - "new widget"
  - "add widget"
  - "IWidget"
  - "IFocusable"
  - "Render"
  - "Measure"
edges:
  - target: context/rendering.md
    condition: for how layout, Measure and the render cache treat the widget
  - target: context/conventions.md
    condition: for the widget shape and the verify checklist
  - target: patterns/add-state-reducer.md
    condition: when the widget is interactive and needs editing/selection/scroll rules
  - target: patterns/perf-sensitive-change.md
    condition: always — any IWidget.Render change needs a benchmark run
  - target: patterns/debug-rendering-and-layout.md
    condition: when the widget renders wrong, stale or in the wrong place
last_updated: 2026-09-22
mex:
  id: mx_01M363AXN8RXSSRHXPC0WG806F
  type: pattern
  status: promoted
  revision: 5
  title: add-widget
  grounds_to:
    - node: method:5689a1ac71e6f00f9a1c9f5961276f49
      fingerprint: mh:64:7b226d696e68617368223a5b31323931353336352c363438323733352c323330363739372c35323739333833312c3135303333373830382c32383836323132392c31343732323234312c343137323934392c36313539343433332c36333536323933362c373030313839342c363734383038362c35323838333337352c33383133383336312c38333638393339322c34393535323839362c39323938363132392c31393833303937382c33333435393930312c33333437343939322c34333935383833332c3131333733313339382c32323333343539382c33333936333732352c31323435383538362c33353630303631322c323034383432372c393938373439322c34353239383230392c333038333636392c36363032303931382c32393433393935352c333031303731342c353236313731332c32393635353335302c32363038343030352c33313830353031302c32353030313332372c35303530313331312c3133323233333533372c33303431373031362c3230393334313638322c3832363536312c34363537383637302c3235313830342c393232333838372c35323731313739322c333235323338352c3132323930393536302c31383735373833302c37343539303032322c32373537313730332c31383632323237342c383036313235342c31333939393539332c33353036333337362c35363938353330392c35323738343333302c383931313832342c31303831393534332c34383536313233342c32303233393733322c32333032383630312c31313732363932355d2c226e65696768626f7273223a5b5d2c22746f6b656e436f756e74223a3335307d
      bodyHash: 6f6ac8029da48da1dc402a1cfb6596c25198c845d60aff614f36ce53a3b94800
    - node: method:f29bb931e75016dde6b35cebcff27956
      fingerprint: mh:64:7b226d696e68617368223a5b3135303434373334362c31373837383234352c33323736303233312c363037383133322c32313337393437342c31343131353131322c32393832353432372c35333633363631312c37393630333235302c32373239303233332c31373931303839362c363734383038362c35323838333337352c313439363835312c31373030383434372c38313132303838342c35353736363634342c35383235303433332c313739363232322c33333437343939322c34383138303736352c3137363130323036302c38343436363135382c36313639373230372c33353934353230322c3134353337373332302c323034383432372c37373233373133352c38323838353231382c313634363838322c36343631393937372c36383832363330362c333031303731342c37303634383735302c32353535303338362c31323235323632312c33313830353031302c36303238383134312c383030343030332c3136393636303031382c3130363738393736362c3133383834393932322c39353131353837332c3337363838333133362c32353134353335302c3232303334303834322c38393234343830392c363435343633362c36383035383334322c32353335343035372c363339373538302c3131343830333933302c3130323534393331372c31373531313233392c33333131393633342c35333336303233352c3131373238333032392c31303636333331302c35373230313133312c38303636353635392c32343935373832312c32373031343337372c31363233373233322c31313732363932355d2c226e65696768626f7273223a5b226d6574686f643a3730376464383763393534646238313637316432356431666433363062626133225d2c22746f6b656e436f756e74223a3131307d
      bodyHash: 53fda245512dc8a9de2d990b2c179316736e43620e249f8054fe08033c9be7ad
  relations:
    - type: related_to
      target: mx_01M363AX7N0N5PKS6YX6NRMDGG
      note: for the widget shape and the verify checklist
    - type: related_to
      target: mx_01M363AXMCYE8RX75CSZ3JVBR9
      note: when the widget is interactive and needs editing/selection/scroll rules
    - type: related_to
      target: mx_01M363AXR4CSBVN1XH7EZGMJZ7
      note: always — any IWidget.Render change needs a benchmark run
    - type: related_to
      target: mx_01M363AXP8X6EYXEBYJ4KJQNEV
      note: when the widget renders wrong, stale or in the wrong place
---

# Add a Widget

## Context
Load `context/rendering.md` and `context/conventions.md`. Use `Widgets/Checkbox.cs` as the template for a leaf focusable widget and `Widgets/Container.cs` / `BorderBox.cs` for composites. A composite reports its size for `Auto` the way [`Container.Measure()`](mex://method:5689a1ac71e6f00f9a1c9f5961276f49) does.

## Steps
1. Create `src/ConsoleForge/Widgets/Foo.cs` as `public sealed record Foo : IWidget` (or `IFocusable`), namespace `ConsoleForge.Widgets`.
2. Properties are `{ get; init; }`: `Width`/`Height` (`SizeConstraint.Flex(1)` unless naturally sized), `Style` (`Style.Default`), widget-specific data. For focusables add `HasFocus` and `FocusKey` (both `init`).
3. Add a parameterless constructor and a positional constructor with defaulted parameters.
4. `Render(IRenderContext ctx)`: guard `ctx.Region` for zero size, resolve style with `Style.Inherit(HasFocus ? ctx.Theme.FocusedStyle : ctx.Theme.BaseStyle)`, measure text with `TextUtils.VisualWidth` / `TruncateToWidth`, write through `ctx.Write`, pad the rest of the row so the background fills.
5. If it should support `SizeConstraint.Auto`, implement `IMeasurable.Measure(availableWidth, availableHeight)` — pure, cheap, never exceeding the offer.
6. If it holds children, implement `IContainer` (`Children`), `ILayeredContainer` (`Layers`) or `ISingleBodyWidget` (`Body`), and lay them out through `LayoutSolver`, not by hand.
7. If focusable, `Update(KeyMsg)` returns `(this with {...}, null)` or `(this, null)`; put non-trivial rules in a reducer (`patterns/add-state-reducer.md`).
8. XML-doc every public member; add `tests/ConsoleForge.Tests/Widgets/FooTests.cs`; add it to the Gallery (`samples/ConsoleForge.Gallery/Pages/`) and to the README widget list.

## Gotchas
- Focus collection and click hit-testing ([`FocusManager.FindFocusableAt()`](mex://method:f29bb931e75016dde6b35cebcff27956)) only descend into `IContainer`, `ILayeredContainer` and `ISingleBodyWidget`. A composite that hides children elsewhere makes them unfocusable and unclickable. Overlapping hits: last in depth-first order wins.
- The model sets `HasFocus` when building the view; the widget never computes it.
- Don't hold cursor/scroll/selection inside the widget between frames — it is rebuilt from the model every `View()`.
- Wide characters: never use `string.Length` for columns.
- The render cache reuses cells for an unchanged widget; a widget whose output depends on something not in its properties will render stale.
- Changing `IWidget` or `IRenderContext` themselves needs discussion first (root `AGENTS.md`).

## Verify
- `dotnet build ConsoleForge.slnx` — no new CS1591.
- `dotnet test tests/ConsoleForge.Tests --filter "FullyQualifiedName~FooTests"`, then the full suite.
- Render tests use `VirtualTerminal` + `TestHelpers.StripAnsi`; add a `PaddingMarginTests`-style case if margins/padding apply.
- Tier 3 benchmark before/after if an existing widget's `Render` changed (`patterns/perf-sensitive-change.md`).
- `CHANGELOG.md` Unreleased entry.

## Debug
See `patterns/debug-rendering-and-layout.md`.

## Update Scaffold
- [ ] Update `.mex/ROUTER.md` "Current Project State" if what's working/not built has changed
- [ ] Update any `.mex/context/` files that are now out of date
- [ ] If this is a new task type without a pattern, create one in `.mex/patterns/` and add to `INDEX.md`
