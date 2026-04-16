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
    Checkbox,
    Tabs,
    TextArea,
    Modal,
}

// ── Model ────────────────────────────────────────────────────────────────────



record GalleryModel(
    Page ActivePage,
    ConsoleForge.Widgets.List NavList,
    int FocusIndex,
    // TextInput page (uses raw widget state — not converted to component
    // because it routes every keypress directly to the widget's OnKeyEvent)
    TextInput Input1,
    TextInput Input2,
    // Page components — each owns its own state and KeyMap
    ListPageComponent    ListPage,
    ProgressBarComponent ProgressPage,
    CheckboxComponent    CheckboxPage,
    TextAreaComponent    TextAreaPage,
    // Remaining pages (low state complexity — kept as fields)
    int    SpinnerFrame,
    int    TableSelected,
    int    TickCount,
    int    TabsActiveIndex,
    bool   ModalOpen,
    int    ModalChoice,
    // Theme cycling
    int    ThemeIndex) : IModel
{
    private static readonly string[] PageNames =
        Enum.GetValues<Page>().Select(p => p.ToString()).ToArray();

    // Built-in themes available for cycling with T key
    private static readonly Theme[] AllThemes =
    [
        Theme.Dark, Theme.Dracula, Theme.Nord, Theme.Monokai,
        Theme.TokyoNight, Theme.Light, Theme.Default,
    ];

    // ── Theme helpers ───────────────────────────────────────────────────────────────
    private Theme T => AllThemes[ThemeIndex];

    /// <summary>Accent / heading style — theme’s primary colour, bold.</summary>
    private Style Heading  => T.AccentStyle().Bold(true);
    /// <summary>Description / hint text — theme muted colour.</summary>
    private Style Muted    => T.MutedStyle();
    /// <summary>Secondary / supporting text — slightly dimmer than muted.</summary>
    private Style Secondary => T.SecondaryStyle();
    /// <summary>Outer page border — no explicit colour, inherits from theme at render time.</summary>
    private static Style PageBorder => Style.Default.Border(Borders.Rounded);
    /// <summary>Border for a focused pane (sidebar or content).</summary>
    private Style FocusedBorder => Style.Default.Border(Borders.Rounded);
    /// <summary>Border for an unfocused pane.</summary>
    private static Style UnfocusedBorder => Style.Default.Border(Borders.Rounded).Faint(true);
    /// <summary>Root-level fill style — applies theme background to the whole terminal.</summary>
    private Style RootStyle => T.BaseColorStyle();

    public static GalleryModel Initial() => new(
        ActivePage:   Page.TextBlock,
        NavList:      new ConsoleForge.Widgets.List(PageNames, selectedIndex: 0) { HasFocus = true },
        FocusIndex:   0,
        Input1:       new TextInput(value: "", placeholder: "Type something…"),
        Input2:       new TextInput(value: "pre-filled text", placeholder: ""),
        ListPage:     new ListPageComponent(),
        ProgressPage: new ProgressBarComponent(),
        CheckboxPage: new CheckboxComponent(States: [false, true, false]),
        TextAreaPage: new TextAreaComponent(),
        SpinnerFrame: 0,
        TableSelected:   0,
        TickCount:       0,
        TabsActiveIndex: 0,
        ModalOpen:    false,
        ModalChoice:  -1,
        ThemeIndex:   0
    );

    public ICmd? Init() => Cmd.Tick(TimeSpan.FromMilliseconds(120), at => new TickMsg(at));

    // ── KeyMaps: declarative input bindings per context ────────────────────────

    static readonly KeyMap GlobalKeys = new KeyMap()
        .On(ConsoleKey.Tab, () => new ToggleFocusMsg())
        .On(KeyPattern.Plain(ConsoleKey.T), () => new CycleThemeMsg())
        .OnScroll(m => m.Button == MouseButton.ScrollUp
            ? (IMsg)new NavUpMsg() : new NavDownMsg());

    static readonly KeyMap SidebarKeys = new KeyMap()
        .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
        .On(ConsoleKey.DownArrow, () => new NavDownMsg())
        .On(ConsoleKey.Enter,     () => new NavSelectMsg())
        .On(KeyPattern.Plain(ConsoleKey.Q), () => new QuitMsg())
        .On(ConsoleKey.Escape,    () => new QuitMsg());

    static readonly KeyMap ListKeys = new KeyMap()
        .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
        .On(ConsoleKey.DownArrow, () => new NavDownMsg())
        .On(ConsoleKey.Enter,     () => new NavSelectMsg());

    static readonly KeyMap TableKeys = new KeyMap()
        .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
        .On(ConsoleKey.DownArrow, () => new NavDownMsg());

    static readonly KeyMap ProgressBarKeys = new KeyMap()
        .On(ConsoleKey.LeftArrow,  () => new AdjustLeftMsg())
        .On(ConsoleKey.RightArrow, () => new AdjustRightMsg());

    static readonly KeyMap CheckboxKeys = new KeyMap()
        .On(ConsoleKey.UpArrow,   () => new NavUpMsg())
        .On(ConsoleKey.DownArrow, () => new NavDownMsg())
        .On(ConsoleKey.Spacebar,  () => new ToggleCheckboxMsg())
        .On(ConsoleKey.Enter,     () => new ToggleCheckboxMsg());

    static readonly KeyMap TabsKeys = new KeyMap()
        .On(ConsoleKey.LeftArrow,  () => new NavUpMsg())
        .On(ConsoleKey.RightArrow, () => new NavDownMsg());

    static readonly KeyMap ModalClosedKeys = new KeyMap()
        .On(ConsoleKey.Enter, () => new OpenModalMsg());

    static readonly KeyMap ModalOpenKeys = new KeyMap()
        .On(ConsoleKey.Escape, () => new DismissModalMsg())
        .On(ConsoleKey.Y, () => new ModalConfirmMsg())
        .On(ConsoleKey.N, () => new ModalCancelMsg());

    /// <summary>Select the right KeyMap for the current context.</summary>
    private KeyMap ActiveKeyMap()
    {
        if (ModalOpen && ActivePage == Page.Modal) return ModalOpenKeys;

        var contextMap = FocusIndex == 0
            ? SidebarKeys
            : ActivePage switch
            {
                // These pages have their own KeyMaps inside their component
                // — GlobalKeys.Merge handles Tab/T/scroll; the component handles the rest
                Page.List        => new KeyMap(), // ListPageComponent has Keys
                Page.ProgressBar => new KeyMap(), // ProgressBarComponent has Keys
                Page.Checkbox    => new KeyMap(), // CheckboxComponent has Keys
                Page.Table       => TableKeys,
                Page.Tabs        => TabsKeys,
                Page.Modal       => ModalClosedKeys,
                _                => new KeyMap(),
            };

        return GlobalKeys.Merge(contextMap);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public (IModel Model, ICmd? Cmd) Update(IMsg msg)
    {
        // 1. Try KeyMap resolution (replaces the giant switch for key/mouse input)
        var map = ActiveKeyMap();
        if (map.Handle(msg) is { } resolved)
            msg = resolved; // replace input msg with the action msg

        // 2. Handle scroll wheel via KeyMap-compatible method
        if (msg is MouseMsg { Button: MouseButton.ScrollUp or MouseButton.ScrollDown } scrollMsg)
            return HandleScrollWheel(scrollMsg);

        // 3. Theme cycling (suppressed on text-entry pages)
        if (msg is CycleThemeMsg)
        {
            var newIdx   = (ThemeIndex + 1) % AllThemes.Length;
            var newTheme = AllThemes[newIdx];
            // Return ThemeChangedMsg synchronously via Cmd so ProcessMsg intercepts
            // it in the same event-loop tick — no async gap where a render could
            // fire with the old theme and cache stale widget cells.
            return (this with { ThemeIndex = newIdx },
                Cmd.Msg(new ThemeChangedMsg(newTheme)));
        }

        // 4. Dispatch action messages to state handlers
        switch (msg)
        {
            // ── Global ────────────────────────────────────────────────────
            case QuitMsg:
                return (this, Cmd.Quit());

            case DismissModalMsg:
                return (this with { ModalOpen = false }, null);

            case ToggleFocusMsg:
            {
                var newFocus = FocusIndex == 0 ? 1 : 0;
                var newNav = new ConsoleForge.Widgets.List(
                    NavList.Items, NavList.SelectedIndex) { HasFocus = newFocus == 0 };
                return (this with { FocusIndex = newFocus, NavList = newNav }, null);
            }

            // ── Sidebar navigation ────────────────────────────────────────
            case NavUpMsg when FocusIndex == 0:
            {
                var newIdx = Math.Max(0, NavList.SelectedIndex - 1);
                return (this with {
                    NavList = new ConsoleForge.Widgets.List(NavList.Items, newIdx) { HasFocus = true },
                    ActivePage = (Page)newIdx, FocusIndex = 0
                }, null);
            }
            case NavDownMsg when FocusIndex == 0:
            {
                var newIdx = Math.Min(PageNames.Length - 1, NavList.SelectedIndex + 1);
                return (this with {
                    NavList = new ConsoleForge.Widgets.List(NavList.Items, newIdx) { HasFocus = true },
                    ActivePage = (Page)newIdx, FocusIndex = 0
                }, null);
            }
            case NavSelectMsg when FocusIndex == 0:
            {
                var newNav = new ConsoleForge.Widgets.List(
                    NavList.Items, NavList.SelectedIndex) { HasFocus = false };
                return (this with { NavList = newNav, FocusIndex = 1 }, null);
            }

            // List / ProgressBar cases now handled by component delegation (default branch)

            // ── Content: Table ────────────────────────────────────────────
            case NavUpMsg when FocusIndex == 1 && ActivePage == Page.Table:
                return (this with { TableSelected = Math.Max(0, TableSelected - 1) }, null);
            case NavDownMsg when FocusIndex == 1 && ActivePage == Page.Table:
                return (this with { TableSelected = Math.Min(TableRows.Count - 1, TableSelected + 1) }, null);

            // Checkbox cases now handled by component delegation (default branch)

            // ── Content: Tabs ─────────────────────────────────────────────
            case NavUpMsg when FocusIndex == 1 && ActivePage == Page.Tabs:
                return (this with { TabsActiveIndex = TabsActiveIndex <= 0 ? 2 : TabsActiveIndex - 1 }, null);
            case NavDownMsg when FocusIndex == 1 && ActivePage == Page.Tabs:
                return (this with { TabsActiveIndex = (TabsActiveIndex + 1) % 3 }, null);

            // ── Content: Modal ────────────────────────────────────────────
            case OpenModalMsg when FocusIndex == 1 && ActivePage == Page.Modal:
                return (this with { ModalOpen = true }, null);
            case ModalConfirmMsg:
                return (this with { ModalOpen = false, ModalChoice = 0 }, null);
            case ModalCancelMsg:
                return (this with { ModalOpen = false, ModalChoice = 1 }, null);

            // ── Content: TextArea ── delegated to TextAreaComponent ───────────
            // (handled via component delegation below — no inline cases needed)

            // ── Content: TextInput (raw key routing) ──────────────────────
            case KeyMsg keyMsg when FocusIndex == 1 && ActivePage == Page.TextInput:
            {
                IMsg? out1 = null, out2 = null;
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

            // ── Theme key (T) — only reaches here from pages that don't use KeyMap ──
            case KeyMsg { Key: ConsoleKey.T, Ctrl: false }
                when ActivePage != Page.TextInput && ActivePage != Page.TextArea:
                return Update(new CycleThemeMsg());

            // ── ListSelectionChangedMsg from sidebar List widget ───────────
            case ListSelectionChangedMsg { Source: var src, NewIndex: var newIdx }
                when ReferenceEquals(src, NavList):
            {
                var page = (Page)newIdx;
                return (this with {
                    NavList = new ConsoleForge.Widgets.List(
                        NavList.Items, newIdx) { HasFocus = NavList.HasFocus },
                    ActivePage = page
                }, null);
            }

            // ── Focus sync (click-to-focus from Program) ──────────────────
            case FocusIndexChangedMsg { Index: var idx }:
            {
                var newFocus = idx == 0 ? 0 : 1;
                var newNav = new ConsoleForge.Widgets.List(
                    NavList.Items, NavList.SelectedIndex) { HasFocus = newFocus == 0 };
                return (this with { FocusIndex = newFocus, NavList = newNav }, null);
            }

            // ── Tick ──────────────────────────────────────────────────────
            case TickMsg:
                return (this with {
                    SpinnerFrame = SpinnerFrame + 1,
                    TickCount    = TickCount + 1
                }, Cmd.Tick(TimeSpan.FromMilliseconds(120), at => new TickMsg(at)));

            // ── Component delegation ─────────────────────────────────────────────
            // Any message not handled above is forwarded to the active page component.
            default:
                if (FocusIndex == 1)
                {
                    switch (ActivePage)
                    {
                        case Page.List:
                        {
                            var (next, cmd) = Component.Delegate(ListPage, msg);
                            if (next is { } n) return (this with { ListPage = n }, cmd);
                            break;
                        }
                        case Page.ProgressBar:
                        {
                            var (next, cmd) = Component.Delegate(ProgressPage, msg);
                            if (next is { } n) return (this with { ProgressPage = n }, cmd);
                            break;
                        }
                        case Page.Checkbox:
                        {
                            var (next, cmd) = Component.Delegate(CheckboxPage, msg);
                            if (next is { } n) return (this with { CheckboxPage = n }, cmd);
                            break;
                        }
                        case Page.TextArea:
                        {
                            var (next, cmd) = Component.Delegate(TextAreaPage, msg);
                            if (next is { } n) return (this with { TextAreaPage = n }, cmd);
                            break;
                        }
                    }
                }
                return (this, null);
        }
    }

    public IWidget View()
    {
        var sidebar    = BuildSidebar();
        var content    = BuildPageContent();
        var statusBar  = BuildStatusBar();

        var body = new Container(Axis.Horizontal, [sidebar, content]);
        return new Container(Axis.Vertical, [body, statusBar]);
    }

    // ── Sidebar ───────────────────────────────────────────────────────────────

    private IWidget BuildSidebar()
    {
        // NavList carries HasFocus so it renders with focus style when active.
        // The BorderBox fills full height — no external hint row needed.

        var navBorder = new BorderBox("Widgets",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, children: [NavList]),
            ]),
            style: (FocusIndex == 0 ? FocusedBorder : UnfocusedBorder));

        return new Container(Axis.Vertical,
            width: SizeConstraint.Fixed(22),
            children: [navBorder]);
    }

    // ── Status bar ────────────────────────────────────────────────────────────

    private IWidget BuildStatusBar()
    {
        var hint = FocusIndex == 0
            ? " ↑3↓ Navigate   Scroll Navigate   Enter Select   Tab Focus content   T Theme   Q/Esc Quit"
            : ActivePage switch
            {
                Page.TextInput   => " Tab Focus sidebar   T Theme   Esc Quit   (keys → inputs)",
                Page.ProgressBar => " ←→/Scroll Adjust value   T Theme   Tab Focus sidebar   Esc Quit",
                Page.Table       => " ↑↓/Scroll Select row   T Theme   Tab Focus sidebar   Esc Quit",
                Page.List        => " ↑↓/Scroll Navigate   Enter Select   T Theme   Tab Focus sidebar   Esc Quit",
                Page.Checkbox    => " ↑↓/Scroll Move focus   Space/Enter Toggle   T Theme   Tab Focus sidebar   Esc Quit",
                Page.Tabs        => " ←→/Scroll Switch tab   T Theme   Tab Focus sidebar   Esc Quit",
                Page.TextArea    => " Type to edit   Scroll Scroll text   Tab Focus sidebar   Esc Quit",
                Page.Modal       => " Enter Open modal   T Theme   Tab Focus sidebar   Esc Quit",
                _                => " T Theme   Tab Focus sidebar   Esc Quit",
            };

        var themeName = AllThemes[ThemeIndex].Name;
        return new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(1),
            children: [
                new TextBlock(hint, style: Muted),
                new Container(Axis.Vertical,
                    width: SizeConstraint.Fixed(themeName.Length + 3),
                    children: [
                        new TextBlock($" ◆ {themeName}", style: Secondary)
                    ])
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
        Page.Checkbox    => PageCheckbox(),
        Page.Tabs        => PageTabs(),
        Page.TextArea    => PageTextArea(),
        Page.Modal       => PageModal(),
        _                => new TextBlock("?")
    };

    // ── PAGE: TextBlock ───────────────────────────────────────────────────────

    private IWidget PageTextBlock()
    {
        var heading = new TextBlock("TextBlock Widget",
            style: Heading);

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
                new TextBlock("Yellow",  style: T.Warning()),
                new TextBlock("Blue",    style: Style.Default.Foreground(Color.Blue)),
                new TextBlock("Magenta", style: Style.Default.Foreground(Color.Magenta)),
                new TextBlock("Cyan",    style: T.AccentStyle()),
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
            style: PageBorder);
    }

    // ── PAGE: Borders ─────────────────────────────────────────────────────────

    private IWidget PageBorders()
    {
        var heading = new TextBlock("BorderBox Widget — all border styles",
            style: Heading);

        IWidget MakeBorderDemo(string label, BorderSpec spec, IColor color) =>
            new Container(Axis.Vertical,
                width: SizeConstraint.Flex(1),
                children: [
                    new BorderBox(label,
                        body: new TextBlock(label,
                            style: Muted),
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
                style: PageBorder));

        return new BorderBox("Borders",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [
                    new TextBlock("Six built-in border sets:", style: Muted)]),
                row1, row2,
                titleDemo, nestedDemo
            ]),
            style: PageBorder);
    }

    // ── PAGE: Layout ──────────────────────────────────────────────────────────

    private IWidget PageLayout()
    {
        var heading = new TextBlock("Container Widget — layout engine",
            style: Heading);

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
            style: PageBorder);
    }

    // ── PAGE: List ────────────────────────────────────────────────────────────

    private IWidget PageList()
    {
        var heading = new TextBlock("List Widget",
            style: Heading);

        var basicList = new ConsoleForge.Widgets.List(
            items: ListItems,
            selectedIndex: ListPage.SelectedIndex,
            selectedItemStyle: Style.Default.Reverse(true));

        var pickedText = string.IsNullOrEmpty(ListPage.LastPicked)
            ? "Press Enter to select an item"
            : $"Last selected: \"{ListPage.LastPicked}\"";

        var picked = new TextBlock(pickedText,
            style: T.Warning());

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
            style: PageBorder);
    }

    // ── PAGE: TextInput ───────────────────────────────────────────────────────

    private IWidget PageTextInput()
    {
        var heading = new TextBlock("TextInput Widget",
            style: Heading);

        var desc = new TextBlock(
            "TextInput accepts printable characters, Backspace, Delete, and ←→ cursor movement.\n" +
            "Both inputs below receive all keystrokes while this page is focused.",
            style: Muted);

        var label1 = new TextBlock("Input 1 (empty placeholder):",
            style: T.Warning());

        var inputBox1 = new BorderBox(
            body: new TextInput(Input1.Value, Input1.Placeholder, Input1.CursorPosition) { HasFocus = true },
            style: PageBorder);

        var label2 = new TextBlock("Input 2 (pre-filled):",
            style: T.Warning());

        var inputBox2 = new BorderBox(
            body: new TextInput(Input2.Value, Input2.Placeholder, Input2.CursorPosition) { HasFocus = true },
            style: PageBorder);

        var lengths = new TextBlock(
            $"Input 1 length: {Input1.Value.Length}   Input 2 length: {Input2.Value.Length}",
            style: Secondary);

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
            style: PageBorder);
    }

    // ── PAGE: Styles ──────────────────────────────────────────────────────────

    private IWidget PageStyles()
    {
        var heading = new TextBlock("Style System",
            style: Heading);

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
            style: PageBorder);
    }

    // ── PAGE: ProgressBar ─────────────────────────────────────────────────────

    private IWidget PageProgressBar()
    {
        var heading = new TextBlock("ProgressBar Widget",
            style: Heading);

        var hint = new TextBlock("← → to adjust value",
            style: Secondary);

        var valueLabel = new TextBlock($"Value: {ProgressPage.Value:0}  (← / → to adjust by 5)",
            style: T.Warning());

        var defaultBar = Section("Default style",
            new ProgressBar(ProgressPage.Value));

        var customFill = Section("Custom fill/empty chars",
            new ProgressBar(ProgressPage.Value,
                fillChar:  '▓',
                emptyChar: '░',
                style: Style.Default.Foreground(Color.FromHex("#00FF87"))));

        var noLabel = Section("No percent label",
            new ProgressBar(ProgressPage.Value, showPercent: false,
                style: T.AccentStyle()));

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
            style: PageBorder);
    }

    // ── PAGE: Spinner ─────────────────────────────────────────────────────────

    private IWidget PageSpinner()
    {
        var heading = new TextBlock("Spinner Widget",
            style: Heading);

        var desc = new TextBlock(
            "Spinners self-animate via TickMsg (120ms tick). " +
            "Each row below shows a different frame set.",
            style: Muted);

        var braille = Section("Braille frames (default)",
            new Container(Axis.Horizontal, height: SizeConstraint.Fixed(1), children: [
                new Spinner(SpinnerFrame, label: "Loading…"),
            ]));

        var ascii = Section("ASCII frames  (- \\ | /)",
            new Container(Axis.Horizontal, height: SizeConstraint.Fixed(1), children: [
                new Spinner(SpinnerFrame, label: "Working…", frames: Spinner.AsciiFrames,
                    style: T.Warning()),
            ]));

        var arc = Section("Arc frames",
            new Container(Axis.Horizontal, height: SizeConstraint.Fixed(1), children: [
                new Spinner(SpinnerFrame, label: "Please wait…", frames: Spinner.ArcFrames,
                    style: T.AccentStyle()),
            ]));

        var noLabel = Section("No label",
            new Container(Axis.Horizontal, height: SizeConstraint.Fixed(1), children: [
                new Spinner(SpinnerFrame,
                    style: Style.Default.Foreground(Color.Magenta)),
                new TextBlock("  ← spinner only",
                    style: Secondary),
            ]));

        return new BorderBox("Spinner",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(2), children: [desc]),
                braille, ascii, arc, noLabel
            ]),
            style: PageBorder);
    }

    // ── PAGE: Table ───────────────────────────────────────────────────────────

    private IWidget PageTable()
    {
        var heading = new TextBlock("Table Widget",
            style: Heading);

        var hint = new TextBlock("↑ ↓ to select a row",
            style: Secondary);

        var table = new Table(
            columns: [
                new TableColumn("Name",       Width: 16),
                new TableColumn("Category",   Width: 12),
                new TableColumn("Status",     Width: 10),
                new TableColumn("Score",      Width: 0),   // flex
            ],
            rows: TableRows,
            selectedIndex: TableSelected,
            headerStyle: Heading,
            rowStyle:    Style.Default);

        var tableWithSep = Section("Default style (no separators, cell padding)",
            new Container(Axis.Vertical,
                height: SizeConstraint.Fixed(TableRows.Count + 2),
                children: [table]));

        // Custom separator
        var tableSep = new Table(
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
            Separator = '│'
        };

        var tableCustomSep = Section("Explicit │ separator (opt-in)",
            new Container(Axis.Vertical,
                height: SizeConstraint.Fixed(4 + 2),
                children: [tableSep]));

        return new BorderBox("Table",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [hint]),
                tableWithSep,
                tableCustomSep,
            ]),
            style: PageBorder);
    }

    // ── Shared data ───────────────────────────────────────────────────────────

    // ── Mouse scroll helper ─────────────────────────────────────────────────

    private (IModel, ICmd?) HandleScrollWheel(MouseMsg msg)
    {
        bool up   = msg.Button == MouseButton.ScrollUp;
        int  dir  = up ? -1 : 1;

        // ── Sidebar focused: navigate pages ───────────────────────────────
        if (FocusIndex == 0)
        {
            var newIdx  = Math.Clamp(NavList.SelectedIndex + dir, 0, PageNames.Length - 1);
            var newPage = (Page)newIdx;
            return (this with {
                NavList    = new ConsoleForge.Widgets.List(NavList.Items, newIdx) { HasFocus = true },
                ActivePage = newPage,
            }, null);
        }

        // ── Content area: page-specific scroll via component delegation ─────
        IMsg navMsg = dir < 0 ? new NavUpMsg() : new NavDownMsg();
        if (ActivePage == Page.List)
        {
            var (next, cmd) = Component.Delegate(ListPage, navMsg);
            return (this with { ListPage = next! }, cmd);
        }
        if (ActivePage == Page.ProgressBar)
        {
            IMsg adjustMsg = dir < 0 ? new AdjustRightMsg() : new AdjustLeftMsg();
            var (next, cmd) = Component.Delegate(ProgressPage, adjustMsg);
            return (this with { ProgressPage = next! }, cmd);
        }
        if (ActivePage == Page.Checkbox)
        {
            var (next, cmd) = Component.Delegate(CheckboxPage, navMsg);
            return (this with { CheckboxPage = next! }, cmd);
        }
        if (ActivePage == Page.TextArea)
        {
            var (next, cmd) = Component.Delegate(TextAreaPage, navMsg);
            return (this with { TextAreaPage = next! }, cmd);
        }
        return ActivePage switch
        {
            Page.Table => (this with { TableSelected = Math.Clamp(TableSelected + dir, 0, TableRows.Count - 1) }, null),
            Page.Tabs  => (this with { TabsActiveIndex = (TabsActiveIndex + dir + 3) % 3 }, null),
            _          => (this, null),
        };
    }

    private static readonly string[] CheckboxLabels =
        ["Enable dark mode", "Show line numbers", "Auto-save on exit"];

    private IWidget PageCheckbox()
    {
        var heading = new TextBlock("Checkbox Widget",
            style: Heading);
        var desc = new TextBlock(
            "\u2191\u2193 move focus  \u00b7  Space/Enter toggle",
            style: Muted);
        var checkboxWidgets = CheckboxLabels
            .Select((label, i) =>
            {
                var cb = new Checkbox(label, CheckboxPage.ActualStates[i]) { HasFocus = i == CheckboxPage.FocusIdx };
                return (IWidget)new Container(Axis.Vertical,
                    height: SizeConstraint.Fixed(1), children: [cb]);
            })
            .ToArray();
        var summary = new TextBlock(
            $"Enabled: {string.Join(", ", CheckboxLabels.Where((_, i) => CheckboxPage.ActualStates[i]).DefaultIfEmpty("none"))}",
            style: T.Warning());
        var grouped = Section("Feature flags",
            new Container(Axis.Vertical, [.. checkboxWidgets]));
        var staticDemo = Section("Static display (no focus)",
            new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1),
                    children: [new Checkbox("Read-only checked",   isChecked: true)]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1),
                    children: [new Checkbox("Read-only unchecked", isChecked: false)]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1),
                    children: [new Checkbox("Custom (X/-)", isChecked: true, checkedChar: 'X', uncheckedChar: '-')]),
            ]));
        return new BorderBox("Checkbox",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [desc]),
                grouped,
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [summary]),
                staticDemo,
            ]),
            style: PageBorder);
    }

    private IWidget PageTabs()
    {
        var heading = new TextBlock("Tabs Widget",
            style: Heading);
        IWidget tabContent = TabsActiveIndex switch
        {
            0 => new Container(Axis.Vertical, [
                    new TextBlock("Overview Tab", style: Heading),
                    new TextBlock(""),
                    new TextBlock("ConsoleForge is an Elm-inspired TUI framework for .NET."),
                    new TextBlock("Model \u2192 Update \u2192 View architecture."),
                    new TextBlock("Immutable widgets, pure render, async commands."),
                ]),
            1 => new Container(Axis.Vertical, [
                    new TextBlock("API Tab", style: Heading),
                    new TextBlock(""),
                    new TextBlock("IWidget    \u2014 base render contract"),
                    new TextBlock("IFocusable \u2014 interactive widgets"),
                    new TextBlock("IModel     \u2014 application state"),
                    new TextBlock("ICmd       \u2014 async side effects"),
                ]),
            _ => new Container(Axis.Vertical, [
                    new TextBlock("Widgets Tab", style: Heading),
                    new TextBlock(""),
                    new TextBlock("\u2713 TextBlock  \u2713 Container  \u2713 BorderBox"),
                    new TextBlock("\u2713 TextInput  \u2713 TextArea   \u2713 List"),
                    new TextBlock("\u2713 Table      \u2713 ProgressBar \u2713 Spinner"),
                    new TextBlock("\u2713 Checkbox   \u2713 Tabs"),
                ]),
        };
        var tabs = new Tabs(
            labels: ["Overview", "API", "Widgets"],
            activeIndex: TabsActiveIndex,
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [new TextBlock("")]),
                tabContent
            ]),
            activeTabStyle:   Heading.Underline(true),
            inactiveTabStyle: Muted)
        { HasFocus = FocusIndex == 1 };
        var demo = Section("\u2190 \u2192 switch tabs (keys 1\u20133 also work)",
            new Container(Axis.Vertical, height: SizeConstraint.Fixed(12), children: [
                new BorderBox(body: tabs,
                    style: UnfocusedBorder)
            ]));
        return new BorderBox("Tabs",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                demo,
            ]),
            style: PageBorder);
    }

    private IWidget PageTextArea()
    {
        var heading = new TextBlock("TextArea Widget",
            style: Heading);
        var desc = new TextBlock(
            "Multiline editor \u2014 type, \u2191\u2193\u2190\u2192, Enter, Backspace, Delete, Home/End.",
            style: Muted);
        var ta = new ConsoleForge.Widgets.TextArea(
            lines: TextAreaPage.ActualLines,
            cursorRow: TextAreaPage.CursorRow,
            cursorCol: TextAreaPage.CursorCol,
            scrollRow: TextAreaPage.ScrollRow)
        { HasFocus = FocusIndex == 1 };
        var editorBox = new BorderBox(
            body: ta,
            style: (FocusIndex == 1 ? FocusedBorder : UnfocusedBorder));
        var stats = new TextBlock(
            $"Lines: {TextAreaPage.ActualLines.Count}  \u00b7  Cursor: row {TextAreaPage.CursorRow + 1}, col {TextAreaPage.CursorCol + 1}",
            style: Secondary);
        return new BorderBox("TextArea",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [desc]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [new TextBlock("")]),
                editorBox,
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [stats]),
            ]),
            style: PageBorder);
    }

    private IWidget PageModal()
    {
        var heading = new TextBlock("Modal + ZStack Overlay",
            style: Heading);
        var desc = new TextBlock(
            "ZStack renders layers back-to-front. Modal centers a dialog on top of existing content.",
            style: Muted);
        var hint = new TextBlock(
            ModalOpen ? "Escape closes the modal" : "Press Enter to open the modal",
            style: T.Warning());
        var choiceText = ModalChoice switch
        {
            0 => "You chose: Confirm (OK)",
            1 => "You chose: Cancel",
            _ => "No choice made yet",
        };
        var choiceLabel = new TextBlock(choiceText,
            style: T.AccentStyle());

        var background = new BorderBox("Background Content",
            body: new Container(Axis.Vertical, [
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [heading]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(2), children: [desc]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [hint]),
                new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [choiceLabel]),
                new Container(Axis.Vertical, children: [new TextBlock(
                    "This text remains visible when the modal is open (no backdrop).\n" +
                    "The modal box renders on top without dimming the content behind it.",
                    style: Secondary)]),
            ]),
            style: PageBorder);

        if (!ModalOpen) return background;

        var modalBody = new Container(Axis.Vertical, [
            new TextBlock(""),
            new TextBlock("  Are you sure you want to continue?",
                style: Style.Default.Bold(true)),
            new TextBlock(""),
            new TextBlock("  Press Y to confirm, N to cancel, Esc to dismiss.",
                style: Muted),
        ]);

        var modal = new ConsoleForge.Widgets.Modal(
            title: " Confirm Action ",
            body: modalBody,
            dialogWidth: 48,
            dialogHeight: 10,
            showBackdrop: false,
            style: FocusedBorder);

        return new ZStack([background, modal]);
    }

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

    private IWidget Section(string label, IWidget body) =>
        new Container(Axis.Vertical, [
            new Container(Axis.Vertical, height: SizeConstraint.Fixed(1), children: [
                new TextBlock("  " + label,
                    style: Secondary)
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
        // Start on Dark theme (index 0 in GalleryModel.AllThemes).
        // Press T in-app to cycle through Dark → Dracula → Nord → Monokai → Tokyo Night → Light → Default.
        await Program.Run(GalleryModel.Initial(), theme: Theme.Dark, enableMouse: true);
    }
}
