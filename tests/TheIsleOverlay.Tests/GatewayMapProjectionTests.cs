using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class GatewayMapProjectionTests
{
    [Fact]
    public void Project_MapsKnownWorldCenterToImageCenter()
    {
        var location = new WorldLocation { X = -49_000, Y = 51_000, Z = 0 };

        var point = GatewayMapProjection.Project(location);

        Assert.Equal(0.5d, point.Left, precision: 8);
        Assert.Equal(0.5d, point.Top, precision: 8);
    }

    [Theory]
    [InlineData(-231654.353, 52099.673, 3907.71 / 7800d, 2629.10 / 7817d)]
    [InlineData(41151.966, -88576.924, 2920.95 / 7800d, 4539.97 / 7817d)]
    [InlineData(-51899.767, 158669.822, 4655.24 / 7800d, 3888.19 / 7817d)]
    public void Project_MatchesVerifiedGatewayAnchors(double x, double y, double expectedLeft, double expectedTop)
    {
        var point = GatewayMapProjection.Project(new WorldLocation { X = x, Y = y });

        Assert.Equal(expectedLeft, point.Left, precision: 5);
        Assert.Equal(expectedTop, point.Top, precision: 5);
    }

    [Fact]
    public void Project_PreservesRealOutOfBoundsLocations()
    {
        var point = GatewayMapProjection.Project(new WorldLocation
        {
            X = 608519.832,
            Y = -178543.339
        });

        Assert.InRange(point.Left, 0d, 1d);
        Assert.True(point.Top > 1d);
    }
}
