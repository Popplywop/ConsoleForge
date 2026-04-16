using System.Diagnostics.CodeAnalysis;

namespace ConsoleForge.Core;

/// <summary>
/// A self-contained sub-program: owns its own state, update logic, and view.
/// Implements <see cref="IModel"/> so it can be run standalone via
/// <see cref="Program.Run"/> or embedded inside a parent model.
/// <para>
/// Use <see cref="IComponent{TResult}"/> when the component needs to signal
/// completion and return a typed result to its parent.
/// </para>
/// </summary>
/// <remarks>
/// <para><b>Pattern</b> — define one <c>sealed record</c> per logical screen or
/// reusable interactive widget, implement <see cref="IComponent"/>, and embed it
/// as a typed field in the parent model:</para>
/// <code>
/// public sealed record MyPage : IComponent
/// {
///     static readonly KeyMap Keys = new KeyMap()
///         .On(ConsoleKey.Escape, () => new QuitMsg());
///
///     public int Counter { get; init; }
///
///     public ICmd? Init() => null;
///
///     public (IModel Model, ICmd? Cmd) Update(IMsg msg)
///     {
///         if (Keys.Handle(msg) is { } action) msg = action;
///         return msg switch
///         {
///             NavUpMsg => (this with { Counter = Counter + 1 }, null),
///             _        => (this, null),
///         };
///     }
///
///     public IWidget View() => new TextBlock($"Count: {Counter}");
/// }
///
/// // In the parent model:
/// record AppModel(MyPage Page, ...) : IModel
/// {
///     public (IModel Model, ICmd? Cmd) Update(IMsg msg)
///     {
///         var (next, cmd) = Component.Delegate(Page, msg);
///         return (this with { Page = next }, cmd);
///     }
/// }
/// </code>
/// </remarks>
public interface IComponent : IModel
{
    // Inherits Init(), Update(IMsg), and View() from IModel.
    // IComponent itself adds no new members — the distinction is semantic:
    // an IComponent is a self-contained sub-program, not a root application.
}

/// <summary>
/// A self-contained sub-program that can signal completion by setting
/// <see cref="Result"/> to a non-null value.
/// </summary>
/// <typeparam name="TResult">
/// The type of value produced when the component completes
/// (e.g. <c>string</c> for a file picker, <c>bool</c> for a confirm dialog).
/// </typeparam>
/// <remarks>
/// <para>When <see cref="Result"/> is non-null the component is considered
/// <em>completed</em>. The parent model should inspect the result in its own
/// <c>Update</c> loop and transition state accordingly:</para>
/// <code>
/// // FilePicker returns the chosen path on completion
/// public sealed record FilePicker : IComponent&lt;string&gt;
/// {
///     public string? Result { get; init; }
///     // ... Init, Update, View
/// }
///
/// // Parent model embeds the picker and handles its result
/// record AppModel(FilePicker? Picker, string? ChosenFile) : IModel
/// {
///     public (IModel Model, ICmd? Cmd) Update(IMsg msg)
///     {
///         if (Picker is not null)
///         {
///             var (next, cmd) = Component.Delegate(Picker, msg);
///             if (next.IsCompleted())
///                 return (this with { Picker = null, ChosenFile = next.Result }, cmd);
///             return (this with { Picker = next }, cmd);
///         }
///         // ... root-level handling
///     }
/// }
/// </code>
/// </remarks>
public interface IComponent<out TResult> : IComponent
{
    /// <summary>
    /// The result value produced when this component completes.
    /// <see langword="null"/> while the component is still running.
    /// Once non-null the component is considered done; the parent should
    /// read the result and decide what happens next.
    /// </summary>
    [MaybeNull]
    TResult Result { get; }
}
