using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.TodoApp;

// ── Domain ───────────────────────────────────────────────────────────────────

record TodoItem(string Text, bool Done);

enum AppMode { Browse, Add }

// ── Messages ─────────────────────────────────────────────────────────────────

record AddTodoMsg(string Text) : IMsg;
record ToggleTodoMsg(int Index) : IMsg;
record DeleteTodoMsg(int Index) : IMsg;
record SetModeMsg(AppMode Mode) : IMsg;

// ── Model ────────────────────────────────────────────────────────────────────

record TodoModel(
    IReadOnlyList<TodoItem> Items,
    int SelectedIndex,
    AppMode Mode,
    TextInput Input) : IModel
{
    public static TodoModel Initial() => new(
        Items: [
            new TodoItem("Buy groceries", false),
            new TodoItem("Read the ConsoleForge docs", true),
            new TodoItem("Write a TUI app", false),
        ],
        SelectedIndex: 0,
        Mode: AppMode.Browse,
        Input: new TextInput(placeholder: "New todo…")
    );

    public ICmd? Init() => null;

    public (IModel Model, ICmd? Cmd) Update(IMsg msg)
    {
        switch (msg)
        {
            // ── Quit ─────────────────────────────────────────────
            case KeyMsg { Key: ConsoleKey.Q } when Mode == AppMode.Browse:
                return (this, Cmd.Quit());

            // ── Enter add mode ───────────────────────────────────
            case KeyMsg { Key: ConsoleKey.A } when Mode == AppMode.Browse:
                return (this with { Mode = AppMode.Add }, null);

            // ── Escape: cancel add ───────────────────────────────
            case KeyMsg { Key: ConsoleKey.Escape } when Mode == AppMode.Add:
                return (this with
                {
                    Mode = AppMode.Browse,
                    Input = new TextInput(placeholder: "New todo…")
                }, null);

            // ── Confirm add ──────────────────────────────────────
            case KeyMsg { Key: ConsoleKey.Enter } when Mode == AppMode.Add:
            {
                if (string.IsNullOrWhiteSpace(Input.Value))
                    return (this with { Mode = AppMode.Browse, Input = new TextInput(placeholder: "New todo…") }, null);

                var newItems = Items.Append(new TodoItem(Input.Value.Trim(), false)).ToArray();
                return (this with
                {
                    Items = newItems,
                    SelectedIndex = newItems.Length - 1,
                    Mode = AppMode.Browse,
                    Input = new TextInput(placeholder: "New todo…")
                }, null);
            }

            // ── Text input keystrokes ────────────────────────────
            case TextInputChangedMsg m when Mode == AppMode.Add:
                return (this with
                {
                    Input = new TextInput(m.NewValue, "New todo…", m.NewCursorPosition)
                }, null);

            case KeyMsg keyMsg when Mode == AppMode.Add:
            {
                // Route key to the input widget
                IMsg? dispatched = null;
                Input.OnKeyEvent(keyMsg, m => dispatched = m);
                if (dispatched is not null)
                    return Update(dispatched);
                return (this, null);
            }

            // ── Navigation ───────────────────────────────────────
            case KeyMsg { Key: ConsoleKey.UpArrow } when Mode == AppMode.Browse:
                return (this with { SelectedIndex = Math.Max(0, SelectedIndex - 1) }, null);

            case KeyMsg { Key: ConsoleKey.DownArrow } when Mode == AppMode.Browse:
                return (this with { SelectedIndex = Math.Min(Items.Count - 1, SelectedIndex + 1) }, null);

            // ── Toggle done (Space) ──────────────────────────────
            case KeyMsg { Key: ConsoleKey.Spacebar } when Mode == AppMode.Browse && Items.Count > 0:
            {
                var item = Items[SelectedIndex];
                var updated = Items.Select((t, i) =>
                    i == SelectedIndex ? t with { Done = !t.Done } : t
                ).ToArray();
                return (this with { Items = updated }, null);
            }

            // ── Delete (D) ───────────────────────────────────────
            case KeyMsg { Key: ConsoleKey.D } when Mode == AppMode.Browse && Items.Count > 0:
            {
                var updated = Items.Where((_, i) => i != SelectedIndex).ToArray();
                var newSel = Math.Clamp(SelectedIndex, 0, Math.Max(0, updated.Length - 1));
                return (this with { Items = updated, SelectedIndex = newSel }, null);
            }

            default:
                return (this, null);
        }
    }

    public IWidget View()
    {
        var doneStyle   = Style.Default.Foreground(Color.Green);
        var todoStyle   = Style.Default;
        var selStyle    = Style.Default.Background(Color.Blue).Foreground(Color.White);
        var selDoneStyle = Style.Default.Background(Color.Blue).Foreground(Color.Green);

        // Build list items: checkbox prefix + text
        var listItems = Items.Select((item, i) =>
        {
            var prefix = item.Done ? "[x] " : "[ ] ";
            return prefix + item.Text;
        }).ToArray();

        var listWidget = new List(
            items: listItems,
            selectedIndex: SelectedIndex,
            selectedItemStyle: Items.Count > 0 && Items[SelectedIndex].Done
                ? selDoneStyle
                : selStyle
        );

        // Status bar — Container carries the background so it fills the full row width,
        // then the TextBlock renders the text on top of it.
        var statusBg = Mode == AppMode.Browse ? Color.Blue : Color.DarkGray;
        var status = Mode == AppMode.Browse
            ? " ↑↓ Navigate   Space Toggle   A Add   D Delete   Q Quit"
            : " Type new todo — Enter to add, Esc to cancel";

        var statusWidget = new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(1),
            style: Style.Default.Background(statusBg),
            children: [
                new TextBlock(status,
                    style: Style.Default.Background(statusBg).Foreground(Color.White))
            ]);

        // Input area (only visible in Add mode, but always present to keep layout stable)
        var promptText = Mode == AppMode.Add ? "> " : "  ";
        var promptWidget = new TextBlock(
            promptText,
            style: Style.Default.Foreground(Color.Yellow)
        );

        // We render the input inline: prompt + input side by side in a horizontal container
        var inputRow = new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(1),
            children: [
                new Container(Axis.Vertical,
                    width: SizeConstraint.Fixed(2),
                    children: [promptWidget]),
                new Container(Axis.Vertical,
                    width: SizeConstraint.Flex(1),
                    children: [new TextInput(Input.Value, Input.Placeholder, Input.CursorPosition) { HasFocus = Mode == AppMode.Add }])
            ]
        );

        // Empty-state message
        IWidget bodyWidget = Items.Count > 0
            ? listWidget
            : new TextBlock("No todos yet! Press A to add one.",
                  style: Style.Default.Foreground(Color.DarkGray));

        var content = new Container(Axis.Vertical, [
            // Main list area fills available space
            new Container(Axis.Vertical,
                height: SizeConstraint.Flex(1),
                children: [bodyWidget]),
            // Divider
            new TextBlock(new string('─', 1),
                style: Style.Default.Foreground(Color.DarkGray)),
            // Input row (fixed 1 line)
            inputRow,
            // Status bar (fixed 1 line, background spans full inner width)
            statusWidget,
        ]);

        var header = Mode == AppMode.Add ? "Todo List — Add Mode" : "Todo List";
        var root = new BorderBox(
            title: header,
            body: content,
            style: Style.Default.BorderForeground(
                Mode == AppMode.Add ? Color.Yellow : Color.Cyan)
        );

        return root;
    }
}

// ── Entry point ───────────────────────────────────────────────────────────────

static class EntryPoint
{
    static void Main()
    {
        var theme = new Theme(
            name: "TodoApp",
            baseStyle: Style.Default.Foreground(Color.White),
            borderStyle: Style.Default.BorderForeground(Color.Cyan),
            focusedStyle: Style.Default.BorderForeground(Color.Yellow)
        );

        Program.Run(TodoModel.Initial(), theme: theme);
    }
}
