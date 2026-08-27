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

    /// <summary>
    /// Intended to size to content (longest line / child count).
    /// <para><b>Not implemented:</b> <c>LayoutEngine</c> currently resolves this as
    /// <see cref="Flex(int)"/> weight 1, so an <c>Auto</c> child takes an equal share of
    /// free space instead of shrinking to fit. Widgets expose no measure API yet.
    /// Use <see cref="Fixed(int)"/> where a content-sized child matters.</para>
    /// </summary>
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
    /// <summary>Constraint intended to size the widget to its natural content size.
    /// Resolved as flex weight 1 until a measure pass exists — see <see cref="Auto"/>.</summary>
    public sealed record AutoConstraint : SizeConstraint;
    /// <summary>Applies a minimum bound of <see cref="MinSize"/> to the resolved value of <see cref="Inner"/>.</summary>
    public sealed record MinConstraint(int MinSize, SizeConstraint Inner) : SizeConstraint;
    /// <summary>Caps the resolved value of <see cref="Inner"/> at <see cref="MaxSize"/>.</summary>
    public sealed record MaxConstraint(int MaxSize, SizeConstraint Inner) : SizeConstraint;
}
