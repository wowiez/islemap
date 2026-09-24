namespace TheIsleOverlay.Core;

public readonly record struct MapPoint(double Left, double Top);

public static class GatewayMapProjection
{
    // Least-squares calibration from verified Asset Location anchors on the
    // 7800 x 7817 Gateway image.
    private const double HorizontalScale = 0.000000899283152195387d;
    private const double HorizontalOffset = 0.45413655923803525d;
    private const double VerticalScale = 0.0000008960590186371682d;
    private const double VerticalOffset = 0.5439068919132213d;

    public static MapPoint Project(WorldLocation location)
    {
        // Gateway's game X runs east/west and game Y runs north/south, so X lands
        // on the horizontal image axis and Y on the vertical one. Keep out-of-bounds
        // values intact so callers can distinguish real off-map locations instead
        // of pinning them to an edge.
        var left = location.X * HorizontalScale + HorizontalOffset;
        var top = location.Y * VerticalScale + VerticalOffset;
        return new MapPoint(left, top);
    }

    public static double DistanceMeters(MapPoint first, MapPoint second)
    {
        var deltaWorldX = (second.Left - first.Left) / HorizontalScale;
        var deltaWorldY = (second.Top - first.Top) / VerticalScale;
        return Math.Sqrt(deltaWorldX * deltaWorldX + deltaWorldY * deltaWorldY) / 100d;
    }
}
