using TheIsleOverlay.App;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class NpcapGameCoordinateTransformTests
{
    [Fact]
    public void UnrealMovementVector_PreservesAssetLocationAxisOrder()
    {
        var rawPacketLocation = new WorldLocation
        {
            X = -170_819.68,
            Y = -52_633.81,
            Z = 49_782
        };

        var assetLocation = NpcapGameCoordinateTransform.ToAssetLocation(rawPacketLocation);

        Assert.Equal(rawPacketLocation.X, assetLocation.X, precision: 2);
        Assert.Equal(rawPacketLocation.Y, assetLocation.Y, precision: 2);
        Assert.Equal(rawPacketLocation.Z, assetLocation.Z);
    }

    [Fact]
    public void SwappedPacketWouldProjectToADifferentRegion()
    {
        var rawPacketLocation = new WorldLocation { X = -170_819.68, Y = -52_633.81, Z = 49_782 };

        var correct = GatewayMapProjection.Project(
            NpcapGameCoordinateTransform.ToAssetLocation(rawPacketLocation));
        var swapped = GatewayMapProjection.Project(new WorldLocation
        {
            X = rawPacketLocation.Y,
            Y = rawPacketLocation.X,
            Z = rawPacketLocation.Z
        });

        Assert.True(Math.Abs(correct.Left - swapped.Left) > 0.1);
        Assert.True(Math.Abs(correct.Top - swapped.Top) > 0.1);
    }
}
