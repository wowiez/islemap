using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class GatewayMapProjectionTests
{
    [Fact]
    public void Project_MapsKnownWorldCenterToImageCenter()
    {
        // World X is the east/west axis on Gateway and world Y the north/south one.
        var location = new WorldLocation { X = 51_000, Y = -49_000, Z = 0 };

        var point = GatewayMapProjection.Project(location);

        Assert.Equal(0.5d, point.Left, precision: 8);
        Assert.Equal(0.5d, point.Top, precision: 8);
    }

    [Theory]
    [InlineData(52099.673, -231654.353, 3907.71 / 7800d, 2629.10 / 7817d)]
    [InlineData(-88576.924, 41151.966, 2920.95 / 7800d, 4539.97 / 7817d)]
    [InlineData(158669.822, -51899.767, 4655.24 / 7800d, 3888.19 / 7817d)]
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
            X = -178543.339,
            Y = 608519.832
        });

        Assert.InRange(point.Left, 0d, 1d);
        Assert.True(point.Top > 1d);
    }
}
