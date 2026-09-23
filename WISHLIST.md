# ConsoleForge Wishlist

Gaps and improvement ideas found while building real Elm-loop applications on
top of ConsoleForge, grouped by the release they are scheduled for:

- PlexTui — a Plex client with drill-down navigation, long scrolling lists and
  poster artwork, which is what surfaced the renderer and event-loop entries in
  the Fixed section below.

**Design target: Elm-correct in C#, with helpers** — see `AGENTS.md`. Not
`bubbles` parity. Several items below cite `bubbles` as prior art; take the
ergonomic goal from it, never the stateful-component mechanism. Where the two
conflict, Elm wins and the gap closes with a pure helper. `TextInputState` is the
worked example — see the 0.4.0 rows in Fixed — and item 8 carries the rest.

## 0.4.0

The release that finishes the Elm-purity migration. `OnKeyEvent` is gone, `HasFocus`
is immutable, focus has moved out of the framework, and the reducers that keep widget
and model from drifting are in. Every remaining breaking change landed here, so 0.5.0
can be purely additive.

Item numbers are stable identifiers, not priorities or ordering — they never get
reused or renumbered.

**Every item is resolved** — see the Fixed table. **Done when:** `CHANGELOG.md` has its
`[Unreleased]` section promoted to `0.4.0`, and the tag is pushed.

## 0.4.1

Found by running PlexTui against a real Plex server on 0.4.0. All three are in the pixel
path, which 0.4.0 changed more than any other — and which nothing benchmarks, so what its
frames actually cost has never been measured, only reasoned about.

Items 10 and 11 have landed — see the Fixed table. Item 12 remains.

### 12. `f=100` declares PNG for whatever bytes it is handed

`KittyPayload.Encode` writes `a=t,f=100`, and `100` means PNG. Nothing checks that the
bytes are one. PlexTui feeds it Plex artwork, which is JPEG (`ff d8 ff e0`), and it draws
correctly — because WezTerm sniffs the content rather than trusting the declaration. That
is a terminal being lenient, not a contract being met.

The protocol offers no JPEG: `f` takes 24 (RGB), 32 (RGBA) or 100 (PNG), so "send the
right format code" is not on the table. Either reject what cannot be honoured, or give
callers a path for pixels they decoded themselves.

**Proposal, smallest first:**

- Check the magic bytes where the payload is built and fail loudly, instead of emitting a
  declaration the data contradicts. `ImageWidget.PngData` is already named for the
  contract; it is simply not enforced.
- Let the Kitty path accept `RgbaImageData` as `f=32`. `ImageWidget` already carries
  `RgbaData` for half-blocks, so this is a branch in `ResolveMode`, and it lets an
  application decode whatever it has and hand over pixels.

Decoding JPEG inside the framework is the option to avoid — it buys one format and puts an
image-codec dependency on every consumer, against a runtime dependency list that is
currently System.Reactive and nothing else.

Note for a consumer meanwhile: Plex returns a real PNG for `&format=png`, at about 5.6x
the bytes of its JPEG — 77 KB against 13.7 KB for one 160x240 poster — so "ask the server
for the format you declared" is a real choice with a real price.

## Later

Additive, so deferring costs no one a migration.

### 9. A widget's viewport height never reaches the model

Scroll is a function of how many rows are visible, and nothing tells a model that
number. A widget learns its region at render time, and `WindowResizeMsg` carries the
terminal's size, not a widget's — so every consumer hand-computes it and hardcodes
the result: `PlexTui` has `private const int ViewportRows = 20`, the Gallery passes
`viewportHeight: 12` and `8`, and `TextArea` pages by a fixed 10 rows. Each of those
is a guess that silently goes wrong when the layout around it changes.

`ListState` carries a `ViewportHeight` and keeps scroll correct against it, which
makes the missing input the only crude part left: `List` itself has nothing to put
there, so it constructs the state with no viewport and leaves `ScrollOffset` to the
model.

**Proposal:** report resolved regions back to the model, so `ViewportHeight` comes
from layout rather than arithmetic. A `RegionChangedMsg` keyed by `FocusKey` — the
identity the application already assigns for click-focus — would fit the existing
shape, and `FocusRequestedMsg` is the precedent: the framework reports what only it
can know, and the model decides what it means.

Letting a widget own its own scroll would also close it, and is the thing to avoid:
that is the stateful-component mechanism the design target rules out.

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
`KeyMap.On(KeyBinding, ...)` overload, and a `HelpBar` widget rendering
`q quit · esc back · ? help` from `IReadOnlyList<KeyBinding>` in the theme's
muted style.

## Fixed (for the record)

What each gap turned out to be, and what shipped.

| Version | Fix |
|---------|-----|
| 0.3.1 | Hardware cursor stayed hidden after quit — `SetCursorVisible` wrote to the render buffer (never flushed on the quit path); `Dispose` now re-shows the cursor **after** leaving the alternate screen (VTE/tmux fold cursor visibility into the private-mode save). |
| 0.3.1 | SourceGen referenced Roslyn 5.3 — failed to load (CS9057) on every stable SDK. Retargeted to 4.4.0; generators should reference the **oldest** Roslyn they need. |
| 0.3.2 | SourceGen couldn't resolve framework message types (`KeyMsg`, `WindowResizeMsg`) — `GetSymbolsWithName` sees source declarations only. Now: parameter-type inference + `ConsoleForge.Core`/`Widgets` metadata fallback. |
| 0.3.2 | `Cmd.Batch` was a `Task.WhenAll` barrier (spinner ticks waited on fetches) and **nested batches were silently dropped** by the event loop. Batch now resolves to `BatchDispatchMsg`; the loop dispatches children independently — messages stream in as they complete, and nesting unfolds correctly. |
| 0.3.2 | `DispatchCmd` executed every async command **twice** (the synchronous fast-path check invoked it, then the slow path re-invoked it via `CmdDispatcher`). The slow path now awaits the already-started task. |
| 0.4.0 | Text editing lived nowhere reusable: `TextInput` was render-only, so every consumer re-implemented append/backspace in its own `Update`. `bubbles` solves this with a nested component owning its own update loop, which the Elm architecture has no room for; the Elm-native answer is a pure reducer. `ConsoleForge.Core.TextInputState` is that — `Value`, `Cursor`, and `HandleKey(KeyMsg)`. Movement and deletion work in grapheme clusters, so an emoji or combining mark moves as one unit rather than leaving half a surrogate pair, and the cursor re-normalises on every `with`, so no copy lands mid-cluster. Adds word jumps (`Ctrl+←/→`, `Ctrl+W`), line jumps (`Home`/`End`, `Ctrl+A`/`Ctrl+E`), kill-to-edge (`Ctrl+U`/`Ctrl+K`) and `Insert`. `TextInput.Update` delegates to it, so widget and reducer cannot drift — and the widget picked up all of the above for free. `TextAreaState` and `ListState` carry on as item 8. |
| 0.4.0 | Three input mechanisms coexisted: model `Update` + `KeyMap`, imperative `OnKeyEvent(KeyMsg, Action<IMsg>)` callbacks with framework-held focus, and reducers. The imperative path worked against the Elm loop — messages left through a side channel and focus state sat outside the model. `OnKeyEvent` is gone; `IFocusable` is now `(IFocusable Next, ICmd? Cmd) Update(KeyMsg key)`. `IFocusable.HasFocus` is `init`-only across the interface and all five focusable widgets, so a widget's focus can no longer be mutated after construction — it was a settable property on records, where the mutation also participated in equality. Every assignment in the repository was already an object initializer or a `with`, so only two test sites moved. Focus *ownership* was a separate gap, closed by the focus-ownership row below. |
| 0.4.0 | Focus was framework state the model mirrored. `App._focusIndex` was the source of truth, computed by walking the tree depth-first on every Tab press and pushed in as `FocusIndexChangedMsg` — a message with no consumers, which the Gallery explicitly discarded because a flat index conflicted with its own two-pane focus and caused double-presses. The index was positional too, so a tree that changed shape between frames re-pointed it at a different widget. All of it is gone: Tab reaches the model as an ordinary `KeyMsg`, which is where every application already handled it. What the framework kept is what only it can do — hit-testing a click against the rendered frame — now reported as `FocusRequestedMsg` carrying the clicked widget's `FocusKey`, an optional identity the application assigns. Null means not click-focusable, so nothing changes for an application that does not opt in. `FocusManager` keeps the traversal helpers, re-shaped to keys, for a model that wants generic Tab order. |
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
| 0.4.0 | Making images movable left one path unfixed: a payload that moved was re-placed through `Refresh`, which returns null outside tmux — correctly, since a payload that stayed put needs nothing and renewing one every frame is what made artwork flicker. But the frame builder had already deleted the placement at the region the payload just left, so outside tmux a scrolling shelf deleted each image and placed nothing, and the artwork simply vanished. Motion and renewal are now distinct operations: `IRawEscapePayload.Place` re-places a payload that moved — mandatory, and cheap, since Kitty separates transmit from place — while `Refresh` keeps the narrower job of renewing a *stationary* placement against tmux's cursor drift. The bug was invisible to anyone running the suite inside tmux, where the renewal happened to cover the moved case, which is the more useful half of the lesson: `KittyPayload` read `TMUX` from the ambient environment, so the test suite's result depended on the developer's terminal. tmux is now a declared capability (`TerminalCapabilities.InsideTmux`, filled by `Detect()`), tests state the mode instead of inheriting it, and a CI workflow runs the suite on every push and pull request — previously the only job running tests fired on a `v*.*.*` tag, so a branch could carry a failing test to the edge of a release. |
| 0.4.0 | `SizeConstraint.Auto` resolved as flex weight 1 — an Auto child took an equal share of free space instead of shrinking to fit, so `TextBlock` and `Spinner`, which default to Auto on both axes, filled their container. Widgets now report a content size through the new `IMeasurable`, implemented by `TextBlock`, `Spinner`, `Container`, `BorderBox` and `ZStack`; anything not implementing it keeps the old behaviour, so no third-party layout moved. Min/Max fold over the measurement (`Max(10, Auto)` is content capped at 10). Prerequisite: `LayoutEngine` and `Container.Render` carried separate copies of the constraint arithmetic and would have disagreed about where Auto children go, so both now share `LayoutSolver`. Overflow of *measured* children clamps rather than throwing — running out of room for text is ordinary; an impossible all-`Fixed` layout still throws. |
| 0.4.0 | Documentation drift: README called the entry point `Program.Run` (it is `App.Run`), typed `Subscriptions()` as `IEnumerable` where the interface requires `IReadOnlyList`, omitted `KeyPattern.WithShift`, and never mentioned `ImageWidget` or Kitty graphics. `SizeConstraint.Auto` claimed to shrink to content in both README and XML docs while resolving as flex weight 1; both now state the real behaviour and point at item 1. `TextArea` carried a `<see cref="OnKeyEvent"/>` to a member deleted in this version, which Doxygen published. `TextInputChangedMsg` and `CheckboxToggledMsg` documented a dispatch that no longer happens; both are now `[Obsolete]`. |
| 0.4.0 | `TextArea` re-implemented cursor movement and editing in its own `Update` and `List` clamped selection itself, so anyone wanting a filter box or a selectable list without taking the widget rewrote both. `ConsoleForge.Core.TextAreaState` and `ConsoleForge.Core.ListState` are the reducers; both widgets delegate, as `TextInput` does, so widget and reducer cannot drift. `TextAreaState` owns only what crosses lines — Up/Down, Enter, and the joins at either edge — and hands every single-line key to `TextInputState`, which is how `TextArea` picked up word jumps, kill-to-edge and `Ctrl+A`/`Ctrl+E` for free, and how its cursor stopped stepping in UTF-16 units and splitting emoji in half. `ListState` holds the item *count* rather than the items, so it serves an array, a filtered view or virtualised pages alike. Its one real design question was where the viewport height lives: it is a layout result, so a state that took it per call could never maintain scroll on its own and could not express paging at all. Putting `ViewportHeight` in the state makes "the selection is visible" an invariant again — reconciled by the same function every constructor and every `init` accessor funnels through, so no `with` in any order can produce a selection outside the list or an offset that hides it. `List` still cannot use that half: a widget learns its region only at render time, so it constructs the state with no viewport and leaves `ScrollOffset` to the model, which is the gap the viewport-plumbing item now carries. `TextAreaChangedMsg` and `ListSelectionChangedMsg` went `[Obsolete]` alongside, having documented a dispatch that stopped happening when widgets stopped emitting messages; the Gallery's `case ListSelectionChangedMsg` arm was dead code and is gone. |
| 0.4.0 | `Modal.ShowBackdrop` documented itself as "a dark overlay" that "replaces background content" — two readings at once, and everyone takes the first, which is a translucent tint. It is a paint-over: the backdrop fills the modal's entire region with spaces, so the cells beneath are erased, not dimmed. The region is the trap. `Modal` defaults both size constraints to flex and `ZStack` hands every layer the full region, so the fill is normally the whole terminal — a backdrop over a `ZStack` blanks the application behind the dialog, which is what "the application disappeared" was. Both widgets now say so, `ZStack` gaining the general form of it: layers composite by painting, not blending, so a layer that fills its region hides every layer under it. `BackdropStyle`'s "faint text" was wrong too — the fill writes spaces, so only its background colour is visible and the default's `Faint` is inert. Behaviour unchanged, and one characterisation test now pins the erasure, which the existing ZStack test set up and then never asserted. The `BackdropStyle` dim stays unimplemented: restyling cells already in the buffer is a read-modify-write the render context does not offer, since `Write` replaces content and style together — a `RenderContext` capability question, and a feature rather than a correction. |
| 0.4.0 | `KeyPattern` could only name a `ConsoleKey`, so every printable binding silently assumed a US keyboard: `?` was `WithShift(Oem2)`, which is that glyph's position on that layout and nowhere else. `KeyMsg` already carried the character the terminal produced — layout applied by the OS long before the byte arrives — so the fix was to match on it. `KeyPattern.OfChar(char)` does, with `KeyMap.On(char, ...)` as the shorthand. Case-sensitive bindings (`n` vs `N`) fall out of the same ordinal comparison, and characters with no `ConsoleKey` mapping became bindable at all for the first time. Shift stays a wildcard, because it was already consumed producing the glyph and requiring it would restore the assumption being removed; Ctrl and Alt must be absent, since Ctrl+letter arrives as a control character and Alt+key is a separate binding that would otherwise match — the Alt path preserves `Character`. **Not additive, as the item claimed:** expressing "match the character, whatever key made it" meant `Key` became `ConsoleKey?`, so null is a wildcard as it already was for the modifiers. Construction is unchanged, but reading or deconstructing `.Key` now yields a nullable, and a pattern with no field set matches every key — a usable trailing catch-all, and a trap for a `default` struct reached by accident. The terminal layer needed nothing: printable keys never touch the escape parser, and the CSI/SS3 paths correctly report no character. |
| 0.4.0 | `Cmd.Debounce` and `Cmd.Throttle` held their state in the closure the factory returned, so they rate-limited only across re-dispatches of one stored instance — and `Update`, which is where you decide to debounce, builds a fresh command every call. The documented usage silently did nothing. Storing an instance was no way out: the captured `fn` varies per item (a different poster URL per row), and a mutable closure in the model breaks immutability. Both now take a key, and the window belongs to that key in the event loop, so re-dispatch supersedes the pending one however many instances were built. The latest `fn` wins, which is what makes a per-item closure safe. Suppressed calls now emit nothing: they used to resolve to a `RedrawMsg` sentinel the model had to discard, which repainted once per suppressed call. Windows are linked to the shutdown token, so none outlives the program. PlexTui's generation-counter-plus-`Cmd.Tick` workaround was the shape of the missing feature. |
| 0.4.1 | The pixel path was unmeasured and its tmux renewal untested (item 11). Only one motion test built `KittyInTmux`, so `Refresh` — the branch renewing a *stationary* image against tmux's cursor drift, whose unconditional version was 0.4.0's flicker bug — had nothing asserting it renews, or that it replaces rather than stacks. Four tests now pin it both ways: one `a=p` per image per frame in tmux, same placement id, no delete; nothing at all outside tmux. Each fails with `Refresh` returning null. Three benchmark classes cover what `AGENTS.md`'s policy never reached: `PixelShelfBenchmarks` (six Kitty posters through `Renderer`, steady / scrolling / one-new frames, in and out of tmux — Tier 2: 5.9 / 9.5 / 164 KB outside tmux, 10.2 / 13.6 / 245 KB inside), `PixelEncodeBenchmarks` (the `ConditionalWeakTable` hit: 24 ns / 40 B against 19.7 µs / 38 KB uncached for a 14 KB poster) and `PixelHalfBlockBenchmarks` (42 µs / 28 KB for an 18x9 card, 176 µs / 111 KB at 36x18). |
| 0.4.1 | A Kitty image uploaded on the frame it was meant to appear in (item 10), so under tmux — which forwards a large DCS-wrapped upload after the cells around it — a shelf's `░` placeholder showed for the gap. `Cmd.Preload(payload)` transmits a payload when its bytes arrive, through the new `IRawEscapePayload.Transmit` (Kitty: the `a=t` chunks `Encode` already started with, now factored out); `RenderContext` records it as held, and its first appearance costs one `a=p`. The event loop sends a synchronously-completing preload straight from `DispatchCmd`, before `ProcessMsg` marks the model dirty, so the upload precedes any frame showing the new model — the render timer's included. Deliberately best-effort: nothing is sent for a payload already on screen or already held, a held entry is consumed by its first appearance and dropped on resize, and before the first frame it is a no-op — each case falling back to today's full `Encode`. Priced as the item asked: opt-in, costs an application one line in the `Update` that receives the bytes, and the frame path gains one `HashSet` lookup per new payload. The effect on the tmux gap itself was reasoned about, not measured against a real tmux. |
