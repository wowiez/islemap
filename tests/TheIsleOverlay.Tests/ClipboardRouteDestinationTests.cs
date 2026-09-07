using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class ClipboardRouteDestinationTests
{
    [Fact]
    public void TryParse_ProjectsCopiedAssetLocationToGatewayMap()
    {
        Assert.True(ClipboardRouteDestination.TryParse(
            "27,840.493, -242,657.129, 30,707.498",
            out var destination));

        var expected = GatewayMapProjection.Project(new WorldLocation
        {
            X = 27_840.493d,
            Y = -242_657.129d,
            Z = 30_707.498d
        });
        Assert.Equal(expected.Left, destination.Left, 10);
        Assert.Equal(expected.Top, destination.Top, 10);
    }

    [Fact]
    public void TryParse_RejectsClipboardTextThatIsNotALocation()
    {
        Assert.False(ClipboardRouteDestination.TryParse("hello map", out _));
    }

}
