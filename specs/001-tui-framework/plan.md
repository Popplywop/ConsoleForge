# Implementation Plan: Terminal UI Framework (ConsoleForge)

**Branch**: `001-tui-framework` | **Date**: 2026-04-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-tui-framework/spec.md`

## Summary

Build a C# / .NET 8 terminal UI framework library inspired by BubbleTea,
Bubbles, and Lipgloss. The framework exposes an Elm Model-Update-View loop
where application state is immutable, updates are pure functions, and rendering
is fully decoupled from the event loop. It provides a constraint-based layout
engine, a fluent immutable Style builder with theme inheritance, and a thin
`ITerminal` abstraction that enables full testability without a real terminal.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (net8.0 TFM)
**Primary Dependencies**:
- `System.Reactive` (IObservable-based input stream)
- `xunit` + `xunit.runner.visualstudio` (testing)
- `Verify.XunitV3` (snapshot/render output testing)
- `FsCheck.Xunit` (property-based layout invariant testing)
- `coverlet.collector` (code coverage — CI gate 80%)
- No production runtime dependencies beyond the .NET 8 BCL

**Storage**: N/A (in-memory only; no persistence)
**Testing**: xUnit v3 + Verify (snapshot) + VirtualTerminal + FsCheck
**Target Platform**: Unix/Linux terminal (VT100+); Windows support deferred to v2
**Project Type**: Class library (NuGet package `ConsoleForge`)
**Performance Goals**: Render 20-widget layout in ≤16ms (60 fps budget, SC-002);
95% of UI updates complete within one render frame of the triggering event (SC-003)
**Constraints**: p95 user-action latency ≤200ms (constitution IV); no production
NuGet dependencies beyond BCL; cyclomatic complexity ≤10 per function (constitution I)
**Scale/Scope**: Single library; initial feature set = 4 user stories (P1–P4)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I — Code Quality ✅

- Elm `Update` is a pure function → single responsibility, zero side effects
- `Style` as `readonly struct` with bitmask → no accidental mutation
- `LayoutEngine` is stateless → trivially testable, no shared state
- `ITerminal` abstraction isolates P/Invoke to one class (`AnsiTerminal`)
- Cyclomatic complexity budget: every method MUST stay ≤10; layout engine
  split into Pass1 (fixed sizing) + Pass2 (flex distribution) to enforce this
- No complexity violations; Complexity Tracking table left empty

### Principle II — Testing Standards ✅

- `ITerminal` seam enables full test coverage without a real terminal
- `VirtualTerminal` allows synthetic key injection + frame inspection
- Snapshot tests via Verify cover all `Widget.Render()` outputs
- Property-based tests (FsCheck) cover layout arithmetic invariants
- TDD mandatory: widget render tests written + confirmed failing before impl
- Coverage gate: 80% line coverage enforced in CI via coverlet.collector

### Principle III — User Experience Consistency ✅

- Single `Theme` applied globally; all widgets inherit via `Style.Inherit()`
- `BorderBox` title, error messages, and status bars follow consistent patterns
  defined in contracts and validated by snapshot tests
- WCAG analog for TUI: `ASCIIBorder` fallback ensures readability on
  non-Unicode terminals (FR-002 + spec edge case)

### Principle IV — Performance Requirements ✅

- Renderer decoupled from event loop via FPS timer (max 60 fps)
- `Style.Render()` fast path: `if (_props == 0) return text;` — zero ANSI
  processing for unstyled text
- `ViewDescriptor` diff: if content string unchanged, no write syscall
- Performance budget declared in spec SC-002 and SC-003; measurable via
  benchmark harness in `tests/ConsoleForge.Benchmarks/`

*Post-Phase-1 re-check*: All gates still pass after contract design. No
violations requiring Complexity Tracking table entries.

## Project Structure

### Documentation (this feature)

```text
specs/001-tui-framework/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── core-loop.md     # IMsg, ICmd, IModel, Program, Cmd
│   ├── widgets-layout.md # IWidget, Container, LayoutEngine, built-in widgets
│   ├── styling.md       # Style, IColor, BorderSpec, Theme
│   └── terminal.md      # ITerminal, AnsiTerminal, VirtualTerminal
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
└── ConsoleForge/
    ├── ConsoleForge.csproj
    ├── Core/
    │   ├── IMsg.cs
    │   ├── ICmd.cs
    │   ├── IModel.cs
    │   ├── ViewDescriptor.cs
    │   ├── Program.cs
    │   └── Cmd.cs
    ├── Layout/
    │   ├── IWidget.cs
    │   ├── IFocusable.cs
    │   ├── IComponent.cs
    │   ├── SizeConstraint.cs
    │   ├── Region.cs
    │   ├── ResolvedLayout.cs
    │   ├── IRenderContext.cs
    │   └── LayoutEngine.cs
    ├── Widgets/
    │   ├── Container.cs
    │   ├── TextBlock.cs
    │   ├── BorderBox.cs
    │   ├── TextInput.cs
    │   └── List.cs
    ├── Styling/
    │   ├── Style.cs
    │   ├── IColor.cs
    │   ├── Color.cs
    │   ├── ColorProfile.cs
    │   ├── BorderSpec.cs
    │   ├── Borders.cs
    │   └── Theme.cs
    └── Terminal/
        ├── ITerminal.cs
        ├── InputEvent.cs
        ├── AnsiTerminal.cs
        └── Termios.cs      # P/Invoke shim (Unix raw mode)

tests/
├── ConsoleForge.Tests/
│   ├── ConsoleForge.Tests.csproj
│   ├── Core/
│   │   ├── ProgramLoopTests.cs
│   │   └── CmdTests.cs
│   ├── Layout/
│   │   ├── LayoutEngineTests.cs        # [Theory] examples
│   │   └── LayoutEngineProperties.cs   # FsCheck [Property]
│   ├── Widgets/
│   │   ├── TextBlockRenderTests.cs     # Verify snapshots
│   │   ├── BorderBoxRenderTests.cs
│   │   ├── TextInputTests.cs
│   │   └── ListTests.cs
│   ├── Styling/
│   │   ├── StyleTests.cs
│   │   └── ThemeInheritanceTests.cs
│   ├── Integration/
│   │   └── FullAppLoopTests.cs         # VirtualTerminal end-to-end
│   └── Testing/
│       └── VirtualTerminal.cs          # Shared test helper
└── ConsoleForge.Benchmarks/
    ├── ConsoleForge.Benchmarks.csproj
    └── RenderBenchmarks.cs             # BenchmarkDotNet, 20-widget layout
```

**Structure Decision**: Single library project. No CLI binary in v1 (library
only, per spec Assumptions). Separate benchmarks project for performance gate.

## Complexity Tracking

> No constitution violations. Table left empty intentionally.
