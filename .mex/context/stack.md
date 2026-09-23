---
name: stack
description: Technology stack, library choices, and the reasoning behind them. Load when working with specific technologies or making decisions about libraries and tools.
triggers:
  - "library"
  - "package"
  - "dependency"
  - "which tool"
  - "technology"
edges:
  - target: context/decisions.md
    condition: when the reasoning behind a tech choice is needed
  - target: context/conventions.md
    condition: when understanding how to use a technology in this codebase
  - target: context/setup.md
    condition: when installing SDKs or tools
  - target: patterns/source-generators.md
    condition: when working on the Roslyn generator or its version constraints
grounds_to: []
last_updated: 2026-09-22
---

# Stack

## Core Technologies
- **C# / .NET 8 (`net8.0`)** — the framework target; `ImplicitUsings` and `Nullable` enabled. No preview language features.
- **`netstandard2.0` Roslyn incremental generator** — `src/ConsoleForge.SourceGen/`, packed as an analyzer in its own NuGet package.
- **`ConsoleForge.slnx`** — the XML solution format; build and test through it.
- **ANSI/VT terminal protocol** — raw mode via `termios` P/Invoke on Unix (`#if !WINDOWS` guards; `WINDOWS` is defined when building on Windows) and the console API on Windows.
- **DocFX 2.78.5** — docs site from `docs/*.md`, `index.md`, `toc.yml` plus generated API metadata.

## Key Libraries
- **System.Reactive 6.1.0** — the only runtime dependency; terminal input is an `IObservable`, and `Sub.FromObservable` adapts Rx streams.
- **xUnit v3 (`xunit.v3` 3.2.2)** — all tests; not MSTest/NUnit. The SourceGen test project also pulls classic `xunit` 2.9.2 through `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit`.
- **FsCheck 3.3.2** — referenced by `tests/ConsoleForge.Tests` for property tests, though no test currently uses it.
- **Verify.XunitV3** — snapshot testing of generator output.
- **Microsoft.Extensions.TimeProvider.Testing** — fake clock for `Cmd.Tick` / rate-limit tests; `App` takes a `TimeProvider`.
- **BenchmarkDotNet 0.15.8** — `tests/ConsoleForge.Benchmarks`, driven through `bench.sh`.
- **Microsoft.SourceLink.GitHub** — embedded sources/symbols in the packages.

## What We Deliberately Do NOT Use
- No additional runtime NuGet packages without asking — System.Reactive is the entire dependency surface.
- No `bubbles`-style stateful components or widget-owned update loops, even where Bubble Tea has them (design target: Elm-correct in C#, with helpers).
- No Doxygen — replaced by DocFX; `docs/` is now handwritten source, not generated output.
- No `Thread.Sleep` / real timers in tests — inject time.
- No newer Roslyn in the generator than the SDK floor allows (see Version Constraints).

## Version Constraints
- The generator references `Microsoft.CodeAnalysis.CSharp` **4.4.0** on purpose; Roslyn/analyzer versions were downgraded so the generator loads in older SDKs. Test projects use 5.3.0 — do not "align" the generator upward.
- Two SDKs are installed locally (8.0.419 and 10.0.201). CI uses `dotnet-version: 8.x`. The sibling `PlexTui` targets `net10.0` and consumes this source directly.
