# Pattern Index

Lookup table for all pattern files in this directory. Check here before starting any task — if a pattern exists, follow it.

<!-- This file is populated during setup (Pass 2) and updated whenever patterns are added.
     Each row maps a pattern file (or section) to its trigger — when should the agent load it?

     Format — simple (one task per file):
     | [filename.md](filename.md) | One-line description of when to use this pattern |

     Format — anchored (multi-section file, one row per task):
     | [filename.md#task-first-task](filename.md#task-first-task) | When doing the first task |
     | [filename.md#task-second-task](filename.md#task-second-task) | When doing the second task |

     Example (from a Flask API project):
     | [add-api-client.md](add-api-client.md) | Adding a new external service integration |
     | [debug-pipeline.md](debug-pipeline.md) | Diagnosing failures in the request pipeline |
     | [crud-operations.md#task-add-endpoint](crud-operations.md#task-add-endpoint) | Adding a new API route with validation |
     | [crud-operations.md#task-add-model](crud-operations.md#task-add-model) | Adding a new database model |

     Keep this table sorted alphabetically. One row per task (not per file).
     If you create a new pattern, add it here. If you delete one, remove it. -->

| Pattern | Use when |
|---------|----------|
| [add-state-reducer.md](add-state-reducer.md) | Adding or changing editing, selection or scroll rules in `TextInputState` / `TextAreaState` / `ListState` |
| [add-widget.md](add-widget.md) | Adding a built-in widget or changing a widget's `Render` / `Measure` / `Update` |
| [debug-rendering-and-layout.md](debug-rendering-and-layout.md) | Output is stale, misplaced, flickering, leaves artifacts, or clicks hit the wrong widget |
| [keybindings-and-focus.md#task-add-key-bindings-to-a-model](keybindings-and-focus.md#task-add-key-bindings-to-a-model) | Wiring keys/mouse to messages with `KeyMap` / `KeyPattern` |
| [keybindings-and-focus.md#task-implement-tab--click-focus](keybindings-and-focus.md#task-implement-tab--click-focus) | Implementing Tab cycling or click-to-focus with `FocusKey` |
| [perf-sensitive-change.md](perf-sensitive-change.md) | Changing `Renderer`, `RenderContext`, `LayoutEngine`, `LayoutSolver`, `CmdDispatcher`, `SizeConstraint` resolution or any `IWidget.Render`; any perf claim |
| [release-and-docs.md#task-cut-a-release](release-and-docs.md#task-cut-a-release) | Publishing both NuGet packages from a `v*.*.*` tag |
| [release-and-docs.md#task-edit-the-docs-site](release-and-docs.md#task-edit-the-docs-site) | Editing DocFX pages, doc samples, or the site theme |
| [source-generators.md#task-change-a-generator](source-generators.md#task-change-a-generator) | Changing `DispatchUpdateGenerator` / `ComponentGenerator` or adding a CFG diagnostic |
| [source-generators.md#task-use-dispatchupdate-in-a-model](source-generators.md#task-use-dispatchupdate-in-a-model) | Writing a model or component with `[DispatchUpdate]` / `[Component]` |
