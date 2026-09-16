using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>
/// Paging arithmetic and viewport virtualisation for <see cref="HorizontalShelf"/>.
/// </summary>
/// <remarks>
/// The shelf takes a column offset rather than a page index, so paging is the model's
/// decision and an animated slide is the same thing with intermediate offsets. These cover
/// both: whole-page positions, and the in-between positions a slide passes through.
/// </remarks>
public class HorizontalShelfTests
{
    private const int CardW = 18, Gap = 2;              // stride 20
    private const int Viewport = 80;                    // 4 cards per page

    private static HorizontalShelf Shelf(int itemCount, int scrollOffset = 0) =>
        new([.. Enumerable.Range(0, itemCount).Select(i => new ShelfItem($"Item {i:D2}"))],
            scrollOffset: scrollOffset, cardWidth: CardW, cardGap: Gap);

    // ── Paging arithmetic ─────────────────────────────────────────────────────

    [Fact]
    public void CardsPerPage_CountsWholeCards_AllowingNoGapAfterTheLast()
    {
        // 4 cards need 18*4 + 2*3 = 78 columns; a fifth would need 98.
        Assert.Equal(4, Shelf(20).CardsPerPage(Viewport));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 80)]
    [InlineData(2, 160)]
    public void PageOffset_StepsByAWholePage(int page, int expected)
        => Assert.Equal(expected, Shelf(20).PageOffset(page, Viewport));

    [Fact]
    public void PageCount_RoundsUp_ForAPartialLastPage()
    {
        Assert.Equal(3, Shelf(9).PageCount(Viewport));   // 4 + 4 + 1
        Assert.Equal(2, Shelf(8).PageCount(Viewport));
        Assert.Equal(1, Shelf(1).PageCount(Viewport));
    }

    [Fact]
    public void PageCount_IsNeverZero_EvenWithNoItems()
        => Assert.Equal(1, Shelf(0).PageCount(Viewport));

    [Fact]
    public void PageOfItem_MapsBackToThePageThatShowsIt()
    {
        var shelf = Shelf(20);
        Assert.Equal(0, shelf.PageOfItem(3, Viewport));
        Assert.Equal(1, shelf.PageOfItem(4, Viewport));
        Assert.Equal(2, shelf.PageOfItem(9, Viewport));
    }

    [Fact]
    public void ViewportNarrowerThanACard_StillPagesOneCardAtATime()
    {
        // Guards against a divide that would yield zero cards per page and stall paging.
        var shelf = Shelf(10);
        Assert.Equal(1, shelf.CardsPerPage(5));
        Assert.Equal(10, shelf.PageCount(5));
    }

    // ── Virtualisation ────────────────────────────────────────────────────────

    [Fact]
    public void OnlyTheCardsTouchingTheViewportAreVisible()
    {
        // A thousand items must cost the same as a handful.
        var (first, count) = Shelf(1000).VisibleRange(Viewport);
        Assert.Equal(0, first);
        Assert.Equal(4, count);
    }

    [Fact]
    public void ScrolledToPageTwo_TheWindowMovesWithIt()
    {
        var (first, count) = Shelf(1000, scrollOffset: 80).VisibleRange(Viewport);
        Assert.Equal(4, first);
        Assert.Equal(4, count);
    }

    [Fact]
    public void MidSlide_BothPartiallyVisibleCardsAreIncluded()
    {
        // Halfway through a slide the viewport straddles five cards: one clipped at each
        // edge. Dropping either would make the animation flicker at its boundaries.
        var (first, count) = Shelf(1000, scrollOffset: 10).VisibleRange(Viewport);
        Assert.Equal(0, first);
        Assert.Equal(5, count);
    }

    [Fact]
    public void TheLastPageDoesNotRunPastTheEndOfTheItems()
    {
        var (first, count) = Shelf(6, scrollOffset: 80).VisibleRange(Viewport);
        Assert.Equal(4, first);
        Assert.Equal(2, count);   // items 4 and 5 only
    }

    [Fact]
    public void ScrolledPastTheEnd_NothingIsVisible()
        => Assert.Equal(0, Shelf(4, scrollOffset: 400).VisibleRange(Viewport).Count);

    [Fact]
    public void EmptyShelf_HasNothingVisible()
        => Assert.Equal(0, Shelf(0).VisibleRange(Viewport).Count);

    // ── Render ────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderDrawsOnlyTheVisibleCaptions()
    {
        var plain = TestHelpers.StripAnsi(
            ViewDescriptor.From(Shelf(20), width: Viewport, height: 12).Content);

        Assert.Contains("Item 00", plain);
        Assert.Contains("Item 03", plain);
        Assert.DoesNotContain("Item 04", plain);   // first card of the next page
        Assert.DoesNotContain("Item 19", plain);
    }

    [Fact]
    public void RenderAtAPageBoundaryShowsThatPage()
    {
        var plain = TestHelpers.StripAnsi(
            ViewDescriptor.From(Shelf(20, scrollOffset: 80), width: Viewport, height: 12).Content);

        Assert.Contains("Item 04", plain);
        Assert.Contains("Item 07", plain);
        Assert.DoesNotContain("Item 03", plain);
    }

    [Fact]
    public void RenderClipsACardStraddlingTheLeftEdge()
    {
        // Item 00 is half off-screen; its caption is truncated rather than drawn at a
        // negative column or dropped entirely.
        var plain = TestHelpers.StripAnsi(
            ViewDescriptor.From(Shelf(20, scrollOffset: 9), width: Viewport, height: 12).Content);

        Assert.DoesNotContain("Item 00", plain);   // full caption cannot fit
        Assert.Contains("Item 01", plain);
    }

    [Fact]
    public void RenderIntoTooSmallARegion_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            ViewDescriptor.From(Shelf(20), width: 3, height: 1));
        Assert.Null(ex);
    }

    [Fact]
    public void MeasureWantsTheWholeStripCappedAtWhatIsOffered()
    {
        var shelf = Shelf(4);
        // 4 cards, 3 gaps, no trailing gap. A shelf that wants 78 columns must not claim
        // the 80 on offer — Auto sizing would then leave a two-column hole.
        Assert.Equal(78, shelf.Measure(200, 20).Width);
        Assert.Equal(78, shelf.Measure(Viewport, 20).Width);
        Assert.Equal(40, shelf.Measure(40, 20).Width);   // capped at what is offered
    }

    [Fact]
    public void MeasureIsArtworkPlusOneCaptionRow()
        => Assert.Equal(10, Shelf(4).Measure(200, 20).Height);   // CardHeight 9 + caption
}
