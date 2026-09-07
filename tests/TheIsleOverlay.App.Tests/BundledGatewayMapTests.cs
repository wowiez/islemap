using System.IO;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class BundledGatewayMapTests
{
    private const string ResourceKey = "assets/gatewaymap.webp";
    private const string ExpectedSha256 = "BA2E5E614995BEC84559B950F1AE978C2F9A66743F0DA47A348278DB01557EF3";

    [Fact]
    public void GatewayMap_IsEmbeddedUnchanged()
    {
        var assembly = typeof(MainWindow).Assembly;
        var manifestName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(".g.resources", StringComparison.Ordinal));

        using var manifestStream = assembly.GetManifestResourceStream(manifestName);
        Assert.NotNull(manifestStream);
        using var reader = new ResourceReader(manifestStream);
        var resources = reader.GetEnumerator();

        while (resources.MoveNext())
        {
            if (!string.Equals(resources.Key as string, ResourceKey, StringComparison.Ordinal))
            {
                continue;
            }

            using var mapStream = Assert.IsAssignableFrom<Stream>(resources.Value);
            Assert.Equal(6_739_366, mapStream.Length);
            Assert.Equal(ExpectedSha256, Convert.ToHexString(SHA256.HashData(mapStream)));
            return;
        }

        Assert.Fail($"Bundled map resource '{ResourceKey}' was not found.");
    }

    [Fact]
    public void Overlay_UsesLocalMapResourceInsteadOfSourceMapUris()
    {
        Assert.Null(typeof(TelemetrySourceDefinition).GetProperty("MapUri"));

        var resourceUriField = typeof(MainWindow).GetField(
            "GatewayMapResourceUri",
            BindingFlags.NonPublic | BindingFlags.Static);
        var resourceUri = Assert.IsType<Uri>(resourceUriField?.GetValue(null));

        Assert.False(resourceUri.IsAbsoluteUri);
        Assert.Equal("Assets/GatewayMap.webp", resourceUri.OriginalString);
    }

    [Theory]
    [InlineData("assets/sbtcsanctuary.png")]
    [InlineData("assets/sbtcmigration.png")]
    public void SbtcZoneIcons_AreBundledPngsWithRealTransparency(string resourceKey)
    {
        var bytes = ReadResource(resourceKey);
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(
            Assert.Single(decoder.Frames),
            PixelFormats.Bgra32,
            null,
            0d);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var alpha = pixels.Where((_, index) => index % 4 == 3).ToArray();

        Assert.Contains((byte)0, alpha);
        Assert.Contains(byte.MaxValue, alpha);
    }

    [Theory]
    [InlineData("assets/dinothumbnails/pachycephalosaurus.png")]
    [InlineData("assets/dinothumbnails/triceratops.png")]
    public void GarageDinoThumbnails_UseOfficialModelShapeWithTransparentBackground(string resourceKey)
    {
        var bytes = ReadResource(resourceKey);
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = Assert.Single(decoder.Frames);
        Assert.Equal(640, frame.PixelWidth);
        Assert.Equal(240, frame.PixelHeight);

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0d);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var alpha = pixels.Where((_, index) => index % 4 == 3).ToArray();
        Assert.Contains((byte)0, alpha);
        Assert.Contains(byte.MaxValue, alpha);
    }

    private static byte[] ReadResource(string resourceKey)
    {
        var assembly = typeof(MainWindow).Assembly;
        var manifestName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(".g.resources", StringComparison.Ordinal));
        using var manifestStream = assembly.GetManifestResourceStream(manifestName);
        Assert.NotNull(manifestStream);
        using var reader = new ResourceReader(manifestStream);
        var resources = reader.GetEnumerator();
        while (resources.MoveNext())
        {
            if (string.Equals(resources.Key as string, resourceKey, StringComparison.Ordinal))
            {
                using var resource = Assert.IsAssignableFrom<Stream>(resources.Value);
                using var copy = new MemoryStream();
                resource.CopyTo(copy);
                return copy.ToArray();
            }
        }

        Assert.Fail($"Bundled resource '{resourceKey}' was not found.");
        return [];
    }
}
