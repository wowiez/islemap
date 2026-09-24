namespace TheIsleOverlay.Core;

public static class MapHeading
{
    // Unreal yaw 0 faces +X, which is the map's east on Gateway, so the compass
    // heading is the yaw turned a quarter turn clockwise.
    public static double FromUnrealYaw(double yawDegrees) => Normalize(yawDegrees + 90d);

    public static double MapRotationForHeadingUp(double headingDegrees) => -Normalize(headingDegrees);

    public static double RelativeToViewer(double headingDegrees, double viewerHeadingDegrees) =>
        Normalize(headingDegrees - viewerHeadingDegrees);

    public static double Normalize(double degrees)
    {
        var normalized = degrees % 360d;
        return normalized < 0d ? normalized + 360d : normalized;
    }
}
