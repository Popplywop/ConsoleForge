using BenchmarkDotNet.Attributes;
using ConsoleForge.Terminal;
using ConsoleForge.Widgets;

/// <summary>
/// Whether <see cref="KittyProtocol.CreatePayload(byte[], TerminalCapabilities)"/> still reuses the encoding of an image
/// it has seen, measured against the work that reuse avoids.
/// </summary>
/// <remarks>
/// Widgets are rebuilt every frame, so every frame constructs a payload for every visible
/// image. The <c>ConditionalWeakTable</c> behind <see cref="KittyProtocol.GetEncoded(byte[])"/> is
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
        PngSignature.CopyTo(_poster, 0); // CreatePayload checks for it
        _caps = new TerminalCapabilities { SupportsKittyGraphics = true };
        KittyProtocol.CreatePayload(_poster, _caps); // warm the cache

        // A 160x240 poster as decoded pixels: a smooth gradient with a little noise, which
        // compresses roughly as a photo does — random bytes would not compress at all.
        var rng = new Random(7);
        _pixels = new byte[160 * 240 * 4];
        for (int i = 0; i < _pixels.Length; i += 4)
        {
            int p = i / 4;
            _pixels[i]     = (byte)(p % 160 + rng.Next(4));
            _pixels[i + 1] = (byte)(p / 160 + rng.Next(4));
            _pixels[i + 2] = (byte)(128 + rng.Next(4));
            _pixels[i + 3] = 255;
        }
        KittyProtocol.CreatePayload(new RgbaImageData(_pixels, 160, 240), _caps); // warm
    }

    internal static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private byte[] _pixels = null!;

    /// <summary>What every frame would pay without the cache: hash plus base64.</summary>
    [Benchmark(Baseline = true)]
    public object EncodeUncached()
        => new KittyProtocol.EncodedImage(KittyProtocol.ImageIdFromBytes(_poster), Convert.ToBase64String(_poster), "f=100");

    /// <summary>
    /// The one-off cost of an image sent as pixels (f=32): hash, zlib, base64 — paid once
    /// per pixel array, on the frame it first appears in unless it was preloaded.
    /// </summary>
    [Benchmark]
    public object EncodeRgbaUncached()
        => KittyProtocol.GetEncoded(new RgbaImageData((byte[])_pixels.Clone(), 160, 240));

    /// <summary>What every frame pays for an image sent as pixels, once encoded.</summary>
    [Benchmark]
    public KittyPayload CreateRgbaPayloadCached()
        => KittyProtocol.CreatePayload(new RgbaImageData(_pixels, 160, 240), _caps);

    /// <summary>What every frame pays: a payload for an image already encoded.</summary>
    [Benchmark]
    public KittyPayload CreatePayloadCached()
        => KittyProtocol.CreatePayload(_poster, _caps);
}
