# Changelog

All notable changes to ConsoleForge are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the major version is 0, breaking changes ship in minor releases and are
marked **Breaking**.

## [Unreleased]

### Added

- `HorizontalShelf` — a paged, virtualised strip of cover art, sized for poster
  rows that are wider than the terminal.
- `SizeConstraint.Auto` now sizes to content for widgets that implement the new
  `IMeasurable`. Widgets that do not implement it keep the previous meaning
  (flex weight 1), so existing layouts are unmoved.
- `ConsoleForge.Core.TextInputState` — a pure editing reducer (`Value`, `Cursor`,
  and the edit operations) usable without a `TextInput` widget.
- Pixel image rendering via the Kitty graphics protocol: `ImageWidget`,
  `RgbaImageData`, and `IRawEscapePayload` for escape sequences that bypass the
  cell buffer.

### Changed

- **Breaking:** focusable widgets moved to the `Update` API. `IFocusable.OnKeyEvent`
  is gone; input flows through the Elm path instead.
- `LayoutEngine` and `Container.Render` now share one layout solver, so the
  regions a container renders into match the ones layout resolved.
- Input is drawn one frame per batch rather than one frame per message. A burst
  of key auto-repeat now costs a single frame instead of one frame per keystroke.
- The benchmark mandate became a tiered performance policy — see the Performance
  section of `AGENTS.md`.

### Fixed

- Layout allocations are keyed by widget **identity**, not value. Widgets are
  records, so two structurally identical widgets in one tree collided in the
  allocation map and the second evicted the first, leaving the first reporting a
  region it did not occupy. Rendering never noticed; hit-testing did, and a
  duplicate widget could be entirely unclickable.
- Child commands returned by nested components are dispatched correctly.
- Character width now follows the Unicode `East_Asian_Width` table, fixing
  wide-glyph column drift.
- The widget render cache survives past the first composite. It had been serving
  one widget per frame across two releases, with pixel-identical output.
- Reused widgets are registered once per frame, not twice.
- Layout and render agree on overflow, which also stopped images flickering.
- Pixel graphics are removed on exit. They are not cells, so leaving the
  alternate screen did not take them with it, and under tmux they outlived the
  process.

### Performance

- Layout resolution reuses its allocation map across frames instead of building a
  new dictionary each time: 2,616 B → 0 B per resolve, ~2.55 KB off every
  steady-state frame. The renderer ping-pongs two buffers so the layout of the
  frame on screen stays readable while the next one resolves.
- Focus handling reads the rendered frame instead of calling `View()` again. A
  mouse click cost a full tree rebuild plus its own layout pass (~8.5 KB); a Tab
  keypress cost a tree rebuild (~5.9 KB). Both now read what is already on
  screen, and `View()` runs once per changed frame rather than twice per input
  message.

## [0.3.2] and earlier

Released before this changelog was kept; see the git history and the release
tags for those versions.
