using BenchmarkDotNet.Attributes;
using ConsoleForge.Terminal;

/// <summary>
/// Whether <see cref="KittyProtocol.CreatePayload"/> still reuses the encoding of an image
/// it has seen, measured against the work that reuse avoids.
/// </summary>
/// <remarks>
/// Widgets are rebuilt every frame, so every frame constructs a payload for every visible
/// image. The <c>ConditionalWeakTable</c> behind <see cref="KittyProtocol.GetEncoded"/> is
/// what keeps hashing the bytes and base64-encoding them off that path. A miss produces the
/// same payload, so nothing but a measurement of cost can tell a hit from a miss.
/// </remarks>
[MemoryDiagnoser]
public class PixelEncodeBenchmarks
{
    private const int PosterBytes = 14 * 1024;

    private byte[] _poster = null!;
    private TerminalCapabilities _caps = null!;

    [GlobalSetup]
    public void Setup()
    {
        _poster = new byte[PosterBytes];
        new Random(42).NextBytes(_poster);
        _caps = new TerminalCapabilities { SupportsKittyGraphics = true };
        KittyProtocol.CreatePayload(_poster, _caps); // warm the cache
    }

    /// <summary>What every frame would pay without the cache: hash plus base64.</summary>
    [Benchmark(Baseline = true)]
    public object EncodeUncached()
        => new KittyProtocol.EncodedPng(KittyProtocol.ImageIdFromBytes(_poster), Convert.ToBase64String(_poster));

    /// <summary>What every frame pays: a payload for an image already encoded.</summary>
    [Benchmark]
    public KittyPayload CreatePayloadCached()
        => KittyProtocol.CreatePayload(_poster, _caps);
}
