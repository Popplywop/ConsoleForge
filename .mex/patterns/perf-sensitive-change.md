---
name: perf-sensitive-change
description: Changing a hot path (Renderer, RenderContext, LayoutEngine, LayoutSolver, CmdDispatcher, SizeConstraint resolution, any IWidget.Render) and proving it with the three-tier performance policy.
triggers:
  - "performance"
  - "benchmark"
  - "allocation"
  - "bench.sh"
  - "hot path"
  - "optimize"
edges:
  - target: context/rendering.md
    condition: for what the renderer, cache and solver do on each frame
  - target: context/setup.md
    condition: for bench.sh commands and benchmark pitfalls
  - target: patterns/debug-rendering-and-layout.md
    condition: when the optimisation changed visible output
grounds_to: []
last_updated: 2026-09-22
mex:
  id: mx_01M363AXR4CSBVN1XH7EZGMJZ7
  type: pattern
  status: promoted
  revision: 3
  title: perf-sensitive-change
  relations:
    - type: related_to
      target: mx_01M363AXKGBNYSM6ST8T0SKZ6V
      note: for bench.sh commands and benchmark pitfalls
    - type: related_to
      target: mx_01M363AXP8X6EYXEBYJ4KJQNEV
      note: when the optimisation changed visible output
---

# Perf-Sensitive Change

## Context
The root `AGENTS.md` Performance section is authoritative. Tier 1 = assertions in `tests/ConsoleForge.Tests/Performance/` (and counting tests like `WidgetCacheTests`), run in CI. Tier 2 = `./bench.sh -f '<filter>'` (short, in-process, wide error bars, exact allocations). Tier 3 = `./bench.sh --full -f '<filter>'` (Release, minutes). Benchmarks live in `tests/ConsoleForge.Benchmarks`.

## Steps
1. Decide whether the question is countable (bytes, cache hits, cells emitted, frames drawn). If yes, write the Tier 1 test first: warm up, average over iterations, `GC.GetAllocatedBytesForCurrentThread()`, assert a **ceiling**.
2. Iterate with Tier 2 on a narrow filter.
3. Before committing, run Tier 3 on the benchmarks that can reach the changed code — before the change, then after, sequentially, nothing else running. Where old and new can coexist, put both in one class with `[Benchmark(Baseline = true)]`.
4. Confirm the benchmark drives the real path (`Renderer`, not `ViewDescriptor.From`).
5. Record numbers that matter in the commit message; an allocation regression needs explicit justification.
6. `CHANGELOG.md` Unreleased; move the WISHLIST item to Fixed if applicable.

## Gotchas
- `bench.sh` exits 0 when BenchmarkDotNet rejects its arguments — it prints help and old results stay in `BenchmarkDotNet.Artifacts/`. Check the run actually executed.
- `--filter '*RenderBenchmarks*'` also matches `NewWidgetRenderBenchmarks`; narrow it.
- Mean-time deltas under ~10% are noise unless reproducible and reachable from the change. Allocated bytes are the trustworthy number.
- Debug builds and concurrent builds/tests invalidate timing.

## Verify
- Tier 1 test exists if the defect was countable, and fails without the fix.
- Tier 3 output shows the new run's timestamp and the expected benchmarks.
- `dotnet test ConsoleForge.slnx` green.

## Update Scaffold
- [ ] Update `.mex/ROUTER.md` "Current Project State" if what's working/not built has changed
- [ ] Update any `.mex/context/` files that are now out of date
- [ ] If this is a new task type without a pattern, create one in `.mex/patterns/` and add to `INDEX.md`
