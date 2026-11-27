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

    // ── Subscriptions ─────────────────────────────────────────────────────────

    /// <summary>
    /// The subscriptions of a nested component, keyed under <paramref name="prefix"/> —
    /// <c>"prefix/key"</c> — for the parent to include in its own
    /// <see cref="IHasSubscriptions.Subscriptions"/>. Empty when <paramref name="child"/> is
    /// null or declares none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework asks only the root model for subscriptions, and the root composes its
    /// children's, the way an Elm parent writes
    /// <c>Sub.batch [ Sub.map PageMsg (Page.subscriptions page) ]</c>. That keeps them
    /// declarative: a child's interval runs exactly while the child is part of the model
    /// and its state asks for it, however its messages are routed meanwhile — unlike a
    /// self-re-arming <see cref="Cmd.Tick"/> chain, which stops for good the first time a
    /// tick is delivered somewhere else.
    /// </para>
    /// <code>
    /// public IReadOnlyList&lt;(string Key, ISub Sub)&gt; Subscriptions() => Sub.Batch(
    ///     [("clock", Sub.Interval(TimeSpan.FromSeconds(1), t => new ClockMsg(t)))],
    ///     Component.Subscriptions("prlist", PrList),   // "prlist/spinner" while it loads
    ///     Component.Subscriptions("modal", Modal));    // nothing while Modal is null
    /// </code>
    /// <para>
    /// The prefix keeps two children's keys apart, and stays stable for as long as a child
    /// does: a subscription is restarted only when its full key appears or disappears.
    /// Pass <paramref name="map"/> to tag the child's messages — when two instances of one
    /// component type would otherwise both act on the same message.
    /// </para>
    /// </remarks>
    /// <param name="prefix">Key prefix for this child, unique among its siblings.</param>
    /// <param name="child">The child model; null contributes nothing.</param>
    /// <param name="map">Optional transform applied to each message the child's subscriptions produce.</param>
    public static IReadOnlyList<(string Key, ISub Sub)> Subscriptions(
        string prefix, IModel? child, Func<IMsg, IMsg>? map = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        if (child is not IHasSubscriptions hasSubs) return [];

        var subs = hasSubs.Subscriptions();
        var result = new (string Key, ISub Sub)[subs.Count];
        for (int i = 0; i < subs.Count; i++)
        {
            var (key, sub) = subs[i];
            result[i] = ($"{prefix}/{key}", map is null ? sub : Sub.Map(sub, map));
        }
        return result;
    }
}
