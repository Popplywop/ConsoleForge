# ConsoleForge.SourceGen {#sourcegen}

Roslyn source generator for [ConsoleForge](https://github.com/Popplywop/ConsoleForge). Eliminates `Update(IMsg)` dispatch boilerplate from your models and components.

## Installation

```bash
dotnet add package ConsoleForge
dotnet add package ConsoleForge.SourceGen
```

> **Note:** This is a development dependency — it runs at compile time and produces no runtime overhead.

## `[DispatchUpdate]` — Auto-generated Update dispatch

Instead of writing the `Update` switch by hand:

```csharp
public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
{
    NavUpMsg   => OnNavUp(),
    NavDownMsg => OnNavDown(),
    SelectMsg  => OnSelect(),
    _          => (this, null),
};
```

Mark the type `partial` and add `[DispatchUpdate]`:

```csharp
[DispatchUpdate]
sealed partial record ListPage(int Index = 0) : IComponent
{
    static readonly KeyMap Keys = new KeyMap()
        .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
        .On(ConsoleKey.DownArrow, () => new NavDownMsg())
        .On(ConsoleKey.Enter,     () => new SelectMsg());

    public ICmd? Init() => null;
    public IWidget View() => new ConsoleForge.Widgets.List(Items, Index);

    // Generator finds On{X} methods → emits Update() with a switch
    (IModel, ICmd?) OnNavUp()   => (this with { Index = Index - 1 }, null);
    (IModel, ICmd?) OnNavDown() => (this with { Index = Index + 1 }, null);
    (IModel, ICmd?) OnSelect()  => (this with { Result = Items[Index] }, null);
}
```

The generator:
1. Finds all methods matching `(IModel, ICmd?) On{X}(...)`
2. Looks for a corresponding `{X}Msg` type in scope
3. Emits the `Update(IMsg)` switch with one arm per handler
4. Detects a `static KeyMap Keys` field → emits `Keys.Handle(msg)` pre-dispatch
5. Default arm returns `(this, null)`

### Handler conventions

| Pattern | Generated switch arm |
|---|---|
| `(IModel, ICmd?) OnFoo()` | `FooMsg => OnFoo(),` |
| `(IModel, ICmd?) OnFoo(FooMsg msg)` | `FooMsg __m => OnFoo((FooMsg)__m),` |

## `[Component]` — Scaffold boilerplate

When combined with `[DispatchUpdate]` (or used alone), `[Component]` generates:

- `IComponent<T>.Result` explicit interface implementation (reads from your `Result` property)
- `public ICmd? Init() => null;` (when not already declared)

```csharp
[DispatchUpdate, Component]
sealed partial record FilePicker(string? Result = null) : IComponent<string>
{
    public IWidget View() => ...;

    (IModel, ICmd?) OnPick(PickMsg msg) => (this with { Result = msg.Path }, null);
    (IModel, ICmd?) OnCancel()          => (this, null);
}

// Generated:
// - string IComponent<string>.Result => Result!;
// - public ICmd? Init() => null;
// - public (IModel, ICmd?) Update(IMsg msg) => msg switch { ... };
```

## Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| CFG001 | Error | Type must be declared `partial` to use `[DispatchUpdate]` |
| CFG002 | Warning | Handler skipped — message type not found or duplicate handler |
| CFG003 | Warning | Handler return type must be `(IModel, ICmd?)` |
| CFG004 | Info | Type already declares `Update(IMsg)` — generator skips dispatch |

## Requirements

- **ConsoleForge** ≥ 0.2.0 (provides the trigger attributes)
- **.NET SDK** ≥ 8.0
- No runtime dependency — the generator runs at compile time only

## License

MIT — see [LICENSE](https://github.com/Popplywop/ConsoleForge/blob/main/LICENSE).
