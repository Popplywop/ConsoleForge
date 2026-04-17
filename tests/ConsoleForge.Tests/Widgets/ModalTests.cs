using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Widgets;

/// <summary>Unit tests for <see cref="Modal"/>.</summary>
public class ModalTests
{
    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_Defaults_AreReasonable()
    {
        var modal = new Modal();
        Assert.Equal("",  modal.Title);
        Assert.Null(modal.Body);
        Assert.Equal(60,  modal.DialogWidth);
        Assert.Equal(16,  modal.DialogHeight);
        Assert.False(modal.ShowBackdrop);
    }

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var body = new TextBlock("content");
        var modal = new Modal("My Dialog", body, dialogWidth: 40, dialogHeight: 10, showBackdrop: true);
        Assert.Equal("My Dialog", modal.Title);
        Assert.Same(body, modal.Body);
        Assert.Equal(40,  modal.DialogWidth);
        Assert.Equal(10,  modal.DialogHeight);
        Assert.True(modal.ShowBackdrop);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_TitleAppearsInOutput()
    {
        var modal = new Modal("Confirm Delete");
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(modal, width: 80, height: 24).Content);
        Assert.Contains("Confirm Delete", plain);
    }

    [Fact]
    public void Render_BodyContentAppearsInOutput()
    {
        var modal = new Modal("Test", body: new TextBlock("ModalBodyText"));
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(modal, width: 80, height: 24).Content);
        Assert.Contains("ModalBodyText", plain);
    }

    [Fact]
    public void Render_NullBody_DoesNotThrow()
    {
        var modal = new Modal("Empty");
        var ex = Record.Exception(() => ViewDescriptor.From(modal, width: 80, height: 24));
        Assert.Null(ex);
    }

    [Fact]
    public void Render_DialogClampedToTerminalSize()
    {
        // Terminal smaller than requested dialog — should clamp, not throw
        var modal = new Modal("Title", dialogWidth: 200, dialogHeight: 100);
        var ex = Record.Exception(() => ViewDescriptor.From(modal, width: 40, height: 10));
        Assert.Null(ex);
    }

    [Fact]
    public void Render_WithBackdrop_DoesNotThrow()
    {
        var modal = new Modal("Alert", showBackdrop: true,
            body: new TextBlock("Are you sure?"));
        var ex = Record.Exception(() => ViewDescriptor.From(modal, width: 80, height: 24));
        Assert.Null(ex);
    }

    [Fact]
    public void Render_Centered_DialogInMiddleOfTerminal()
    {
        // 80-wide terminal, 60-wide dialog → left edge at col 10
        const int W = 80, H = 24;
        var modal = new Modal("Dlg", dialogWidth: 60, dialogHeight: 10,
            body: new TextBlock("CenteredContent"));

        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(modal, width: W, height: H).Content);
        Assert.Contains("CenteredContent", plain);
        Assert.Contains("Dlg", plain); // title present
    }

    [Fact]
    public void Render_ZeroTerminalDimensions_DoesNotThrow()
    {
        var modal = new Modal("X");
        var ex = Record.Exception(() => ViewDescriptor.From(modal, width: 0, height: 0));
        Assert.Null(ex);
    }

    // ── ZStack integration ────────────────────────────────────────────────────

    [Fact]
    public void ZStack_WithModal_BackgroundAndModalBothVisible()
    {
        var background = new TextBlock("BackgroundText");
        var modal = new Modal("Dialog", body: new TextBlock("ModalContent"),
            dialogWidth: 30, dialogHeight: 8);

        var zs = new ZStack([background, modal]);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(zs, width: 80, height: 24).Content);

        Assert.Contains("BackgroundText", plain);
        Assert.Contains("ModalContent",   plain);
    }

    [Fact]
    public void ZStack_WithBackdropModal_ModalContentVisible()
    {
        var background = new TextBlock("Hidden under backdrop");
        var modal = new Modal("Alert", body: new TextBlock("ImportantMessage"),
            showBackdrop: true, dialogWidth: 40, dialogHeight: 8);

        var zs = new ZStack([background, modal]);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(zs, width: 80, height: 24).Content);

        Assert.Contains("ImportantMessage", plain);
    }

    // ── Focus traversal ───────────────────────────────────────────────────────

    [Fact]
    public void FocusManager_TraversesIntoModalBody()
    {
        var input = new TextInput("inside modal");
        var modal = new Modal("Form", body: input);

        var focusable = FocusManager.CollectFocusable(modal);
        Assert.Single(focusable);
        Assert.Same(input, focusable[0]);
    }

    [Fact]
    public void FocusManager_ZStackWithModal_CollectsFromBothLayers()
    {
        var baseInput  = new TextInput("base");
        var modalInput = new TextInput("modal");

        var zs = new ZStack([
            new Container(Axis.Vertical, [baseInput]),
            new Modal("Dlg", body: modalInput),
        ]);

        var focusable = FocusManager.CollectFocusable(zs);
        Assert.Equal(2, focusable.Count);
        Assert.Same(baseInput,  focusable[0]);
        Assert.Same(modalInput, focusable[1]);
    }

    // ── ISingleBodyWidget ─────────────────────────────────────────────────────

    [Fact]
    public void Modal_ImplementsISingleBodyWidget()
    {
        var body = new TextBlock("body");
        IWidget modal = new Modal("T", body);
        Assert.IsAssignableFrom<ISingleBodyWidget>(modal);
        Assert.Same(body, ((ISingleBodyWidget)modal).Body);
    }

    // ── ModalDismissedMsg ─────────────────────────────────────────────────────

    [Fact]
    public void ModalDismissedMsg_IsIMsg()
    {
        IMsg msg = new ModalDismissedMsg();
        Assert.NotNull(msg);
    }
}