# ConsoleForge — Agent Memory

## Project

TUI framework for .NET. Elm-inspired architecture (Model → Update → View).
Single library: `src/ConsoleForge/`. Gallery sample: `samples/ConsoleForge.Gallery/`.
Solution file: `ConsoleForge.slnx`.

### Key namespaces

| Namespace | Purpose |
|---|---|
| `ConsoleForge.Core` | Runtime: `Program`, `Renderer`, `FocusManager`, `Cmd`, `IModel`, `IMsg` |
| `ConsoleForge.Layout` | Layout engine, `SizeConstraint`, `IWidget`, `IFocusable`, `IRenderContext` |
| `ConsoleForge.Styling` | `Style`, `Color`, `Borders`, `Theme` |
| `ConsoleForge.Widgets` | Built-in widgets: `TextInput`, `TextBlock`, `Container`, `BorderBox`, `List`, `Table`, `ProgressBar`, `Spinner` |

### Architecture rules

- **Immutable widgets.** All `IWidget` implementations use `{ get; init; }` props. No mutation after construction.
- **Immutable model.** `IModel.Update` returns new model via `with` expressions. Never mutate `this`.
- **Message dispatch.** Widgets signal intent via `IMsg` records dispatched through `Action<IMsg>`. Callers update model state; widgets do not hold mutable state.
- **Render is pure.** `IWidget.Render(IRenderContext)` must have no side effects other than writing to `ctx`.
- **`SizeConstraint`** — use `Fixed(n)` for known sizes, `Flex(n)` for proportional fill. Never hardcode pixel positions.

---

## Coding rules

### General

- Target **net8.0**. No preview features unless already in use in the codebase.
- `nullable enable` is on. No `!` null-forgiving operators without a comment explaining why.
- Prefer `record` for messages (`IMsg` impls), `sealed class` for widgets and commands.
- No `static` mutable state.
- XML doc comments (`///`) on all `public` members of library code (`src/`). Not required in samples or tests.

### Naming

- Widgets: `PascalCase`, suffix matches type role (e.g. `TextInput`, `BorderBox`).
- Messages: `PascalCase` + `Msg` suffix (e.g. `TextInputChangedMsg`).
- Commands: `PascalCase` + `Cmd` suffix if implementing `ICmd` directly; factory methods live on `Cmd` static class.
- Private fields: `_camelCase`. Locals/params: `camelCase`.

### Widgets

- Every new widget needs a default `()` constructor and a positional constructor.
- `HasFocus` is set by the framework (`FocusManager`), not by the widget itself.
- `Width` / `Height` default to `Flex(1)` unless the widget has a natural fixed size.
- Cursor / selection state lives in the **model**, not inside the widget instance.

### Tests

- Tests live in `tests/ConsoleForge.Tests/`.
- Use xUnit. No MSTest, no NUnit.
- Test file mirrors source path: `src/ConsoleForge/Widgets/Foo.cs` → `tests/ConsoleForge.Tests/Widgets/FooTests.cs`.
- No `Thread.Sleep` / real timers in tests. Inject time via `DateTimeOffset` params or fake ticks.

---

## Build & test

```bash
# Build
dotnet build ConsoleForge.slnx

# Test
dotnet test ConsoleForge.slnx

# Run gallery
dotnet run --project samples/ConsoleForge.Gallery
```

---

## Benchmarks

Project: `tests/ConsoleForge.Benchmarks/`. Uses BenchmarkDotNet. All benchmark classes have `[MemoryDiagnoser]`.

Benchmark files:
- `RenderBenchmarks.cs` — widget render pipeline
- `CmdDispatchBenchmarks.cs` — `CmdDispatcher` channel roundtrip
- `CmdAdvancedBenchmarks.cs` — advanced cmd scenarios
- `NewWidgetRenderBenchmarks.cs` — new widget render paths

**Rule: run benchmarks before AND after any change that touches:**
- `Renderer`, `RenderContext`, `LayoutEngine`, `CmdDispatcher`
- Any `IWidget.Render` implementation
- `SizeConstraint` resolution logic
- Hot paths in `Update` loops

```bash
# Run all benchmarks (Release required — never run in Debug)
dotnet run --project tests/ConsoleForge.Benchmarks -c Release -- --filter "*"

# Run a specific class
dotnet run --project tests/ConsoleForge.Benchmarks -c Release -- --filter "*RenderBenchmarks*"
```

**Perf rules:**
- No regression in mean time or allocated bytes vs baseline.
- If a change increases allocations, justify it explicitly before committing.
- Never benchmark in Debug config — results invalid.
- Save baseline output before making changes; diff after.

---

## What NOT to do

- Don't add NuGet packages without asking first.
- Don't change `IRenderContext` or `IWidget` interfaces without discussing impact on all widgets.
- Don't make `Update` or `Render` async.
- Don't write to stdout/stderr directly — all output goes through `IRenderContext`.
- Don't auto-format the entire file when making a targeted change.
