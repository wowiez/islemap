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
    public void FromUnrealYaw_ConvertsEastBasedYawToNorthBasedMapAngle(double yaw, double expected) =>
        Assert.Equal(expected, MapHeading.FromUnrealYaw(yaw), precision: 8);

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
