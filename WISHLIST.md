# ConsoleForge Wishlist

Gaps and improvement ideas found while building [devo](https://github.com/Popplywop/azboard)
(a C# port of azboard) on top of ConsoleForge — a real Elm-loop application
with API-backed pages, a modal picker, tables, spinners, and keybound
navigation. Ordered roughly by impact.

## Open

### 1. `Auto` size constraint doesn't measure content

`LayoutEngine.ResolveFixed` treats `AutoConstraint` as flex weight 1
(`LayoutEngine.cs`, "Auto = flex weight 1"), while the README and XML docs say
"shrink to content". Consequence: flex-spacer centering silently becomes
equal-thirds splitting — devo had to hand-compute `Fixed(label.Length + 2)`
widths to center a spinner.

**Proposal:** real measure pass. Widgets expose a desired size
(`TextBlock` = text width/line count, `Spinner` = frame + label,
containers = sum/max of children along/across axis); `Auto` resolves to it
during pass 1. Fallback: fix the docs to say `Auto` ≡ `Flex(1)` — but
content-sizing is what every layout consumer actually wants.

### 2. Component-level subscriptions

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

### 3. `KeyBinding` with help metadata + `HelpBar` widget

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

### 4. Layout-independent character matching (`KeyPattern.OfChar`)

`KeyPattern` matches `ConsoleKey` + modifiers only. Symbol keys therefore
assume a US keyboard layout: devo binds `?` as `WithShift(Oem2)` and `/` as
`Plain(Oem2)` — wrong on non-US layouts. `KeyMsg` already carries
`Character`.

**Proposal:** `KeyPattern.OfChar(char)` matching on `KeyMsg.Character`,
preferred for printable bindings (`?`, `/`, case-sensitive letters like
`n` vs `N`).

### 5. `TextInputState` — pure editing reducer

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

### 6. Consolidate input handling into one model

Three input mechanisms coexist: model `Update` + `KeyMap` (Elm style), widget
`OnKeyEvent(KeyMsg, Action<IMsg>)` + `HasFocus`/FocusManager (imperative
callbacks), and now reducers (#5). The imperative widget path works against
the Elm loop: messages are emitted through a side channel and focus state
lives outside the model.

**Proposal:** standardize on the Elm path plus reducers; deprecate
`OnKeyEvent` before more code depends on it.

### 7. `Modal` backdrop semantics

`showBackdrop: true` paints over everything beneath it, which reads as "the
application disappeared" when composed with `ZStack` (devo's PR list vanished
behind the repo picker until the backdrop was disabled). With it disabled,
lower layers show through — good — but nothing dims them.

**Proposal:** document the flag's actual behavior, and consider a
`BackdropStyle`-driven dim (restyle the underlying cells faint/desaturated
rather than blanking them) for a proper modal feel.

### 8. Documentation drift

- README `Subscriptions()` example returns `IEnumerable<(string, ISub)>`;
  the interface requires `IReadOnlyList`.
- README `KeyPattern` lists `Of/WithCtrl/WithAlt/Plain` but omits
  `WithShift` (it exists and is essential for case-sensitive bindings).
- `SizeConstraint.Auto` docs vs. actual flex behavior (#1).
- Test-suite footgun worth a comment somewhere: a test namespace ending in
  `.Terminal` shadows `ConsoleForge.Terminal` for partially-qualified
  references in sibling tests.

## Fixed during the devo build (for the record)

| Version | Fix |
|---------|-----|
| 0.3.1 | Hardware cursor stayed hidden after quit — `SetCursorVisible` wrote to the render buffer (never flushed on the quit path); `Dispose` now re-shows the cursor **after** leaving the alternate screen (VTE/tmux fold cursor visibility into the private-mode save). |
| 0.3.1 | SourceGen referenced Roslyn 5.3 — failed to load (CS9057) on every stable SDK. Retargeted to 4.4.0; generators should reference the **oldest** Roslyn they need. |
| 0.3.2 | SourceGen couldn't resolve framework message types (`KeyMsg`, `WindowResizeMsg`) — `GetSymbolsWithName` sees source declarations only. Now: parameter-type inference + `ConsoleForge.Core`/`Widgets` metadata fallback. |
| 0.3.2 | `Cmd.Batch` was a `Task.WhenAll` barrier (spinner ticks waited on fetches) and **nested batches were silently dropped** by the event loop. Batch now resolves to `BatchDispatchMsg`; the loop dispatches children independently — messages stream in as they complete, and nesting unfolds correctly. |
| 0.3.2 | `DispatchCmd` executed every async command **twice** (the synchronous fast-path check invoked it, then the slow path re-invoked it via `CmdDispatcher`). The slow path now awaits the already-started task. |
