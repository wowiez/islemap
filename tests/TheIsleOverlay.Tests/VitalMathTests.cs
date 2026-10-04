using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class VitalMathTests
{
    [Fact]
    public void Percent_UsesExactCurrentAndMaximumFirst() =>
        Assert.Equal(25d, VitalMath.Percent(50, 200, 99));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.75, 0.75)]
    [InlineData(1, 1)]
    [InlineData(1.01, 1.01)]
    [InlineData(42, 42)]
    [InlineData(900, 100)]
    [InlineData(-10, 0)]
    public void Percent_PreservesLowPercentagesAndClampsFallback(double fallback, double expected) =>
        Assert.Equal(expected, VitalMath.Percent(null, null, fallback));

    [Fact]
    public void Percent_UsesTheSameUnitsForExactAndFallbackLowFood()
    {
        Assert.Equal(1d, VitalMath.Percent(1, 100, 99));
        Assert.Equal(1d, VitalMath.Percent(null, null, 1));
        Assert.Equal(0.5d, VitalMath.Percent(1, 200, 99));
        Assert.Equal(0.5d, VitalMath.Percent(null, null, 0.5));
    }

    [Fact]
    public void Percent_FallsBackWhenExactValuesAreInvalid()
    {
        Assert.Equal(1d, VitalMath.Percent(double.NaN, 100, 1));
        Assert.Equal(1d, VitalMath.Percent(100, double.PositiveInfinity, 1));
        Assert.Equal(1d, VitalMath.Percent(100, 0, 1));
        Assert.Equal(0d, VitalMath.Percent(null, null, double.NaN));
        Assert.Equal(0d, VitalMath.Percent(null, null));
    }
}
