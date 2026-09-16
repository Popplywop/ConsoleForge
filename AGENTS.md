# ConsoleForge — Agent Memory

## Project

TUI framework for .NET. Elm-inspired architecture (Model → Update → View).
Two packages: `src/ConsoleForge/` (framework) and `src/ConsoleForge.SourceGen/`
(Roslyn generator). Gallery sample: `samples/ConsoleForge.Gallery/`.
Solution file: `ConsoleForge.slnx`.

### Design target

**Elm-correct in C#, with helpers.** Not parity with Bubble Tea or its `bubbles`
component library — the README's Bubble Tea comparison is about predictability,
not about copying its component model.

When ergonomics pull against Elm purity, purity wins and the gap is closed with a
*helper*: a pure reducer, a static factory, a computed helper on the model. Never
a nested mutable update loop, and never widget-owned state. `bubbles` makes each
input a stateful mini-program with its own update loop; that works in Go and
fights every rule in the section below.

A proposed API passes if state stays in the model, `Update` stays the only place
it changes, and the helper is a pure function over it.

### Key namespaces

| Namespace | Purpose |
|---|---|
| `ConsoleForge.Core` | Runtime: `App`, `Renderer`, `FocusManager`, `Cmd`, `Sub`, `KeyMap`, `IModel`, `IMsg`, `IComponent`, `TextInputState` |
| `ConsoleForge.Layout` | `LayoutEngine`, `LayoutSolver`, `SizeConstraint`, `IWidget`, `IFocusable`, `IMeasurable`, `Size`, `IRenderContext` |
| `ConsoleForge.Styling` | `Style`, `Color`, `Borders`, `Theme` |
| `ConsoleForge.Widgets` | Built-in widgets: `TextBlock`, `TextInput`, `TextArea`, `List`, `Table`, `Checkbox`, `Tabs`, `ProgressBar`, `Spinner`, `BorderBox`, `Container`, `Modal`, `ZStack`, `ImageWidget` |
| `ConsoleForge.Terminal` | `ITerminal`, `AnsiTerminal`, `TerminalCapabilities`, `KittyProtocol` |

### Architecture rules

- **Immutable widgets.** All `IWidget` implementations use `{ get; init; }` props. No mutation after construction.
- **Immutable model.** `IModel.Update` returns new model via `with` expressions. Never mutate `this`.
- **Message dispatch.** A focusable widget's `Update(KeyMsg)` returns the next widget plus an optional `ICmd` — it never invokes a callback and never mutates itself. Callers fold that result into the model.
- **Render is pure.** `IWidget.Render(IRenderContext)` must have no side effects other than writing to `ctx`.
- **Editing logic is a reducer, not a widget.** Rules for changing text/selection/scroll belong in a pure state record in `ConsoleForge.Core` (`TextInputState` is the pattern), which the widget's `Update` delegates to. One copy of the rules, usable from a model that never builds the widget.
- **`SizeConstraint`** — `Fixed(n)` for known sizes, `Flex(n)` for proportional fill, `Auto` to size to content. Never hardcode pixel positions.
- **One solver.** All constraint arithmetic lives in `LayoutSolver`. `LayoutEngine` (which builds the `ResolvedLayout` focus and hit-testing read) and `Container.Render` (which places children) both call it — a second copy makes widgets render where the rest of the framework doesn't think they are.
- **`Auto` needs `IMeasurable`.** A widget sizes to content only if it implements `Measure`, which runs during layout: pure, cheap, and never asking for more than it was offered. Without it `Auto` falls back to flex weight 1.

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

- Framework tests live in `tests/ConsoleForge.Tests/`; generator tests in
  `tests/ConsoleForge.SourceGen.Tests/`, which verifies emitted source by snapshot.
- Use xUnit v3. No MSTest, no NUnit. FsCheck for property tests, Verify.XunitV3 for
  snapshots.
- State reducers (`TextInputState`, `TextAreaState`, `ListState`) are the cheapest
  place to test editing and selection rules — prefer a reducer test to driving a
  widget, and let the widget test cover only that it delegates.
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

## Performance

Three tiers. Pick by the question you are answering, not by habit. Most performance
questions in this codebase have an exact answer, and reaching for a stopwatch to answer
one costs ten minutes and returns a number you then have to hedge.

### Tier 1 — assertions (milliseconds, runs in CI)

For anything countable: bytes allocated, cache hits, frames drawn, cells emitted.

- `tests/ConsoleForge.Tests/Performance/` holds the allocation budgets.
- `GC.GetAllocatedBytesForCurrentThread()` around the operation; assert a **ceiling**, not
  equality, so JIT tiering cannot flake it. Warm up first, then average over iterations.
- Better still, assert the work itself. `WidgetCacheTests` calls `TryReuseWidget` directly
  and proved a whole-tree re-render in 79ms — the benchmark that found the same bug took
  ten minutes.

**A perf fix lands with a Tier 1 test whenever the defect has a countable signature.** The
widget render cache served exactly one widget per frame across two releases because nothing
counted the hits, and it produced identical pixels, so no other test could see it.

### Tier 2 — quick benchmark (under a minute)

For iterating, when a rough number is enough:

```bash
./bench.sh -f '*WidgetCache*'
```

Short job, in-process. Wide error bars by design: this answers "4x faster" or "about the
same". It cannot answer "3% faster" — do not quote it as if it could.

**Allocation numbers here are exact.** `MemoryDiagnoser` counts bytes, it does not sample
them, so a short job reports the same allocations as a full one — measured: identical to
the byte, with means within 4% and the error bar 50x wider. Since allocations are the
number worth trusting anyway, Tier 2 settles most questions on its own, and Tier 3 is for
when the timing itself is the claim.

### Tier 3 — full benchmark (minutes)

Required before committing a change to `Renderer`, `RenderContext`, `LayoutEngine`,
`LayoutSolver`, `CmdDispatcher`, `SizeConstraint` resolution, or any `IWidget.Render`.

```bash
./bench.sh --full -f '*RenderBenchmarks*'
```

- Release only. Debug numbers are meaningless.
- Run before and after **sequentially, with nothing else running.** A concurrent build or
  test run moves unrelated benchmarks by more than 10%.
- Prefer one run to two. Where both implementations can coexist, put them in the same class
  with `[Benchmark(Baseline = true)]` and let BenchmarkDotNet compute the ratio against
  identical machine state. Cross-run comparison is where the noise lives, and it is why a
  two-run diff needs so much interpretation.

### Reading results

- **Allocated bytes are the trustworthy number.** Byte-identical allocations across a
  refactor is strong evidence that nothing structural changed. A regression here needs an
  explicit justification in the commit message.
- **Treat a mean-time delta under ~10% as noise** unless it reproduces *and* the change can
  reach that code path. Check reachability first: a `Container` change cannot affect
  `RenderSingleTextBlock_Cold`, which contains no container.
- `--filter '*RenderBenchmarks*'` also matches `NewWidgetRenderBenchmarks`. Narrow the
  filter to what the change can actually touch.

## Definition of Done

A change is finished when every line below is true, not when the code works. The
boxes exist because each one has been skipped here at least once, and the cost
showed up later rather than never.

- [ ] `dotnet build ConsoleForge.slnx` is clean.
- [ ] `dotnet test ConsoleForge.slnx` is green.
- [ ] A Tier 1 assertion exists if the defect had a countable signature — bytes,
      cache hits, cells emitted. See Performance above.
- [ ] Tier 3 was run if the change touched `Renderer`, `RenderContext`,
      `LayoutEngine`, `LayoutSolver`, `CmdDispatcher`, `SizeConstraint`
      resolution, or any `IWidget.Render`. Confirm the run actually happened:
      `bench.sh` exits 0 when BenchmarkDotNet rejects its arguments, so a bad
      invocation prints help, leaves the previous results in place, and reads as
      a successful run.
- [ ] The benchmark exercises the path the change is on. Tier 3 render
      benchmarks drove `ViewDescriptor.From` for three releases while the
      framework ran `Renderer`, so a change to the real frame path moved nothing.
- [ ] `WISHLIST.md` is updated — moved to Fixed, or marked partly landed.
- [ ] `CHANGELOG.md` has an entry under Unreleased.
- [ ] It is one coherent commit. A working tree holding three unrelated
      accomplishments is three commits that have not been written yet.

## What NOT to do

- Don't add NuGet packages without asking first.
- Don't change `IRenderContext` or `IWidget` interfaces without discussing impact on all widgets.
- Don't make `Update` or `Render` async.
- Don't write to stdout/stderr directly — all output goes through `IRenderContext`.
- Don't auto-format the entire file when making a targeted change.
