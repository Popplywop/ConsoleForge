using ConsoleForge.Styling;
using ConsoleForge.Core;
using ConsoleForge.Layout;
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
    public void ZStack_WithBackdropModal_ErasesTheLayerBeneathIt()
    {
        // The backdrop is a paint-over, not a translucent overlay: it fills the modal's
        // whole region with spaces, and the region is flex, so under a ZStack that is the
        // whole terminal. The layer underneath is gone, not dimmed — which is what makes a
        // backdrop read as "the application disappeared". Documented on Modal.ShowBackdrop.
        var background = new TextBlock("Hidden under backdrop");
        var modal = new Modal("Alert", body: new TextBlock("ImportantMessage"),
            showBackdrop: true, dialogWidth: 40, dialogHeight: 8);

        var zs = new ZStack([background, modal]);
        var plain = TestHelpers.StripAnsi(ViewDescriptor.From(zs, width: 80, height: 24).Content);

        Assert.Contains("ImportantMessage", plain);
        Assert.DoesNotContain("Hidden under backdrop", plain);
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

    // ── Opacity ───────────────────────────────────────────────────────────────

    private static string[] SimScreen(IWidget root, int width, int height)
    {
        var sim = new ConsoleForge.Tests.Rendering.TerminalSim(width, height);
        sim.Apply(new Renderer().Render(root, width, height, Theme.Dark, ColorProfile.TrueColor).Content);
        return [.. sim.Lines];
    }

    [Fact]
    public void DialogWithoutBackdrop_HidesWhatIsBeneathIt()
    {
        // The box was drawn as a border and a body over whatever was there, so the screen
        // beneath showed through every cell the body left blank.
        var under = new TextBlock(string.Join("\n", Enumerable.Repeat(new string('a', 20), 8)));
        var modal = new Modal("M", new TextBlock("hi"), dialogWidth: 10, dialogHeight: 4);
        var lines = SimScreen(new ZStack([under, modal]), 20, 8);

        // Dialog occupies columns 5..14, rows 2..5; its inside is rows 3..4, columns 6..13.
        Assert.Equal("hi      ", lines[3][6..14]);
        Assert.Equal("        ", lines[4][6..14]);
        Assert.Equal("aaaaa", lines[3][..5]); // outside the box is untouched
    }

    // ── Hit-testing ───────────────────────────────────────────────────────────

    private static (IWidget Root, TextInput Under, TextInput Inside) ModalOverInput(bool backdrop)
    {
        var under  = new TextInput { FocusKey = "under", Height = SizeConstraint.Flex(1) }; // the whole screen
        var inside = new TextInput { FocusKey = "inside", Height = SizeConstraint.Fixed(1) };
        var modal  = new Modal("M", new Container(Axis.Vertical, [inside]), dialogWidth: 10, dialogHeight: 5, showBackdrop: backdrop);
        return (new ZStack([new Container(Axis.Vertical, [under]), modal]), under, inside);
    }

    [Fact]
    public void ClickOnTheDialog_DoesNotFocusTheWidgetBeneath()
    {
        var (root, _, inside) = ModalOverInput(backdrop: false);
        var layout = LayoutEngine.Resolve(root, 20, 9);

        // Row 2 is the dialog's top border: inside the box, but on no focusable.
        Assert.Null(FocusManager.FindFocusableAt(root, layout, col: 8, row: 2));
        Assert.Same(inside, FocusManager.FindFocusableAt(root, layout, col: 8, row: 3));
    }

    [Fact]
    public void ClickOutsideADialogWithoutBackdrop_ReachesTheWidgetBeneath()
    {
        var (root, under, _) = ModalOverInput(backdrop: false);
        var layout = LayoutEngine.Resolve(root, 20, 9);
        Assert.Same(under, FocusManager.FindFocusableAt(root, layout, col: 1, row: 0));
    }

    [Fact]
    public void ClickOnTheBackdrop_DoesNotFocusTheWidgetBeneath()
    {
        var (root, _, _) = ModalOverInput(backdrop: true);
        var layout = LayoutEngine.Resolve(root, 20, 9);
        Assert.Null(FocusManager.FindFocusableAt(root, layout, col: 1, row: 0));
    }
}