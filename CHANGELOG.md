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
- `TerminalCapabilities.InsideTmux` — whether the host is a tmux session, which decides
  DCS passthrough wrapping and per-frame placement renewal. `Detect()` fills it from
  `TMUX`/`TERM` as before; the point is that a caller can now state it. A payload built
  without capabilities still probes the environment, so nothing changes for one.
- `IRawEscapePayload.Place` — re-position content the terminal already holds, for a
  payload that moved between frames. Defaults to `Encode`, so existing implementations
  keep working; override it when the protocol can reposition without re-transmitting.
- `KeyPattern.OfChar(char)` — match a binding against the character the terminal
  produced rather than a `ConsoleKey`, so printable bindings work on every keyboard
  layout. `?` was `WithShift(ConsoleKey.Oem2)`, which is that glyph's position on a US
  layout and nowhere else; it is now `OfChar('?')`. Case-sensitive letter bindings
  (`n` vs `N`) fall out of the same comparison. Shift stays a wildcard — it has already
  been consumed producing the glyph — while Ctrl and Alt must be absent, since
  Ctrl+letter arrives as a control character and Alt+key is a separate binding.
- `KeyMap.On(char, ...)` — bind a character directly, mirroring the `ConsoleKey`
  overloads.
- `ConsoleForge.Core.ListState` — selection and scroll as a pure value (`Count`,
  `SelectedIndex`, `ScrollOffset`, `ViewportHeight`) with `HandleKey`, `MoveUp/Down/To`,
  `WithCount`, `WithViewport` and a `VisibleRange` slice. It holds the item *count*, not
  the items, so it serves an array, a filtered view or virtualised pages alike. Because
  the viewport lives in the state, scroll is maintained on every move rather than being
  something a model remembers to recompute.
- `ConsoleForge.Core.TextAreaState` — multi-line editing as a pure value (`Lines`,
  `CursorRow`, `CursorCol`, `MaxLines`). Single-line edits delegate to `TextInputState`,
  so grapheme clusters, word jumps and kill-to-edge behave identically in both; this type
  owns only what crosses lines — Up/Down, Enter, and the joins at either edge.
- `ConsoleForge.Core.TextInputState` — a pure editing reducer (`Value`, `Cursor`,
  and the edit operations) usable without a `TextInput` widget.
- `IFocusable.FocusKey` — an optional, application-assigned identity for a focusable
  widget. Null (the default) means the widget is not a click-focus target, so nothing
  changes for applications that do not opt in.
- `FocusRequestedMsg` — raised when a left-click lands on a widget carrying a
  `FocusKey`. The framework hit-tests, because a model cannot; the model decides what,
  if anything, focus means.
- `FocusManager.CollectFocusKeys` — the focus keys in a tree, depth-first, skipping
  widgets without one.
- Pixel image rendering via the Kitty graphics protocol: `ImageWidget`,
  `RgbaImageData`, and `IRawEscapePayload` for escape sequences that bypass the
  cell buffer.

### Changed

- `TextAreaChangedMsg` and `ListSelectionChangedMsg` are `[Obsolete]`. Neither has been
  raised since widgets stopped emitting messages — `TextArea.Update` and `List.Update`
  return the next widget — but both still documented a dispatch that does not happen, and
  the Gallery carried a `case ListSelectionChangedMsg` arm that could never run. Same
  treatment `TextInputChangedMsg` and `CheckboxToggledMsg` already had.
- `TextArea` and `List` delegate to `TextAreaState` and `ListState`, so widget and reducer
  cannot drift. Both gain bindings they never had: `TextArea` picks up word jumps
  (`Ctrl+←/→`, `Ctrl+W`), kill-to-edge (`Ctrl+U`/`Ctrl+K`) and `Ctrl+A`/`Ctrl+E`, and its
  cursor now moves and deletes in grapheme clusters rather than UTF-16 units, so an emoji
  is no longer split in half. `List` picks up Home/End. `Ctrl+A` in a `TextArea` was
  previously ignored and now means start-of-line, matching `TextInput`.
- **Breaking:** `KeyPattern.Key` is `ConsoleKey?`. Null is a wildcard, matching how the
  modifier fields already behaved, which is what lets a pattern match on `Character`
  alone. Constructing a pattern is unchanged, since `ConsoleKey` widens implicitly, but
  code that reads or deconstructs `.Key` now gets a nullable. A consequence worth
  knowing: a pattern with no field set — `default(KeyPattern)` — matches every key
  event, which is a usable catch-all but will swallow all input if reached by accident.
- **Breaking:** focusable widgets moved to the `Update` API. `IFocusable.OnKeyEvent`
  is gone; input flows through the Elm path instead.
- **Breaking:** `IFocusable.HasFocus` is `init`-only. It was a mutable setter on
  record widgets, so focus could be changed after construction and the change
  participated in record equality. Every assignment in this repository was
  already an object initializer or a `with`, so call sites are unaffected unless
  they assigned after construction.
- **Breaking:** `FocusManager.GetNext` and `GetPrev` take and return a focus key rather
  than an `IFocusable`, so a model can drive Tab traversal from the key it already
  stores. They also step a plain key list, so widget-shaped concerns stay in
  `CollectFocusKeys`.
- **Breaking:** `Cmd.Debounce` and `Cmd.Throttle` take a key: `Cmd.Debounce(key,
  interval, fn)`. The window belongs to the key and is held by the running program,
  so a command built fresh in `Update` — the only way the Elm loop builds one — now
  rate-limits correctly. The previous overloads kept their state in the closure the
  factory returned, so they worked only if one command instance was stored and
  re-dispatched, and did nothing at all in normal use. Add a key to each call site;
  a per-item closure is safe, since the latest invocation under a key wins.
- **Breaking:** a suppressed `Debounce`/`Throttle` call now produces no message.
  It previously resolved to a `RedrawMsg` sentinel, which models had to recognise
  and discard, and which repainted the screen once per suppressed call.
- **Breaking:** `BatchMsg`, `BatchDispatchMsg` and `SequenceMsg` are `internal`.
  They carry `Cmd.Batch` and `Cmd.Sequence` results to the event loop, which unfolds
  them before the model runs, so a model never received one. Matching on them in an
  `Update` no longer compiles.
- `LayoutEngine` and `Container.Render` now share one layout solver, so the
  regions a container renders into match the ones layout resolved.
- Input is drawn one frame per batch rather than one frame per message. A burst
  of key auto-repeat now costs a single frame instead of one frame per keystroke.
- The benchmark mandate became a tiered performance policy — see the Performance
  section of `AGENTS.md`.

### Removed

- **Breaking:** `FocusIndexChangedMsg`, and the framework's Tab traversal along with it.
  `App` no longer intercepts Tab, tracks a focus index, or walks the tree on every Tab
  press — Tab arrives at the model as an ordinary `KeyMsg`, which is where every
  application was already handling it. The message had no consumers: the Gallery
  explicitly discarded it, documenting that letting a flat depth-first index drive its
  own two-pane focus "conflicts with ToggleFocusMsg and causes double-press issues."
  Focus now lives in the model, which is where it already lived in practice.

### Fixed

- Documentation: `Modal.ShowBackdrop` described itself as "a dark overlay" that "replaces
  background content", which reads as a translucent tint and is not what it does. The
  backdrop fills the modal's entire region with spaces, erasing whatever is beneath it —
  and since the modal's size constraints are flex and `ZStack` hands every layer the full
  region, that is normally the whole terminal, so a backdrop over a `ZStack` blanks the
  application behind the dialog. `Modal` and `ZStack` now both say so. `BackdropStyle`
  claimed "faint text": the fill writes spaces, so only its background colour is visible
  and the default's `Faint` does nothing. The flag's behaviour is unchanged; a dim that
  restyles the cells underneath rather than blanking them remains unimplemented.
- An image that moved vanished outside tmux. A payload present in the previous frame
  was re-placed through `Refresh`, which returns null when not inside tmux — correctly,
  because a payload that stayed put needs nothing and renewing one every frame is what
  made artwork flicker. But the frame builder had already emitted the delete for the
  region the payload just left, so outside tmux a scrolling shelf deleted each image and
  placed nothing. Motion and renewal are now separate: `IRawEscapePayload.Place` re-places
  a payload that moved (mandatory, and cheap — Kitty sends one `a=p`, never a re-upload),
  while `Refresh` keeps its narrower job of renewing a *stationary* placement against
  tmux's cursor drift. The behaviour was invisible to anyone running the test suite
  inside tmux, where the renewal happened to cover the moved case.
- Layout allocations are keyed by widget **identity**, not value. Widgets are
  records, so two structurally identical widgets in one tree collided in the
  allocation map and the second evicted the first, leaving the first reporting a
  region it did not occupy. Rendering never noticed; hit-testing did, and a
  duplicate widget could be entirely unclickable.
- Child commands returned by nested components are dispatched correctly.
- Debounce and throttle windows survive across command instances. Both factories
  held their state in the returned closure, so the documented usage — deciding to
  debounce inside `Update`, which builds a new command each call — silently never
  debounced. Storing one instance was no way out either: the captured function
  varies per item, and parking a mutable closure in the model breaks immutability.
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
