using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class MapHeadingTests
{
    [Theory]
    [InlineData(0, 90)]
    [InlineData(90, 180)]
    [InlineData(180, 270)]
    [InlineData(270, 0)]
    [InlineData(-90, 0)]
    public void FromUnrealYaw_ConvertsUnrealYawToNorthBasedMapAngle(double yaw, double expected) =>
        Assert.Equal(expected, MapHeading.FromUnrealYaw(yaw), precision: 8);

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void FromUnrealYaw_AgreesWithTheCalibratedProjection(double yaw)
    {
        // The IslePilot path derives the heading by projecting a point 10 m ahead
        // along the yaw direction. Both paths describe one compass direction, so
        // they may only differ by the calibration's tiny axis anisotropy.
        var origin = new WorldLocation { X = 0, Y = 0 };
        var far = new WorldLocation { X = 120_000, Y = -90_000 };
        var originPoint = GatewayMapProjection.Project(origin);
        var farPoint = GatewayMapProjection.Project(far);
        var projection = new MapProjectionTelemetry(
            origin.X,
            origin.Y,
            originPoint.Left,
            originPoint.Top,
            far.X,
            far.Y,
            farPoint.Left,
            farPoint.Top);

        var fromProjection = projection.ProjectHeading(origin, yaw);
        var fromYaw = MapHeading.FromUnrealYaw(yaw);
        var difference = Math.Abs(MapHeading.Normalize(fromProjection - fromYaw + 180d) - 180d);

        Assert.True(difference < 0.5d, $"projection {fromProjection} vs yaw {fromYaw}");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, -90)]
    [InlineData(270, -270)]
    [InlineData(-90, -270)]
    public void MapRotationForHeadingUp_RotatesWorldOppositeToPlayer(double heading, double expected) =>
        Assert.Equal(expected, MapHeading.MapRotationForHeadingUp(heading), precision: 8);

    [Theory]
    [InlineData(90, 30, 60)]
    [InlineData(10, 350, 20)]
    [InlineData(350, 10, 340)]
    public void RelativeToViewer_ExpressesMarkerHeadingInRotatedMap(
        double heading,
        double viewer,
        double expected) =>
        Assert.Equal(expected, MapHeading.RelativeToViewer(heading, viewer), precision: 8);
}
