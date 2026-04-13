using ConsoleForge.Layout;

namespace ConsoleForge.Core;

/// <summary>
/// The root interface for all ConsoleForge application models.
/// Implement this to define your application's state and behavior.
/// </summary>
public interface IModel
{
    /// <summary>
    /// Called once at program start. Return a command to execute on startup,
    /// or null for no initial side-effect.
    /// </summary>
    ICmd? Init();

    /// <summary>
    /// Pure update function. Given the current message, return the new model
    /// state and an optional follow-up command.
    /// Convention: return a new record copy (C# 'with' expression).
    /// MUST NOT return a null IModel.
    /// </summary>
    (IModel Model, ICmd? Cmd) Update(IMsg msg);

    /// <summary>
    /// Produce the root widget for the current model state.
    /// The framework renders this widget using a persistent double-buffered context.
    /// Called after every Update. MUST be a pure, side-effect-free function.
    /// </summary>
    IWidget View();
}
