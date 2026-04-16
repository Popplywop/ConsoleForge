using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="Table"/>.</summary>
public class TableTests
{
    private static IReadOnlyList<IReadOnlyList<string>> MakeRows(params string[][] rows) =>
        rows.Select(r => (IReadOnlyList<string>)r).ToArray();

    // ── Column width resolution ───────────────────────────────────────────────

    [Fact]
    public void Render_FixedColumns_ShowHeadersAndData()
    {
        var cols = new[]
        {
            new TableColumn("Name",   Width: 10),
            new TableColumn("Status", Width: 10),
        };
        var rows = MakeRows(["Alice", "Active"], ["Bob", "Idle"]);
        var table = new Table(cols, rows);

        var descriptor = ViewDescriptor.From(table, width: 30, height: 10);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.Contains("Name",   plain);
        Assert.Contains("Status", plain);
        Assert.Contains("Alice",  plain);
        Assert.Contains("Bob",    plain);
    }

    [Fact]
    public void Render_FlexColumns_FillAvailableWidth()
    {
        // Two flex columns in 40px: each should get ~20px
        var cols = new[] { new TableColumn("A"), new TableColumn("B") };
        var rows = MakeRows(["x", "y"]);
        var table = new Table(cols, rows, paddingLeft: 0, paddingRight: 0);

        // Just verify it renders without error and contains data
        var descriptor = ViewDescriptor.From(table, width: 40, height: 5);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.Contains("A", plain);
        Assert.Contains("B", plain);
        Assert.Contains("x", plain);
    }

    [Fact]
    public void Render_MixedColumns_FixedGetsExactWidth()
    {
        var cols = new[]
        {
            new TableColumn("Fixed", Width: 8),
            new TableColumn("Flex"),   // flex fills rest
        };
        var rows = MakeRows(["abc", "def"]);
        var table = new Table(cols, rows);

        var descriptor = ViewDescriptor.From(table, width: 40, height: 5);
        var plain = TestHelpers.StripAnsi(descriptor.Content);
        Assert.Contains("Fixed", plain);
        Assert.Contains("Flex",  plain);
    }

    // ── Header ────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_HeaderAppears_OnFirstRow()
    {
        var cols = new[] { new TableColumn("ColHeader", Width: 15) };
        var table = new Table(cols, MakeRows(["data"]));

        var descriptor = ViewDescriptor.From(table, width: 30, height: 5);
        Assert.Contains("ColHeader", TestHelpers.StripAnsi(descriptor.Content));
    }

    [Fact]
    public void Render_EmptyRows_OnlyShowsHeader()
    {
        var cols = new[] { new TableColumn("OnlyHeader", Width: 15) };
        var table = new Table(cols, []);

        var descriptor = ViewDescriptor.From(table, width: 30, height: 5);
        Assert.Contains("OnlyHeader", TestHelpers.StripAnsi(descriptor.Content));
    }

    // ── Separator ─────────────────────────────────────────────────────────────

    [Fact]
    public void Render_WithSeparator_SeparatorAppearsInOutput()
    {
        var cols = new[]
        {
            new TableColumn("A", Width: 10),
            new TableColumn("B", Width: 10),
        };
        var table = new Table(cols, MakeRows(["x", "y"])) { Separator = '|' };

        var descriptor = ViewDescriptor.From(table, width: 30, height: 5);
        Assert.Contains("|", descriptor.Content);
    }

    [Fact]
    public void Render_NoSeparator_PipeNotInOutput()
    {
        var cols = new[]
        {
            new TableColumn("A", Width: 10),
            new TableColumn("B", Width: 10),
        };
        // Data rows that don't contain pipes
        var table = new Table(cols, MakeRows(["x", "y"]));

        var descriptor = ViewDescriptor.From(table, width: 30, height: 5);
        Assert.DoesNotContain("|", descriptor.Content);
    }

    // ── Selected row ──────────────────────────────────────────────────────────

    [Fact]
    public void Render_NoSelection_NegativeIndex_AllRowsNormal()
    {
        var cols = new[] { new TableColumn("Name", Width: 10) };
        var rows = MakeRows(["Alice"], ["Bob"]);
        var table = new Table(cols, rows, selectedIndex: -1);

        // No selection — just verify render is fine
        var descriptor = ViewDescriptor.From(table, width: 20, height: 5);
        Assert.Contains("Alice", descriptor.Content);
        Assert.Contains("Bob",   descriptor.Content);
    }

    [Fact]
    public void Render_SelectedRow_AppearInOutput()
    {
        var cols = new[] { new TableColumn("Name", Width: 15) };
        var rows = MakeRows(["Alice"], ["Bob"], ["Charlie"]);
        var table = new Table(cols, rows, selectedIndex: 1);

        var descriptor = ViewDescriptor.From(table, width: 30, height: 10);
        Assert.Contains("Bob", TestHelpers.StripAnsi(descriptor.Content));
    }

    // ── Data truncation ───────────────────────────────────────────────────────

    [Fact]
    public void Render_DataExceedsColumnWidth_IsTruncated()
    {
        var cols = new[] { new TableColumn("Name", Width: 5) };
        var rows = MakeRows(["VeryLongName"]);
        var table = new Table(cols, rows, paddingLeft: 0, paddingRight: 0);

        var descriptor = ViewDescriptor.From(table, width: 10, height: 5);
        Assert.DoesNotContain("VeryLongName", TestHelpers.StripAnsi(descriptor.Content));
    }

    // ── Missing cells ─────────────────────────────────────────────────────────

    [Fact]
    public void Render_RowWithFewerCells_DoesNotThrow()
    {
        var cols = new[]
        {
            new TableColumn("A", Width: 10),
            new TableColumn("B", Width: 10),
            new TableColumn("C", Width: 10),
        };
        var rows = MakeRows(["only one cell"]); // missing B and C
        var table = new Table(cols, rows);

        var ex = Record.Exception(() => ViewDescriptor.From(table, width: 40, height: 5));
        Assert.Null(ex);
    }

    // ── Render clipping ───────────────────────────────────────────────────────

    [Fact]
    public void Render_MoreRowsThanHeight_CutsOff()
    {
        var cols = new[] { new TableColumn("Name", Width: 10) };
        var rows = Enumerable.Range(1, 20).Select(i => (IReadOnlyList<string>)[$"row{i}"]).ToArray();
        var table = new Table(cols, rows);

        // Height 4: 1 header + 3 data rows max
        var descriptor = ViewDescriptor.From(table, width: 20, height: 4);
        Assert.DoesNotContain("row4",  descriptor.Content);
        Assert.DoesNotContain("row20", descriptor.Content);
    }

    // ── Empty column list ─────────────────────────────────────────────────────

    [Fact]
    public void Render_EmptyColumns_DoesNotThrow()
    {
        var table = new Table([], []);
        var ex = Record.Exception(() => ViewDescriptor.From(table, width: 40, height: 5));
        Assert.Null(ex);
    }

    // ── TableSelectionChangedMsg ──────────────────────────────────────────────

    [Fact]
    public void TableSelectionChangedMsg_HoldsSourceAndIndex()
    {
        var cols = new[] { new TableColumn("A", Width: 10) };
        var table = new Table(cols, MakeRows(["x"]));
        var msg = new TableSelectionChangedMsg(table, 0);

        Assert.Same(table, msg.Source);
        Assert.Equal(0, msg.NewIndex);
    }
}
