---
_layout: landing
---

# ConsoleForge

An Elm-architecture TUI framework for .NET 8. Immutable model → pure update → declarative view.

```bash
dotnet add package ConsoleForge
```

<!-- doccheck: program -->
```csharp
using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

await App.Run(new HelloModel(), theme: Theme.Dark);

sealed record HelloModel(int Count = 0) : IModel
{
    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg) => msg switch
    {
        KeyMsg { Key: ConsoleKey.UpArrow } => (this with { Count = Count + 1 }, null),
        KeyMsg { Key: ConsoleKey.Q }       => (this, Cmd.Quit()),
        _ => (this, null),
    };

    public IWidget View() => new TextBlock($"Count: {Count}");
}
```

## Where to go

- **[Introduction](docs/introduction.md)** — the Elm loop, and why widgets hold no state of their own.
- **[Getting Started](docs/getting-started.md)** — build and run your first app.
- **[Widgets](docs/widgets.md)** — the sixteen built-ins and what each is for.
- **[Source Generators](src/ConsoleForge.SourceGen/README.md)** — drop the `Update` switch with `[DispatchUpdate]`.
- **[Changelog](CHANGELOG.md)** — what shipped in each release.
- **[API Reference](api/ConsoleForge.Core.App.yml)** — every public type.
