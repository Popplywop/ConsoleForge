---
name: debug-rendering-and-layout
description: Diagnosing wrong, stale, flickering or misplaced output — the model→View→layout→render→diff→terminal boundary.
triggers:
  - "not redrawing"
  - "stale frame"
  - "flicker"
  - "artifacts"
  - "wrong position"
  - "LayoutConstraintException"
  - "click hits wrong widget"
  - "image"
edges:
  - target: context/rendering.md
    condition: for how the solver, renderer, cache and raw payloads are supposed to behave
  - target: context/event-loop.md
    condition: when the question is whether a frame was scheduled at all
  - target: patterns/add-widget.md
    condition: when the fault is in a widget's Render or Measure
  - target: patterns/perf-sensitive-change.md
    condition: when the fix touches a hot path and needs benchmarking
last_updated: 2026-09-22
mex:
  id: mx_01M363AXP8X6EYXEBYJ4KJQNEV
  type: pattern
  status: promoted
  revision: 3
  title: debug-rendering-and-layout
  grounds_to:
    - node: method:f15e3fea0df2ea590eaf89969a744ebf
      fingerprint: mh:64:7b226d696e68617368223a5b33333830393232392c323737393737382c35373136343434392c34333030313533352c31303332343130332c3134353933393831312c363633363132322c31363137323133332c31303033373637352c32363732353039392c31373931303839362c333135343831372c35333237353337332c373238393137332c333534383631352c34393535323839362c35353736363634342c343931373939362c313739363232322c32363736383734342c34383138303736352c3131363135363934372c313830393030342c34353939323637362c33353934353230322c37313036373638312c323034383432372c32353739333139302c373038383238302c313634363838322c33393532303131302c393032323938382c333031303731342c33323639333630322c34333235323031372c33333939343430362c33323034373138352c32353030313332372c33393632313939382c3131353333383633372c32393734303930392c31303732353631392c34363737363635332c3138313936303338392c32353134353335302c343439393438302c32323031313436312c35313139333435372c36383035383334322c31383735373833302c343538343935362c33313534353734392c31383632323237342c34343337373932322c31333939393539332c33353036333337362c3130323336393631312c33333239313739342c3133323539373539362c32303132343137322c31373433353234322c313938343038352c35303736303330382c31313732363932355d2c226e65696768626f7273223a5b226d6574686f643a3135303261343031653037373933663737333034326431316133316335336233222c226d6574686f643a3933356238373561323762656465613137316463343530396533366465376337225d2c22746f6b656e436f756e74223a3231347d
      bodyHash: 98cecbfaeda1434df92e31b8209ae0409d9e3d79e4689fe382273772295169dd
    - node: method:dba722abd68449112b711cfc7dde497d
      fingerprint: mh:64:7b226d696e68617368223a5b3532393631313831382c3431313737313131302c3438393230393136382c3332313232383737342c3136323830363330372c3535323738383930312c3335363136333434302c3530373136303232302c3137393434383537342c35313039363337352c37333335383834312c3231353736353534382c36323639353132302c33383133383336312c3633333639353931392c36313734303938372c3130323034333833392c3131393734363131312c3137303237343331362c3631373832323037372c3432363931363231352c3137333239363830322c3130393530333130392c313238353335333839392c3834303738343438382c313031353535303438312c3738333730393230312c32353739333139302c3239313737303939332c373132353935382c3336353335323338372c313139373138343934382c3134323439373833302c3230343731323033332c39383937383731342c35353033343233352c35313032393036322c32313836313330362c3239333933333439382c3430333439313436302c3137353031323133372c313231393933343036362c3133393938323834392c3333323739303632372c3334393230393838352c3339353631363236372c3234373634373638302c3131363932323034302c3433373733393736312c3535303731363537332c32383034353339322c3630323532353938342c3135303339313533392c3334313136333839392c3135353837393337372c3135333836333130362c3238333938323730332c3130373236323236302c3132323137333137332c35343930343435322c3131353035383232322c3132343933363734392c3138323239343030342c3130303239383236335d2c226e65696768626f7273223a5b5d2c22746f6b656e436f756e74223a31397d
      bodyHash: fe8a55a60a18935daa90cb1898138b0616c120c315aa58cb493b38022acd07e0
  relations:
    - type: related_to
      target: mx_01M363AXN8RXSSRHXPC0WG806F
      note: when the fault is in a widget's Render or Measure
    - type: related_to
      target: mx_01M363AXR4CSBVN1XH7EZGMJZ7
      note: when the fix touches a hot path and needs benchmarking
---

# Debug Rendering and Layout

## Context
Load `context/rendering.md`. The frame path is `App.ProcessMsg` (marks dirty) → [`Renderer.RenderIfDirty()`](mex://method:f15e3fea0df2ea590eaf89969a744ebf) → `LayoutEngine.ResolveInto` → `root.Render(ctx)` → cell diff → `Flush`. [`Renderer.Invalidate()`](mex://method:dba722abd68449112b711cfc7dde497d) forces the next frame to redraw every cell.

## Steps
1. **Reproduce headlessly.** Drive the app with `tests/ConsoleForge.Tests/Testing/VirtualTerminal.cs` (`new VirtualTerminal(w, h)` passed to `App.Run`; `EnqueueKey`, `EnqueueMouse`, `SimulateResize`, `await WaitForFrames(n)`), then assert on `ScreenContent` / `Lines` / `WriteHistory`. Use `TestHelpers.StripAnsi`/`StripApc` for styled output. Never sleep.
2. **Nothing changes on screen** → was a frame scheduled? `Update` returning the *same* model reference (or `(this, null)` from a fallthrough) is "nothing to draw". With `[DispatchUpdate]`, check the handler name matched (CFG002).
3. **Right content, wrong place** → print the `ResolvedLayout` region for the widget; compare with where `Container.Render` placed it. A disagreement means sizing arithmetic outside `LayoutSolver` (see `OverflowAgreementTests`).
4. **`LayoutConstraintException`** → `Fixed` children exceed the container with no `Flex` sibling and nothing measured. Add a flex child or shrink the fixed sizes.
5. **`Auto` widget fills space** → it does not implement `IMeasurable`, so `Auto` fell back to flex weight 1.
6. **Stray characters after navigation** → check wide-character width (`TextUtils.VisualWidth`) and that the widget pads its whole row; then check whether the diff skipped a cell it should have cleared (`IncrementalRedrawTests`).
7. **Stale widget content** → the render cache reused cells; the widget's output depends on something not in its properties. `WidgetCacheTests` shows how to count cache hits.
8. **Click focuses the wrong thing** → hit-testing uses the last rendered layout; overlapping regions resolve to the last in depth-first order.
9. **Images flicker / duplicate / linger** → `ContentHash` must be stable for unchanged content and change when content changes; inside tmux placements are renewed per frame; teardown relies on `BuildRawCleanup` (`ImageMotionTests`, `ImageTeardownTests`).

## Gotchas
- Snapshot tests cannot see cache or diff regressions that produce identical pixels — count the work instead.
- `WindowResizeMsg` renders immediately and invalidates; a bug that disappears on resize is usually a diff/cache bug, not layout.
- Hardware cursor: `SetCursorVisible` writes directly to the terminal (not the frame buffer), so cursor bugs don't show in `Lines`; check `WriteHistory`.

## Verify
- A regression test reproducing the bug in `tests/ConsoleForge.Tests/Rendering/` or `Layout/`, failing before the fix.
- If the fix touched a hot path, run Tier 3 before/after (`patterns/perf-sensitive-change.md`).

## Update Scaffold
- [ ] Update `.mex/ROUTER.md` "Current Project State" if what's working/not built has changed
- [ ] Update any `.mex/context/` files that are now out of date
- [ ] If this is a new task type without a pattern, create one in `.mex/patterns/` and add to `INDEX.md`
