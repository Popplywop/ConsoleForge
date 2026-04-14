using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Gallery;

// ── Pages ─────────────────────────────────────────────────────────────────────

enum Page
{
    TextBlock,
    Borders,
    Layout,
    List,
    TextInput,
    Styles,
    ProgressBar,
    Spinner,
    Table,
}

// ── Messages ─────────────────────────────────────────────────────────────────

record TickMsg(DateTimeOffset At) : IMsg;

// ── Model ────────────────────────────────────────────────────────────────────

record GalleryModel(
    Page ActivePage,
    // Sidebar list widget — carries its own selection state
    ConsoleForge.Widgets.List NavList,
    // Focus: 0 = sidebar, 1+ = content area
    int FocusIndex,
    // TextInput page
    TextInput Input1,
    TextInput Input2,
    // List page
    int ListPageIndex,
    string LastPicked,
    // ProgressBar page
    double ProgressValue,
    // Spinner page
    int SpinnerFrame,
    // Table page
    int TableSelected,
    // Styles page animation
    int TickCount) : IModel
{
    private static readonly string[] PageNames =
        Enum.GetValues<Page>().Select(p => $"  {p}").ToArray();

    public static GalleryModel Initial() => new(
        ActivePage:    Page.TextBlock,
        NavList:       new ConsoleForge.Widgets.List(PageNames, selectedIndex: 0) { HasFocus = true },
        FocusIndex:    0,
        Input1:        new TextInput(value: "", placeholder: "Type something…"),
        Input2:        new TextInput(value: "pre-filled text", placeholder: ""),
        ListPageIndex: 0,
        LastPicked:    "",
        ProgressValue: 42,
        SpinnerFrame:  0,
        TableSelected: 0,
        TickCount:     0
    );

    public ICmd? Init() => Cmd.Tick(TimeSpan.FromMilliseconds(120), at => new TickMsg(at));

    public (IModel Model, ICmd? Cmd) Update(IMsg msg)
    {
        switch (msg)
        {
            // ── Quit ──────────────────────────────────────────────────────────
            case KeyMsg { Key: ConsoleKey.Escape }:
            case KeyMsg { Key: ConsoleKey.Q, Ctrl: false } when FocusIndex == 0:
                return (this, Cmd.Quit());

            // ── Tab: toggle focus between sidebar (0) and content area (1) ──
            // The framework also sends FocusIndexChangedMsg; we handle Tab here
            // to sync our own FocusIndex and flip HasFocus on the NavList.
            case KeyMsg { Key: ConsoleKey.Tab }:
            {
                var newFocus = FocusIndex == 0 ? 1 : 0;
                var newNav = new ConsoleForge.Widgets.List(NavList.Items, NavList.SelectedIndex) { HasFocus = newFocus == 0 };
                return (this with { FocusIndex = newFocus, NavList = newNav }, null);
            }

            // ── Sidebar navigation (only when sidebar has focus) ──────────────
            case KeyMsg { Key: ConsoleKey.UpArrow } when FocusIndex == 0:
            {
                var newIdx = Math.Max(0, NavList.SelectedIndex - 1);
                var page   = (Page)newIdx;
                return (this with {
                    NavList = new ConsoleForge.Widgets.List(NavList.Items, newIdx) { HasFocus = true },
                    ActivePage = page,
                    FocusIndex = 0
                }, null);
            }

            case KeyMsg { Key: ConsoleKey.DownArrow } when FocusIndex == 0:
            {
                var newIdx = Math.Min(PageNames.Length - 1, NavList.SelectedIndex + 1);
                var page   = (Page)newIdx;
                return (this with {
                    NavList = new ConsoleForge.Widgets.List(NavList.Items, newIdx) { HasFocus = true },
                    ActivePage = page,
                    FocusIndex = 0
                }, null);
            }

            case KeyMsg { Key: ConsoleKey.Enter } when FocusIndex == 0:
            {
                // Enter on sidebar: navigate to page and shift focus to content
                var newNav = new ConsoleForge.Widgets.List(NavList.Items, NavList.SelectedIndex) { HasFocus = false };
                return (this with { NavList = newNav, FocusIndex = 1 }, null);
            }

            // ── ListSelectionChangedMsg from sidebar List widget ───────────────
            case ListSelectionChangedMsg { Source: var src, NewIndex: var newIdx }
                when ReferenceEquals(src, NavList):
            {
                var page = (Page)newIdx;
                return (this with {
                    NavList = new ConsoleForge.Widgets.List(NavList.Items, newIdx) { HasFocus = NavList.HasFocus },
                    ActivePage = page
                }, null);
            }

            // ── Content area: List page ───────────────────────────────────────
            case KeyMsg { Key: ConsoleKey.UpArrow } when FocusIndex == 1 && ActivePage == Page.List:
                return (this with { ListPageIndex = Math.Max(0, ListPageIndex - 1) }, null);

            case KeyMsg { Key: ConsoleKey.DownArrow } when FocusIndex == 1 && ActivePage == Page.List:
                return (this with { ListPageIndex = Math.Min(ListItems.Length - 1, ListPageIndex + 1) }, null);

            case KeyMsg { Key: ConsoleKey.Enter } when FocusIndex == 1 && ActivePage == Page.List:
                return (this with { LastPicked = ListItems[ListPageIndex] }, null);

            // ── Content area: ProgressBar page ───────────────────────────────
            case KeyMsg { Key: ConsoleKey.LeftArrow } when FocusIndex == 1 && ActivePage == Page.ProgressBar:
                return (this with { ProgressValue = Math.Max(0, ProgressValue - 5) }, null);

            case KeyMsg { Key: ConsoleKey.RightArrow } when FocusIndex == 1 && ActivePage == Page.ProgressBar:
                return (this with { ProgressValue = Math.Min(100, ProgressValue + 5) }, null);

            // ── Content area: Table page ──────────────────────────────────────
            case KeyMsg { Key: ConsoleKey.UpArrow } when FocusIndex == 1 && ActivePage == Page.Table:
                return (this with { TableSelected = Math.Max(0, TableSelected - 1) }, null);

            case KeyMsg { Key: ConsoleKey.DownArrow } when FocusIndex == 1 && ActivePage == Page.Table:
                return (this with { TableSelected = Math.Min(TableRows.Count - 1, TableSelected + 1) }, null);

            // ── Content area: TextInput page ──────────────────────────────────
            case KeyMsg keyMsg when FocusIndex == 1 && ActivePage == Page.TextInput:
            {
                IMsg? out1 = null;
                IMsg? out2 = null;
                Input1.OnKeyEvent(keyMsg, m => out1 = m);
                Input2.OnKeyEvent(keyMsg, m => out2 = m);

                var m1 = out1 is TextInputChangedMsg t1
                    ? this with { Input1 = new TextInput(t1.NewValue, Input1.Placeholder, t1.NewCursorPosition) { HasFocus = true } }
                    : this;
                var m2 = out2 is TextInputChangedMsg t2
                    ? m1 with { Input2 = new TextInput(t2.NewValue, Input2.Placeholder, t2.NewCursorPosition) { HasFocus = true } }
                    : m1;
                return (m2, null);
            }

            // ── Tick: spinner animation + styles gradient ─────────────────────
            case TickMsg:
                return (this with {
                    SpinnerFrame = SpinnerFrame + 1,
                    TickCount    = TickCount + 1
                }, Cmd.Tick(TimeSpan.FromMilliseconds(120), at => new TickMsg(at)));

            default:
                return (this, null);
        }
    }

    public IWidget View()
    {
        var sidebar    = BuildSidebar();
        var content    = BuildPageContent();
        var statusBar  = BuildStatusBar();

        var body = new Container(Axis.Horizontal,
            style: Style.Default.Background(Color.FromHex("#1C1C1C")),
            children: [sidebar, content]);
        return new Container(Axis.Vertical,
            style: Style.Default.Background(Color.FromHex("#1C1C1C")),
            children: [body, statusBar]);
    }

    // ── Sidebar ───────────────────────────────────────────────────────────────

    private IWidget BuildSidebar()
    {
        // NavList carries HasFocus so it renders with focus style when active.
        // The BorderBox provides the header title and a consistent border.
        var navBorder = new BorderBox("Widgets",
            body: new Container(Axis.Vertical,
                style: Style.Default.Background(Color.FromHex("#1C1C1C")),
                children: [NavList]),
            style: Style.Default
                .Background(Color.FromHex("#1C1C1C"))
                .Border(Borders.Rounded)
                .BorderForeground(FocusIndex == 0
                    ? Color.FromHex("#00D7FF")
                    : Color.FromHex("#444444")));

        var hint = new Container(Axis.Vertical,
            height: SizeConstraint.Fixed(1),
            style: Style.Default.Background(Color.FromHex("#1C1C1C")),
            children: [
                new TextBlock(FocusIndex == 0 ? " ↑↓ Enter·Tab" : " Tab to focus",
                    style: Style.Default
                        .Background(Color.FromHex("#1C1C1C"))
                        .Foreground(Color.FromHex("#444444")))
            ]);

        return new Container(Axis.Vertical,
            width: SizeConstraint.Fixed(22),
            style: Style.Default.Background(Color.FromHex("#1C1C1C")),
            children: [navBorder, hint]);
    }

    // ── Status bar ────────────────────────────────────────────────────────────

    private IWidget BuildStatusBar()
    {
        var hint = FocusIndex == 0
            ? " ↑↓ Navigate   Enter Select   Tab Focus content   Q/Esc Quit"
            : ActivePage switch
            {
                Page.TextInput   => " Tab Focus sidebar   Esc Quit   (keys → inputs)",
                Page.ProgressBar => " ←→ Adjust value   Tab Focus sidebar   Esc Quit",
                Page.Table       => " ↑↓ Select row   Tab Focus sidebar   Esc Quit",
                Page.List        => " ↑↓ Navigate   Enter Select   Tab Focus sidebar   Esc Quit",
                _                => " Tab Focus sidebar   Esc Quit",
            };

        return new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(1),
            style: Style.Default.Background(Color.FromHex("#1C1C1C")),
            children: [
                new TextBlock(hint,
                    style: Style.Default
                        .Background(Color.FromHex("#1C1C1C"))
                        .Foreground(Color.FromHex("#626262")))
            ]);
    }

    // ── Page dispatch ─────────────────────────────────────────────────────────

    private IWidget BuildPageContent() => ActivePage switch
    {
        Page.TextBlock   => PageTextBlock(),
        Page.Borders     => PageBorders(),
        Page.Layout      => PageLayout(),
        Page.List        => PageList(),
        Page.TextInput   => PageTextInput(),
        Page.Styles      => PageStyles(),
        Page.ProgressBar => PageProgressBar(),
        Page.Spinner     => PageSpinner(),
        Page.Table       => PageTable(),
        _                => new TextBlock("?")
    };

    // ── PAGE: TextBlock ───────────────────────────────────────────────────────

    private static IWidget PageTextBlock()
    {
        var heading = new TextBlock("TextBlock Widget",
            style: Style.Default.Foreground(Color.FromHex("#00D7FF")).Bold());

        var plain = Section("Plain text",
            new TextBlock("A plain TextBlock renders UTF-8 text with no styling."));

        var styled = Section("Styled text",
            new Container(Axis.Vertical, [
                new TextBlock("Bold text",     style: Style.Default.Bold()),
                new TextBlock("Italic text",   style: Style.Default.Italic()),
                new TextBlock("Underlined",    style: Style.Default.Underline()),
                new TextBlock("Strikethrough", style: Style.Default.Strikethrough()),
                new TextBlock("Faint/dim",     style: Style.Default.Faint()),
            ]));

        var colored = Section("Foreground colors",
            new Container(Axis.Vertical, [
                new TextBlock("Red",     style: Style.Default.Foreground(Color.Red)),
                new TextBlock("Green",   style: Style.Default.Foreground(Color.Green)),
                new TextBlock("Yellow",  style: Style.Default.Foreground(Color.Yellow)),
                new TextBlock("Blue",    style: Style.Default.Foreground(Color.Blue)),
                new TextBlock("Magenta", style: Style.Default.Foreground(Color.Magenta)),
                new TextBlock("Cyan",    style: Style.Default.Foreground(Color.Cyan)),
                new TextBlock("TrueColor #FF5733", style: Style.Default.Foreground(Color.FromHex("#FF5733"))),
            ]));

        var backgrounds = Section("Background colors",
            new Container(Axis.Horizontal, [
                Swatch("  Red  ", Color.Red,              Color.BrightWhite),
                Swatch(" Green ", Color.Green,            Color.Black),
                Swatch(" Blue  ", Color.Blue,             Color.BrightWhite),
                Swatch("  Hex  ", Color.FromHex("#5F0087"), Color.BrightWhite),
            ]));

        var multiline = Section("Multi-line text",
            new TextBlock("Line one\nLine two\nLine three — TextBlock\nsplits on embedded \\n characters."));

        return new BorderBox("TextBlock",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                plain, styled, colored, backgrounds, multiline
            ]),
            style: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded));
    }

    // ── PAGE: Borders ─────────────────────────────────────────────────────────

    private static IWidget PageBorders()
    {
        var heading = new TextBlock("BorderBox Widget — all border styles",
            style: Style.Default.Foreground(Color.FromHex("#00D7FF")).Bold());

        IWidget MakeBorderDemo(string label, BorderSpec spec, IColor color) =>
            new Container(Axis.Vertical,
                width: SizeConstraint.Flex(1),
                children: [
                    new BorderBox(label,
                        body: new TextBlock(label,
                            style: Style.Default.Foreground(Color.FromHex("#808080"))),
                        style: Style.Default.Border(spec).BorderForeground(color))
                ]);

        var row1 = new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(5),
            children: [
                MakeBorderDemo("Normal",  Borders.Normal,  Color.White),
                MakeBorderDemo("Rounded", Borders.Rounded, Color.Cyan),
                MakeBorderDemo("Thick",   Borders.Thick,   Color.Yellow),
            ]);

        var row2 = new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(5),
            children: [
                MakeBorderDemo("Double", Borders.Double, Color.Magenta),
                MakeBorderDemo("ASCII",  Borders.ASCII,  Color.Green),
                MakeBorderDemo("Hidden", Borders.Hidden, Color.DarkGray),
            ]);

        var titleDemo = Section("Titled border",
            new BorderBox("My Title Here",
                body: new TextBlock("Titles render in the top edge of the border."),
                style: Style.Default.Border(Borders.Rounded).BorderForeground(Color.FromHex("#00D7FF"))));

        var nestedDemo = Section("Nested borders",
            new BorderBox("Outer",
                body: new BorderBox("Inner",
                    body: new TextBlock("Borders can nest arbitrarily deep."),
                    style: Style.Default.Border(Borders.Thick).BorderForeground(Color.Yellow)),
                style: Style.Default.Border(Borders.Rounded).BorderForeground(Color.Cyan)));

        return new BorderBox("Borders",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [
                    new TextBlock("Six built-in border sets:", style: Style.Default.Foreground(Color.FromHex("#808080")))]),
                row1, row2,
                titleDemo, nestedDemo
            ]),
            style: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded));
    }

    // ── PAGE: Layout ──────────────────────────────────────────────────────────

    private static IWidget PageLayout()
    {
        var heading = new TextBlock("Container Widget — layout engine",
            style: Style.Default.Foreground(Color.FromHex("#00D7FF")).Bold());

        var hFlex = Section("Horizontal — Flex children (equal share)",
            new Container(Axis.Horizontal,
                height: SizeConstraint.Fixed(3),
                children: [
                    ColoredBox("Flex(1)", Color.FromHex("#5F0000"), SizeConstraint.Flex(1)),
                    ColoredBox("Flex(2)", Color.FromHex("#005F00"), SizeConstraint.Flex(2)),
                    ColoredBox("Flex(1)", Color.FromHex("#00005F"), SizeConstraint.Flex(1)),
                ]));

        var hMixed = Section("Horizontal — Fixed(12) + Flex fill",
            new Container(Axis.Horizontal,
                height: SizeConstraint.Fixed(3),
                children: [
                    ColoredBox("Fixed(12)",       Color.FromHex("#5F005F"), SizeConstraint.Fixed(12)),
                    ColoredBox("Flex(1) fills rest", Color.FromHex("#005F5F"), SizeConstraint.Flex(1)),
                ]));

        var vLayout = Section("Vertical — Fixed(2) rows + Flex fill",
            new Container(Axis.Vertical,
                height: SizeConstraint.Fixed(6),
                children: [
                    ColoredBox("Fixed(2) row", Color.FromHex("#3A3A00"), SizeConstraint.Flex(1),
                        heightConstraint: SizeConstraint.Fixed(2)),
                    ColoredBox("Flex fills",  Color.FromHex("#003A3A"), SizeConstraint.Flex(1)),
                ]));

        var nested = Section("Nested containers",
            new Container(Axis.Horizontal,
                height: SizeConstraint.Fixed(5),
                children: [
                    new Container(Axis.Vertical,
                        width: SizeConstraint.Flex(1),
                        children: [
                            ColoredBox("Top-left", Color.FromHex("#3A0000"), SizeConstraint.Flex(1)),
                            ColoredBox("Bot-left", Color.FromHex("#003A00"), SizeConstraint.Flex(1)),
                        ]),
                    new Container(Axis.Vertical,
                        width: SizeConstraint.Flex(1),
                        children: [
                            ColoredBox("Top-right", Color.FromHex("#00003A"), SizeConstraint.Flex(1)),
                            ColoredBox("Bot-right", Color.FromHex("#3A003A"), SizeConstraint.Flex(1)),
                        ]),
                ]));

        return new BorderBox("Layout",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                hFlex, hMixed, vLayout, nested
            ]),
            style: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded));
    }

    // ── PAGE: List ────────────────────────────────────────────────────────────

    private IWidget PageList()
    {
        var heading = new TextBlock("List Widget",
            style: Style.Default.Foreground(Color.FromHex("#00D7FF")).Bold());

        var basicList = new ConsoleForge.Widgets.List(
            items: ListItems,
            selectedIndex: ListPageIndex,
            selectedItemStyle: Style.Default
                .Background(Color.FromHex("#005F87"))
                .Foreground(Color.BrightWhite));

        var pickedText = string.IsNullOrEmpty(LastPicked)
            ? "Press Enter to select an item"
            : $"Last selected: \"{LastPicked}\"";

        var picked = new TextBlock(pickedText,
            style: Style.Default.Foreground(Color.Yellow));

        var customList = new ConsoleForge.Widgets.List(
            items: ["  ◆ Option Alpha", "  ◆ Option Beta", "  ◆ Option Gamma"],
            selectedIndex: 1,
            selectedItemStyle: Style.Default.Foreground(Color.FromHex("#00FF87")).Bold());

        return new BorderBox("List",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                Section("Basic list — ↑↓ to navigate, Enter to select",
                    new Container(Axis.Vertical,
                        height: SizeConstraint.Fixed(ListItems.Length + 1),
                        children: [basicList])),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [picked]),
                Section("Custom selection style",
                    new Container(Axis.Vertical,
                        height: SizeConstraint.Fixed(3),
                        children: [customList])),
            ]),
            style: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded));
    }

    // ── PAGE: TextInput ───────────────────────────────────────────────────────

    private IWidget PageTextInput()
    {
        var heading = new TextBlock("TextInput Widget",
            style: Style.Default.Foreground(Color.FromHex("#00D7FF")).Bold());

        var desc = new TextBlock(
            "TextInput accepts printable characters, Backspace, Delete, and ←→ cursor movement.\n" +
            "Both inputs below receive all keystrokes while this page is focused.",
            style: Style.Default.Foreground(Color.FromHex("#808080")));

        var label1 = new TextBlock("Input 1 (empty placeholder):",
            style: Style.Default.Foreground(Color.Yellow));

        var inputBox1 = new BorderBox(
            body: new TextInput(Input1.Value, Input1.Placeholder, Input1.CursorPosition) { HasFocus = true },
            style: Style.Default.Border(Borders.Rounded).BorderForeground(Color.Cyan));

        var label2 = new TextBlock("Input 2 (pre-filled):",
            style: Style.Default.Foreground(Color.Yellow));

        var inputBox2 = new BorderBox(
            body: new TextInput(Input2.Value, Input2.Placeholder, Input2.CursorPosition) { HasFocus = true },
            style: Style.Default.Border(Borders.Rounded).BorderForeground(Color.Magenta));

        var lengths = new TextBlock(
            $"Input 1 length: {Input1.Value.Length}   Input 2 length: {Input2.Value.Length}",
            style: Style.Default.Foreground(Color.FromHex("#626262")));

        return new BorderBox("TextInput",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(3), children: [desc]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [label1]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(3), children: [inputBox1]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [label2]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(3), children: [inputBox2]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [lengths]),
            ]),
            style: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded));
    }

    // ── PAGE: Styles ──────────────────────────────────────────────────────────

    private IWidget PageStyles()
    {
        var heading = new TextBlock("Style System",
            style: Style.Default.Foreground(Color.FromHex("#00D7FF")).Bold());

        var ansiColors = new[]
        {
            (Color.Black,       "Black"),        (Color.Red,           "Red"),
            (Color.Green,       "Green"),         (Color.Yellow,        "Yellow"),
            (Color.Blue,        "Blue"),          (Color.Magenta,       "Magenta"),
            (Color.Cyan,        "Cyan"),          (Color.White,         "White"),
            (Color.BrightBlack, "BrightBlack"),   (Color.BrightRed,     "BrightRed"),
            (Color.BrightGreen, "BrightGreen"),   (Color.BrightYellow,  "BrightYellow"),
            (Color.BrightBlue,  "BrightBlue"),    (Color.BrightMagenta, "BrightMagenta"),
            (Color.BrightCyan,  "BrightCyan"),    (Color.BrightWhite,   "BrightWhite"),
        };

        var colorSwatches = ansiColors
            .Select(c => new TextBlock($" {c.Item2,-14}",
                style: Style.Default.Foreground(c.Item1)))
            .Cast<IWidget>()
            .ToArray();

        var palette = Section("ANSI 16 colors (foreground)",
            new Container(Axis.Vertical,
                children: [
                    new Container(Axis.Horizontal, height: SizeConstraint.Fixed(1),
                        children: colorSwatches[..8].Select(w =>
                            new Container(Axis.Vertical, width: SizeConstraint.Fixed(16), children: [w])
                        ).Cast<IWidget>().ToArray()),
                    new Container(Axis.Horizontal, height: SizeConstraint.Fixed(1),
                        children: colorSwatches[8..].Select(w =>
                            new Container(Axis.Vertical, width: SizeConstraint.Fixed(16), children: [w])
                        ).Cast<IWidget>().ToArray()),
                ]));

        var gradientChars = Enumerable.Range(0, 40).Select(i =>
        {
            var hue = (i * 9 + TickCount * 5) % 360;
            var (r, g, b) = HsvToRgb(hue, 1.0, 1.0);
            return new TextBlock("█", style: Style.Default.Foreground(Color.FromRgb(r, g, b)));
        }).Cast<IWidget>().ToArray();

        var gradient = Section("TrueColor gradient (animated)",
            new Container(Axis.Horizontal,
                height: SizeConstraint.Fixed(1),
                children: gradientChars));

        var combos = Section("Style combinations",
            new Container(Axis.Vertical, [
                new TextBlock("Bold + Underline + Red",
                    style: Style.Default.Bold().Underline().Foreground(Color.Red)),
                new TextBlock("Italic + Cyan bg",
                    style: Style.Default.Italic().Background(Color.Cyan).Foreground(Color.Black)),
                new TextBlock("Faint + Strikethrough",
                    style: Style.Default.Faint().Strikethrough()),
                new TextBlock("Reverse video (inverts fg/bg)",
                    style: Style.Default.Reverse()),
            ]));

        var inheritDemo = Section("Style inheritance (theme → widget → child)",
            new BorderBox("Outer (BorderFg=Yellow)",
                body: new Container(Axis.Vertical, [
                    new TextBlock("TextBlock inherits base fg from theme"),
                    new TextBlock("Overrides to Green",
                        style: Style.Default.Foreground(Color.Green)),
                ]),
                style: Style.Default.Border(Borders.Rounded).BorderForeground(Color.Yellow)));

        return new BorderBox("Styles",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                palette, gradient, combos, inheritDemo
            ]),
            style: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded));
    }

    // ── PAGE: ProgressBar ─────────────────────────────────────────────────────

    private IWidget PageProgressBar()
    {
        var heading = new TextBlock("ProgressBar Widget",
            style: Style.Default.Foreground(Color.FromHex("#00D7FF")).Bold());

        var hint = new TextBlock("← → to adjust value",
            style: Style.Default.Foreground(Color.FromHex("#626262")));

        var valueLabel = new TextBlock($"Value: {ProgressValue:0}  (← / → to adjust by 5)",
            style: Style.Default.Foreground(Color.Yellow));

        var defaultBar = Section("Default style",
            new ProgressBar(ProgressValue));

        var customFill = Section("Custom fill/empty chars",
            new ProgressBar(ProgressValue,
                fillChar:  '▓',
                emptyChar: '░',
                style: Style.Default.Foreground(Color.FromHex("#00FF87"))));

        var noLabel = Section("No percent label",
            new ProgressBar(ProgressValue, showPercent: false,
                style: Style.Default.Foreground(Color.Cyan)));

        var full = Section("Full  (100%)",
            new ProgressBar(100,
                style: Style.Default.Foreground(Color.Green)));

        var empty = Section("Empty (  0%)",
            new ProgressBar(0,
                style: Style.Default.Foreground(Color.Red)));

        return new BorderBox("ProgressBar",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [hint]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [valueLabel]),
                defaultBar, customFill, noLabel, full, empty
            ]),
            style: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded));
    }

    // ── PAGE: Spinner ─────────────────────────────────────────────────────────

    private IWidget PageSpinner()
    {
        var heading = new TextBlock("Spinner Widget",
            style: Style.Default.Foreground(Color.FromHex("#00D7FF")).Bold());

        var desc = new TextBlock(
            "Spinners self-animate via TickMsg (120ms tick). " +
            "Each row below shows a different frame set.",
            style: Style.Default.Foreground(Color.FromHex("#808080")));

        var braille = Section("Braille frames (default)",
            new Container(Axis.Horizontal, height: SizeConstraint.Fixed(1), children: [
                new Spinner(SpinnerFrame, label: "Loading…"),
            ]));

        var ascii = Section("ASCII frames  (- \\ | /)",
            new Container(Axis.Horizontal, height: SizeConstraint.Fixed(1), children: [
                new Spinner(SpinnerFrame, label: "Working…", frames: Spinner.AsciiFrames,
                    style: Style.Default.Foreground(Color.Yellow)),
            ]));

        var arc = Section("Arc frames",
            new Container(Axis.Horizontal, height: SizeConstraint.Fixed(1), children: [
                new Spinner(SpinnerFrame, label: "Please wait…", frames: Spinner.ArcFrames,
                    style: Style.Default.Foreground(Color.Cyan)),
            ]));

        var noLabel = Section("No label",
            new Container(Axis.Horizontal, height: SizeConstraint.Fixed(1), children: [
                new Spinner(SpinnerFrame,
                    style: Style.Default.Foreground(Color.Magenta)),
                new TextBlock("  ← spinner only",
                    style: Style.Default.Foreground(Color.FromHex("#626262"))),
            ]));

        return new BorderBox("Spinner",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(2), children: [desc]),
                braille, ascii, arc, noLabel
            ]),
            style: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded));
    }

    // ── PAGE: Table ───────────────────────────────────────────────────────────

    private IWidget PageTable()
    {
        var heading = new TextBlock("Table Widget",
            style: Style.Default.Foreground(Color.FromHex("#00D7FF")).Bold());

        var hint = new TextBlock("↑ ↓ to select a row",
            style: Style.Default.Foreground(Color.FromHex("#626262")));

        var table = new Table(
            columns: [
                new TableColumn("Name",       Width: 16),
                new TableColumn("Category",   Width: 12),
                new TableColumn("Status",     Width: 10),
                new TableColumn("Score",      Width: 0),   // flex
            ],
            rows: TableRows,
            selectedIndex: TableSelected,
            headerStyle: Style.Default.Bold().Foreground(Color.FromHex("#00D7FF")),
            rowStyle:    Style.Default.Foreground(Color.BrightWhite));

        var tableWithSep = Section("Default separator (│)",
            new Container(Axis.Vertical,
                height: SizeConstraint.Fixed(TableRows.Count + 3),
                children: [table]));

        // Custom separator
        var tableNoSep = new Table(
            columns: [
                new TableColumn("Language", Width: 14),
                new TableColumn("Paradigm", Width: 14),
                new TableColumn("Year",     Width: 6),
            ],
            rows: [
                ["C#",         "OO / FP",  "2000"],
                ["F#",         "FP / OO",  "2005"],
                ["Rust",       "Systems",  "2015"],
                ["TypeScript", "OO / FP",  "2012"],
            ],
            selectedIndex: -1,
            headerStyle: Style.Default.Bold().Foreground(Color.Yellow))
        {
            Separator = ' '
        };

        var tableCustomSep = Section("Space separator (no lines)",
            new Container(Axis.Vertical,
                height: SizeConstraint.Fixed(4 + 3),
                children: [tableNoSep]));

        return new BorderBox("Table",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [hint]),
                tableWithSep,
                tableCustomSep,
            ]),
            style: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded));
    }

    // ── Shared data ───────────────────────────────────────────────────────────

    private static readonly string[] ListItems =
    [
        "Apple", "Banana", "Cherry", "Date", "Elderberry",
        "Fig", "Grape", "Honeydew", "Kiwi", "Lemon",
    ];

    private static readonly IReadOnlyList<IReadOnlyList<string>> TableRows =
    [
        ["ConsoleForge", "Framework",  "Stable",  "98"],
        ["BenchmarkDotNet", "Testing", "Stable",  "95"],
        ["System.Reactive", "Library", "Stable",  "90"],
        ["Spectre.Console", "UI",      "Stable",  "88"],
        ["Terminal.Gui",    "UI",      "Stable",  "82"],
        ["Avalonia",        "UI",      "Beta",    "79"],
    ];

    // ── Shared helper widgets ─────────────────────────────────────────────────

    private static IWidget Section(string label, IWidget body) =>
        new Container(Axis.Vertical, [
            new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [
                new TextBlock("  " + label,
                    style: Style.Default.Foreground(Color.FromHex("#626262")))
            ]),
            new Container(Axis.Vertical, children: [body]),
            new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [
                new TextBlock("") // blank spacer
            ]),
        ]);

    private static IWidget Swatch(string label, IColor bg, IColor fg) =>
        new TextBlock(label, style: Style.Default.Background(bg).Foreground(fg));

    private static IWidget ColoredBox(string label, IColor bg, SizeConstraint width,
        SizeConstraint? heightConstraint = null) =>
        new Container(Axis.Vertical,
            width: width,
            height: heightConstraint ?? SizeConstraint.Flex(1),
            style: Style.Default.Background(bg),
            children: [
                new TextBlock(label, style: Style.Default.Background(bg).Foreground(Color.BrightWhite))
            ]);

    private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        var m = v - c;
        var (r1, g1, b1) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0),
            2 => (0.0, c, x), 3 => (0.0, x, c),
            4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return ((byte)((r1 + m) * 255), (byte)((g1 + m) * 255), (byte)((b1 + m) * 255));
    }
}

// ── Entry point ───────────────────────────────────────────────────────────────

static class EntryPoint
{
    static async Task Main()
    {
        var theme = new Theme(
            name: "Gallery",
            baseStyle: Style.Default.Foreground(Color.BrightWhite),
            borderStyle: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded),
            focusedStyle: Style.Default.BorderForeground(Color.Yellow)
        );

        await Program.Run(GalleryModel.Initial(), theme: theme);
    }
}
