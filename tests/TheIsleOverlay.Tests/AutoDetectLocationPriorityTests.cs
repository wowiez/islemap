using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class AutoDetectLocationPriorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RepeatedSnapshotWithSameMapRevision_DoesNotReleaseClipboard()
    {
        Assert.False(AutoDetectLocationPriority.HasNewLocation(
            Location(0, 0),
            Now,
            Location(10_000, 10_000),
            Now));
    }

    [Fact]
    public void NewMapRevisionWithDifferentLocation_ReleasesClipboard()
    {
        Assert.True(AutoDetectLocationPriority.HasNewLocation(
            Location(0, 0),
            Now,
            Location(100_000, -200_000),
            Now.AddSeconds(2)));
    }

    [Fact]
    public void NewMapRevisionWithTinyLocationChange_ReleasesClipboard()
    {
        Assert.True(AutoDetectLocationPriority.HasNewLocation(
            Location(50_000, 60_000),
            Now,
            Location(50_000.0001, 60_000),
            Now.AddSeconds(2)));
    }

    [Fact]
    public void NewMapRevisionWithDuplicateLocation_KeepsClipboardPriority()
    {
        var location = Location(50_000, 60_000);

        Assert.False(AutoDetectLocationPriority.HasNewLocation(
            location,
            Now,
            location,
            Now.AddSeconds(2)));
    }

    [Fact]
    public void SourcesWithoutRevisionStillReleaseWhenLocationChanges()
    {
        Assert.True(AutoDetectLocationPriority.HasNewLocation(
            Location(0, 0),
            null,
            Location(2, 0),
            null));
    }

    [Fact]
    public void SourcesWithoutRevisionReleaseForAnyExactCoordinateChange()
    {
        Assert.True(AutoDetectLocationPriority.HasNewLocation(
            Location(0, 0),
            null,
            Location(0, 0.0001),
            null));
    }

    private static WorldLocation Location(double x, double y) => new() { X = x, Y = y };
}
