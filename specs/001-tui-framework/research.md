# Research: Terminal UI Framework (ConsoleForge)

**Feature**: specs/001-tui-framework
**Date**: 2026-04-12
**Resolved**: All NEEDS CLARIFICATION items from Technical Context

---

## 1. Language & Runtime

**Decision**: C# 12 / .NET 8 (LTS), targeting `net8.0`.

**Rationale**: User specified C#. .NET 8 is the current LTS release with full
Unix terminal support, `System.Console` improvements, and first-class NuGet
library packaging. C# 12 records, pattern matching, and tuples map cleanly to
the BubbleTea Elm architecture patterns.

**Alternatives considered**: .NET 6 (LTS, but EOL 2024-11), .NET 9 (current,
non-LTS). .NET 8 is the correct long-term stable target for a new library.

---

## 2. Architecture: Elm Model-Update-View

**Decision**: Adopt the Elm architecture as implemented by BubbleTea.

**Core contract**:
- `IModel` interface: `Init() → ICmd`, `Update(IMsg) → (IModel, ICmd)`,
  `View() → ViewDescriptor`
- `ICmd` = `Func<IMsg>` (a blocking closure returning a message)
- `IMsg` = marker interface; concrete types are user-defined records
- `ViewDescriptor` = value type capturing rendered string + terminal mode flags

**Rationale**: The Elm loop is provably simple and testable. `Update` is a pure
function — given a message and a model, returns a new model. No shared mutable
state, no callbacks, no event hierarchies. This directly satisfies the
constitution's Principle I (simplicity) and Principle II (testability).

**C# translation of key patterns**:

| Go pattern | C# equivalent |
|---|---|
| `type Msg = interface{}` + type switch | `interface IMsg` + C# pattern matching |
| `type Cmd func() Msg` | `Func<IMsg>` or `Func<Task<IMsg>>` for async |
| `(Model, Cmd)` return tuple | `UpdateResult` record: `(IModel Model, ICmd? Cmd)` |
| `Batch(cmds...)` | `BatchCmd : IMsg` wrapping `ICmd[]`; runtime fans out |
| Component: partial model | `IComponent<TModel>` generic interface |
| nil Cmd | `null` Cmd — framework skips dispatch |

**Alternatives considered**: MVVM (too much infrastructure, coupling to UI
update notifications), Reactive Extensions / Rx.NET (powerful but complexity
budget exceeded for a simple framework), raw event callbacks (no unidirectional
guarantee, hard to test).

---

## 3. Terminal Abstraction Layer

**Decision**: Thin `ITerminal` interface, two implementations:
`AnsiTerminal` (production) and `VirtualTerminal` (test).

**`ITerminal` core surface**:
- `int Width`, `int Height` (current terminal dimensions)
- `void Write(int col, int row, StyledText text)` — positional write
- `void Clear()` — blank the full buffer
- `void Flush()` — push buffer to stdout
- `IObservable<InputEvent> Input` — keyboard events as observable stream
- `void EnterRawMode()` / `void ExitRawMode()` — alternate screen + raw input
- `event EventHandler<TerminalResizedEventArgs> Resized`

**Rationale**: Without this seam, zero tests can run in CI without a real
terminal. The seam has near-zero cost (thin interface over `System.Console`)
and enables the entire test strategy.

**Raw mode on .NET 8 (Unix)**:
- `Console.TreatControlCAsInput = true` disables Ctrl+C signal
- No built-in `cfmakeraw` / `tcsetattr` equivalent; must P/Invoke `termios`
  on Unix or use `Console.SetIn` with raw stream
- Recommended: include a minimal `Termios` P/Invoke shim (Unix) + equivalent
  Win32 Console mode flag (Windows) behind the `ITerminal` abstraction
- The P/Invoke shim is isolated to `AnsiTerminal`; test code never touches it

**Terminal resize (Unix SIGWINCH)**:
- .NET 8 does not expose SIGWINCH directly
- `PosixSignalRegistration.Create(PosixSignal.SIGWINCH, handler)` — available
  since .NET 6, works on .NET 8
- On signal: read `Console.WindowWidth/Height`, publish `TerminalResizedEvent`

**Alternatives considered**: Spectre.Console (rich markup rendering, but not a
full TUI event loop framework — layout and input are not its domain), CursesSharp
(P/Invoke to ncurses, too heavy and Linux-only), Terminal.Gui (existing full
framework — building on top of it defeats the goal; rolling our own is the
stated intent).

---

## 4. Renderer Architecture

**Decision**: FPS-decoupled renderer, targeting 60 fps (16ms budget per frame).

**Pattern** (directly from BubbleTea):
- Event loop calls `Render(model)` on each `Update` — this captures the view
  string into a staging buffer but does NOT flush to the terminal
- A separate timer-driven goroutine (in C#: a `Timer` callback or dedicated
  `Thread`) calls `Flush()` at ≤60 fps
- `Flush()` diffs the new view string against `lastView`; if identical, exits early
  (fast path — no write syscall)
- If changed: write cursor-home + new content using ANSI escape sequences

**Rationale**: Decoupling prevents the event loop from stalling on slow terminal
writes. 60 fps is the SC-002 budget from the spec (render 20 widgets in <16ms).

**Alternatives considered**: Synchronous render on each update (simpler but
causes input lag on slow terminals), cell-level diff (more efficient for large
UIs but significantly more complex; defer to v2).

---

## 5. Styling System (Lipgloss-inspired)

**Decision**: `Style` is an immutable struct with fluent builder methods and a
bitmask tracking which properties are explicitly set vs. unset.

**Key design choices**:
- `Style` is a `readonly struct` — assignment copies, no heap allocation
- Each settable property has a corresponding bit in a `long` bitmask
- `Style.Inherit(parent)` copies only bits set in `parent` but not in `this`
  (margins and padding excluded from inheritance — they are local)
- Color types: `IColor` interface; `AnsiColor`, `Ansi256Color`, `TrueColor`,
  `NoColor` (absent sentinel) implementations
- Color downsampling at render time via `ColorProfile` enum
  (`TrueColor`, `Ansi256`, `Ansi`, `NoColor`) detected from environment
- `Style.Render(string)` applies: text styling → padding → height → alignment
  → borders → margins; if bitmask is 0, returns string unchanged (fast path)
- Border types: `NormalBorder`, `RoundedBorder`, `ThickBorder`, `DoubleBorder`,
  `ASCIIBorder` — each a struct with 13 string fields for each edge and corner

**Alternatives considered**: CSS-style cascading (too complex, requires style
resolution tree), mutable style builder (breaks value semantics, hard to share
styles safely), plain string formatting (no inheritance, no theme support).

---

## 6. Layout Engine

**Decision**: Constraint-based 1D flex layout, resolved in two passes per axis.

**Algorithm** (similar to CSS flexbox, simplified):
1. Pass 1 — assign fixed sizes; sum fixed widths; compute remaining space
2. Pass 2 — distribute remaining space among flex children proportional to
   their `flex` weight (integer ratio); minimum-size constraints enforced;
   rounding remainder given to last flex child

**Constraints**:
- `Fixed(n)` — exact column/row count
- `Flex(n)` — relative weight (default 1); takes proportional share of free space
- `Min(n)` — minimum size; flex child never shrinks below this
- `Max(n)` — maximum size; flex child never grows above this (excess space
  redistributed)

**Conflict resolution**: If fixed children alone exceed parent size, they are
proportionally clipped. Documented behavior, not an error (terminal may resize).

**Alternatives considered**: Full CSS box model (too complex, cyclomatic
complexity would violate constitution), manual size specification only (too
rigid, violates FR-003), constraint solver (overkill for terminal layouts).

---

## 7. Testing Strategy

**Decision**: xUnit v3 + Verify (snapshot) + VirtualTerminal + FsCheck.

| Test type | Tool | What it tests |
|---|---|---|
| Render output | Verify.XunitV3 (snapshot) | `Widget.Render()` string output |
| Layout arithmetic | FsCheck.Xunit (property-based) | Constraint resolution invariants |
| Layout examples | xUnit `[Theory]` | Named constraint scenarios |
| Event routing | xUnit + VirtualTerminal | Focus traversal, key dispatch |
| Integration | xUnit + VirtualTerminal | Full app loop with synthetic input |
| Code coverage | coverlet.collector | CI gate at 80% line coverage |

**Rationale**: See research findings above. xUnit is the .NET OSS standard.
Verify eliminates brittle string assertions for multi-line ANSI output. FsCheck
catches arithmetic edge cases that example tests miss. VirtualTerminal is the
critical test seam.

---

## 8. Project Structure Decision

**Decision**: Single library project (`src/ConsoleForge/`) + test project
(`tests/ConsoleForge.Tests/`) at repository root. No CLI wrapper in v1 (library
only, per spec Assumptions).

**NuGet packaging**: `ConsoleForge` as the package ID.

**Namespace root**: `ConsoleForge` (matches project name and directory).

**Subnamespaces**:
- `ConsoleForge.Core` — IModel, IMsg, ICmd, Program runtime
- `ConsoleForge.Layout` — Container, LayoutEngine, constraints
- `ConsoleForge.Widgets` — built-in widgets (TextBlock, BorderBox, List, Input)
- `ConsoleForge.Styling` — Style, Theme, border types, color types
- `ConsoleForge.Terminal` — ITerminal, AnsiTerminal, Termios shim

---

## 9. Open Items / Deferred to v2

- Mouse input (out of scope per spec Assumptions)
- Inline / partial-screen mode (out of scope per spec Assumptions)
- Internationalization / RTL layouts (out of scope per spec Assumptions)
- Cell-level diff rendering (performance optimization, v1 uses full-frame diff)
- Cancellation of running Cmds (leak until natural return, same as BubbleTea v1)
- Windows terminal raw mode (the `ITerminal` abstraction is present; the
  Win32 implementation is deferred to a follow-up PR)
