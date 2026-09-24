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
    public void DecodedMovementAxes_MatchTheMapAxes()
    {
        // Gateway's game X runs east/west (the horizontal image axis) and its
        // game Y runs north/south, so packet values feed the projection unchanged.
        var origin = GatewayMapProjection.Project(
            NpcapGameCoordinateTransform.ToAssetLocation(new WorldLocation { X = 0, Y = 0, Z = 1_000 }));
        var east = GatewayMapProjection.Project(
            NpcapGameCoordinateTransform.ToAssetLocation(new WorldLocation { X = 10_000, Y = 0, Z = 1_000 }));
        var south = GatewayMapProjection.Project(
            NpcapGameCoordinateTransform.ToAssetLocation(new WorldLocation { X = 0, Y = 10_000, Z = 1_000 }));

        Assert.True(east.Left > origin.Left);
        Assert.Equal(origin.Top, east.Top, precision: 10);
        Assert.True(south.Top > origin.Top);
        Assert.Equal(origin.Left, south.Left, precision: 10);
    }

    [Fact]
    public void CopiedAssetLocation_LandsWhereTheSameDecodedPacketLands()
    {
        // The clipboard prints north/south first while the movement RPC sends the
        // east/west component first; both pipelines describe the same spot.
        Assert.True(ClipboardCoordinateParser.TryParse(
            "-122,965.899, -19,431.901, 44,207.348",
            out var copied));
        var decoded = NpcapGameCoordinateTransform.ToAssetLocation(new WorldLocation
        {
            X = copied.X,
            Y = copied.Y,
            Z = copied.Z
        });

        var fromClipboard = GatewayMapProjection.Project(copied);
        var fromPacket = GatewayMapProjection.Project(decoded);

        Assert.Equal(fromClipboard.Left, fromPacket.Left, precision: 10);
        Assert.Equal(fromClipboard.Top, fromPacket.Top, precision: 10);
    }

    [Fact]
    public void HighlandsAssetLocation_ProjectsIntoHighlandsRegion()
    {
        // Real copy taken while standing in the Highlands migration zone.
        Assert.True(ClipboardCoordinateParser.TryParse(
            "-122,965.899, -19,431.901, 44,207.348",
            out var location));

        var mapPoint = GatewayMapProjection.Project(location);

        Assert.InRange(mapPoint.Left, 0.325d, 0.512d);
        Assert.InRange(mapPoint.Top, 0.330d, 0.560d);
    }
}
