namespace ConsoleForge.Core;

/// <summary>
/// A command: a blocking function that produces one message when complete.
/// The framework executes each non-null command on a background thread.
/// Return null (Cmd.None) to indicate no operation.
/// </summary>
public delegate IMsg ICmd();
