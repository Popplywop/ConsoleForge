---
name: conventions
description: How code is written in this project — naming, structure, patterns, and style. Load when writing new code or reviewing existing code.
triggers:
  - "convention"
  - "pattern"
  - "naming"
  - "style"
  - "how should I"
  - "what's the right way"
edges:
  - target: context/architecture.md
    condition: when a convention depends on understanding the system structure
  - target: context/decisions.md
    condition: when a convention seems arbitrary and the reason matters
  - target: patterns/add-widget.md
    condition: when the code being written is a widget
  - target: patterns/add-state-reducer.md
    condition: when the code being written changes editing, selection or scroll rules
grounds_to:
  - node: method:609fa0da2fad3f78351e138cd0c3e671
    fingerprint: mh:64:7b226d696e68617368223a5b3134303237393036312c34313233303035322c33313930363737342c3138383430363032342c31393637303235392c3236303439333634342c36303431303830302c343137323934392c33313130313639392c3336323938343734332c3339323234372c3132353037333139332c37323039333639302c33383133383336312c31373030383434372c39373733353238392c36343432343631362c33303130323934322c39353933303538332c3132313730393935382c3232303639353831372c3137363130323036302c313830393030342c32313833393533372c33353934353230322c38303732363536352c323034383432372c35313532323032362c3139333137363431302c32353336353634352c373236393034312c36383832363330362c343431393134332c31393335353334362c34333235323031372c3138333532353737352c3134353331383038342c36303238383134312c3230303035363937382c3130363138313433352c36353530383733302c3137383737363733372c31333034363236372c3138313936303338392c32353134353335302c3136313534383036302c3236373033303030372c35313139333435372c36383035383334322c3132303632313938342c3130353630323034362c3138303032303032332c31383737353335302c3234373334343536372c31333939393539332c35343238333536332c3131373238333032392c33333239313739342c3130333138383835352c38373831373638332c37383135313335382c34363333373838382c3134303834313135352c31313732363932355d2c226e65696768626f7273223a5b5d2c22746f6b656e436f756e74223a37347d
    bodyHash: f9de3dd8ee71a1d0f8ab0ef1f53e12be144d4bf6add63213dbace46e26bdaf47
last_updated: 2026-09-22
mex:
  id: mx_01M363AX7N0N5PKS6YX6NRMDGG
  type: convention
  status: promoted
  revision: 4
  title: conventions
  relations:
    - type: related_to
      target: mx_01M363AX3KJ8JPXRY5W6Z49DY8
      note: when a convention depends on understanding the system structure
    - type: related_to
      target: mx_01M363AXN8RXSSRHXPC0WG806F
      note: when the code being written is a widget
    - type: related_to
      target: mx_01M363AXMCYE8RX75CSZ3JVBR9
      note: when the code being written changes editing, selection or scroll rules
---

# Conventions

The repo-root `AGENTS.md` is the enforced contract; this file condenses it and records where current code differs.

<!-- mex:entity
id: mx_01M363AX6QDKPT15ADRPSP6RXA
type: convention
status: promoted
revision: 1
-->
## Naming
- Messages: `PascalCase` + `Msg`, declared as `sealed record ... : IMsg` (e.g. `FocusRequestedMsg(string Key)`).
- `[DispatchUpdate]` handlers: `On{MsgName-without-Msg}` returning `(IModel, ICmd?)` — `NavUpMsg` → `OnNavUp()` or `OnNavUp(NavUpMsg msg)`. A different name is silently not dispatched.
- Commands: factories on the static `Cmd` class; a type implementing `ICmd` directly gets a `Cmd` suffix.
- Reducers: `{Widget}State` in `ConsoleForge.Core` (`TextInputState`, `TextAreaState`, `ListState`).
- Private fields `_camelCase`; locals/params `camelCase`.
- Tests mirror source path: `src/ConsoleForge/Widgets/Foo.cs` → `tests/ConsoleForge.Tests/Widgets/FooTests.cs`; test names read `Subject_Condition` / `Action_Result` (e.g. `Backspace_RemovesChar`).

<!-- mex:entity
id: mx_01M363AX5XYCX69NZ2S289SNTR
type: convention
status: promoted
revision: 1
-->
## Structure
- Namespaces follow folders: `Core` (runtime, messages, reducers, keys), `Layout` (interfaces, solver, render contexts), `Styling`, `Widgets`, `Terminal`.
- Widgets are `sealed record`s (the root `AGENTS.md` still says `sealed class`; only `ImageWidget` is a class) with `{ get; init; }` properties, a parameterless constructor **and** a positional constructor, and `// ── Section ──` dividers (`IFocusable`, `IWidget`, widget-specific, Render).
- Every `public` member in `src/` has an XML doc comment (`<inheritdoc/>` for interface members). `GenerateDocumentationFile` is on and CS1591 is not suppressed, so a missing one warns at build.
- Messages a widget no longer raises are kept but marked `[Obsolete("...")]` rather than deleted (`CheckboxToggledMsg`, `TextInputChangedMsg`, `ListSelectionChangedMsg`, `TextAreaChangedMsg`).
- `nullable enable` everywhere; a `!` null-forgiving operator needs a comment saying why. No `static` mutable state.
- Samples and tests do not need doc comments; samples live under `samples/` and are not packed.

<!-- mex:entity
id: mx_01M363AX56S5NQASGFCN253T9Y
type: convention
status: promoted
revision: 1
-->
## Patterns
Focusable widget `Update` returns the next widget; it never mutates, never invokes a callback, and editing rules live in a reducer ([`TextInput.Update()`](mex://method:609fa0da2fad3f78351e138cd0c3e671)):
```csharp
// Correct — delegate to the reducer, return this when nothing changed
public (IFocusable Next, ICmd? Cmd) Update(KeyMsg key)
{
    var before = new TextInputState(Value, CursorPosition);
    var after = before.HandleKey(key);
    if (ReferenceEquals(after, before)) return (this, null);
    return (this with { Value = after.Value, CursorPosition = after.Cursor }, null);
}

// Wrong — widget owns the rule and mutates / raises a message
public void OnKeyEvent(KeyMsg key) { Value += key.KeyChar; OnChanged?.Invoke(Value); }
```

Model updates return a new model via `with`; return `this` unchanged when nothing happened, because `App` only redraws when the model reference changes:
```csharp
// Correct
(IModel, ICmd?) OnNavDown() => (this with { List = List.MoveDown() }, null);
// Wrong — forces a redraw and allocates for a no-op
(IModel, ICmd?) OnIgnored() => (this with { }, null);
```

Printable key bindings match the character, not the physical key: `KeyPattern.OfChar('?')` / `keys.On('?', ...)`, not `WithShift(ConsoleKey.Oem2)`.

Early-exit guard clauses (`if (region.Width <= 0 || region.Height <= 0) return;`) are the house style; the repo deliberately stopped recommending ternaries in their place.

<!-- mex:entity
id: mx_01M363AX4F1BA2CAPMA9AAKFV9
type: convention
status: promoted
revision: 1
-->
## Verify Checklist
Before presenting any code:
- [ ] No mutation of `this`, widgets, or models; all changes go through `with` / `init`.
- [ ] No state (cursor, selection, scroll, focus) stored inside a widget instance or in `static` fields.
- [ ] `Render` writes only through `IRenderContext`; nothing is `async`; no `Console.Write`.
- [ ] New `public` members in `src/` have `///` docs; `dotnet build ConsoleForge.slnx` shows no new CS1591 warnings.
- [ ] New widget has both constructors and `Width`/`Height` defaults (`Flex(1)` unless naturally sized); implements `IMeasurable` if it should support `Auto`.
- [ ] Editing/selection behaviour is tested at the reducer level; the widget test only covers delegation.
- [ ] No sizing arithmetic outside `LayoutSolver`; no `string.Length` used as a column width.
- [ ] If the change touches a hot path listed in `AGENTS.md` Performance, Tier 3 ran before and after; `CHANGELOG.md` (Unreleased) and `WISHLIST.md` are updated.
