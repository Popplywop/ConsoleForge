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
    /// <param name="scrollable">
    /// The container scrolls along <paramref name="axis"/>: children keep their own sizes
    /// even when together they overflow <paramref name="available"/> — that overflow is what
    /// there is to scroll — and an <c>Auto</c> child is measured without an along-axis
    /// limit. Flex children still share only what the viewport has left.
    /// </param>
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
        Span<int> sizes,
        bool scrollable = false)
    {
        bool horizontal = axis == Axis.Horizontal;
        int totalFixed = 0, totalFlexWeight = 0;
        bool anyMeasured = false, anyBoundedFlex = false;

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
                int alongBudget = scrollable ? UnboundedAlong : Math.Max(0, available - margin);
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
                if (constraint is SizeConstraint.MinConstraint or SizeConstraint.MaxConstraint)
                    anyBoundedFlex = true;
            }
        }

        int freeSpace = Math.Max(0, available - totalFixed);
        if (anyBoundedFlex)
            DistributeBounded(children, horizontal, includeMargins, freeSpace, sizes);
        else
            DistributeFlex(children.Count, freeSpace, totalFlexWeight, sizes);

        int total = 0;
        for (int i = 0; i < children.Count; i++) total += Math.Max(0, sizes[i]);
        if (scrollable || total <= available || total == 0) return;

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

    // An along-axis measure budget for a scrolling container: no limit worth the name,
    // with room left for a widget that adds padding to it without overflowing.
    private const int UnboundedAlong = int.MaxValue / 4;

    /// <summary>
    /// Split <paramref name="freeSpace"/> between the flex children (those holding a
    /// negative weight in <paramref name="sizes"/>), giving the rounding remainder to the
    /// last of them so the row is filled exactly.
    /// </summary>
    private static void DistributeFlex(int count, int freeSpace, int totalFlexWeight, Span<int> sizes)
    {
        int distributed = 0, lastFlex = -1;
        for (int i = 0; i < count; i++)
        {
            if (sizes[i] >= 0) continue;
            int weight = -sizes[i];
            int share  = totalFlexWeight > 0 ? freeSpace * weight / totalFlexWeight : 0;
            sizes[i]    = share;
            distributed += share;
            lastFlex     = i;
        }
        if (lastFlex >= 0) sizes[lastFlex] += freeSpace - distributed;
    }

    /// <summary>
    /// <see cref="DistributeFlex"/> for when some flex children carry Min/Max bounds.
    /// </summary>
    /// <remarks>
    /// The flexbox resolution: share the space by weight, clamp each share to its bounds,
    /// and if clamping moved the total, freeze the children that moved in the dominant
    /// direction — all the ones raised to a minimum if the shares came out short, all
    /// the ones cut to a maximum if long — and share what is left among the rest again.
    /// Each round freezes at least one child, so it ends within one round per child.
    /// </remarks>
    private static void DistributeBounded(
        IReadOnlyList<IWidget> children, bool horizontal, bool includeMargins, int freeSpace, Span<int> sizes)
    {
        int remaining = freeSpace;
        while (true)
        {
            int totalWeight = 0, lastActive = -1;
            for (int i = 0; i < children.Count; i++)
                if (sizes[i] < 0) { totalWeight -= sizes[i]; lastActive = i; }
            if (lastActive < 0) return;

            // Unclamped shares and how far clamping would move each, without writing yet.
            int space = Math.Max(0, remaining), distributed = 0, violation = 0;
            for (int i = 0; i <= lastActive; i++)
            {
                if (sizes[i] >= 0) continue;
                int share = i == lastActive ? space - distributed : space * -sizes[i] / totalWeight;
                distributed += share;
                violation   += ClampSlot(children[i], horizontal, includeMargins, share) - share;
            }

            distributed = 0;
            for (int i = 0; i <= lastActive; i++)
            {
                if (sizes[i] >= 0) continue;
                int weight  = -sizes[i];
                int share   = i == lastActive ? space - distributed : space * weight / totalWeight;
                distributed += share;
                int clamped = ClampSlot(children[i], horizontal, includeMargins, share);

                // No violation: every share stands. Otherwise freeze only the children
                // clamped in the direction the total moved; the rest go round again.
                if (violation == 0 || (violation > 0 ? clamped > share : clamped < share))
                {
                    sizes[i]   = clamped;
                    remaining -= clamped;
                }
            }
            if (violation == 0) return;
        }
    }

    // A slot holds the child's margins when the caller asked for them; the bounds are on
    // the child itself, so they apply to the slot less its margins.
    private static int ClampSlot(IWidget child, bool horizontal, bool includeMargins, int slot)
    {
        var constraint = horizontal ? child.Width : child.Height;
        if (constraint is not (SizeConstraint.MinConstraint or SizeConstraint.MaxConstraint))
            return slot;

        int margin = 0;
        if (includeMargins && child.Style.HasMargin)
        {
            var style = child.Style;
            margin = horizontal ? style.MarginLeft + style.MarginRight
                                : style.MarginTop  + style.MarginBottom;
        }
        return Clamp(constraint, Math.Max(0, slot - margin)) + margin;
    }

    /// <summary>
    /// Fold a flex constraint's Min/Max wrappers over a share of free space, inside out:
    /// <c>Max(10, Min(4, Flex))</c> raises a share to at least 4, then caps it at 10.
    /// </summary>
    internal static int Clamp(SizeConstraint constraint, int share) =>
        constraint switch
        {
            SizeConstraint.MinConstraint m  => Math.Max(m.MinSize, Clamp(m.Inner, share)),
            SizeConstraint.MaxConstraint mx => Math.Min(mx.MaxSize, Clamp(mx.Inner, share)),
            _                               => share,
        };

    /// <summary>
    /// <paramref name="offset"/> limited to the scroll range of content with the given
    /// <paramref name="sizes"/> in a viewport <paramref name="available"/> cells long: never
    /// negative, and never past the point where the content's end meets the viewport's.
    /// </summary>
    internal static int ClampScroll(int offset, ReadOnlySpan<int> sizes, int available)
    {
        int total = 0;
        foreach (int s in sizes) total += Math.Max(0, s);
        return Math.Clamp(offset, 0, Math.Max(0, total - available));
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
    /// so <c>Max(40, Auto)</c> is a content size capped at 40. Over a flex inner they
    /// leave the constraint flex: the bounds apply to its share, in <see cref="Clamp"/>.
    /// </param>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static int ResolveFixed(SizeConstraint constraint, int measured = -1) =>
        constraint switch
        {
            SizeConstraint.FixedConstraint f => f.Size,
            SizeConstraint.AutoConstraint    => measured,
            SizeConstraint.MinConstraint m   => ResolveFixed(m.Inner, measured) is int inner and >= 0
                                                   ? Math.Max(m.MinSize, inner)
                                                   : -1,
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

        return new Size(width  < 0 ? Clamp(child.Width,  flexWidth)  : width,
                        height < 0 ? Clamp(child.Height, flexHeight) : height);
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
