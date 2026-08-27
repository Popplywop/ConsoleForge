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
    /// <param name="crossAvailable">
    /// Cells available across <paramref name="axis"/>. Already known before this runs — it
    /// is the container's own cross dimension — which is what lets an <c>Auto</c> child be
    /// measured here without a circular dependency on its own allocation.
    /// </param>
    /// <param name="includeMargins">
    /// When true each child's slot includes its along-axis margins, and the caller is
    /// expected to subtract them again when building the child's region.
    /// </param>
    /// <param name="throwWhenImpossible">
    /// Whether an unsatisfiable layout — fixed children that cannot fit, with no flex child
    /// to give up space and nothing measured — is an error. The layout pass says yes; the
    /// render pass says no, because by then the exception has already been raised and
    /// throwing again would only turn a drawable frame into a crash.
    /// </param>
    /// <param name="sizes">Destination, at least <c>children.Count</c> long.</param>
    /// <exception cref="LayoutConstraintException">
    /// Fixed children cannot fit and there is no flex child to give up space.
    /// </exception>
    internal static void ResolveSizes(
        IReadOnlyList<IWidget> children,
        Axis axis,
        int available,
        int crossAvailable,
        bool includeMargins,
        bool throwWhenImpossible,
        Span<int> sizes)
    {
        bool horizontal = axis == Axis.Horizontal;
        int totalFixed = 0, totalFlexWeight = 0;
        bool anyMeasured = false;

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

            // Only pay for a measure when the constraint actually contains an Auto and the
            // child can answer; otherwise -1 keeps the old flex-weight-1 meaning.
            int measured = -1;
            if (child is IMeasurable measurable && ContainsAuto(constraint))
            {
                int alongBudget = Math.Max(0, available - margin);
                var desired = horizontal
                    ? measurable.Measure(alongBudget, crossAvailable)
                    : measurable.Measure(crossAvailable, alongBudget);
                measured    = Math.Max(0, horizontal ? desired.Width : desired.Height);
                anyMeasured = true;
            }

            int size = ResolveFixed(constraint, measured);
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

        int total = 0;
        for (int i = 0; i < children.Count; i++) total += Math.Max(0, sizes[i]);
        if (total <= available || total == 0) return;

        // Only an impossible *explicit* layout is an error. A container holding more
        // measured content than fits is ordinary — more text than rows, say — and must
        // scale back rather than throw.
        if (throwWhenImpossible && totalFlexWeight == 0 && !anyMeasured)
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
    /// The fixed cell count for a constraint, or -1 when it should take a share of the free
    /// space instead.
    /// </summary>
    /// <param name="constraint">The constraint to resolve.</param>
    /// <param name="measured">
    /// The child's measured size along this axis, or -1 when it could not be measured.
    /// <c>Auto</c> resolves to it; passing -1 therefore restores the older behaviour of
    /// treating <c>Auto</c> as flex weight 1. Min and Max wrappers fold over the result,
    /// so <c>Max(40, Auto)</c> is a content size capped at 40.
    /// </param>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static int ResolveFixed(SizeConstraint constraint, int measured = -1) =>
        constraint switch
        {
            SizeConstraint.FixedConstraint f => f.Size,
            SizeConstraint.AutoConstraint    => measured,
            SizeConstraint.MinConstraint m   => Math.Max(m.MinSize, ResolveFixed(m.Inner, measured)),
            SizeConstraint.MaxConstraint mx  => ResolveFixed(mx.Inner, measured) is int inner and >= 0
                                                   ? Math.Min(mx.MaxSize, inner)
                                                   : -1,
            SizeConstraint.FlexConstraint    => -1,
            _                                => -1,
        };

    /// <summary>
    /// The size <paramref name="child"/> wants, folding its own constraints over a measure.
    /// Used by composite widgets implementing <see cref="IMeasurable"/> to ask their children.
    /// </summary>
    /// <param name="child">The child to size.</param>
    /// <param name="availableWidth">Columns on offer to the child.</param>
    /// <param name="availableHeight">Rows on offer to the child.</param>
    /// <param name="flexWidth">What a flex width resolves to. A flex child has no intrinsic
    /// size — it fills what it is given — so callers pass the space it would fill on a cross
    /// axis, and 0 along the axis being summed.</param>
    /// <param name="flexHeight">The same for a flex height.</param>
    internal static Size DesiredSize(
        IWidget child,
        int availableWidth,
        int availableHeight,
        int flexWidth,
        int flexHeight)
    {
        bool autoWidth  = ContainsAuto(child.Width);
        bool autoHeight = ContainsAuto(child.Height);

        Size measured = default;
        if ((autoWidth || autoHeight) && child is IMeasurable measurable)
            measured = measurable.Measure(availableWidth, availableHeight);

        int width  = ResolveFixed(child.Width,  autoWidth  ? measured.Width  : -1);
        int height = ResolveFixed(child.Height, autoHeight ? measured.Height : -1);

        return new Size(width  < 0 ? flexWidth  : width,
                        height < 0 ? flexHeight : height);
    }

    /// <summary>True when the constraint is <c>Auto</c> or wraps one in Min/Max.</summary>
    internal static bool ContainsAuto(SizeConstraint constraint) =>
        constraint switch
        {
            SizeConstraint.AutoConstraint   => true,
            SizeConstraint.MinConstraint m  => ContainsAuto(m.Inner),
            SizeConstraint.MaxConstraint mx => ContainsAuto(mx.Inner),
            _                               => false,
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
