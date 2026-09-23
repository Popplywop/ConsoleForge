---
name: router
description: Session bootstrap and navigation hub. Read at the start of every session before any task. Contains project state, routing table, and behavioural contract.
edges:
  - target: context/architecture.md
    condition: when working on system design, integrations, or understanding how components connect
  - target: context/stack.md
    condition: when working with specific technologies, libraries, or making tech decisions
  - target: context/conventions.md
    condition: when writing new code, reviewing code, or unsure about project patterns
  - target: context/decisions.md
    condition: when making architectural choices or understanding why something is built a certain way
  - target: context/setup.md
    condition: when setting up the dev environment or running the project for the first time
  - target: context/event-loop.md
    condition: when working on App's message loop, commands, subscriptions, focus or components
  - target: context/rendering.md
    condition: when working on layout, the renderer, widget rendering or raw escape payloads
  - target: patterns/INDEX.md
    condition: when starting a task — check the pattern index for a matching pattern file
last_updated: 2026-09-22
---

# Session Bootstrap

If you haven't already read `AGENTS.md`, read it now — it contains the project identity, non-negotiables, and commands.

Then read this file fully before doing anything else in this session.

## Current Project State
Version 0.4.0 (both packages) — the release that finished the Elm-purity migration; WISHLIST says 0.5.0 is meant to be purely additive.

**Working:**
- Elm loop (`App.Run`) with batched input (one frame per drained batch), `Cmd` (Run/Batch/Sequence/Tick/Debounce/Throttle), root-level `Sub`s
- 15 widgets incl. `HorizontalShelf` and `ImageWidget` (Kitty graphics with half-block fallback); 6 themes; SGR mouse
- Layout via one `LayoutSolver`, `SizeConstraint.Auto` through `IMeasurable`, margin/padding
- Double-buffered cell-diff renderer with a working widget render cache (guarded by counting tests)
- Pure reducers `TextInputState`, `TextAreaState`, `ListState`; model-owned focus with `FocusKey` + `FocusRequestedMsg`
- `[DispatchUpdate]` / `[Component]` source generators; DocFX site deployed from `main`; CI runs build, tests and doc-sample compile on every push/PR

**Not yet built (WISHLIST "Later"):**
- Reporting a widget's resolved region/viewport back to the model (item 9) — consumers hardcode viewport heights
- Component-level subscriptions (item 1) — only the root model's `Subscriptions()` are consulted
- `KeyBinding` help metadata + `HelpBar` widget (item 2)

**Known issues:**
- Workspace `CLAUDE.md` (one level up) is stale on focus: it describes a framework-owned focus index / `FocusIndexChangedMsg`; focus is model-owned since 0.4.0. It also says the README uses `Program.Run`, which is no longer true.
- Root `AGENTS.md` says widgets are `sealed class`; they are `sealed record` (except `ImageWidget`).
- CS1591 (missing XML doc) warns but CI does not fail on it.
- FsCheck is referenced by the test project but unused.
- `.mex/graph.db` reports one partially parsed file; re-run `mex graph refresh` if grounding looks off.

## Routing Table

Load the relevant file based on the current task. Always load `context/architecture.md` first if not already in context this session.

| Task type | Load |
|-----------|------|
| Understanding how the system works | `context/architecture.md` |
| Working with a specific technology | `context/stack.md` |
| Writing or reviewing code | `context/conventions.md` |
| Making a design decision | `context/decisions.md` |
| Setting up or running the project | `context/setup.md` |
| Event loop, `Cmd`, `Sub`, rate limits, focus, `IComponent` | `context/event-loop.md` |
| Layout, renderer, widget `Render`/`Measure`, images | `context/rendering.md` |
| Any specific task | Check `patterns/INDEX.md` for a matching pattern |

## Behavioural Contract

For every task, follow this loop:

1. **CONTEXT** — Load the relevant context file(s) from the routing table above. Check `patterns/INDEX.md` for a matching pattern. If one exists, follow it.
2. **BUILD** — Do the work. If a pattern exists, follow its Steps. If you are about to deviate from an established pattern, say so before writing any code — state the deviation and why.
3. **VERIFY** — Load `context/conventions.md` and run the Verify Checklist item by item. State each item and whether the output passes. Do not summarise — enumerate explicitly.
4. **DEBUG** — If verification fails or something breaks, check `patterns/INDEX.md` for a debug pattern. Follow it. Fix the issue and re-run VERIFY.
5. **GROW** — After meaningful work, run this binary checklist:
   - **Ground:** What changed in reality? Name the changed behavior, system, command, dependency, or workflow.
   - **Record:** If project state changed, update the "Current Project State" section above. If documented facts changed, update the relevant `context/` file surgically.
   - **Orient:** If this task can recur and no pattern exists, create one in `patterns/` using `patterns/README.md`, then add it to `patterns/INDEX.md`. If a pattern exists but you learned a gotcha, update it.
   - **Write:** Bump `last_updated` in every scaffold file you changed. Read `mex logging --json` before optional `mex log` notes: `significant` records material rationale, `checkpoints` batches useful notes at task/session boundaries, and `manual` avoids unsolicited notes. Honor explicit user log requests in every mode; mandatory workflow Activity and recovery audits remain required.
