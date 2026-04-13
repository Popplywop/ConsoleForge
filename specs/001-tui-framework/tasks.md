---

description: "Task list for ConsoleForge Terminal UI Framework"
---

# Tasks: Terminal UI Framework (ConsoleForge)

**Input**: Design documents from `/specs/001-tui-framework/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

**Organization**: Tasks grouped by user story for independent implementation and delivery.
**Tests**: Not requested in spec — test tasks omitted from all story phases.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repository and project initialization

- [x] T001 Initialize .NET 8 solution file at `ConsoleForge.sln` with `dotnet new sln`
- [x] T002 Create library project at `src/ConsoleForge/ConsoleForge.csproj` targeting `net8.0` as a class library with package ID `ConsoleForge`
- [x] T003 Create test project at `tests/ConsoleForge.Tests/ConsoleForge.Tests.csproj` with xunit, Verify.XunitV3, FsCheck, coverlet.collector references
- [x] T004 Create benchmarks project at `tests/ConsoleForge.Benchmarks/ConsoleForge.Benchmarks.csproj` with BenchmarkDotNet reference
- [x] T005 [P] Add `src/ConsoleForge/` and `tests/ConsoleForge.Tests/` to `ConsoleForge.sln`
- [x] T006 [P] Create root `.gitignore` covering `bin/`, `obj/`, `*.received.*`, `TestResults/`, `coverage-report/`
- [x] T007 [P] Create `tests/ConsoleForge.Tests/Testing/VirtualTerminal.cs` — in-memory `ITerminal` with `EnqueueKey`, `SimulateResize`, `Lines`, `WriteHistory`, `ExitedCleanly`, `HasArtifacts` surface per contracts/terminal.md
- [x] T008 [P] Create directory scaffolding: `src/ConsoleForge/Core/`, `src/ConsoleForge/Layout/`, `src/ConsoleForge/Widgets/`, `src/ConsoleForge/Styling/`, `src/ConsoleForge/Terminal/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core abstractions that ALL user stories depend on. No story work begins until this phase is complete.

**⚠️ CRITICAL**: Phases 3–6 are blocked until Phase 2 is complete.

- [x] T009 Create `src/ConsoleForge/Core/IMsg.cs` — marker interface `IMsg` per contracts/core-loop.md
- [x] T010 Create `src/ConsoleForge/Core/ICmd.cs` — `delegate IMsg ICmd()` type alias per contracts/core-loop.md
- [x] T011 Create `src/ConsoleForge/Core/IModel.cs` — `IModel` interface with `Init()`, `Update(IMsg)`, `View()` per contracts/core-loop.md
- [x] T012 Create `src/ConsoleForge/Core/ViewDescriptor.cs` — `readonly record struct ViewDescriptor` with `Content`, `Title`, `Cursor`; `CursorDescriptor` nested record; static `From(IWidget, ...)` factory stub per contracts/core-loop.md
- [x] T013 Create `src/ConsoleForge/Core/Cmd.cs` — static `Cmd` class with `None`, `Quit()`, `Batch(...)`, `Sequence(...)`, `Tick(...)` factory methods per contracts/core-loop.md
- [x] T014 [P] Create `src/ConsoleForge/Core/Messages.cs` — sealed records `QuitMsg`, `KeyMsg`, `WindowResizeMsg`, `FocusChangedMsg`, `BatchMsg`, `SequenceMsg` all implementing `IMsg`
- [x] T015 Create `src/ConsoleForge/Layout/SizeConstraint.cs` — abstract record with `Fixed(int)`, `Flex(int)`, `Auto`, `Min(int, SizeConstraint)`, `Max(int, SizeConstraint)` per contracts/widgets-layout.md
- [x] T016 Create `src/ConsoleForge/Layout/Region.cs` — `readonly record struct Region(int Col, int Row, int Width, int Height)`
- [x] T017 Create `src/ConsoleForge/Layout/IWidget.cs` — `IWidget` interface with `Style`, `Width`, `Height`, `Render(IRenderContext)` per contracts/widgets-layout.md
- [x] T018 Create `src/ConsoleForge/Layout/IFocusable.cs` — `IFocusable : IWidget` with `HasFocus`, `OnKeyEvent` per contracts/widgets-layout.md
- [x] T019 Create `src/ConsoleForge/Layout/IRenderContext.cs` — `IRenderContext` interface with `Region`, `Theme`, `ColorProfile`, `Write(int, int, string, Style)` per contracts/widgets-layout.md
- [x] T020 Create `src/ConsoleForge/Terminal/ITerminal.cs` — `ITerminal : IDisposable` interface, `InputEvent` hierarchy (`KeyInputEvent`, `ResizeInputEvent`), `TerminalResizedEventArgs` per contracts/terminal.md
- [x] T021 Create `src/ConsoleForge/Styling/IColor.cs` — `IColor` interface with `ToAnsiSequence(bool, ColorProfile)` per contracts/styling.md
- [x] T022 [P] Create `src/ConsoleForge/Styling/ColorProfile.cs` — `ColorProfile` enum: `NoColor=0`, `Ansi=1`, `Ansi256=2`, `TrueColor=3`
- [x] T023 [P] Create `src/ConsoleForge/Styling/Colors.cs` — `NoColor`, `AnsiColor`, `Ansi256Color`, `TrueColor` records; static `Color` factory class with `FromHex`, `FromRgb`, `FromAnsi`, named color constants per contracts/styling.md
- [x] T024 [P] Create `src/ConsoleForge/Styling/BorderSpec.cs` and `src/ConsoleForge/Styling/Borders.cs` — `BorderSpec` struct with 13 string fields; `Borders` static class with `Normal`, `Rounded`, `Thick`, `Double`, `ASCII`, `Hidden` per contracts/styling.md
- [x] T025 Create `src/ConsoleForge/Styling/Style.cs` — `readonly struct Style` with `_props` bitmask, all property methods returning new Style, `Inherit(Style)`, `Render(string, ColorProfile)` fast-path and full ANSI rendering per contracts/styling.md

**Checkpoint**: All interfaces, value types, and foundational enums exist. User story implementation can begin.

---

## Phase 3: User Story 1 - Render a Basic Layout (Priority: P1) 🎯 MVP

**Goal**: Developers can declare a bordered box with title and body text, run the program, and see it rendered in the terminal. Framework handles ANSI output, alternate screen, and clean exit.

**Independent Test**: Run a single-file program that renders a `BorderBox` with title "ConsoleForge" and body "Press Q to quit." Confirm the box appears, Q exits cleanly, and no terminal artifacts remain.

### Implementation for User Story 1

- [x] T026 [P] [US1] Create `src/ConsoleForge/Styling/Theme.cs` — `Theme` sealed record with `Default`, `Name`, `BaseStyle`, `BorderStyle`, `FocusedStyle`, `DisabledStyle`, `Named` per contracts/styling.md
- [x] T027 [P] [US1] Create `src/ConsoleForge/Layout/ResolvedLayout.cs` — `ResolvedLayout` with `Dictionary<IWidget, Region> Allocations`
- [x] T063 [P] Add `IComponent<TModel>` interface to `src/ConsoleForge/Core/IComponent.cs` per contracts/widgets-layout.md
- [x] T028 [US1] Create `src/ConsoleForge/Widgets/TextBlock.cs` — `TextBlock : IWidget` with `Text`, `Style`, `Width=Auto`, `Height=Auto`; `Render` writes text to `ctx.Write()` respecting region bounds, wrapping at region width per contracts/widgets-layout.md
- [x] T029 [US1] Create `src/ConsoleForge/Styling/Style.cs` — complete `Render(string, ColorProfile)` implementation: apply bold/italic/underline/faint/blink/reverse ANSI sequences, foreground/background colors with downsampling, padding (spaces), height padding, alignment, then borders; return `text` unchanged when `_props == 0` (fast path verified by benchmark)
- [x] T030 [US1] Create `src/ConsoleForge/Widgets/BorderBox.cs` — `BorderBox : IWidget` with `Title`, `Body`, `Style`, `Width=Flex(1)`, `Height=Flex(1)`; render border characters from `Style.Border` (default `Borders.Normal`), render title in top edge, delegate body render to child widget per contracts/widgets-layout.md
- [x] T031 [US1] Create `src/ConsoleForge/Layout/LayoutEngine.cs` — `static LayoutEngine.Resolve(IWidget root, int w, int h)` two-pass algorithm: Pass1 assign fixed sizes, sum fixed, compute free space; Pass2 distribute free space proportionally to flex weights; apply min/max; remainder to last flex child; recurse into Container children; return `ResolvedLayout` per data-model.md and contracts/widgets-layout.md
- [x] T032 [US1] Create `src/ConsoleForge/Layout/RenderContext.cs` — concrete `RenderContext : IRenderContext` backed by a `StringBuilder` frame buffer; `Write(col, row, text, style)` clips to `Region`; `ToAnsiFrame()` produces full-frame ANSI string using cursor positioning
- [x] T033 [US1] Create `src/ConsoleForge/Core/Renderer.cs` — internal `Renderer` that holds `ViewDescriptor _lastView`; `Capture(ViewDescriptor)` stores new view; `Flush(ITerminal)` diffs `Content` against `_lastView.Content`, writes cursor-home + new content if changed; updates `_lastView`
- [x] T034 [US1] Create `src/ConsoleForge/Terminal/AnsiTerminal.cs` — `AnsiTerminal : ITerminal` backed by `System.Console`; `Write(string)` appends to stdout buffer; `Flush()` calls `Console.Out.Flush()`; `SetCursorVisible/Position` via ANSI codes; `EnterAlternateScreen`/`ExitAlternateScreen` via `\x1b[?1049h`/`\x1b[?1049l`; `Dispose` guarantees `ExitRawMode` + `ExitAlternateScreen` in finally block per contracts/terminal.md
- [x] T035 [US1] Create `src/ConsoleForge/Terminal/Termios.cs` — P/Invoke shim for Unix `tcgetattr`/`tcsetattr`/`cfmakeraw`; used only by `AnsiTerminal.EnterRawMode()`/`ExitRawMode()`; wrapped in `#if !WINDOWS` conditional compile
- [x] T036 [US1] Create `src/ConsoleForge/Terminal/AnsiTerminal.cs` (input) — `IObservable<InputEvent> Input` backed by `Observable.Create` that reads `Console.ReadKey(intercept: true)` in a loop on a dedicated thread; maps to `KeyInputEvent`; `PosixSignalRegistration.Create(PosixSignal.SIGWINCH, ...)` triggers `Resized` event and `ResizeInputEvent`
- [x] T037 [US1] Create `src/ConsoleForge/Core/Program.cs` — `Program.Run(IModel, ITerminal?, Theme?, int targetFps)`: create `AnsiTerminal` if null; enter raw mode + alternate screen; send `WindowResizeMsg`; call `model.Init()`; dispatch Cmd; start FPS render timer (`System.Threading.Timer` at `1000/targetFps` ms); enter event loop (blocking `Channel<IMsg>` dequeue → `model.Update(msg)` → store new model + Cmd → `renderer.Capture(model.View())` → dispatch Cmd); on `QuitMsg` cancel loop; `finally`: stop timer, `terminal.Dispose()` per data-model.md
- [x] T038 [US1] Implement `ViewDescriptor.From(IWidget, ...)` — resolve layout via `LayoutEngine.Resolve`, create `RenderContext`, call `root.Render(ctx)`, return `ViewDescriptor { Content = ctx.ToAnsiFrame() }`
- [x] T039 [US1] Wire `Program.Run` command dispatch — `Cmd.Batch` fans out all cmds via `Task.Run`; `Cmd.Sequence` runs cmds serially; each completed cmd result injected into message channel via `channel.Writer.TryWrite(msg)`

**Checkpoint**: User Story 1 complete. Developers can write a single-file TUI app rendering `BorderBox` + `TextBlock`, exit with Q, no terminal artifacts.

---

## Phase 4: User Story 2 - Compose Complex Layouts (Priority: P2)

**Goal**: Developers compose multi-pane layouts (sidebar + main + status bar) with fixed and flex sizing. Terminal resize re-renders the layout to fit new dimensions.

**Independent Test**: Three-region layout (fixed 24-col sidebar, flex-1 main, fixed 1-row footer). Verify all three regions render at expected sizes. Verify resize event triggers re-render that fills new terminal dimensions without overlap.

### Implementation for User Story 2

- [x] T040 [US2] Create `src/ConsoleForge/Widgets/Container.cs` — `Container : IWidget` with `Direction`, `Children`, `Style`, `Width=Flex(1)`, `Height=Flex(1)`, `Scrollable`, `ScrollOffset`; `Render` iterates resolved children, delegates each child's `Render` to its own sub-`RenderContext` clipped to child's `Region` per contracts/widgets-layout.md
- [x] T041 [US2] Extend `src/ConsoleForge/Layout/LayoutEngine.cs` — handle recursive `Container` children: after resolving the container's own region, recurse into its children array applying the same two-pass algorithm within the container's allocated dimensions; handle horizontal and vertical `Axis` separately
- [x] T042 [US2] Implement resize handling in `src/ConsoleForge/Terminal/AnsiTerminal.cs` — `SIGWINCH` handler re-reads `Console.WindowWidth`/`Console.WindowHeight`, updates `Width`/`Height` properties, fires `Resized` event and publishes `ResizeInputEvent` to `Input` observable
- [x] T043 [US2] Handle `WindowResizeMsg` in `src/ConsoleForge/Core/Program.cs` event loop — on receipt, update stored terminal dimensions; force `renderer.Capture(model.View())` with new dimensions; trigger immediate flush bypassing FPS timer
- [x] T044 [US2] Implement content clipping in `src/ConsoleForge/Layout/RenderContext.cs` — `Write(col, row, text, style)` truncates text that extends beyond `Region.Col + Region.Width`; ignores writes where `row >= Region.Row + Region.Height`; handles multi-line strings by splitting on `\n` and applying per-line clipping
- [x] T045 [US2] Implement scrollable container in `src/ConsoleForge/Widgets/Container.cs` — when `Scrollable=true`, `Render` offsets child rendering by `ScrollOffset` rows/cols; clamps `ScrollOffset` to `[0, Math.Max(0, totalContentSize - allocatedSize)]`; arrow keys (when focused via `IFocusable`) adjust `ScrollOffset` and re-render

**Checkpoint**: User Story 2 complete. Three-pane layouts with resize work. Each region renders independently and absorbs new terminal dimensions on resize.

---

## Phase 5: User Story 3 - Handle User Input and Events (Priority: P3)

**Goal**: Keyboard events route to the focused widget. Tab advances focus. `TextInput` and `List` widgets accept keyboard interaction and dispatch custom messages.

**Independent Test**: `List` widget with 3 items — arrow keys change highlighted row, Enter dispatches `ListItemSelectedMsg`. Tab moves focus from `TextInput` to `List` and back.

### Implementation for User Story 3

- [x] T046 [US3] Create `src/ConsoleForge/Core/FocusManager.cs` — stateless helper that traverses the widget tree depth-first and returns all `IFocusable` instances in declaration order; `GetNext(current, all)` returns next in list (wrapping); `GetPrev(current, all)` returns previous (wrapping)
- [x] T047 [US3] Integrate `FocusManager` into `src/ConsoleForge/Core/Program.cs` — on `KeyMsg { Key: Tab }`: call `FocusManager.GetNext`, set `HasFocus=false` on previous, `HasFocus=true` on next, publish `FocusChangedMsg`; on any other `KeyMsg`: find focused widget in last resolved layout, call `widget.OnKeyEvent(key, msg => channel.Writer.TryWrite(msg))`
- [x] T048 [US3] Create `src/ConsoleForge/Widgets/TextInput.cs` — `TextInput : IFocusable` with `Value`, `Placeholder`, `CursorPosition`; `OnKeyEvent`: printable chars append to `Value`, Backspace removes last char, Left/Right adjust `CursorPosition`; `Render` draws `Value` (or placeholder when empty), styled cursor at `CursorPosition` when `HasFocus` per contracts/widgets-layout.md
- [x] T049 [US3] Create `src/ConsoleForge/Widgets/List.cs` — `List : IFocusable` with `Items`, `SelectedIndex`; `OnKeyEvent`: UpArrow decrements `SelectedIndex` (clamped), DownArrow increments, Enter dispatches `ListItemSelectedMsg(SelectedIndex, Items[SelectedIndex])`; `Render` draws each item, applies `SelectedItemStyle` to `SelectedIndex` row when `HasFocus` per contracts/widgets-layout.md
- [x] T050 [US3] Update `src/ConsoleForge/Core/Program.cs` focus bootstrap — on program start, find first `IFocusable` in widget tree and set `HasFocus=true` so Tab traversal has a starting point; if none found, skip silently

**Checkpoint**: User Story 3 complete. Focus traversal, `TextInput`, and `List` all work with keyboard interaction. Custom messages dispatch back into the event loop.

---

## Phase 6: User Story 4 - Style and Theme Widgets (Priority: P4)

**Goal**: Developers define a `Theme` once and all widgets inherit its colors and border style. Per-widget style overrides only the properties explicitly set.

**Independent Test**: Program with `Theme(borderStyle: Style.Default.BorderForeground(Color.Cyan))`. Verify all `BorderBox` widgets render with cyan borders without per-widget style calls. Override one widget to red border; verify only that widget changes.

### Implementation for User Story 4

- [x] T051 [US4] Pass `Theme` through `IRenderContext` in `src/ConsoleForge/Layout/RenderContext.cs` — store `Theme` on concrete `RenderContext`; expose via `IRenderContext.Theme` property; child `RenderContext` instances inherit parent theme
- [x] T052 [US4] Apply theme inheritance in `src/ConsoleForge/Widgets/TextBlock.cs` — before rendering, compute effective style as `widget.Style.Inherit(ctx.Theme.BaseStyle)`; use effective style for all ANSI output
- [x] T053 [US4] Apply theme inheritance in `src/ConsoleForge/Widgets/BorderBox.cs` — compute effective style as `widget.Style.Inherit(ctx.Theme.BorderStyle)`; apply `ctx.Theme.FocusedStyle` on top when `BorderBox` is the focused widget (checked via `widget is IFocusable f && f.HasFocus`)
- [x] T054 [US4] Apply theme inheritance in `src/ConsoleForge/Widgets/Container.cs` — containers inherit `ctx.Theme.BaseStyle` for background fill; child widgets resolve their own inheritance independently
- [x] T055 [US4] Apply theme inheritance in `src/ConsoleForge/Widgets/TextInput.cs` — effective style = `widget.Style.Inherit(ctx.Theme.BaseStyle)`; when focused, merge `ctx.Theme.FocusedStyle` on top
- [x] T056 [US4] Apply theme inheritance in `src/ConsoleForge/Widgets/List.cs` — base row style inherits from `ctx.Theme.BaseStyle`; selected row style = `widget.SelectedItemStyle.Inherit(ctx.Theme.FocusedStyle)`
- [x] T057 [US4] Pass theme and detect `ColorProfile` in `src/ConsoleForge/Core/Program.cs` — detect `ColorProfile` from environment variables (`COLORTERM`, `TERM`) on startup; store on `Program`; thread through `ViewDescriptor.From(root, theme: _theme, colorProfile: _colorProfile)` so all `Style.Render()` calls use correct downsampling
- [x] T058 [US4] Implement runtime theme swap — `Program` exposes `void SetTheme(Theme theme)` that updates `_theme` and sends a synthetic re-render signal (empty no-op `IMsg` subtype `RedrawMsg`) to force a new `View()` call with the updated theme

**Checkpoint**: User Story 4 complete. Global theme applies to all widgets. Per-widget overrides work. Runtime theme swap re-renders without restart.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Hardening, edge cases, documentation, and performance validation.

- [x] T059 Implement minimum-size clamp in `src/ConsoleForge/Layout/LayoutEngine.cs` — when terminal is too small to satisfy fixed constraints, proportionally reduce fixed children to fit; document deterministic clip rule in code comments
- [x] T060 Implement multi-byte / emoji width handling in `src/ConsoleForge/Layout/RenderContext.cs` — use `StringInfo.GetTextElementEnumerator` for grapheme counting; treat emoji as 2-column wide for clip math
- [x] T061 Add `LayoutConstraintException` in `src/ConsoleForge/Layout/LayoutEngine.cs` — throw with clear message when all children are fixed and collectively exceed parent width; tested by LayoutEngineTests
- [x] T062 Add panic recovery in `src/ConsoleForge/Core/Program.cs` — wrap event loop body in try/catch; on unhandled exception: call `terminal.Dispose()` (guarantees raw mode exit + screen restore), then rethrow
- [x] T063 [P] Add `IComponent<TModel>` interface to `src/ConsoleForge/Core/IComponent.cs` per contracts/widgets-layout.md
- [x] T064 [P] Add `ConsoleForge.Testing` namespace — move `VirtualTerminal.cs` to `src/ConsoleForge/Testing/VirtualTerminal.cs` (included in main library behind `#if DEBUG` or as a separate `ConsoleForge.Testing` package reference assembly)
- [x] T065 Create `tests/ConsoleForge.Benchmarks/RenderBenchmarks.cs` — BenchmarkDotNet benchmark rendering a 20-widget layout; baseline must complete in ≤16ms per SC-002; commit benchmark results as `benchmarks/baseline.md`
- [x] T066 [P] Write XML doc comments on all `public` types and members in `src/ConsoleForge/` following C# XML doc conventions
- [x] T067 Run `quickstart.md` validation — build and run each code example from `specs/001-tui-framework/quickstart.md`; confirm each example compiles and runs without errors

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all stories
- **US1 (Phase 3)**: Depends on Phase 2 — first story, no story dependencies
- **US2 (Phase 4)**: Depends on Phase 2; T040/T041 depend on T031 (LayoutEngine)
- **US3 (Phase 5)**: Depends on Phase 2; T046/T047 depend on T037 (Program loop)
- **US4 (Phase 6)**: Depends on Phase 2; T051–T058 depend on T026 (Theme) and all widget impls
- **Polish (Phase 7)**: Depends on all story phases

### User Story Dependencies

- **US1 (P1)**: No dependency on other stories — implement first
- **US2 (P2)**: No story dependency; shares `LayoutEngine` from US1 (T031)
- **US3 (P3)**: No story dependency; shares `Program` loop from US1 (T037)
- **US4 (P4)**: Depends on all widget impls from US1–US3 for inheritance application

### Within Each Story

- Interfaces and records before concrete implementations
- `LayoutEngine` before `Container` (US2)
- `FocusManager` before `TextInput`/`List` (US3)
- `Theme` before inheritance application (US4)
- Foundational phase complete before any story starts

### Parallel Opportunities

- All `[P]`-marked tasks within a phase can run in parallel
- US2 and US3 can run in parallel once US1 and Foundational are complete
- US4 blocked until all widget implementations exist (US1–US3 complete)

---

## Parallel Example: User Story 1

```bash
# Run in parallel (different files, no deps between them):
Task: "Create TextBlock in src/ConsoleForge/Widgets/TextBlock.cs"           # T028
Task: "Complete Style.Render in src/ConsoleForge/Styling/Style.cs"          # T029
Task: "Create BorderBox in src/ConsoleForge/Widgets/BorderBox.cs"           # T030 (after T028)

# Run in parallel:
Task: "Create LayoutEngine in src/ConsoleForge/Layout/LayoutEngine.cs"      # T031
Task: "Create RenderContext in src/ConsoleForge/Layout/RenderContext.cs"    # T032
Task: "Create Renderer in src/ConsoleForge/Core/Renderer.cs"               # T033

# Sequential (each depends on previous):
Task: "Create AnsiTerminal (output)"   # T034
Task: "Create Termios.cs"              # T035
Task: "Create AnsiTerminal (input)"    # T036
Task: "Create Program.cs"             # T037 (depends on T033, T036)
Task: "Wire ViewDescriptor.From"       # T038 (depends on T031, T032)
Task: "Wire Cmd dispatch"              # T039 (depends on T037)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1 (T026–T039)
4. **STOP and VALIDATE**: Build and run the quickstart.md "Hello World" example
5. Confirm: bordered box renders, Q exits, no terminal artifacts

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 → Basic render → **DEMO: Hello World TUI**
3. US2 → Layout composition → **DEMO: Two-pane app with sidebar**
4. US3 → Input + events → **DEMO: Interactive list navigation**
5. US4 → Theming → **DEMO: Full themed app from quickstart.md**
6. Polish → Hardening → **RELEASE: v1.0.0 NuGet package**

### Parallel Team Strategy

With multiple developers (after Foundational complete):
- **Dev A**: US1 (render pipeline: T026–T039)
- **Dev B** (after US1): US2 (layout composition: T040–T045)
- **Dev B** (parallel with Dev A on US2): US3 (input: T046–T050)
- Both → US4 (theming: T051–T058) after their respective stories complete

---

## Notes

- `[P]` = different files, no incomplete dependencies — safe to parallelize
- `[USn]` maps each task to its user story for traceability
- Each story checkpoint describes exactly how to verify that story independently
- T035 (Termios P/Invoke) is Unix-specific; Windows raw mode deferred to v2
- T064 (`ConsoleForge.Testing`) packaging decision (inline vs. separate package) can be deferred — test helpers are needed in T007 regardless
- Avoid: vague tasks, same-file conflicts between parallel tasks, cross-story dependencies that break story independence
