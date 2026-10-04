namespace TheIsleOverlay.Core;

public static class VitalMath
{
    public static double Percent(double? current, double? maximum, double? fallback = null)
    {
        if (current is { } currentValue && double.IsFinite(currentValue) &&
            maximum is > 0 && double.IsFinite(maximum.Value))
        {
            return Math.Clamp(currentValue / maximum.Value * 100d, 0d, 100d);
        }

        if (fallback is null || !double.IsFinite(fallback.Value))
        {
            return 0d;
        }

        // Providers already convert fractions to percentages. A fallback of 1
        // means 1%, including when food or another vital falls below that value.
        return Math.Clamp(fallback.Value, 0d, 100d);
    }
}
