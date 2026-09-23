---
name: release-and-docs
description: Cutting a NuGet release of both packages from a v*.*.* tag, and editing/checking the DocFX doc site.
triggers:
  - "release"
  - "publish"
  - "NuGet"
  - "tag"
  - "changelog"
  - "docs"
  - "docfx"
edges:
  - target: context/setup.md
    condition: for docfx and doc-sample commands
  - target: context/stack.md
    condition: for the package list and version constraints
  - target: context/decisions.md
    condition: for why the site moved from Doxygen to DocFX
grounds_to: []
last_updated: 2026-09-22
mex:
  id: mx_01M363AXRSJKZ3GGZPR4D11RXV
  type: pattern
  status: promoted
  revision: 2
  title: release-and-docs
  relations:
    - type: related_to
      target: mx_01M363AXKGBNYSM6ST8T0SKZ6V
      note: for docfx and doc-sample commands
---

# Release and Docs

## Context
`publish.yml` fires on a `v*.*.*` tag: Release build, full test run, `dotnet pack` of `src/ConsoleForge` and `src/ConsoleForge.SourceGen` with `-p:Version=<tag without v>`, push to nuget.org via OIDC trusted publishing (`NuGet/login`), and a GitHub Release with the `.nupkg`s. `docs.yml` runs on push to `main`: DocFX 2.78.5 with `--warningsAsErrors`, after `check-doc-samples.py`, deployed as a Pages artifact.

## Task: Cut a release

### Steps
1. Every item for the release is resolved in `WISHLIST.md` (moved to Fixed).
2. Promote `CHANGELOG.md` `[Unreleased]` to `[X.Y.Z]`; mark breaking changes **Breaking** (0.x: breaking changes ship in minor releases).
3. Bump `<Version>` and `<PackageReleaseNotes>` in both `src/ConsoleForge/ConsoleForge.csproj` and `src/ConsoleForge.SourceGen/ConsoleForge.SourceGen.csproj`.
4. `dotnet build ConsoleForge.slnx -c Release && dotnet test ConsoleForge.slnx -c Release`.
5. Tag `vX.Y.Z` on `main` and push the tag (only with the user's explicit go-ahead — it publishes publicly).

### Gotchas
- The tag, not the csproj, sets the published version (`-p:Version`). Keep them equal anyway so local packs match.
- `--skip-duplicate` means re-pushing an existing version silently does nothing; bump instead.
- A published NuGet version cannot be replaced — only unlisted.

## Task: Edit the docs site

### Steps
1. Edit handwritten `docs/*.md`, `index.md`, `toc.yml`, or the theme in `templates/consoleforge/`. Never edit `api/` or `_site/` (generated).
2. Mark compilable C# blocks with `<!-- doccheck: program -->` or `<!-- doccheck: snippet -->`; unmarked blocks are treated as fragments.
3. `python3 scripts/check-doc-samples.py`, then `docfx docfx.json --serve` to preview.
4. Public API changes need `///` docs in `src/` — the API pages come from them, and cref errors fail the build.

### Gotchas
- CI treats DocFX warnings as errors; a broken xref fails the deploy.
- The entry point is `App.Run` (renamed from `Program` in April 2026); the README now uses it, but the workspace `CLAUDE.md` still says the README shows `Program.Run`.

## Update Scaffold
- [ ] Update `.mex/ROUTER.md` "Current Project State" if what's working/not built has changed
- [ ] Update any `.mex/context/` files that are now out of date
- [ ] If this is a new task type without a pattern, create one in `.mex/patterns/` and add to `INDEX.md`
