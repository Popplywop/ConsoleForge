using ConsoleForge.Layout;
using ConsoleForge.Styling;

namespace ConsoleForge.Widgets;

/// <summary>
/// A horizontally scrolling strip of cards — cover art with a caption — of the kind a media
/// library uses for "Continue Watching" or "Recently Added".
/// </summary>
/// <remarks>
/// <para>
/// The shelf renders a window onto <see cref="Items"/> at <see cref="ScrollOffset"/> columns
/// and draws only the cards that fall inside it, so a shelf of a thousand items costs the
/// same as one of ten.
/// </para>
/// <para>
/// It knows nothing about pages. <see cref="ScrollOffset"/> is a column position, and what
/// moves it is the model's business: jump it to a <see cref="PageOffset"/> for paging, ease
/// it between two of them over several frames for an animated slide, or step it by
/// <see cref="Stride"/> for a continuous scroll. Cards that straddle an edge are clipped,
/// which is what makes the intermediate frames of a slide look right.
/// </para>
/// <para>
/// Selection and scroll live in your model. The shelf is not focusable and never changes
/// them; give it the values to draw and fold key handling into your own <c>Update</c>, where
/// a two-dimensional layout of several shelves needs it anyway.
/// </para>
/// <code>
/// // paging: the model owns the page number and asks the shelf where that lands
/// var shelf = new HorizontalShelf(items, cardWidth: 18, cardGap: 2);
/// shelf = shelf with { ScrollOffset = shelf.PageOffset(Page, shelfWidth) };
///
/// // later, an animated slide is the same call with an eased offset in between
/// shelf = shelf with { ScrollOffset = Anim?.CurrentOffset ?? shelf.PageOffset(Page, shelfWidth) };
/// </code>
/// </remarks>
public sealed record HorizontalShelf : IWidget, IMeasurable
{
    // ── IWidget ─────────────────────────────────────────────────────────────
    public SizeConstraint Width  { get; init; } = SizeConstraint.Flex(1);
    public SizeConstraint Height { get; init; } = SizeConstraint.Auto;

    /// <summary>Visual style for the shelf. Inherits the theme's base style when unset.</summary>
    public Style Style { get; init; } = Style.Default;

    // ── Shelf-specific ───────────────────────────────────────────────────────

    /// <summary>Cards in display order.</summary>
    public IReadOnlyList<ShelfItem> Items { get; init; } = [];

    /// <summary>Columns each card's artwork occupies. Captions are clipped to it.</summary>
    public int CardWidth { get; init; } = 18;

    /// <summary>Rows of artwork above the caption.</summary>
    public int CardHeight { get; init; } = 9;

    /// <summary>Blank columns between adjacent cards.</summary>
    public int CardGap { get; init; } = 2;

    /// <summary>
    /// Horizontal scroll position in columns from the start of the strip. Cards straddling
    /// either edge are clipped rather than dropped.
    /// </summary>
    public int ScrollOffset { get; init; }

    /// <summary>Index of the highlighted card, or -1 for none.</summary>
    public int SelectedIndex { get; init; } = -1;

    /// <summary>Style for the caption of the selected card.</summary>
    public Style SelectedCaptionStyle { get; init; } = Style.Default.Bold(true);

    /// <summary>Style for the caption of unselected cards.</summary>
    public Style CaptionStyle { get; init; } = Style.Default;

    /// <summary>Object-initializer constructor; all properties default.</summary>
    public HorizontalShelf() { }

    /// <summary>Positional constructor for inline usage.</summary>
    /// <param name="items">Cards in display order.</param>
    /// <param name="scrollOffset">Column position of the viewport.</param>
    /// <param name="selectedIndex">Highlighted card, or -1.</param>
    /// <param name="cardWidth">Columns per card.</param>
    /// <param name="cardHeight">Rows of artwork per card.</param>
    /// <param name="cardGap">Blank columns between cards.</param>
    public HorizontalShelf(
        IReadOnlyList<ShelfItem> items,
        int scrollOffset = 0,
        int selectedIndex = -1,
        int cardWidth = 18,
        int cardHeight = 9,
        int cardGap = 2)
    {
        Items         = items;
        ScrollOffset  = scrollOffset;
        SelectedIndex = selectedIndex;
        CardWidth     = cardWidth;
        CardHeight    = cardHeight;
        CardGap       = cardGap;
    }

    // ── Paging helpers ────────────────────────────────────────────────────────
    // Pure functions over the shelf's geometry. Paging is the model's decision; these
    // just answer the arithmetic it needs to make it.

    /// <summary>Columns one card occupies including the gap that follows it.</summary>
    public int Stride => CardWidth + CardGap;

    /// <summary>
    /// How many whole cards fit in <paramref name="viewportWidth"/>. At least one, so a
    /// viewport narrower than a card still pages one card at a time rather than stalling.
    /// </summary>
    public int CardsPerPage(int viewportWidth) =>
        Math.Max(1, (viewportWidth + CardGap) / Math.Max(1, Stride));

    /// <summary>Number of pages needed to show every card.</summary>
    public int PageCount(int viewportWidth)
    {
        int perPage = CardsPerPage(viewportWidth);
        return Math.Max(1, (Items.Count + perPage - 1) / perPage);
    }

    /// <summary>The scroll offset that puts <paramref name="page"/> at the left edge.</summary>
    public int PageOffset(int page, int viewportWidth) =>
        Math.Max(0, page) * CardsPerPage(viewportWidth) * Stride;

    /// <summary>The page <paramref name="itemIndex"/> falls on.</summary>
    public int PageOfItem(int itemIndex, int viewportWidth) =>
        Math.Max(0, itemIndex) / CardsPerPage(viewportWidth);

    /// <summary>
    /// The range of card indices that <paramref name="viewportWidth"/> columns at
    /// <see cref="ScrollOffset"/> touch, including cards clipped by either edge.
    /// </summary>
    /// <param name="viewportWidth">Width of the visible strip.</param>
    /// <returns>First index, and the count of cards from it. Count is 0 when none are visible.</returns>
    public (int First, int Count) VisibleRange(int viewportWidth)
    {
        if (Items.Count == 0 || viewportWidth <= 0) return (0, 0);

        int stride = Math.Max(1, Stride);
        int first  = Math.Max(0, ScrollOffset / stride);
        if (first >= Items.Count) return (0, 0);

        // Walk forward until a card starts beyond the right edge. Cards are uniform, so
        // this is arithmetic, but the clamp matters for the last page.
        int last = (ScrollOffset + viewportWidth - 1) / stride;
        last = Math.Min(last, Items.Count - 1);

        return (first, Math.Max(0, last - first + 1));
    }

    // ── IMeasurable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>Artwork plus one caption row; as wide as the strip, capped at what is offered.</remarks>
    public Size Measure(int availableWidth, int availableHeight)
    {
        int wanted = Items.Count == 0 ? 0 : (Items.Count * Stride) - CardGap;
        return new Size(
            Math.Min(availableWidth, Math.Max(0, wanted)),
            Math.Min(availableHeight, CardHeight + 1));
    }

    // ── Render ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Render(IRenderContext ctx)
    {
        var region = ctx.Region;
        if (region.Width <= 0 || region.Height <= 0 || Items.Count == 0) return;

        var (first, count) = VisibleRange(region.Width);
        if (count == 0) return;

        int stride      = Math.Max(1, Stride);
        int captionRow  = region.Row + Math.Min(CardHeight, region.Height - 1);
        int artHeight   = Math.Min(CardHeight, region.Height);

        for (int i = first; i < first + count; i++)
        {
            // Column of this card's left edge relative to the viewport. Negative means the
            // card is clipped by the left edge, which happens mid-slide.
            int cardLeft = (i * stride) - ScrollOffset;

            int visibleLeft  = Math.Max(cardLeft, 0);
            int visibleRight = Math.Min(cardLeft + CardWidth, region.Width);
            int visibleWidth = visibleRight - visibleLeft;
            if (visibleWidth <= 0) continue;

            var item = Items[i];

            // ── Artwork ──────────────────────────────────────────────────────
            var artRegion = new Region(
                region.Col + visibleLeft, region.Row, visibleWidth, artHeight);

            if (item.Poster is { Length: > 0 })
            {
                // The image is a raw-escape payload tracked by content identity, so a card
                // that moves is re-placed rather than re-uploaded — see IRawEscapePayload.
                var image = new ImageWidget(item.Poster, item.Capabilities)
                {
                    Width  = SizeConstraint.Fixed(artRegion.Width),
                    Height = SizeConstraint.Fixed(artRegion.Height),
                };
                image.Render(new SubRenderContext(ctx, artRegion));
            }
            else if (artRegion.Height > 0)
            {
                // Placeholder so the shelf has shape before artwork arrives.
                var placeholder = Style.Inherit(ctx.Theme.BaseStyle);
                var fill = new string('░', artRegion.Width);
                for (int r = 0; r < artRegion.Height; r++)
                    ctx.Write(artRegion.Col, artRegion.Row + r, fill, placeholder);
            }

            // ── Caption ──────────────────────────────────────────────────────
            if (captionRow < region.Row + region.Height && item.Title.Length > 0)
            {
                var style = (i == SelectedIndex ? SelectedCaptionStyle : CaptionStyle)
                    .Inherit(ctx.Theme.BaseStyle);

                // Truncate to the card, then write at the card's true left column — which is
                // negative for a card clipped by the left edge. Write clips at both bounds,
                // so the caption scrolls off with its card instead of sliding along the edge.
                ctx.Write(region.Col + cardLeft, captionRow,
                          TextUtils.TruncateToWidth(item.Title, CardWidth), style);
            }
        }
    }
}

/// <summary>One card in a <see cref="HorizontalShelf"/>.</summary>
/// <param name="Title">Caption below the artwork. Truncated to the card width.</param>
/// <param name="Poster">
/// PNG artwork, or null while it is still loading — the card draws a placeholder until
/// the bytes arrive, so a shelf can render before its images do.
/// </param>
/// <param name="Capabilities">
/// Terminal capabilities, forwarded to <see cref="ImageWidget"/> so it can pick the Kitty
/// path where available and half-blocks otherwise.
/// </param>
/// <param name="Key">
/// Caller's identifier for the item this card came from — a rating key, an id. The shelf
/// does not read it; it is here so an <c>Update</c> handling a selection knows what was
/// selected without indexing back into a list that may have been replaced.
/// </param>
public sealed record ShelfItem(
    string Title,
    byte[]? Poster = null,
    ConsoleForge.Terminal.TerminalCapabilities? Capabilities = null,
    string? Key = null);
