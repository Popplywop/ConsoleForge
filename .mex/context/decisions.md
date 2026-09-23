---
name: decisions
description: Key architectural and technical decisions with reasoning. Load when making design choices or understanding why something is built a certain way.
triggers:
  - "why do we"
  - "why is it"
  - "decision"
  - "alternative"
  - "we chose"
edges:
  - target: context/architecture.md
    condition: when a decision relates to system structure
  - target: context/stack.md
    condition: when a decision relates to technology choice
  - target: context/event-loop.md
    condition: when a decision concerns focus, batching or rate limiting
  - target: context/rendering.md
    condition: when a decision concerns the layout solver or renderer
grounds_to:
  - node: method:82f16e943049535fbab20d6eea3405c9
    fingerprint: mh:64:7b226d696e68617368223a5b31323931353336352c363130343038352c3333393134322c34373336323530322c33333437303234392c33313235313232372c32363737313336312c31303134363434322c363934353434382c31353633393333382c31373931303839362c363734383038362c32343536303035312c393836373136312c31333738383932382c353139393338392c333935373632332c383339303930342c313739363232322c33333437343939322c32363139353335322c333135363131332c313830393030342c313239393232302c31323435383538362c31313834333936302c323034383432372c32353739333139302c313235323230302c313634363838322c323633383732302c393032323938382c333031303731342c353236313731332c333638373230312c333032303236312c31393939393132392c373431373337312c35383934373537372c31343034323036322c31313730373835332c33313338373030352c34353535383034362c33343934313330362c333831343234382c393232333838372c32323031313436312c333235323338352c373832383038302c31383735373833302c32383034353339322c32333736373130352c31383632323237342c373035373936302c31333939393539332c33343137383631392c32383435343538372c33373033383830302c3532353034382c31383136323435362c353336393534312c373735323734322c31303432323038392c333236333234385d2c226e65696768626f7273223a5b226d6574686f643a3431643738393963633532326637666666656661383261303938633264336565222c226d6574686f643a3639313535656165383961346530663437653566336330643465323830626535222c226d6574686f643a3939613539663239343466626133346666363233623064626338343636366531225d2c22746f6b656e436f756e74223a3630307d
    bodyHash: 6d52f2fb630d0fa7e313c52b0221c392587ae35aebfac5c15423bc634f66d799
  - node: method:2b219049aa77cebe9da3add5d564f259
    fingerprint: mh:64:7b226d696e68617368223a5b33333830393232392c323737393737382c36323333353137382c3138383430363032342c3231363939353335302c32383836323132392c363633363132322c343137323934392c36313539343433332c3139313237313336302c3339323234372c37343532333235372c35373339363331382c35323931303230312c343831303535372c32353536373834312c3133363634303537302c33303130323934322c39303236373838332c34353039333438372c31393738373739352c333135363131332c34373837343834362c3133343232353733372c33353934353230322c3132323734373736382c323034383432372c32353739333139302c3233313830353633302c34333636393037392c323633383732302c3130353337353137322c343431393134332c35313135383432372c32353535303338362c35353033343233352c38333437393334342c36303238383134312c36343134393030332c36343531303332342c36353530383733302c3239333833343731372c34363737363635332c3134353339343431382c32353134353335302c33373830343637332c31383835393236372c35313139333435372c373832383038302c363536313337362c36303232323432362c32333736373130352c33373738363738342c3239323833343835352c3133353031353738332c3134353032313836352c36333036333237352c31303636333331302c3232333035303135312c393237383235332c3131383733353536302c37373637393735362c31353737353839332c31313732363932355d2c226e65696768626f7273223a5b226d6574686f643a3265303833356430313738363530626366613431383962653736306331623232225d2c22746f6b656e436f756e74223a38347d
    bodyHash: 92e68691593094a8b0ad189173dbcbe9a255d9f09fc8d39deb68d9d46135113b
last_updated: 2026-09-22
---

# Decisions

Dates are commit dates from `git log` on `main`.

## Decision Log

<!-- mex:entity
id: mx_01M363AXEQ19WZXKW4ASZS6KBW
type: decision
status: promoted
revision: 1
-->
### Replace Doxygen with DocFX for the doc site
**Date:** 2026-09-18
**Status:** Active
**Decision:** The docs site is built by DocFX from handwritten `docs/*.md`, `index.md`, `toc.yml` and a custom `templates/consoleforge/` theme, deployed as a Pages artifact by `docs.yml`.
**Reasoning:** The Doxygen pipeline needed repeated fixes (April 2026 commits) and produced generated output committed under `docs/`.
**Alternatives considered:** Keeping Doxygen (rejected by the swap commit; no further rationale recorded).
**Consequences:** `docs/` is now source. `api/` and `_site/` are generated and gitignored. The `gh-pages` branch is vestigial.

<!-- mex:entity
id: mx_01M363AXE0VC8S8YCVRNNYPYKR
type: decision
status: promoted
revision: 1
-->
### Model owns focus; framework only reports clicks
**Date:** 2026-09-14
**Status:** Active
**Decision:** `IFocusable.HasFocus` is `init`-only and set by the model's `View()`; widgets carry an optional app-assigned `FocusKey`, and a left click is reported as `FocusRequestedMsg(key)` by [`App.HandleMouseFocus()`](mex://method:2b219049aa77cebe9da3add5d564f259).
**Reasoning:** A mutable, framework-set `HasFocus` broke widget immutability. The framework hit-tests because a model cannot; the model decides what focus means.
**Alternatives considered:** Framework-tracked focus index pushed to the model (the earlier design, still described in the workspace `CLAUDE.md` as `FocusIndexChangedMsg` — stale).
**Consequences:** **Breaking** in 0.4.0. Tab cycling is app code using `FocusManager.CollectFocusKeys` / `GetNext`. Widgets without a `FocusKey` are not click targets.

<!-- mex:entity
id: mx_01M363AXD78HEF2B6FP7Q817DB
type: decision
status: promoted
revision: 1
-->
### Editing rules live in pure reducers, not widgets
**Date:** 2026-08-26 (`TextInputState`); 2026-09-15 (`ListState`, `TextAreaState`)
**Status:** Active
**Decision:** Text editing, selection and scroll are pure value types in `ConsoleForge.Core`; `TextInput`, `TextArea`, `List` delegate to them.
**Reasoning:** One copy of the rules, usable from a model that never builds the widget, so widget and model cannot drift. Follows the design target "Elm-correct in C#, with helpers".
**Alternatives considered:** Bubble Tea `bubbles`-style stateful components with their own update loop — rejected; they fight every immutability rule.
**Consequences:** New interactive behaviour starts as a reducer with reducer-level tests. `*ChangedMsg` types that widgets used to raise are `[Obsolete]`.

<!-- mex:entity
id: mx_01M363AXC7WM4GY61REG1SRN3K
type: decision
status: promoted
revision: 1
-->
### One layout solver
**Date:** 2026-08-26
**Status:** Active
**Decision:** All constraint arithmetic is in [`LayoutSolver.ResolveSizes()`](mex://method:82f16e943049535fbab20d6eea3405c9), called by both `LayoutEngine` and `Container.Render`.
**Reasoning:** Two copies disagreed, so widgets rendered where focus and hit-testing thought they were not, and overflow handling differed (see "Make layout and render agree on overflow").
**Alternatives considered:** Keeping separate engine/render arithmetic (the prior state).
**Consequences:** Any sizing change goes in `LayoutSolver`; `OverflowAgreementTests` guards the agreement.

<!-- mex:entity
id: mx_01M363AXBAT737SAA9M7K3YAY6
type: decision
status: promoted
revision: 1
-->
### One frame per batch of input
**Date:** 2026-08-26
**Status:** Active
**Decision:** The event loop drains every queued message before drawing a single frame.
**Reasoning:** Key auto-repeat outruns frame drawing; a frame per message threw away work and made held arrow keys lag.
**Alternatives considered:** Draw per message (the prior behaviour).
**Consequences:** Intermediate states are never drawn. Tests wait on frames (`InputBurstTests`), not the clock.

<!-- mex:entity
id: mx_01M363AXAG28T5X68MXGSK71PZ
type: decision
status: promoted
revision: 1
-->
### Tiered performance policy instead of a blanket benchmark mandate
**Date:** 2026-08-26
**Status:** Active
**Decision:** Countable perf properties get Tier 1 assertions in CI; Tier 2 quick benchmarks for iteration; Tier 3 full benchmarks only for hot-path changes.
**Reasoning:** The widget render cache served one widget per frame for two releases with identical pixels — only a counting test could see it; a stopwatch answer took ten minutes and still needed hedging.
**Consequences:** Perf fixes ship with a Tier 1 test when the defect is countable. See `patterns/perf-sensitive-change.md`.

<!-- mex:entity
id: mx_01M363AX8QXBCK2ESGT5KHQZHK
type: decision
status: promoted
revision: 1
-->
### Match key bindings on the character, not the key
**Date:** 2026-09-15
**Status:** Active
**Decision:** `KeyPattern.OfChar(char)` / `KeyMap.On(char, ...)` match the produced glyph; `KeyPattern.Key` became `ConsoleKey?` (null = wildcard).
**Reasoning:** `WithShift(ConsoleKey.Oem2)` meant `?` only on US layouts.
**Consequences:** **Breaking** for code reading `.Key`. `default(KeyPattern)` matches every key event — a catch-all that swallows input if reached by accident.
