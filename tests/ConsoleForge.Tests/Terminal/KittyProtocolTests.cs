using System.IO.Compression;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Terminal;
using ConsoleForge.Widgets;

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

    // ── Format declaration (WISHLIST 12) ────────────────────────────────────────

    [Fact]
    public void IsPng_ChecksTheSignature()
    {
        Assert.True(KittyProtocol.IsPng(Png));
        Assert.False(KittyProtocol.IsPng([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0]));
        Assert.False(KittyProtocol.IsPng(Png.AsSpan(0, 7)));
        Assert.False(KittyProtocol.IsPng([]));
    }

    [Fact]
    public void CreatePayload_RejectsNonPng()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => KittyProtocol.CreatePayload([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0], TerminalCapabilities.WithKitty));
        Assert.Contains("JPEG", ex.Message);
        Assert.Contains("RgbaImageData", ex.Message);
    }

    [Fact]
    public void RgbaPayload_DeclaresPixelsAndRoundTrips()
    {
        byte[] pixels = [1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255, 13, 14, 15, 255, 16, 17, 18, 255];
        var payload = KittyProtocol.CreatePayload(new RgbaImageData(pixels, 3, 2), TerminalCapabilities.WithKitty);

        var transmit = string.Concat(payload.Transmit(ColorProfile.TrueColor)!);
        Assert.Matches(@"^\x1b_Ga=t,f=32,s=3,v=2,o=z,i=\d+,q=2,m=0;[A-Za-z0-9+/=]+\x1b\\$", transmit);

        // The payload is the RFC 1950 zlib stream o=z declares, inflating to the pixels.
        var base64 = transmit[(transmit.IndexOf(';') + 1)..^2];
        using var zlib = new ZLibStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        zlib.CopyTo(inflated);
        Assert.Equal(pixels, inflated.ToArray());
    }

    [Fact]
    public void RgbaPayload_IdCoversDimensions()
    {
        var pixels = new byte[16 * 4];
        var wide = KittyProtocol.CreatePayload(new RgbaImageData(pixels, 8, 2), TerminalCapabilities.WithKitty);
        var tall = KittyProtocol.CreatePayload(new RgbaImageData(pixels, 2, 8), TerminalCapabilities.WithKitty);
        Assert.NotEqual(wide.ContentHash, tall.ContentHash);

        // Same array, same dimensions: same image, so the frame diff sees no change.
        var again = KittyProtocol.CreatePayload(new RgbaImageData(pixels, 8, 2), TerminalCapabilities.WithKitty);
        Assert.Equal(wide.ContentHash, again.ContentHash);
    }

    [Theory]
    [InlineData(0, 2, 16)]
    [InlineData(2, -1, 16)]
    [InlineData(4, 4, 63)]   // needs 64 bytes
    public void RgbaPayload_RejectsInconsistentData(int width, int height, int bytes)
    {
        Assert.Throws<ArgumentException>(
            () => KittyProtocol.CreatePayload(new RgbaImageData(new byte[bytes], width, height), TerminalCapabilities.WithKitty));
    }
}
