using ConsoleForge.Terminal;

namespace ConsoleForge.Tests;

/// <summary>
/// The probe's read loop against scripted stdin. Every script ends in a reply or in
/// <see cref="StdinReader.Closed"/>, so no test waits on the timeout.
/// </summary>
public class TerminalProbeTests
{
    private sealed class ScriptedReader(params string[] reads) : StdinReader
    {
        private int _next;

        internal override int Read(char[] buffer, int timeoutMs)
        {
            if (_next >= reads.Length) return Closed;
            var chunk = reads[_next++];
            chunk.CopyTo(0, buffer, 0, chunk.Length);
            return chunk.Length;
        }
    }

    private static KittyProbeResult Probe(params string[] reads) =>
        TerminalProbe.AwaitKittyReply(new ScriptedReader(reads), TimeSpan.FromSeconds(5));

    [Fact]
    public void GraphicsReplyBeforeAttributes_IsSupported()
    {
        Assert.Equal(KittyProbeResult.Supported, Probe("\x1b_Gi=31;OK\x1b\\\x1b[?62;22c"));
    }

    [Fact]
    public void AnyGraphicsReply_CountsAsSupport()
    {
        // The protocol's own advice: a terminal that answers a graphics command at all
        // implements it, even if it rejects this particular one.
        Assert.Equal(KittyProbeResult.Supported, Probe("\x1b_Gi=31;EINVAL:bad\x1b\\\x1b[?62c"));
    }

    [Fact]
    public void AttributesAlone_IsUnsupported()
    {
        Assert.Equal(KittyProbeResult.Unsupported, Probe("\x1b[?1;2c"));
    }

    [Fact]
    public void RepliesSplitAcrossReads()
    {
        Assert.Equal(KittyProbeResult.Supported, Probe("\x1b_Gi=3", "1;OK\x1b", "\\\x1b[?6", "2c"));
    }

    [Fact]
    public void KeysTypedDuringProbe_AreIgnored()
    {
        Assert.Equal(KittyProbeResult.Unsupported, Probe("abc\x1b[A", "\x1b[?62c"));
    }

    [Fact]
    public void ReplyForAnotherImage_DoesNotCount()
    {
        Assert.Equal(KittyProbeResult.Unsupported, Probe("\x1b_Gi=99;OK\x1b\\\x1b[?62c"));
    }

    [Fact]
    public void NoAnswer_IsUnknown()
    {
        Assert.Equal(KittyProbeResult.Unknown, Probe());
    }
}
