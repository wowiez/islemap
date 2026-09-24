using TheIsleOverlay.App;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class NpcapPositionStabilizerTests
{
    [Fact]
    public void ContinuousSamples_AreSmoothedWithoutChangingTheirTimestamp()
    {
        var filter = new NpcapPositionStabilizer();
        var startedAt = DateTimeOffset.UtcNow;
        Assert.True(filter.TryStabilize(Sample(0, 0, startedAt, 1), out _));

        Assert.True(filter.TryStabilize(Sample(1_000, 0, startedAt.AddMilliseconds(200), 1.2f), out var result));

        Assert.InRange(result.Location.X, 850, 950);
        Assert.Equal(1.2f, result.GameTimestamp);
    }

    [Fact]
    public void ImplausibleJump_IsRejectedAndDoesNotPoisonFollowingSamples()
    {
        var filter = new NpcapPositionStabilizer();
        var startedAt = DateTimeOffset.UtcNow;
        Assert.True(filter.TryStabilize(Sample(100_000, -200_000, startedAt, 1), out _));

        Assert.False(filter.TryStabilize(
            Sample(-500_000, 600_000, startedAt.AddMilliseconds(200), 1.2f),
            out _));
        Assert.True(filter.TryStabilize(
            Sample(100_500, -200_100, startedAt.AddMilliseconds(400), 1.4f),
            out var recovered));
        Assert.InRange(recovered.Location.X, 100_000, 100_500);
    }

    [Fact]
    public void LongGap_AllowsLegitimateRelockWithoutInterpolatingAcrossMap()
    {
        var filter = new NpcapPositionStabilizer();
        var startedAt = DateTimeOffset.UtcNow;
        Assert.True(filter.TryStabilize(Sample(100_000, -200_000, startedAt, 1), out _));

        var relockLocation = new WorldLocation { X = -300_000, Y = 400_000, Z = 100 };
        filter.Seed(relockLocation);

        Assert.True(filter.TryStabilize(
            Sample(-300_000, 400_000, startedAt + NpcapPositionStabilizer.SourceHoldDuration + TimeSpan.FromSeconds(1), 10),
            out var result));
        Assert.Equal(-300_000, result.Location.X);
        Assert.Equal(400_000, result.Location.Y);
    }

    [Fact]
    public void ZeroLocationAfterStablePoint_IsRejectedInsteadOfCenteringMap()
    {
        var filter = new NpcapPositionStabilizer();
        var startedAt = DateTimeOffset.UtcNow;
        Assert.True(filter.TryStabilize(Sample(100_000, -200_000, startedAt, 1), out _));

        Assert.False(filter.TryStabilize(
            Sample(0, 0, startedAt.AddMilliseconds(200), 1.2f), out _));
        Assert.True(filter.TryStabilize(
            Sample(100_100, -200_050, startedAt.AddMilliseconds(400), 1.4f), out var recovered));
        Assert.InRange(recovered.Location.X, 100_000, 100_100);
    }

    [Fact]
    public void Seed_SnapsSmoothedLocationToExactAnchor()
    {
        var filter = new NpcapPositionStabilizer();
        var startedAt = DateTimeOffset.UtcNow;
        Assert.True(filter.TryStabilize(Sample(100_000, -200_000, startedAt, 1), out _));

        filter.Seed(new WorldLocation { X = 120_000, Y = -210_000, Z = 100 });

        Assert.True(filter.TryStabilize(Sample(120_100, -210_050, startedAt.AddMilliseconds(50), 1.1f), out var stabilized));
        Assert.InRange(stabilized.Location.X, 120_000, 120_100);
        Assert.InRange(stabilized.Location.Y, -210_050, -210_000);
    }

    private static NpcapPositionSample Sample(
        double x,
        double y,
        DateTimeOffset capturedAt,
        float gameTimestamp) =>
        new(new WorldLocation { X = x, Y = y, Z = 100 }, capturedAt, gameTimestamp);
}
