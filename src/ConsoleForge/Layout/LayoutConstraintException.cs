namespace ConsoleForge.Layout;

/// <summary>
/// Thrown by <see cref="LayoutEngine"/> when fixed-size children collectively exceed
/// the available space in a container that has no flex children to absorb the overflow.
/// </summary>
public sealed class LayoutConstraintException : Exception
{
    /// <summary>Initialises the exception with a descriptive message.</summary>
    public LayoutConstraintException(string message) : base(message) { }
    /// <summary>Initialises the exception with a message and an inner exception that is the cause.</summary>
    public LayoutConstraintException(string message, Exception inner) : base(message, inner) { }
}
