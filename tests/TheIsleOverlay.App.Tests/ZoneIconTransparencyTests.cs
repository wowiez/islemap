namespace TheIsleOverlay.App.Tests;

public sealed class ZoneIconTransparencyTests
{
    [Fact]
    public void CutOpaqueBackgroundConnectedToEdges_DoesNotEraseEnclosedWhiteDetail()
    {
        const int width = 5;
        const int height = 5;
        const int stride = width * 4;
        var pixels = Enumerable.Repeat(byte.MaxValue, stride * height).ToArray();

        // A dark ring encloses a legitimate white detail in the icon center.
        foreach (var (x, y) in new[] { (2, 1), (1, 2), (3, 2), (2, 3) })
        {
            var offset = y * stride + x * 4;
            pixels[offset] = 0;
            pixels[offset + 1] = 0;
            pixels[offset + 2] = 0;
        }

        ZoneIconTransparency.CutOpaqueBackgroundConnectedToEdges(
            pixels,
            width,
            height,
            stride);

        Assert.Equal(0, AlphaAt(0, 0));
        Assert.Equal(byte.MaxValue, AlphaAt(2, 2));
        Assert.Equal(byte.MaxValue, AlphaAt(2, 1));

        byte AlphaAt(int x, int y) => pixels[y * stride + x * 4 + 3];
    }
}
