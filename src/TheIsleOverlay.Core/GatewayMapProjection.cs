namespace TheIsleOverlay.Core;

public readonly record struct MapPoint(double Left, double Top);

public static class GatewayMapProjection
{
    // Least-squares calibration from verified Asset Location anchors on the
    // 7800 x 7817 Gateway image. Asset Location's first value maps vertically
    // and its second value maps horizontally.
    private const double HorizontalScale = 0.000000899283152195387d;
    private const double HorizontalOffset = 0.45413655923803525d;
    private const double VerticalScale = 0.0000008960590186371682d;
    private const double VerticalOffset = 0.5439068919132213d;

    public static MapPoint Project(WorldLocation location)
    {
        // Gateway uses game Y on the horizontal image axis and game X on the
        // vertical image axis. Keep out-of-bounds values intact so callers can
        // distinguish real off-map locations instead of pinning them to an edge.
        var left = location.Y * HorizontalScale + HorizontalOffset;
        var top = location.X * VerticalScale + VerticalOffset;
        return new MapPoint(left, top);
    }

    public static double DistanceMeters(MapPoint first, MapPoint second)
    {
        var deltaWorldY = (second.Left - first.Left) / HorizontalScale;
        var deltaWorldX = (second.Top - first.Top) / VerticalScale;
        return Math.Sqrt(deltaWorldX * deltaWorldX + deltaWorldY * deltaWorldY) / 100d;
    }
}
