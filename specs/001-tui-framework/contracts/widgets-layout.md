# Contract: Widget & Layout Interfaces

**Feature**: specs/001-tui-framework
**Date**: 2026-04-12

---

## `IWidget` — Widget Base Interface

```csharp
namespace ConsoleForge.Layout;

/// <summary>
/// Base interface for all visual elements in the widget tree.
/// </summary>
public interface IWidget
{
    /// <summary>Visual style for this widget. Inherits from Theme if unset.</summary>
    Style Style { get; }

    /// <summary>Width constraint used by the layout engine.</summary>
    SizeConstraint Width { get; }

    /// <summary>Height constraint used by the layout engine.</summary>
    SizeConstraint Height { get; }

    /// <summary>
    /// Render this widget into the provided context.
    /// The context carries the allocated region, theme, and color profile.
    /// Implementations MUST NOT write outside ctx.Region.
    /// </summary>
    void Render(IRenderContext ctx);
}
```

---

## `IFocusable` — Focusable Widget

```csharp
namespace ConsoleForge.Layout;

/// <summary>
/// Extended interface for interactive widgets that can receive keyboard focus.
/// </summary>
public interface IFocusable : IWidget
{
    /// <summary>True when this widget holds keyboard focus.</summary>
    bool HasFocus { get; set; }

    /// <summary>
    /// Called by the runtime when a key is pressed and this widget has focus.
    /// Call dispatch to inject a custom IMsg into the event loop.
    /// </summary>
    void OnKeyEvent(KeyMsg key, Action<IMsg> dispatch);
}
```

---

## `IComponent<TModel>` — Reusable Component

```csharp
namespace ConsoleForge.Core;

/// <summary>
/// Interface for reusable sub-components that maintain their own state
/// but do not own the program loop. Parent models hold TModel as a field
/// and delegate Update calls.
/// </summary>
public interface IComponent<TModel> where TModel : IComponent<TModel>
{
    /// <summary>
    /// Handle a message. Returns the updated component state and
    /// an optional command.
    /// </summary>
    (TModel Model, ICmd? Cmd) Update(IMsg msg);

    /// <summary>Render this component to a string (not a full ViewDescriptor).</summary>
    string View();
}
```

---

## `SizeConstraint` — Dimension Specification

```csharp
namespace ConsoleForge.Layout;

/// <summary>
/// Discriminated union for widget dimension constraints.
/// </summary>
public abstract record SizeConstraint
{
    /// <summary>Exactly n characters.</summary>
    public static SizeConstraint Fixed(int n);

    /// <summary>Proportional share of free space. Weight is a positive integer.</summary>
    public static SizeConstraint Flex(int weight = 1);

    /// <summary>Size to content (longest line / child count).</summary>
    public static SizeConstraint Auto { get; }

    /// <summary>Apply a minimum bound to an inner constraint.</summary>
    public static SizeConstraint Min(int min, SizeConstraint inner);

    /// <summary>Apply a maximum bound to an inner constraint.</summary>
    public static SizeConstraint Max(int max, SizeConstraint inner);
}
```

---

## `Container` — Layout Container Widget

```csharp
namespace ConsoleForge.Widgets;

/// <summary>
/// Composite widget that arranges children along one axis.
/// </summary>
public sealed class Container : IWidget
{
    public Axis Direction { get; init; }
    public IWidget[] Children { get; init; }
    public Style Style { get; init; } = Style.Default;
    public SizeConstraint Width { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);

    /// <summary>
    /// When true, content overflowing the allocated height (Vertical) or
    /// width (Horizontal) is clipped. Arrow keys scroll when widget has focus.
    /// </summary>
    public bool Scrollable { get; init; } = false;

    public void Render(IRenderContext ctx);
}

public enum Axis { Horizontal, Vertical }
```

---

## `LayoutEngine` — Constraint Resolver

```csharp
namespace ConsoleForge.Layout;

/// <summary>
/// Stateless layout engine. Resolves size constraints and assigns
/// absolute terminal coordinates to every widget in the tree.
/// </summary>
public static class LayoutEngine
{
    /// <summary>
    /// Resolve the full widget tree against the given terminal dimensions.
    /// Returns a mapping from each IWidget to its allocated Region.
    /// </summary>
    /// <exception cref="LayoutConstraintException">
    /// Thrown if fixed children collectively exceed parent size and
    /// no flex children exist to absorb the overflow.
    /// </exception>
    public static ResolvedLayout Resolve(
        IWidget root,
        int terminalWidth,
        int terminalHeight);
}
```

---

## `IRenderContext` — Render Context

```csharp
namespace ConsoleForge.Layout;

/// <summary>
/// Passed to IWidget.Render(). Provides the allocated screen region
/// and render-time context (theme, color profile, terminal writer).
/// </summary>
public interface IRenderContext
{
    /// <summary>The allocated region for this widget (absolute terminal coordinates).</summary>
    Region Region { get; }

    /// <summary>Active theme for style inheritance.</summary>
    Theme Theme { get; }

    /// <summary>Detected terminal color capability.</summary>
    ColorProfile ColorProfile { get; }

    /// <summary>
    /// Write a styled string at an absolute terminal position.
    /// The call is a no-op if (col, row) falls outside Region.
    /// </summary>
    void Write(int col, int row, string text, Style style);
}

public readonly record struct Region(int Col, int Row, int Width, int Height);
```

---

## Built-in Leaf Widgets

```csharp
namespace ConsoleForge.Widgets;

/// <summary>Renders a single or multi-line string.</summary>
public sealed class TextBlock : IWidget
{
    public string Text { get; init; }
    public Style Style { get; init; } = Style.Default;
    public SizeConstraint Width { get; init; } = SizeConstraint.Auto;
    public SizeConstraint Height { get; init; } = SizeConstraint.Auto;
    public void Render(IRenderContext ctx);
}

/// <summary>Renders a box with an optional title and a single child widget.</summary>
public sealed class BorderBox : IWidget
{
    public string? Title { get; init; }
    public IWidget? Body { get; init; }
    public Style Style { get; init; } = Style.Default;
    public SizeConstraint Width { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);
    public void Render(IRenderContext ctx);
}

/// <summary>Single-line text input. Implements IFocusable.</summary>
public sealed class TextInput : IFocusable
{
    public string Value { get; private set; } = "";
    public string Placeholder { get; init; } = "";
    public Style Style { get; init; } = Style.Default;
    public SizeConstraint Width { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Fixed(1);
    public bool HasFocus { get; set; }
    public void OnKeyEvent(KeyMsg key, Action<IMsg> dispatch);
    public void Render(IRenderContext ctx);
}

/// <summary>
/// Vertically scrollable list of string items. Implements IFocusable.
/// Dispatches ListItemSelectedMsg when Enter is pressed.
/// </summary>
public sealed class List : IFocusable
{
    public string[] Items { get; init; }
    public int SelectedIndex { get; private set; }
    public Style Style { get; init; } = Style.Default;
    public Style SelectedItemStyle { get; init; } = Style.Default;
    public SizeConstraint Width { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Flex(1);
    public bool HasFocus { get; set; }
    public void OnKeyEvent(KeyMsg key, Action<IMsg> dispatch);
    public void Render(IRenderContext ctx);
}

/// <summary>Dispatched by List when the user presses Enter on an item.</summary>
public sealed record ListItemSelectedMsg(int Index, string Item) : IMsg;
```
