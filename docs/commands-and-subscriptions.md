# Commands and subscriptions

`Update` is pure, so it cannot await anything or touch the outside world. Instead it
*describes* the work and hands it back. Two kinds exist:

- A **command** (<xref:ConsoleForge.Core.ICmd>) runs once and produces one message.
- A **subscription** (<xref:ConsoleForge.Core.ISub>) stays alive and produces many.

Both run off the loop and feed their results back in as ordinary messages, so there is only
ever one place where state changes.

## Commands

Return one from `Update` alongside the next model:

```csharp
KeyMsg { Key: ConsoleKey.R } => (
    this with { Loading = true },
    Cmd.Run(async ct => new DataLoadedMsg(await Fetch(ct)))),
```

`Cmd.Run` hands you a `CancellationToken` — the app cancels in-flight work on shutdown, so
pass it down to anything that accepts one.

| Command | Does |
|---|---|
| `Cmd.Run(fn)` | Runs `fn` on the thread pool; its returned message comes back to `Update`. |
| `Cmd.Msg(msg)` | Delivers a message on the next turn of the loop. |
| `Cmd.Quit()` | Ends the app. The terminal is restored on the way out. |
| `Cmd.Batch(a, b, …)` | Runs several at once, in no particular order. |
| `Cmd.Sequence(a, b, …)` | Runs several in order, each after the last finishes. |
| `Cmd.Tick(interval, fn)` | Fires `fn` once, after `interval`. |
| `Cmd.Debounce(key, interval, fn)` | Fires only after `interval` of quiet, per `key`. |
| `Cmd.Throttle(key, interval, fn)` | Fires at most once per `interval`, per `key`. |

`Cmd.None` is `null` — returning no command is just returning `null`.

`Batch` and `Sequence` return `ICmd?`, and skip nulls, so you can build a list conditionally
without filtering it first.

### Repeating with Tick

`Cmd.Tick` fires **once**. To keep something going, return a fresh one each time the tick
arrives — which is how a spinner animates:

```csharp
private (IModel, ICmd?) OnTick() => (
    this with { Frame = Frame + 1 },
    Cmd.Tick(TimeSpan.FromMilliseconds(80), _ => new TickMsg()));
```

### Debounce and throttle

Both key off a string, so a command built fresh inside `Update` still coalesces with the one
before it. That is what makes them usable from a pure function — you are not holding a timer,
the dispatcher is:

```csharp
TextChangedMsg t => (
    this with { Query = t.Value },
    Cmd.Debounce("search", TimeSpan.FromMilliseconds(250), _ => new SearchMsg(t.Value))),
```

Use the same key for the same logical action. Different keys never coalesce with each other.

## Subscriptions

A command ends. A subscription keeps producing until you stop asking for it. Implement
<xref:ConsoleForge.Core.IHasSubscriptions> on your model and return the ones that should be
running *right now*:

```csharp
public IReadOnlyList<(string Key, ISub Sub)> Subscriptions() =>
    Streaming
        ? [("clock", Sub.Interval(TimeSpan.FromSeconds(1), _ => new TickMsg()))]
        : [];
```

The framework diffs that list against the previous frame by key and starts or stops the
difference. Returning a subscription every frame does not restart it; dropping it from the
list stops it. **The key must be stable** — a key built from a changing value restarts the
subscription on every frame.

| Subscription | Source |
|---|---|
| `Sub.Interval(interval, fn)` | A repeating timer. |
| `Sub.FromAsyncEnumerable(factory)` | An `IAsyncEnumerable<IMsg>`; the token cancels on stop. |
| `Sub.FromObservable(observable)` | Any `IObservable<IMsg>`. |

Subscriptions are built on System.Reactive, the framework's only runtime dependency.

## Which to reach for

Fetch a page of results, save a file, run a subprocess: a command. A clock, a file watcher,
a socket, anything that pushes: a subscription. If you find yourself returning a `Cmd.Tick`
that reschedules itself forever, that is a subscription wearing a disguise.
