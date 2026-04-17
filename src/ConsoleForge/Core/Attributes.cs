namespace ConsoleForge.Core;

/// <summary>
/// Instructs the ConsoleForge source generator to produce the
/// <c>Update(IMsg)</c> dispatch method for this partial record or class.
/// <para>
/// The generator scans for methods matching the pattern
/// <c>(IModel Model, ICmd? Cmd) On{MsgType}(...)</c> and emits a
/// <c>switch</c> that routes each message type to its handler.
/// The type must be declared <c>partial</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DispatchUpdateAttribute : Attribute {}

/// <summary>
/// Instructs the generator to also emit the <c>Init()</c> method
/// (returns null) and the explicit <c>IComponent&lt;TResult&gt;.Result</c>
/// property implementation when the type implements
/// <c>IComponent&lt;TResult&gt;</c>.
/// <para>
/// Requires the type to have a nullable property named <c>Result</c>
/// whose non-null value signals completion.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentAttribute : Attribute {}