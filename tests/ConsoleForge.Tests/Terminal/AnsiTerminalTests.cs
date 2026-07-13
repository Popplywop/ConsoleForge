using ConsoleForge.Terminal;

// note: not ConsoleForge.Tests.Terminal — that would shadow the
// ConsoleForge.Terminal namespace for sibling tests using Terminal.* refs
namespace ConsoleForge.Tests;

/// <summary>Regression tests for terminal teardown — the quit path must
/// restore the hardware cursor (issue: cursor stayed hidden after quit).</summary>
public class AnsiTerminalTests
{
    [Fact]
    public void SetCursorVisible_WritesImmediately_NotBuffered()
    {
        var captured = new StringWriter();
        TextWriter original = Console.Out;
        try
        {
            Console.SetOut(captured);
            using var terminal = new AnsiTerminal();

            terminal.SetCursorVisible(true);

            // Regression: this used to land in the render buffer, which the
            // quit path never flushes — the cursor stayed hidden after exit.
            Assert.Contains("\x1b[?25h", captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Dispose_ShowsCursor_AfterLeavingAlternateScreen()
    {
        var captured = new StringWriter();
        TextWriter original = Console.Out;
        try
        {
            Console.SetOut(captured);
            var terminal = new AnsiTerminal();

            terminal.Dispose();

            string output = captured.ToString();
            int altScreenExit = output.IndexOf("\x1b[?1049l", StringComparison.Ordinal);
            int cursorShow = output.LastIndexOf("\x1b[?25h", StringComparison.Ordinal);
            Assert.True(altScreenExit >= 0, "Dispose must exit the alternate screen");
            // Order matters: ?1049l restores saved cursor state on VTE/tmux,
            // re-hiding a cursor that was shown while still on the alt screen.
            Assert.True(cursorShow > altScreenExit,
                "Dispose must show the cursor after exiting the alternate screen");
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var captured = new StringWriter();
        TextWriter original = Console.Out;
        try
        {
            Console.SetOut(captured);
            var terminal = new AnsiTerminal();

            terminal.Dispose();
            int lengthAfterFirst = captured.ToString().Length;
            terminal.Dispose();

            Assert.Equal(lengthAfterFirst, captured.ToString().Length);
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
