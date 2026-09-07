namespace TheIsleOverlay.Core;

public static class MovementHeading
{
    public static bool TryCalculate(
        WorldLocation previous,
        WorldLocation current,
        out double degrees,
        double minimumDistance = 100d,
        double maximumDistance = 200_000d)
    {
        var deltaX = current.X - previous.X;
        var deltaY = current.Y - previous.Y;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

        if (distance < minimumDistance || distance > maximumDistance)
        {
            degrees = 0d;
            return false;
        }

        // Gateway game X increases southward and game Y increases eastward.
        // atan2(east, north) therefore maps north to 0 and east to 90 degrees.
        degrees = Math.Atan2(deltaY, -deltaX) * 180d / Math.PI;
        if (degrees < 0d)
        {
            degrees += 360d;
        }

        return true;
    }

    public static double Smooth(double previous, double current, double weight = 0.6d)
    {
        var delta = (current - previous + 540d) % 360d - 180d;
        return (previous + delta * Math.Clamp(weight, 0d, 1d) + 360d) % 360d;
    }
}
