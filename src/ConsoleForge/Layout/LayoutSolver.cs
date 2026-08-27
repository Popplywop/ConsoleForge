namespace ConsoleForge.Layout;

/// <summary>
/// The one implementation of "given these children and this much space along an axis,
/// how many cells does each one get".
/// </summary>
/// <remarks>
/// <para>
/// Two callers need this answer and must agree on it: <see cref="LayoutEngine"/>, which
/// builds the <see cref="ResolvedLayout"/> used for focus and hit-testing, and
/// <c>Container.Render</c>, which places children into the render context. They used to
/// carry separate copies of the arithmetic — including separate copies of
/// <c>ResolveFixed</c> and <c>GetFlexWeight</c> — so a change to one silently moved
/// widgets away from where the other believed they were.
/// </para>
/// <para>
/// Region construction stays with the callers: they genuinely differ, since only the
/// render path applies scroll offset and clipping.
/// </para>
/// </remarks>
internal static class LayoutSolver
{
    /// <summary>
    /// Resolve the along-axis size of each child into <paramref name="sizes"/>.
    /// </summary>
    /// <param name="children">Children in declaration order.</param>
    /// <param name="axis">Axis being distributed; the cross axis is unconstrained here.</param>
    /// <param name="available">Cells available along <paramref name="axis"/>.</param>
    /// <param name="includeMargins">
    /// When true each child's slot includes its along-axis margins, and the caller is
    /// expected to subtract them again when building the child's region.
    /// </param>
    /// <param name="clampOverflow">
    /// When true, children that collectively exceed <paramref name="available"/> are scaled
    /// back to fit, and a set of fixed children that cannot fit with no flex child to absorb
    /// the difference throws. When false the overflow is left in place for the caller to clip
    /// — which is what the render path does.
    /// </param>
    /// <param name="sizes">Destination, at least <c>children.Count</c> long.</param>
    /// <exception cref="LayoutConstraintException">
    /// Fixed children cannot fit and there is no flex child to give up space.
    /// </exception>
    internal static void ResolveSizes(
        IReadOnlyList<IWidget> children,
        Axis axis,
        int available,
        bool includeMargins,
        bool clampOverflow,
        Span<int> sizes)
    {
        bool horizontal = axis == Axis.Horizontal;
        int totalFixed = 0, totalFlexWeight = 0;

        // Pass 1: fixed children take their size; flex children are parked as negative
        // weights so a second pass can recognise them without a second constraint switch.
        for (int i = 0; i < children.Count; i++)
        {
            var child      = children[i];
            var constraint = horizontal ? child.Width : child.Height;

            int margin = 0;
            if (includeMargins)
            {
                var style = child.Style;
                if (style.HasMargin)
                    margin = horizontal
                        ? style.MarginLeft + style.MarginRight
                        : style.MarginTop  + style.MarginBottom;
            }

            int size = ResolveFixed(constraint);
            if (size >= 0)
            {
                sizes[i]    = size + margin;
                totalFixed += size + margin;
            }
            else
            {
                int weight = GetFlexWeight(constraint);
                sizes[i]         = -weight;
                totalFlexWeight += weight;
            }
        }

        // Pass 2: split what's left between the flex children, giving the rounding
        // remainder to the last of them so the row is filled exactly.
        int freeSpace = Math.Max(0, available - totalFixed);
        int distributed = 0, lastFlex = -1;
        for (int i = 0; i < children.Count; i++)
        {
            if (sizes[i] >= 0) continue;
            int weight = -sizes[i];
            int share  = totalFlexWeight > 0 ? freeSpace * weight / totalFlexWeight : 0;
            sizes[i]    = share;
            distributed += share;
            lastFlex     = i;
        }
        if (lastFlex >= 0) sizes[lastFlex] += freeSpace - distributed;

        if (!clampOverflow) return;

        int total = 0;
        for (int i = 0; i < children.Count; i++) total += Math.Max(0, sizes[i]);
        if (total <= available || total == 0) return;

        if (totalFlexWeight == 0)
            throw new LayoutConstraintException(
                $"Fixed children ({total}px) collectively exceed available space ({available}px) " +
                $"in a {axis} container with no flex children.");

        int lastNonZero = -1;
        for (int i = children.Count - 1; i >= 0; i--)
            if (sizes[i] > 0) { lastNonZero = i; break; }

        int remainder = available;
        for (int i = 0; i < children.Count; i++)
        {
            if (sizes[i] <= 0) { sizes[i] = 0; continue; }
            int clamped = i != lastNonZero ? sizes[i] * available / total : remainder;
            sizes[i]    = Math.Max(0, clamped);
            remainder  -= sizes[i];
        }
    }

    /// <summary>
    /// The fixed cell count for a constraint, or -1 when it should take a share of the
    /// free space instead. <c>Auto</c> reports -1: it is documented as sizing to content,
    /// but no widget can be asked its content size yet, so it behaves as flex weight 1.
    /// See WISHLIST item 1.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static int ResolveFixed(SizeConstraint constraint) =>
        constraint switch
        {
            SizeConstraint.FixedConstraint f => f.Size,
            SizeConstraint.AutoConstraint    => -1,
            SizeConstraint.MinConstraint m   => Math.Max(m.MinSize, ResolveFixed(m.Inner)),
            SizeConstraint.MaxConstraint mx  => ResolveFixed(mx.Inner) is int inner and >= 0
                                                   ? Math.Min(mx.MaxSize, inner)
                                                   : -1,
            SizeConstraint.FlexConstraint    => -1,
            _                                => -1,
        };

    /// <summary>Flex weight of a constraint, looking through Min/Max wrappers. Defaults to 1.</summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static int GetFlexWeight(SizeConstraint constraint) =>
        constraint switch
        {
            SizeConstraint.FlexConstraint f => f.Weight,
            SizeConstraint.MinConstraint m  => GetFlexWeight(m.Inner),
            SizeConstraint.MaxConstraint mx => GetFlexWeight(mx.Inner),
            _                               => 1,
        };
}
