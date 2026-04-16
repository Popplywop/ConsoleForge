using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Core;

/// <summary>Unit tests for mouse support: messages, SGR parsing helpers, and click-to-focus.</summary>
public class MouseTests
{
    // ── MouseMsg construction ────────────────────────────────────────────────

    [Fact]
    public void MouseMsg_DefaultModifiers_AreFalse()
    {
        var msg = new MouseMsg(MouseButton.Left, MouseAction.Press, 10, 5);
        Assert.False(msg.Shift);
        Assert.False(msg.Alt);
        Assert.False(msg.Ctrl);
    }

    [Fact]
    public void MouseMsg_ExplicitModifiers_Stored()
    {
        var msg = new MouseMsg(MouseButton.Right, MouseAction.Release, 0, 0,
            Shift: true, Alt: false, Ctrl: true);
        Assert.True(msg.Shift);
        Assert.False(msg.Alt);
        Assert.True(msg.Ctrl);
    }

    [Fact]
    public void MouseMsg_Coordinates_ZeroBased()
    {
        var msg = new MouseMsg(MouseButton.Left, MouseAction.Press, 79, 23);
        Assert.Equal(79, msg.Col);
        Assert.Equal(23, msg.Row);
    }

    [Fact]
    public void MouseMsg_IsIMsg()
    {
        IMsg msg = new MouseMsg(MouseButton.ScrollUp, MouseAction.Press, 0, 0);
        Assert.NotNull(msg);
    }

    // ── MouseButton / MouseAction enum coverage ───────────────────────────────

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Middle)]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.ScrollUp)]
    [InlineData(MouseButton.ScrollDown)]
    [InlineData(MouseButton.None)]
    public void MouseButton_AllValues_Defined(MouseButton btn)
    {
        Assert.True(Enum.IsDefined(btn));
    }

    [Theory]
    [InlineData(MouseAction.Press)]
    [InlineData(MouseAction.Release)]
    [InlineData(MouseAction.Move)]
    public void MouseAction_AllValues_Defined(MouseAction action)
    {
        Assert.True(Enum.IsDefined(action));
    }

    // ── FocusManager.FindFocusableAt ──────────────────────────────────────────

    [Fact]
    public void FindFocusableAt_ClickInsideWidget_ReturnsIt()
    {
        // Two side-by-side inputs: left 0-39, right 40-79 (width 80)
        var left  = new TextInput("left");
        var right = new TextInput("right");
        var root  = new Container(Axis.Horizontal, [left, right]);

        var layout = LayoutEngine.Resolve(root, 80, 1);

        // Click at col 10, row 0 → should hit left input
        var hit = FocusManager.FindFocusableAt(root, layout, col: 10, row: 0);
        Assert.Same(left, hit);
    }

    [Fact]
    public void FindFocusableAt_ClickOnRightSide_ReturnsRightWidget()
    {
        var left  = new TextInput("left");
        var right = new TextInput("right");
        var root  = new Container(Axis.Horizontal, [left, right]);

        var layout = LayoutEngine.Resolve(root, 80, 1);

        var hit = FocusManager.FindFocusableAt(root, layout, col: 60, row: 0);
        Assert.Same(right, hit);
    }

    [Fact]
    public void FindFocusableAt_ClickOutsideAllWidgets_ReturnsNull()
    {
        var input = new TextInput("hi");
        var root  = new Container(Axis.Vertical,
            [input],
            height: SizeConstraint.Fixed(1));

        var layout = LayoutEngine.Resolve(root, 80, 24);

        // Click on row 5, which is outside the input's single row
        var hit = FocusManager.FindFocusableAt(root, layout, col: 10, row: 5);
        Assert.Null(hit);
    }

    [Fact]
    public void FindFocusableAt_NoFocusableWidgets_ReturnsNull()
    {
        var root   = new TextBlock("no inputs here");
        var layout = LayoutEngine.Resolve(root, 80, 1);
        var hit    = FocusManager.FindFocusableAt(root, layout, col: 0, row: 0);
        Assert.Null(hit);
    }

    [Fact]
    public void FindFocusableAt_WidgetInsideBorderBox_Found()
    {
        var input = new TextInput("inside box");
        var box   = new BorderBox(body: input);
        var layout = LayoutEngine.Resolve(box, 40, 5);

        // Inner region starts at col 1, row 1 (inside the border)
        var hit = FocusManager.FindFocusableAt(box, layout, col: 5, row: 2);
        Assert.Same(input, hit);
    }

    [Fact]
    public void FindFocusableAt_VerticalStack_CorrectRow()
    {
        var top    = new TextInput("top");
        var bottom = new TextInput("bottom");
        var root   = new Container(Axis.Vertical,
            [top, bottom],
            height: SizeConstraint.Fixed(2));

        var layout = LayoutEngine.Resolve(root, 40, 2);

        // top is row 0, bottom is row 1
        Assert.Same(top,    FocusManager.FindFocusableAt(root, layout, col: 5, row: 0));
        Assert.Same(bottom, FocusManager.FindFocusableAt(root, layout, col: 5, row: 1));
    }

    // ── Scroll wheel helpers ──────────────────────────────────────────────────

    [Fact]
    public void ScrollUp_Msg_HasCorrectButton()
    {
        var msg = new MouseMsg(MouseButton.ScrollUp, MouseAction.Press, 0, 0);
        Assert.Equal(MouseButton.ScrollUp, msg.Button);
        Assert.Equal(MouseAction.Press, msg.Action);
    }

    [Fact]
    public void ScrollDown_Msg_HasCorrectButton()
    {
        var msg = new MouseMsg(MouseButton.ScrollDown, MouseAction.Press, 0, 0);
        Assert.Equal(MouseButton.ScrollDown, msg.Button);
    }

    // ── VirtualTerminal mouse injection ───────────────────────────────────────

    [Fact]
    public void VirtualTerminal_EnqueueMouse_CanBeConsumed()
    {
        using var vt = new ConsoleForge.Testing.VirtualTerminal();
        ConsoleForge.Terminal.InputEvent? received = null;
        vt.Input.Subscribe(ev => received = ev);

        var msg = new MouseMsg(MouseButton.Left, MouseAction.Press, 10, 5);
        vt.EnqueueMouse(msg);

        Assert.NotNull(received);
        var mouseEv = Assert.IsType<ConsoleForge.Terminal.MouseInputEvent>(received);
        Assert.Equal(MouseButton.Left,  mouseEv.Mouse.Button);
        Assert.Equal(MouseAction.Press, mouseEv.Mouse.Action);
        Assert.Equal(10, mouseEv.Mouse.Col);
        Assert.Equal(5,  mouseEv.Mouse.Row);
    }

    [Fact]
    public void VirtualTerminal_EnqueueMouse_ScrollWheel_Delivered()
    {
        using var vt = new ConsoleForge.Testing.VirtualTerminal();
        ConsoleForge.Terminal.MouseInputEvent? received = null;
        vt.Input.Subscribe(ev => received = ev as ConsoleForge.Terminal.MouseInputEvent);

        vt.EnqueueMouse(new MouseMsg(MouseButton.ScrollDown, MouseAction.Press, 0, 0));

        Assert.NotNull(received);
        Assert.Equal(MouseButton.ScrollDown, received!.Mouse.Button);
    }
}
