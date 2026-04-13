using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests;

public class RenderTests
{
    /// <summary>
    /// Regression: children inside a Container nested in a BorderBox must appear in the frame.
    /// Before the fix, Container relied on ResolvedLayout for child regions; those regions were
    /// absolute-from-root and did not account for the BorderBox inner offset, so all children
    /// were clipped to zero and silently dropped.
    /// </summary>
    [Fact]
    public void Container_ChildrenInsideBorderBox_AreRendered()
    {
        var widget = new BorderBox(
            title: "Test",
            body: new Container(Axis.Vertical, [
                new TextBlock("Hello"),
                new TextBlock("World"),
            ])
        );

        var descriptor = ViewDescriptor.From(widget, width: 40, height: 10);
        var frame = descriptor.Content;

        Assert.Contains("Hello", frame);
        Assert.Contains("World", frame);
    }

    /// <summary>Children in a plain Container (not inside BorderBox) must also render.</summary>
    [Fact]
    public void Container_ChildrenAtRoot_AreRendered()
    {
        var widget = new Container(Axis.Vertical, [
            new TextBlock("Line1"),
            new TextBlock("Line2"),
        ]);

        var descriptor = ViewDescriptor.From(widget, width: 40, height: 10);
        var frame = descriptor.Content;

        Assert.Contains("Line1", frame);
        Assert.Contains("Line2", frame);
    }

    /// <summary>Deeply nested containers must render their content.</summary>
    [Fact]
    public void Container_NestedContainers_AreRendered()
    {
        var widget = new Container(Axis.Horizontal, [
            new Container(Axis.Vertical, [
                new TextBlock("Left"),
            ]),
            new Container(Axis.Vertical, [
                new TextBlock("Right"),
            ]),
        ]);

        var descriptor = ViewDescriptor.From(widget, width: 40, height: 10);
        var frame = descriptor.Content;

        Assert.Contains("Left", frame);
        Assert.Contains("Right", frame);
    }

    /// <summary>
    /// Regression: SysMonitor layout — sidebar (Fixed 20) + main pane (flex) inside a header/body/status
    /// vertical stack. Sidebar content must not bleed into main pane and both must render content.
    /// </summary>
    [Fact]
    public void SysMonitor_Layout_SidebarAndMainBothRender()
    {
        var sidebar = new BorderBox(
            title: "Nav",
            body: new TextBlock("SideContent"),
            style: Style.Default.BorderForeground(Color.Cyan)
        ) { Width = SizeConstraint.Fixed(20) };

        var main = new BorderBox(
            title: "CPU",
            body: new TextBlock("MainContent"),
            style: Style.Default.BorderForeground(Color.Cyan)
        );

        var header = new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(1),
            children: [new TextBlock("HEADER")]);

        var statusBar = new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(1),
            children: [new TextBlock("STATUS")]);

        var body = new Container(Axis.Horizontal,
            height: SizeConstraint.Flex(1),
            children: [sidebar, main]);

        var root = new Container(Axis.Vertical,
            children: [header, body, statusBar]);

        var layout = LayoutEngine.Resolve(root, 60, 12);

        // Verify sidebar gets exactly 20 cols, main gets the rest (40)
        var sidebarRegion = layout.GetRegion(sidebar);
        var mainRegion = layout.GetRegion(main);

        Assert.NotNull(sidebarRegion);
        Assert.NotNull(mainRegion);
        Assert.Equal(20, sidebarRegion!.Value.Width);
        Assert.Equal(40, mainRegion!.Value.Width);
        Assert.Equal(0, sidebarRegion!.Value.Col);   // sidebar starts at col 0
        Assert.Equal(20, mainRegion!.Value.Col);     // main starts at col 20

        // Both widgets must render their content
        var descriptor = ViewDescriptor.From(root, width: 60, height: 12);
        var frame = descriptor.Content;
        Assert.Contains("SideContent", frame);
        Assert.Contains("MainContent", frame);
        Assert.Contains("HEADER", frame);
        Assert.Contains("STATUS", frame);
    }

    /// <summary>
    /// Render SysMonitor-style layout at 120x30 and verify content appears in the right columns.
    /// Header and statusbar must span full width. Sidebar content must be in cols 0-19.
    /// Main pane content must be in cols 20+.
    /// </summary>
    [Fact]
    public void SysMonitor_RenderPositions_AreCorrect()
    {
        const int W = 120, H = 30;

        var sidebar = new BorderBox(
            title: " Nav ",
            body: new TextBlock("SIDECONTENT"),
            style: Style.Default.BorderForeground(Color.Cyan).Border(Borders.Thick)
        ) { Width = SizeConstraint.Fixed(20) };

        var main = new BorderBox(
            title: " CPU ",
            body: new TextBlock("MAINCONTENT"),
            style: Style.Default.BorderForeground(Color.Cyan).Border(Borders.Rounded)
        );

        var header = new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(1),
            style: Style.Default.Background(Color.FromHex("#1C1C1C")).Foreground(Color.BrightWhite),
            children: [new TextBlock("HEADERTEXT")]);

        var statusBar = new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(1),
            style: Style.Default.Background(Color.FromHex("#1C1C1C")).Foreground(Color.BrightWhite),
            children: [new TextBlock("STATUSTEXT")]);

        var body = new Container(Axis.Horizontal,
            height: SizeConstraint.Flex(1),
            children: [sidebar, main]);

        var root = new Container(Axis.Vertical,
            children: [header, body, statusBar]);

        var descriptor = ViewDescriptor.From(root, width: W, height: H);
        var grid = DecodeAnsiToGrid(descriptor.Content, W, H);

        var (sideRow, sideCol) = FindText(grid, "SIDECONTENT");
        var (mainRow, mainCol) = FindText(grid, "MAINCONTENT");
        var (hdrRow, hdrCol)   = FindText(grid, "HEADERTEXT");
        var (stRow, stCol)     = FindText(grid, "STATUSTEXT");

        Assert.True(sideRow >= 0, "SIDECONTENT not found in frame");
        Assert.True(mainRow >= 0, "MAINCONTENT not found in frame");
        Assert.True(hdrRow  >= 0, "HEADERTEXT not found in frame");
        Assert.True(stRow   >= 0, "STATUSTEXT not found in frame");

        // Sidebar content must be inside cols 1-18 (inside border of Fixed(20) sidebar)
        Assert.True(sideCol >= 1 && sideCol < 20,
            $"SIDECONTENT at col {sideCol}, expected inside sidebar cols 1-18");

        // Main pane content must start at col 21+ (inside border of main, which starts at col 20)
        Assert.True(mainCol >= 21,
            $"MAINCONTENT at col {mainCol}, expected in main pane (col >= 21)");

        // Header must be on row 0
        Assert.Equal(0, hdrRow);

        // Status bar must be on the last row
        Assert.Equal(H - 1, stRow);
    }

    /// <summary>
    /// Regression: double-buffer diff must not leave stale content when text changes position.
    /// Render frame 1 then frame 2 (different content) using the same RenderContext.
    /// Apply frame2 diff on top of decoded frame1 grid — result must not contain frame1-only text.
    /// </summary>
    [Fact]
    public void DoubleBuffer_SecondFrame_DoesNotContainStaleContent()
    {
        const int W = 80, H = 10;

        var ctx = new RenderContext(new Region(0, 0, W, H), Theme.Default, ColorProfile.TrueColor,
            LayoutEngine.Resolve(new TextBlock(""), W, H));

        // Frame 1: "FRAME_ONE" at row 0, "SAME_TEXT" at row 1
        var w1 = new Container(Axis.Vertical, [
            new TextBlock("FRAME_ONE"),
            new TextBlock("SAME_TEXT"),
        ]);
        var frame1Content = ViewDescriptor.From(w1, existingCtx: ctx, width: W, height: H).Content;

        // Frame 2: "FRAME_TWO" at row 0, same "SAME_TEXT" at row 1
        var w2 = new Container(Axis.Vertical, [
            new TextBlock("FRAME_TWO"),
            new TextBlock("SAME_TEXT"),
        ]);
        var frame2Content = ViewDescriptor.From(w2, existingCtx: ctx, width: W, height: H).Content;

        // Simulate terminal state: start from frame1 full draw, then apply frame2 diff
        var grid = DecodeAnsiToGrid(frame1Content, W, H);
        ApplyDiff(grid, frame2Content, W, H);

        var (r1, _) = FindText(grid, "FRAME_TWO");
        var (r2, c2) = FindText(grid, "FRAME_ONE");

        Assert.True(r1 >= 0, "FRAME_TWO not found after second frame diff");
        Assert.True(r2 < 0,
            $"FRAME_ONE still visible after second frame at row={r2},col={c2} — stale content in double-buffer");
    }

    /// <summary>
    /// Simulate two consecutive tick frames (like SysMonitor produces).
    /// Both frames share a RenderContext (like Renderer does).
    /// After applying frame2 diff on top of frame1, the terminal grid must show frame2 content exactly.
    /// </summary>
    [Fact]
    public void SysMonitor_TwoConsecutiveFrames_TerminalGridIsCorrect()
    {
        const int W = 120, H = 30;
        var theme = new Theme(
            name: "SysMonitor",
            baseStyle: Style.Default.Foreground(Color.BrightWhite),
            borderStyle: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded)
        );

        IWidget MakeFrame(string mainText, string headerTime) =>
            new Container(Axis.Vertical, children: [
                new Container(Axis.Horizontal,
                    height: SizeConstraint.Fixed(1),
                    style: Style.Default.Background(Color.FromHex("#1C1C1C")).Foreground(Color.BrightWhite),
                    children: [
                        new Container(Axis.Vertical, width: SizeConstraint.Flex(1),
                            children: [new TextBlock(" ConsoleForge SysMonitor")]),
                        new Container(Axis.Vertical, width: SizeConstraint.Fixed(10),
                            children: [new TextBlock(headerTime)]),
                    ]),
                new Container(Axis.Horizontal, height: SizeConstraint.Flex(1), children: [
                    new BorderBox(title: " Nav ",
                        body: new TextBlock("NAVLIST"),
                        style: Style.Default.Border(Borders.Thick)
                    ) { Width = SizeConstraint.Fixed(20) },
                    new BorderBox(title: " CPU ",
                        body: new TextBlock(mainText),
                        style: Style.Default.Border(Borders.Rounded)
                    ),
                ]),
                new Container(Axis.Horizontal,
                    height: SizeConstraint.Fixed(1),
                    style: Style.Default.Background(Color.FromHex("#1C1C1C")).Foreground(Color.BrightWhite),
                    children: [new TextBlock("STATUSBAR")]),
            ]);

        var ctx = new RenderContext(new Region(0, 0, W, H), theme, ColorProfile.TrueColor,
            LayoutEngine.Resolve(new TextBlock(""), W, H));

        // Frame 1
        var f1 = MakeFrame("CPU_TICK_ONE", "12:00:00 ");
        var frame1 = ViewDescriptor.From(f1, existingCtx: ctx, width: W, height: H, theme: theme);

        // Frame 2 (tick)
        var f2 = MakeFrame("CPU_TICK_TWO", "12:00:01 ");
        var frame2 = ViewDescriptor.From(f2, existingCtx: ctx, width: W, height: H, theme: theme);

        // Build terminal state: full frame1 then apply frame2 diff
        var grid = DecodeAnsiToGrid(frame1.Content, W, H);
        ApplyDiff(grid, frame2.Content, W, H);

        var (r1, c1) = FindText(grid, "CPU_TICK_TWO");
        var (r2, c2) = FindText(grid, "CPU_TICK_ONE");
        var (rH, _)  = FindText(grid, "12:00:01");
        var (rS, _)  = FindText(grid, "STATUSBAR");
        var (rN, cN) = FindText(grid, "NAVLIST");

        Assert.True(r1 >= 0, "CPU_TICK_TWO not in grid after frame2");
        Assert.True(r2 < 0,  $"CPU_TICK_ONE still in grid after frame2 at row={r2} col={c2} — stale");
        Assert.True(rH >= 0, "12:00:01 not in header");
        Assert.Equal(0,     rH);  // header row 0
        Assert.Equal(H - 1, rS);  // status bar on last row
        Assert.True(cN >= 1 && cN < 20, $"NAVLIST at col {cN}, expected inside sidebar");
        Assert.True(c1 >= 21, $"CPU_TICK_TWO at col {c1}, expected inside main pane (>= 21)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Decode an ANSI escape sequence string into a plain-text grid (all escapes stripped).
    /// Cursor movement sequences (\x1b[row;colH) reposition the write head.
    /// </summary>
    private static char[,] DecodeAnsiToGrid(string ansi, int width, int height)
    {
        var grid = new char[height, width];
        for (int r = 0; r < height; r++)
            for (int c = 0; c < width; c++)
                grid[r, c] = ' ';

        int row = 0, col = 0, i = 0;
        while (i < ansi.Length)
        {
            if (ansi[i] == '\x1b' && i + 1 < ansi.Length && ansi[i + 1] == '[')
            {
                int j = i + 2;
                while (j < ansi.Length && (ansi[j] < 0x40 || ansi[j] > 0x7E)) j++;
                if (j < ansi.Length)
                {
                    if (ansi[j] == 'H')
                    {
                        var param = ansi[(i + 2)..j];
                        var parts = param.Split(';');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out int r1) &&
                            int.TryParse(parts[1], out int c1))
                        { row = r1 - 1; col = c1 - 1; }
                    }
                    i = j + 1;
                }
                else { i++; }
            }
            else if (ansi[i] == '\x1b')
            {
                int j = i + 1;
                while (j < ansi.Length && ansi[j] < 'A') j++;
                i = j + 1;
            }
            else
            {
                char ch = ansi[i];
                if (ch == '\n') { row++; col = 0; }
                else if (ch == '\r') { col = 0; }
                else
                {
                    if (row >= 0 && row < height && col >= 0 && col < width)
                        grid[row, col] = ch;
                    col++;
                }
                i++;
            }
        }
        return grid;
    }

    /// <summary>Apply an ANSI diff on top of an existing grid (in place).</summary>
    private static void ApplyDiff(char[,] grid, string ansiDiff, int width, int height)
    {
        int row = 0, col = 0, i = 0;
        while (i < ansiDiff.Length)
        {
            if (ansiDiff[i] == '\x1b' && i + 1 < ansiDiff.Length && ansiDiff[i + 1] == '[')
            {
                int j = i + 2;
                while (j < ansiDiff.Length && (ansiDiff[j] < 0x40 || ansiDiff[j] > 0x7E)) j++;
                if (j < ansiDiff.Length)
                {
                    if (ansiDiff[j] == 'H')
                    {
                        var param = ansiDiff[(i + 2)..j];
                        var parts = param.Split(';');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out int r1) &&
                            int.TryParse(parts[1], out int c1))
                        { row = r1 - 1; col = c1 - 1; }
                    }
                    i = j + 1;
                }
                else { i++; }
            }
            else if (ansiDiff[i] == '\x1b')
            {
                int j = i + 1;
                while (j < ansiDiff.Length && ansiDiff[j] < 'A') j++;
                i = j + 1;
            }
            else
            {
                char ch = ansiDiff[i];
                if (ch == '\n') { row++; col = 0; }
                else if (ch == '\r') { col = 0; }
                else
                {
                    if (row >= 0 && row < height && col >= 0 && col < width)
                        grid[row, col] = ch;
                    col++;
                }
                i++;
            }
        }
    }

    /// <summary>Find a text string in the grid. Returns (row, col) of first char, or (-1,-1) if not found.</summary>
    private static (int row, int col) FindText(char[,] grid, string text)
    {
        int h = grid.GetLength(0), w = grid.GetLength(1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c <= w - text.Length; c++)
            {
                bool match = true;
                for (int k = 0; k < text.Length; k++)
                    if (grid[r, c + k] != text[k]) { match = false; break; }
                if (match) return (r, c);
            }
        }
        return (-1, -1);
    }
}
