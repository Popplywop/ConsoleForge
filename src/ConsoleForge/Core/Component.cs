using System.Diagnostics.CodeAnalysis;

namespace ConsoleForge.Core;

/// <summary>
/// Static helpers for working with <see cref="IComponent"/> and
/// <see cref="IComponent{TResult}"/> in parent model <c>Update</c> methods.
/// </summary>
/// <remarks>
/// These methods exist to eliminate the cast boilerplate that would otherwise
/// be required when delegating <c>Update</c> calls to a typed component field:
/// <code>
/// // Without helpers:
/// var (next, cmd) = myPage.Update(msg);
/// var typed = (MyPage)next;           // cast required — Update returns IModel
///
/// // With helpers:
/// var (next, cmd) = Component.Delegate(myPage, msg);  // next is MyPage
/// </code>
/// </remarks>
public static class Component
{
    // ── Delegation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Delegate an <see cref="IMsg"/> to <paramref name="component"/> and return
    /// the updated component (typed) alongside the optional follow-up command.
    /// Returns <c>(null, null)</c> when <paramref name="component"/> is null.
    /// </summary>
    /// <typeparam name="T">Concrete component type (must implement <see cref="IComponent"/>).</typeparam>
    /// <param name="component">The component to update, or null.</param>
    /// <param name="msg">The message to process.</param>
    /// <returns>
    /// The updated component cast back to <typeparamref name="T"/> and the
    /// optional <see cref="ICmd"/> to dispatch. Both are null when the component
    /// is null.
    /// </returns>
    /// <exception cref="InvalidCastException">
    /// Thrown if the component's <c>Update</c> implementation returns a different
    /// concrete type than <typeparamref name="T"/>. Components must return
    /// <c>this with { … }</c> (a new instance of the same type).
    /// </exception>
    public static (T? Next, ICmd? Cmd) Delegate<T>(T? component, IMsg msg)
        where T : class, IComponent
    {
        if (component is null) return (null, null);
        var (next, cmd) = ((IModel)component).Update(msg);
        return ((T)next, cmd);
    }

    // ── Completion helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="component"/> has
    /// completed — i.e. its <see cref="IComponent{TResult}.Result"/> is non-null.
    /// Safe to call on a null reference; returns <see langword="false"/>.
    /// </summary>
    /// <typeparam name="TResult">The component's result type.</typeparam>
    /// <param name="component">The component to check, or null.</param>
    public static bool IsCompleted<TResult>([NotNullWhen(false)] this IComponent<TResult>? component) =>
        component is not null && component.Result is not null;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialise a component and dispatch its startup command if any.
    /// Equivalent to calling <see cref="IModel.Init"/> and passing the result
    /// to <see cref="Cmd.Batch"/>.
    /// </summary>
    /// <typeparam name="T">Concrete component type.</typeparam>
    /// <param name="component">The component to initialise.</param>
    /// <param name="extraCmds">
    /// Additional commands to batch alongside the component's own init command.
    /// </param>
    /// <returns>
    /// The same component reference (init is side-effect-free) and the
    /// combined startup command, or <see langword="null"/> if none.
    /// </returns>
    public static (T Component, ICmd? Cmd) Init<T>(T component, params ICmd?[] extraCmds)
        where T : class, IComponent
    {
        var initCmd = component.Init();
        return (component, Cmd.Batch([initCmd, .. extraCmds]));
    }
}
