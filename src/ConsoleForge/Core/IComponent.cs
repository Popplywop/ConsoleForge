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
