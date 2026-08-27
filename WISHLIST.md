# ConsoleForge Wishlist

Gaps and improvement ideas found while building real Elm-loop applications on
top of ConsoleForge, ordered roughly by impact:

- [devo](https://github.com/Popplywop/azboard) (a C# port of azboard) —
  API-backed pages, a modal picker, tables, spinners, keybound navigation.
- PlexTui — a Plex client with drill-down navigation, long scrolling lists and
  poster artwork, which is what surfaced the renderer and event-loop entries in
  the 0.4.0 rows below.

**Design target: Elm-correct in C#, with helpers** — see `AGENTS.md`. Not
`bubbles` parity. Several items below cite `bubbles` as prior art; take the
ergonomic goal from it, never the stateful-component mechanism. Where the two
conflict, Elm wins and the gap closes with a pure helper. Item 5 is the worked
example, and is why items 4 and 5 rank above the `bubbles`-shaped items 2 and 6.

## Open

### 1. Component-level subscriptions

`IHasSubscriptions` is only consulted on the **root** model
(`App.ReconcileSubscriptions`). Pages/components can't declare recurring
timers, so they fall back to self-re-arming `Cmd.Tick` chains — which die
whenever a message gets routed elsewhere (devo's spinner froze because an
open modal swallowed the in-flight `TickMsg`; gating the chain on the loading
flag helped, but the fragility is structural).

**Proposal:** aggregate subscriptions from nested components — e.g.
`IHasSubscriptions` on any `IComponent`, with the root composing keys like
`"prlist/spinner"` (explicit `Component.CollectSubs(PageA, PageB)` helper or
similar). Spinner animation then becomes declarative:
`("spinner", Sub.Interval(120ms, _ => TickMsg))` active only while loading.

### 2. `KeyBinding` with help metadata + `HelpBar` widget

`KeyMap` handles dispatch but carries no help text, so applications maintain
a second hand-synced list for the help bar — the two inevitably drift apart
(bubbles prevents this with `key.WithHelp` + `help.Model`). devo hand-rolled:

```csharp
record KeyBinding(IReadOnlyList<KeyPattern> Patterns, string HelpKey, string HelpDescription);
KeyMap On(this KeyMap map, KeyBinding b, Func<IMsg> msg); // registers all patterns
```

**Proposal:** promote `KeyBinding` into `ConsoleForge.Core` with an `Enabled`
flag (disabled = skipped by `Handle`, hidden from help), a
`KeyMap.On(KeyBinding, ...)` overload, and a `HelpBar` widget (#14) rendering
`q quit · esc back · ? help` from `IReadOnlyList<KeyBinding>` in the theme's
muted style.

### 3. Layout-independent character matching (`KeyPattern.OfChar`)

`KeyPattern` matches `ConsoleKey` + modifiers only. Symbol keys therefore
assume a US keyboard layout: devo binds `?` as `WithShift(Oem2)` and `/` as
`Plain(Oem2)` — wrong on non-US layouts. `KeyMsg` already carries
`Character`.

**Proposal:** `KeyPattern.OfChar(char)` matching on `KeyMsg.Character`,
preferred for printable bindings (`?`, `/`, case-sensitive letters like
`n` vs `N`).

### 4. `TextInputState` — pure editing reducer *(landed for TextInput)*

Text-editing logic (cursor movement, backspace/delete, word jumps, paste,
unicode handling) currently lives nowhere reusable: the `TextInput` widget is
render-only, so every consumer re-implements append/backspace in its `Update`
(devo's repo-picker filter did). bubbles solves this by making the input a
nested component with its own update loop — a poor fit for the Elm
architecture; the Elm-native answer is a **pure state reducer**:

```csharp
sealed record TextInputState(string Value = "", int Cursor = 0)
{
    public TextInputState HandleKey(KeyMsg key); // all editing logic, written once
}
// consumer: this with { Filter = Filter.HandleKey(key) }
// view:     new TextInput(Filter.Value, cursorPosition: Filter.Cursor)
```

Same pattern later for `TextAreaState` and `ListState` (selection + scroll
clamping). Cursor blink: render-side, or a framework subscription once #2
lands.

**Landed (0.4.0):** `ConsoleForge.Core.TextInputState` — `Value`, `Cursor`, and a
pure `HandleKey(KeyMsg)`. Cursor movement and deletion work in grapheme clusters,
so an emoji or a combining mark moves and deletes as one unit instead of leaving
half a surrogate pair behind, and the cursor is re-normalised on every `with`, so
no copy can land mid-cluster. Adds word jumps (`Ctrl+←/→`, `Ctrl+W`), line jumps
(`Home`/`End`, `Ctrl+A`/`Ctrl+E`), kill-to-edge (`Ctrl+U`/`Ctrl+K`), and `Insert`
for paste. `TextInput.Update` now delegates to it, so the widget and the reducer
cannot drift — and the widget picked up all of the above for free. Still open:
`TextAreaState` and `ListState`.

### 5. Consolidate input handling into one model *(partly done)*

Three input mechanisms coexist: model `Update` + `KeyMap` (Elm style), widget
`OnKeyEvent(KeyMsg, Action<IMsg>)` + `HasFocus`/FocusManager (imperative
callbacks), and now reducers (#5). The imperative widget path works against
the Elm loop: messages are emitted through a side channel and focus state
lives outside the model.

**Proposal:** standardize on the Elm path plus reducers; deprecate
`OnKeyEvent` before more code depends on it.

**Landed:** `OnKeyEvent` is gone — `IFocusable` is now
`(IFocusable Next, ICmd? Cmd) Update(KeyMsg key)`. Still open: the reducers (#5),
and `IFocusable.HasFocus { get; set; }`, a mutable setter that keeps focus state
outside the model. Under the design target that setter is the next thing to go.

### 6. `Modal` backdrop semantics *(documented, dim not implemented)*

`showBackdrop: true` paints over everything beneath it, which reads as "the
application disappeared" when composed with `ZStack` (devo's PR list vanished
behind the repo picker until the backdrop was disabled). With it disabled,
lower layers show through — good — but nothing dims them.

**Proposal:** document the flag's actual behavior, and consider a
`BackdropStyle`-driven dim (restyle the underlying cells faint/desaturated
rather than blanking them) for a proper modal feel.

### 7. `Cmd.Debounce` / `Cmd.Throttle` can't debounce from `Update`

Both hold their state in the closure the factory returns, so they only work if
the *same cmd instance* is re-dispatched — as their XML docs say. But `Update`
is where you decide to debounce, and it builds a fresh cmd each call, so the
natural Elm usage silently never debounces. Storing one instance is not a way
out either: the captured `fn` usually varies per item (PlexTui needed a
different poster URL per row), and parking a mutable closure in the model
violates the immutability rule the architecture is built on.

**Proposal:** key the state outside the closure — `Cmd.Debounce(key, interval,
fn)` with the pending-cancellation table owned by the dispatcher, so re-dispatch
under the same key supersedes the previous one. PlexTui works around it with a
generation counter plus `Cmd.Tick`, which is the pattern the framework should
be providing.

## Fixed (for the record)

What each gap turned out to be, and what shipped.

| Version | Fix |
|---------|-----|
| 0.3.1 | Hardware cursor stayed hidden after quit — `SetCursorVisible` wrote to the render buffer (never flushed on the quit path); `Dispose` now re-shows the cursor **after** leaving the alternate screen (VTE/tmux fold cursor visibility into the private-mode save). |
| 0.3.1 | SourceGen referenced Roslyn 5.3 — failed to load (CS9057) on every stable SDK. Retargeted to 4.4.0; generators should reference the **oldest** Roslyn they need. |
| 0.3.2 | SourceGen couldn't resolve framework message types (`KeyMsg`, `WindowResizeMsg`) — `GetSymbolsWithName` sees source declarations only. Now: parameter-type inference + `ConsoleForge.Core`/`Widgets` metadata fallback. |
| 0.3.2 | `Cmd.Batch` was a `Task.WhenAll` barrier (spinner ticks waited on fetches) and **nested batches were silently dropped** by the event loop. Batch now resolves to `BatchDispatchMsg`; the loop dispatches children independently — messages stream in as they complete, and nesting unfolds correctly. |
| 0.3.2 | `DispatchCmd` executed every async command **twice** (the synchronous fast-path check invoked it, then the slow path re-invoked it via `CmdDispatcher`). The slow path now awaits the already-started task. |
| 0.4.0 | Character widths came from hand-written ranges that called the whole `U+1F300`–`U+1FAFF` block 2 columns wide. Many pictographs there have default *text* presentation and East_Asian_Width `N`, so terminals draw them in one column (`U+1F39E` FILM FRAMES among them), and combining marks / ZWJ / variation selectors were counted as 1 rather than 0. Every glyph after one drifted a column, and since the frame diff trusts its own model of the screen it never repaired it. Table is now generated from the UCD; `WidthWalker` applies the `U+FE0F` promotion that a single rune can't express. |
| 0.4.0 | The frame diff skipped its comparison entirely for cells holding `null` — i.e. every cell no widget wrote, which is most of the screen — and re-emitted them each frame. A 300-key burst emitted 27403 characters against 27435 for a full repaint, so "only changed cells are emitted" was close to false. `null` now compares as the themed default cell, and a cell whose previous content was a sentinel always repaints, because what the terminal shows there isn't derivable from the buffer. |
| 0.4.0 | Fixing the above made a theme switch skip untouched cells and strand the old background on screen. `Reset` now drops the previous buffer when the theme or colour profile changes, comparing themes by value so an equal-but-distinct instance per frame doesn't force a full repaint. |
| 0.4.0 | The event loop rendered synchronously inside `ProcessMsg`, so every message paid for `View` + layout + render + diff + a blocking terminal write. Key auto-repeat outruns a frame, so holding an arrow key queued a redraw per row: scrolling lagged and the selection appeared to skip. Each pass now drains what's already queued and draws once — the same 300-key burst went from 302 frames to 3, with all 300 events applied — rate-limited to the frame budget with the FPS timer as the backstop. |
| 0.4.0 | `Container` called `RegisterWidget` for a widget `TryReuseWidget` had already registered, so every cache hit took two slots in the frame's widget map. |
| 0.4.0 | `ImageWidget` rebuilt its Kitty payload every render, hashing and base64-encoding the whole image each frame to produce a value the diff then used to decide nothing had changed. The encoding is now cached against the byte array's identity, held weakly. |
| 0.4.0 | The widget render cache switched itself off after the first composite of each frame: `RegisterWidget` lazily allocated its buffer by *stealing* `_prevWidgets` and nulling it, and `TryReuseWidget` bails when that is null — so every widget after the first missed and re-rendered. The two maps now ping-pong in `Reset`, which keeps the previous frame readable all frame and still allocates nothing in steady state. A frame where nothing changed went from 192.8 µs / 131 KB to 22.4 µs / 6.5 KB. Invisible in output — a re-render produces identical cells — so it needed benchmarks and cache-level tests to see at all. |
| 0.4.0 | Pixel graphics outlived the application. Images are not cells, so leaving the alternate screen need not remove them, and under tmux the payloads are passed through to the outer terminal — whose screen tmux does not model and will never repaint over. Quitting therefore left artwork on the shell prompt. The teardown path now deletes every placement it drew, before the terminal goes away, and treats the write as best-effort so it cannot mask whatever caused the shutdown. |
| 0.4.0 | Placed images flickered. A placement whose region and content were unchanged was renewed every frame, and Kitty's place command *adds* a placement unless given an id that already exists — so every visible image grew a stack of identical placements at the frame rate and the terminal recomposited the pile. Placements now carry an id derived from their region, making a renewal a replacement. Deletes name that id too, so the same artwork appearing twice on screen — one show in two shelves — can lose one copy without blanking the other. Outside tmux the renewal is skipped entirely: nothing moves an already-placed image there, and the renewal only ever existed for tmux's re-render drift. |
| 0.4.0 | `LayoutEngine` and `Container.Render` disagreed about where an overflowing child goes. The layout pass scaled oversized children back to fit; the render pass placed them at full size and let the region clip. Everything after the first overflowing child then drew somewhere other than where focus and hit-testing believed it was — measured at an 8-row gap for a three-child stack — so a screen holding more content than rows, which is ordinary, came out scrambled with text left behind on navigation. Both paths clamp now; only the throw differs, since the layout pass has already raised it and doing so again at render turns a drawable frame into a crash. Surfaced by a PlexTui home screen with more shelves than fit. |
| 0.4.0 | Images could not move. `RenderContext` paired raw-escape regions across frames by `Region`, so a scrolling row — every region changing every frame — never matched, and each visible image was deleted and re-transmitted in full every frame: measured at three full uploads per frame for a three-card row. The region-keyed cleanup also missed a payload whose slot was taken over by another, stranding a copy of it on screen, since a graphics protocol places an image *in addition to* its existing placements. Payloads are now tracked across frames by `ContentHash` identity: present last frame means re-place, new means upload, moved means delete the old position first. Found by de-risking the PlexTui poster shelf before building it. |
| 0.4.0 | `SizeConstraint.Auto` resolved as flex weight 1 — an Auto child took an equal share of free space instead of shrinking to fit, so `TextBlock` and `Spinner`, which default to Auto on both axes, filled their container. Widgets now report a content size through the new `IMeasurable`, implemented by `TextBlock`, `Spinner`, `Container`, `BorderBox` and `ZStack`; anything not implementing it keeps the old behaviour, so no third-party layout moved. Min/Max fold over the measurement (`Max(10, Auto)` is content capped at 10). Prerequisite: `LayoutEngine` and `Container.Render` carried separate copies of the constraint arithmetic and would have disagreed about where Auto children go, so both now share `LayoutSolver`. Overflow of *measured* children clamps rather than throwing — running out of room for text is ordinary; an impossible all-`Fixed` layout still throws. |
| 0.4.0 | Documentation drift: README called the entry point `Program.Run` (it is `App.Run`), typed `Subscriptions()` as `IEnumerable` where the interface requires `IReadOnlyList`, omitted `KeyPattern.WithShift`, and never mentioned `ImageWidget` or Kitty graphics. `SizeConstraint.Auto` claimed to shrink to content in both README and XML docs while resolving as flex weight 1; both now state the real behaviour and point at item 1. `TextArea` carried a `<see cref="OnKeyEvent"/>` to a member deleted in this version, which Doxygen published. `TextInputChangedMsg` and `CheckboxToggledMsg` documented a dispatch that no longer happens; both are now `[Obsolete]`. |
