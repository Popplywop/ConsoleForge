namespace ConsoleForge.Core;

/// <summary>
/// A command: an async function that produces one message when complete.
/// The framework awaits each non-null command. Use async lambdas or
/// <see cref="Task.FromResult{TResult}"/> for synchronous results.
/// Return null (Cmd.None) to indicate no operation.
/// </summary>
public delegate Task<IMsg> ICmd();
