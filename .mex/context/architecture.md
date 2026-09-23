---
name: architecture
description: How the major pieces of this project connect and flow. Load when working on system design, integrations, or understanding how components interact.
triggers:
  - "architecture"
  - "system design"
  - "how does X connect to Y"
  - "integration"
  - "flow"
edges:
  - target: context/event-loop.md
    condition: when the task touches App's message loop, commands, subscriptions, focus or components
  - target: context/rendering.md
    condition: when the task touches layout, the renderer, the widget render cache or raw escape payloads
  - target: context/stack.md
    condition: when specific technology details are needed
  - target: context/decisions.md
    condition: when understanding why the architecture is structured this way
  - target: context/conventions.md
    condition: when about to write code against these components
grounds_to:
  - node: method:d9061895dc5381d1205e8ddc7d3c2593
    fingerprint: mh:64:7b226d696e68617368223a5b33333830393232392c323737393737382c31383733363738372c34333030313533352c31393637303235392c31373839303838352c32393238343833372c343137323934392c31303033373637352c32373239303233332c333631343330342c333135343831372c3337303834332c393836373136312c333534383631352c32353536373834312c333935373632332c33303130323934322c313739363232322c31333536343130392c32303637353135352c31353338313034352c313830393030342c31303839343936382c33353934353230322c35393530333536392c323034383432372c33363936363632392c31303635383136382c32313435343331332c323633383732302c32303330363133312c343431393134332c31303236383738352c34313839363534322c313830393230382c323739373632382c353638393334352c383030343030332c31303838313937342c32303636373539382c31303339333833342c31333034363236372c32353534373136352c31303835323039342c32383934373634372c32363234373734362c333932393834342c373832383038302c31333935323337312c32383034353339322c32333736373130352c31373530353133362c31373531313233392c33343532393532362c333433303238332c333139363438312c33333239313739342c34343930383434392c393237383235332c343739373734372c373735323734322c31303834373139312c31313732363932355d2c226e65696768626f7273223a5b226d6574686f643a3038326439343161313837386631373134363265613563396336386262376430222c226d6574686f643a3039613336366637643134373465376235363636663561316436346664343363222c226d6574686f643a3066373162343362386433346361353038626635333531666633393866383730222c226d6574686f643a3130393530633230336632623339313761313335643430333138343264653838222c226d6574686f643a3265303833356430313738363530626366613431383962653736306331623232222c226d6574686f643a3637626532666338326238383033396633376332656237306635333566346463222c226d6574686f643a3965653965396662363263623635356538633731343337343036396430373864225d2c22746f6b656e436f756e74223a3633377d
    bodyHash: 14d8b8d5148a805f0b9f173c24c5da534df4aff92cd707a1467b5450c7be63ab
  - node: method:f15e3fea0df2ea590eaf89969a744ebf
    fingerprint: mh:64:7b226d696e68617368223a5b33333830393232392c323737393737382c35373136343434392c34333030313533352c31303332343130332c3134353933393831312c363633363132322c31363137323133332c31303033373637352c32363732353039392c31373931303839362c333135343831372c35333237353337332c373238393137332c333534383631352c34393535323839362c35353736363634342c343931373939362c313739363232322c32363736383734342c34383138303736352c3131363135363934372c313830393030342c34353939323637362c33353934353230322c37313036373638312c323034383432372c32353739333139302c373038383238302c313634363838322c33393532303131302c393032323938382c333031303731342c33323639333630322c34333235323031372c33333939343430362c33323034373138352c32353030313332372c33393632313939382c3131353333383633372c32393734303930392c31303732353631392c34363737363635332c3138313936303338392c32353134353335302c343439393438302c32323031313436312c35313139333435372c36383035383334322c31383735373833302c343538343935362c33313534353734392c31383632323237342c34343337373932322c31333939393539332c33353036333337362c3130323336393631312c33333239313739342c3133323539373539362c32303132343137322c31373433353234322c313938343038352c35303736303330382c31313732363932355d2c226e65696768626f7273223a5b226d6574686f643a3135303261343031653037373933663737333034326431316133316335336233222c226d6574686f643a3933356238373561323762656465613137316463343530396533366465376337225d2c22746f6b656e436f756e74223a3231347d
    bodyHash: 98cecbfaeda1434df92e31b8209ae0409d9e3d79e4689fe382273772295169dd
last_updated: 2026-09-22
mex:
  id: mx_01M363AX3KJ8JPXRY5W6Z49DY8
  type: architecture
  status: promoted
  revision: 2
  title: architecture
  relations:
    - type: related_to
      target: mx_01M363AX7N0N5PKS6YX6NRMDGG
      note: when about to write code against these components
---

# Architecture

<!-- mex:entity
id: mx_01M363AX2SER8PEY9W32T7MJBH
type: component
status: promoted
revision: 1
-->
## System Overview
An application supplies an immutable `IModel`; `App.Run(model, theme:, enableMouse:)` owns everything else.

Terminal input (`ITerminal.Input`: key, mouse, resize events) → written into an unbounded `Channel<IMsg>` →
[`App.RunInternal()`](mex://method:d9061895dc5381d1205e8ddc7d3c2593) awaits one message, then drains every message already queued →
each goes through `ProcessMsg` → `model.Update(msg)` returns `(IModel, ICmd?)` →
the cmd runs off-loop and its result `IMsg` re-enters the channel; subscriptions are reconciled against the new model →
if the model reference changed, the renderer is marked dirty →
after the drain, one frame: [`Renderer.RenderIfDirty()`](mex://method:f15e3fea0df2ea590eaf89969a744ebf) calls `model.View()` →
`LayoutEngine.ResolveInto` turns `SizeConstraint`s into a `ResolvedLayout` of `Region`s (via `LayoutSolver`) →
`root.Render(RenderContext)` writes styled cells (and raw escape payloads) →
the cell buffer is diffed against the previous frame and only changed cells are flushed to the terminal.
A timer at the target FPS (clamped 1–60) also calls `RenderFrame`, so dirty state is drawn even when no input arrives.

<!-- mex:entity
id: mx_01M363AX20Z7JXMQ1DQWDFNAET
type: component
status: promoted
revision: 1
-->
## Key Components
- **`App`** (`Core/App.cs`) — the event loop, cmd dispatch, rate limiting (`Cmd.Debounce`/`Throttle`), subscription reconciliation, click hit-testing. Holds `_renderLock` around every render and every read of the last frame. Detail: `context/event-loop.md`.
- **`Renderer`** (`Core/Renderer.cs`) — double-buffered cell diff with per-widget dirty tracking and a widget render cache; keeps the last `(root, layout)` for hit-testing. Detail: `context/rendering.md`.
- **`LayoutEngine` / `LayoutSolver`** (`Layout/`) — two-phase layout. `LayoutSolver` is the only place constraint arithmetic lives; `LayoutEngine` and `Container.Render` both call it.
- **State reducers** (`Core/TextInputState.cs`, `TextAreaState.cs`, `ListState.cs`) — pure values that own editing, selection and scroll rules; `TextInput`, `TextArea`, `List` delegate to them.
- **`KeyMap` / `KeyPattern`** (`Core/`) — declarative key/mouse bindings mapping input to app messages; `[DispatchUpdate]` models with a static `Keys` KeyMap get it wired automatically.
- **`FocusManager`** (`Core/FocusManager.cs`) — static helpers over `FocusKey`s (collect, next/prev, hit-test). Focus itself is model state, not framework state.
- **`ConsoleForge.SourceGen`** — `DispatchUpdateGenerator` and `ComponentGenerator` emit `Update(IMsg)` switches, `Init()` and `IComponent<T>.Result` for `partial` types.
- **Terminal layer** (`Terminal/`) — `AnsiTerminal` (+ `Termios` on Unix, `WindowsConsole`), `TerminalCapabilities.Detect()`, `KittyProtocol`.

<!-- mex:entity
id: mx_01M363AX16S0SY2TEPWSDND05N
type: component
status: promoted
revision: 1
-->
## External Dependencies
- **System.Reactive 6.1.0** — the framework's only runtime package; `ITerminal.Input` is an `IObservable` and `Sub.FromObservable` bridges Rx streams into subscriptions.
- **Host terminal** — ANSI/VT escape sequences, SGR 1006 mouse, alternate screen, raw mode via `termios` (Unix) or the Windows console API. Colour depth and Kitty graphics support are probed, not assumed (`TerminalCapabilities.Detect()`; tmux is detected from `TMUX`/`TERM` and needs DCS passthrough).
- **Roslyn (`Microsoft.CodeAnalysis.CSharp` 4.4.0)** — the source generator targets `netstandard2.0` and this deliberately old Roslyn so it loads in older SDKs.
- **NuGet.org / GitHub Pages** — `publish.yml` packs and pushes both packages on a `v*.*.*` tag; `docs.yml` builds DocFX and deploys a Pages artifact on push to `main`.
- **PlexTui** (sibling directory, untracked) — the main real consumer, referencing this source by relative `ProjectReference`; a breaking change here breaks its next build.

<!-- mex:entity
id: mx_01M363AWXZCXCWA13AMY7M9WGB
type: component
status: promoted
revision: 1
-->
## What Does NOT Exist Here
- No widget-owned state or nested update loops (no `bubbles`-style stateful components) — state lives in the model, helpers are pure reducers.
- No framework-owned focus index: the framework reports clicks as `FocusRequestedMsg(key)`; the model decides focus and sets `HasFocus` when building the view.
- No component-level subscriptions: `IHasSubscriptions` is consulted only on the root model (WISHLIST item 1).
- No path from resolved layout back to the model — a widget's viewport height is not reported, so consumers pass it in by hand (WISHLIST item 9).
- No async `Update` or `Render`, and no direct console writes outside `ITerminal`.
