using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;
using ConsoleForge.Testing;
using ConsoleForge.Widgets;

namespace ConsoleForge.Tests.Rendering;

/// <summary>
/// Pixel graphics must not outlive the application that drew them.
/// </summary>
/// <remarks>
/// Images are not cells, so exiting the alternate screen does not necessarily remove them.
/// Under tmux the payloads are passed through to the outer terminal, whose screen tmux does
/// not model and will never repaint over — so nothing cleans them up unless the application
/// does it on the way out.
/// </remarks>
public class ImageTeardownTests
{
    private static byte[] Png() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x07,
    ];

    private sealed record ImageModel(byte[] Data) : IModel
    {
        public ICmd? Init() => null;

        public (IModel Model, ICmd? Cmd) Update(IMsg msg) =>
            msg is KeyMsg { Key: ConsoleKey.Q } ? (this, Cmd.Quit()) : (this, null);

        public IWidget View() =>
            new ImageWidget(Data, new TerminalCapabilities { SupportsKittyGraphics = true })
            {
                Width  = SizeConstraint.Fixed(10),
                Height = SizeConstraint.Fixed(5),
            };
    }

    [Fact]
    public async Task QuittingDeletesThePlacementsItDrew()
    {
        var terminal = new VirtualTerminal(40, 12);
        var run = App.Run(new ImageModel(Png()), terminal, Theme.Dark, targetFps: 30);

        await Task.Delay(150);                       // let a frame with the image go out
        terminal.EnqueueKey(new KeyMsg(ConsoleKey.Q, 'q'));
        await Task.WhenAny(run, Task.Delay(2000));

        uint imageId = KittyProtocol.ImageIdFromBytes(Png());
        var written  = string.Concat(terminal.WriteHistory);

        Assert.Contains($"a=p,i={imageId}", written);
        Assert.Contains($"a=d,d=i,i={imageId}", written);
    }

    [Fact]
    public void ARenderContextWithNoImagesHasNothingToClean()
    {
        var root   = new TextBlock("no pictures here");
        var layout = LayoutEngine.Resolve(root, 40, 5);
        var ctx    = new RenderContext(new Region(0, 0, 40, 5), Theme.Dark,
                                       ColorProfile.TrueColor, layout);
        root.Render(ctx);
        ctx.ToAnsiFrame();

        Assert.Null(ctx.BuildRawCleanup());
    }
}
