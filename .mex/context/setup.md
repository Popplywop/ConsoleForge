---
name: setup
description: Dev environment setup and commands. Load when setting up the project for the first time or when environment issues arise.
triggers:
  - "setup"
  - "install"
  - "environment"
  - "getting started"
  - "how do I run"
  - "local development"
edges:
  - target: context/stack.md
    condition: when specific technology versions or library details are needed
  - target: context/architecture.md
    condition: when understanding how components connect during setup
  - target: patterns/perf-sensitive-change.md
    condition: when running benchmarks
  - target: patterns/release-and-docs.md
    condition: when publishing a release or changing the docs site
grounds_to: []
last_updated: 2026-09-22
mex:
  id: mx_01M363AXKGBNYSM6ST8T0SKZ6V
  type: guide
  status: promoted
  revision: 4
  title: setup
  relations:
    - type: related_to
      target: mx_01M363AX3KJ8JPXRY5W6Z49DY8
      note: when understanding how components connect during setup
    - type: related_to
      target: mx_01M363AXR4CSBVN1XH7EZGMJZ7
      note: when running benchmarks
    - type: related_to
      target: mx_01M363AXRSJKZ3GGZPR4D11RXV
      note: when publishing a release or changing the docs site
---

# Setup

<!-- mex:entity
id: mx_01M363AXJM248AKM0458BPE9P6
type: guide
status: promoted
revision: 1
-->
## Prerequisites
- .NET 8 SDK (CI uses `8.x`; 8.0.419 installed locally alongside 10.0.201).
- A real terminal for running samples — mouse (SGR 1006), 24-bit colour; Kitty/Ghostty/WezTerm-class terminals for pixel images, otherwise half-block fallback.
- Python 3 for `scripts/check-doc-samples.py`.
- DocFX 2.78.5 (`dotnet tool install -g docfx`) only if building docs.

<!-- mex:entity
id: mx_01M363AXHHY18YN03VEAARYZCY
type: guide
status: promoted
revision: 1
-->
## First-time Setup
1. Work from the `main` worktree: the repo is a bare clone (`ConsoleForge/.git`) with worktrees; add one with `git -C ConsoleForge worktree add <dir> <branch>`.
2. `dotnet restore ConsoleForge.slnx` (`nuget.config` clears inherited sources and pins nuget.org only).
3. `dotnet build ConsoleForge.slnx`
4. `dotnet test ConsoleForge.slnx`
5. `dotnet run --project samples/ConsoleForge.Gallery`

## Environment Variables
- None required to build or test.
- `TMUX` / `TERM` (read, not set) — `TerminalCapabilities.Detect()` uses them to decide tmux DCS passthrough and colour/graphics support. Running samples inside tmux changes image behaviour.

<!-- mex:entity
id: mx_01M363AXGEFFR7Z1Q7C8P0Q5RY
type: guide
status: promoted
revision: 1
-->
## Common Commands
- `dotnet build ConsoleForge.slnx` — builds framework, generator, tests, samples, benchmarks; watch for CS1591 (missing doc comment) warnings.
- `dotnet test ConsoleForge.slnx` — both test projects. CI runs it in `-c Release`.
- `dotnet test tests/ConsoleForge.Tests --filter "FullyQualifiedName~TextInputTests"` — one class or test.
- `dotnet run --project samples/ConsoleForge.Gallery` — widget showcase; also `ConsoleForge.TodoApp`, `ConsoleForge.SysMonitor`.
- `./bench.sh -f '*WidgetCache*'` — Tier 2 short in-process benchmark; `./bench.sh --full -f '<filter>'` — Tier 3; `./bench.sh --full --list` — list names.
- `python3 scripts/check-doc-samples.py` — compiles C# blocks in Markdown marked `<!-- doccheck: program -->` / `<!-- doccheck: snippet -->`.
- `docfx docfx.json --serve` — build and preview the site on localhost:8080 (`api/` and `_site/` are generated, gitignored).

<!-- mex:entity
id: mx_01M363AXFG0CCX46T1GS9TCP15
type: guide
status: promoted
revision: 1
-->
## Common Issues
**A benchmark "passed" but numbers did not change:** `bench.sh` exits 0 when BenchmarkDotNet rejects its arguments — it prints help and leaves old results in `BenchmarkDotNet.Artifacts/`. Check the run actually executed.
**Benchmarks disagree run to run:** Debug builds or concurrent builds/tests skew results by >10%. Release only, sequential, nothing else running.
**Doc sample check fails in CI but the site builds:** CI's `ci.yml` runs `check-doc-samples.py` on every push/PR, while `docs.yml` only runs on `main`. Fix the marked block in the Markdown.
**PlexTui stops compiling after a framework change:** it references `../ConsoleForge/main/src/...` by `ProjectReference`, so breaking API changes land there immediately.
