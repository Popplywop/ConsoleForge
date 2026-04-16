namespace ConsoleForge.Layout;

/// <summary>
/// Discriminated union for widget dimension constraints.
/// </summary>
public abstract record SizeConstraint
{
    private SizeConstraint() { }

    /// <summary>Exactly n characters.</summary>
    public static SizeConstraint Fixed(int n) => new FixedConstraint(n);

    /// <summary>Proportional share of free space. Weight is a positive integer.</summary>
    public static SizeConstraint Flex(int weight = 1) => new FlexConstraint(weight);

    /// <summary>Size to content (longest line / child count).</summary>
    public static SizeConstraint Auto { get; } = new AutoConstraint();

    /// <summary>Apply a minimum bound to an inner constraint.</summary>
    public static SizeConstraint Min(int min, SizeConstraint inner) => new MinConstraint(min, inner);

    /// <summary>Apply a maximum bound to an inner constraint.</summary>
    public static SizeConstraint Max(int max, SizeConstraint inner) => new MaxConstraint(max, inner);

    // ── Subtypes ──────────────────────────────────────────────────────

    /// <summary>Constraint that fixes the dimension to exactly <see cref="Size"/> characters.</summary>
    public sealed record FixedConstraint(int Size) : SizeConstraint;
    /// <summary>Constraint that takes a proportional share of remaining space, weighted by <see cref="Weight"/>.</summary>
    public sealed record FlexConstraint(int Weight) : SizeConstraint;
    /// <summary>Constraint that sizes the widget to its natural content size.</summary>
    public sealed record AutoConstraint : SizeConstraint;
    /// <summary>Applies a minimum bound of <see cref="MinSize"/> to the resolved value of <see cref="Inner"/>.</summary>
    public sealed record MinConstraint(int MinSize, SizeConstraint Inner) : SizeConstraint;
    /// <summary>Caps the resolved value of <see cref="Inner"/> at <see cref="MaxSize"/>.</summary>
    public sealed record MaxConstraint(int MaxSize, SizeConstraint Inner) : SizeConstraint;
}
