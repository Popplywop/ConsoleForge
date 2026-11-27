using ConsoleForge.Terminal;

namespace ConsoleForge.Tests;

public class TerminalCapabilitiesTests
{
    // KittyProbeResult is internal, and a public test method cannot take it as a parameter.
    [Theory]
    [InlineData(false, (int)KittyProbeResult.Supported, true)]     // a terminal no variable names
    [InlineData(true, (int)KittyProbeResult.Unsupported, false)]   // KITTY_WINDOW_ID inherited over SSH
    [InlineData(true, (int)KittyProbeResult.Unknown, true)]        // no answer: keep the guess
    [InlineData(false, (int)KittyProbeResult.Unknown, false)]
    public void TerminalAnswer_OverridesEnvironment(bool env, int probe, bool expected)
    {
        Assert.Equal(expected, TerminalCapabilities.ResolveKittyGraphics(env, (KittyProbeResult)probe));
    }

    [Fact]
    public void ZeroTimeout_DoesNotAsk()
    {
        // Under the test runner stdin is not a terminal either way; this pins that a zero
        // timeout is the environment-only path, which tests and scripted callers rely on.
        var detected = TerminalCapabilities.Detect(TimeSpan.Zero);
        var env      = TerminalCapabilities.FromEnvironment();
        Assert.Equal(env.SupportsKittyGraphics, detected.SupportsKittyGraphics);
        Assert.Equal(env.InsideTmux, detected.InsideTmux);
    }
}
