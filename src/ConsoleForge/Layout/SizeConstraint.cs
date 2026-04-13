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

    public sealed record FixedConstraint(int Size) : SizeConstraint;
    public sealed record FlexConstraint(int Weight) : SizeConstraint;
    public sealed record AutoConstraint : SizeConstraint;
    public sealed record MinConstraint(int MinSize, SizeConstraint Inner) : SizeConstraint;
    public sealed record MaxConstraint(int MaxSize, SizeConstraint Inner) : SizeConstraint;
}
