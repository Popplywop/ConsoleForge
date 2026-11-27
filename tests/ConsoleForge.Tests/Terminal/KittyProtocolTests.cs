using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;

namespace ConsoleForge.Tests;

public class KittyProtocolTests
{
    // 1x1 transparent PNG; the bytes only need to be stable.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public void DetectProbe_IsAWellFormedQuery()
    {
        // f=24 at 1x1 is three bytes, base64 "AAAA". The old probe declared f=32 (four
        // bytes) and sent a PNG, so a correct terminal answered with an error.
        var probe = KittyProtocol.BuildDetectProbe();
        Assert.Equal("\x1b_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA\x1b\\", probe);
        // No q key: q=1 suppresses the OK reply, which is the answer being asked for.
        Assert.DoesNotContain("q=", probe);
    }

    [Theory]
    [InlineData("\x1b_Gi=31;OK\x1b\\", true)]
    [InlineData("\x1b_Gi=31;ENOTSUPPORTED:x\x1b\\", true)]
    [InlineData("\x1b[?62;22c", false)]
    [InlineData("\x1b_Gi=310;OK\x1b\\", false)]
    public void ParseDetectResponse(string response, bool expected)
    {
        Assert.Equal(expected, KittyProtocol.ParseDetectResponse(response));
    }

    [Fact]
    public void Cleanup_IsQuiet()
    {
        // Without q=2 the terminal answers every delete on stdin.
        var payload = KittyProtocol.CreatePayload(Png, TerminalCapabilities.WithKitty);
        var cleanup = payload.Cleanup(new Region(2, 3, 4, 5));
        Assert.NotNull(cleanup);
        Assert.Matches(@"^\x1b_Ga=d,d=i,i=\d+,p=\d+,q=2\x1b\\$", cleanup);
    }

    [Fact]
    public void EveryCommand_IsQuiet()
    {
        // A PNG large enough to need continuation chunks.
        var big = new byte[KittyProtocol.MaxChunkBase64Chars * 2];
        Png.CopyTo(big, 0);
        var payload = KittyProtocol.CreatePayload(big, TerminalCapabilities.WithKitty);

        var sequences = payload.Encode(new Region(0, 0, 4, 2), ColorProfile.TrueColor).ToList();
        Assert.True(sequences.Count >= 3);
        Assert.All(sequences, s => Assert.Contains("q=2", s));
    }
}
