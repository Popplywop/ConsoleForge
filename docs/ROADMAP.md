# ConsoleForge — Next Version Roadmap

Generated from architecture review session. Ordered by impact.

---

## Priority List (biggest bang first)

1. **Tests** — foundation for everything else; framework has no safety net now
2. **TextArea** + **Checkbox** + **Tabs** — complete the "basic form/navigation" story
3. **Modal/overlay + z-layer** — unlocks a whole class of interactive patterns
4. **Unicode wide-char** — correctness bug, not a feature
5. **Mouse support** — large usability jump
6. **Virtualized list/table** — required for real data apps
7. **Built-in themes + margin/padding enforcement** — polish

---

## 🧱 New Widgets

| Widget | Why |
|---|---|
| `TextArea` | `TextInput` is 1-line. Multiline input is missing entirely. |
| `Checkbox` / `Toggle` | Fundamental form primitive. Every real app needs it. |
| `RadioGroup` | Mutually exclusive selection. Companion to Checkbox. |
| `Select` / `Dropdown` | Single-pick from list; overlays current position on select. |
| `Tabs` / `TabBar` | Core TUI navigation pattern. View switching. |
| `Modal` / `Dialog` | Overlay/popup. Needs z-layer support (see layout). |
| `StatusBar` | Bottom bar showing mode/keyhints. Used in every serious TUI. |
| `Tree` | Hierarchical collapsible list. File browsers, JSON viewers, etc. |
| `Sparkline` / `BarChart` | Mini data viz. SysMonitor sample cries out for this. |
| `ScrollBar` | `Container` scrolls but has zero visual indicator. |
| `Toast` / `Notification` | Ephemeral overlay message. No current pattern for it. |

---

## 🏗️ Core / Architecture

**Mouse support** — zero support now. Click, scroll wheel, hover all absent. ANSI CSI 1006 (SGR mouse) exists in every modern terminal.

**Unicode wide-char handling** — CJK chars, emoji are 2 columns wide. Current `Write` logic treats all chars as width=1. Will corrupt layouts with non-ASCII content.

**Key binding system** — models write giant `switch` on `ConsoleKey`. A declarative `KeyMap` would cut boilerplate and enable rebindable keys.

**Z-layering / absolute positioning** — no way to render modals/dropdowns over existing content. `LayoutEngine` only knows one flat plane.

**`IComponent`** — interface exists in `Core/IComponent.cs` but appears unused. Full composable sub-programs (own init/update/view, mounted inside a parent model) would unlock complex apps.

**Proper `IHasSubscriptions` pattern** — reconciliation exists but the model must implement a separate interface. Could be unified into `IModel` as optional method.

---

## 🧪 Tests

Currently only `CmdTests.cs` + stub `UnitTest1.cs`. Coverage is near zero.

- `LayoutEngineTests` — fixed overflow, flex distribution, remainder math
- `ContainerRenderTests` — scroll offset clipping, nested containers
- `FocusManagerTests` — tab order, wrap, reverse, BorderBox traversal
- `TextInputTests` — cursor, backspace, delete, overflow
- `ListTests` — selection clamping, scroll, empty list
- `TableTests` — flex column math, separator, selected row style
- `RendererTests` — dirty flag, double-buffer diff, flush
- `StyleTests` — inherit, bitmask, ANSI cache correctness

---

## 🎨 Styling / Theming

**Built-in themes** — only `Theme.Default` (no color). Ship `Theme.Dark`, `Theme.Light`, `Theme.Monokai` out of the box.

**Margin/padding NOT enforced** — `Style` stores margin/padding values but `LayoutEngine` and `Container.Render` never consume them. They're dead weight until wired up.

**Gradient colors** — foreground gradients across a row. Cosmetic but high visual impact.

**Theme from file** — load `theme.json`/`toml`. Nice for end-user customization.

---

## ⚡ Performance

**Virtualized `List` / `Table`** — current render iterates ALL items. 10k-row table = 10k iterations every frame. Only visible rows should render.

**Per-region dirty tracking** — renderer marks the entire frame dirty on any model change. Partial invalidation (only changed regions repaint) would halve work in static-heavy UIs.

**`Container.Render` re-does layout math** — duplicates `LayoutEngine` logic on every frame. Caching resolved sizes (invalidate on resize) would save repeated arithmetic.

---

## 📦 Ecosystem / DX

**NuGet package + CI publish** — no `*.csproj` packaging metadata visible. Can't be consumed as a library yet.

**README + quickstart** — no documentation visible at root. First user experience is currently "read source."

**More samples** — `Gallery`, `SysMonitor`, `TodoApp` exist. Missing: file browser, form demo, dashboard with charts, modal dialog demo.

**Source generator** — `Update` methods are a wall of `if (msg is X x)`. A generator emitting typed dispatch tables would cut boilerplate.
