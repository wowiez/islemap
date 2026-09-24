using System.IO;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class PlayerPathTrailStoreTests
{
    [Fact]
    public void PlayerPathTrailStore_TracksPointsAndFiltersStationaryMovement()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IsleLiveMap.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "player-path-trail.json");
        try
        {
            var store = new PlayerPathTrailStore(path);
            var now = DateTimeOffset.UtcNow;

            Assert.Empty(store.GetPoints(now));

            // First point
            var added1 = store.AddPoint(new MapPoint(0.5d, 0.5d), new WorldLocation { X = 0, Y = 0 }, now);
            Assert.True(added1);
            Assert.Single(store.GetPoints(now));

            // Stationary point immediately after
            var added2 = store.AddPoint(new MapPoint(0.5001d, 0.5001d), new WorldLocation { X = 1, Y = 1 }, now.AddSeconds(2));
            Assert.False(added2);
            Assert.Single(store.GetPoints(now));

            // Movement beyond threshold (> 0.0008 map fraction)
            var added3 = store.AddPoint(new MapPoint(0.51d, 0.51d), new WorldLocation { X = 100, Y = 100 }, now.AddSeconds(5));
            Assert.True(added3);
            Assert.Equal(2, store.GetPoints(now).Count);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void PlayerPathTrailStore_RollsTheWindowBehindTheNewestPoint()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IsleLiveMap.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "player-path-trail.json");
        try
        {
            var store = new PlayerPathTrailStore(path);
            var now = DateTimeOffset.UtcNow;

            // Point from 3 hours + 10 minutes ago
            store.AddPoint(new MapPoint(0.1d, 0.1d), null, now.AddHours(-3.17));
            // Point from 2 hours ago
            store.AddPoint(new MapPoint(0.2d, 0.2d), null, now.AddHours(-2));
            // Point from 10 minutes ago
            store.AddPoint(new MapPoint(0.3d, 0.3d), null, now.AddMinutes(-10));

            var points = store.GetPoints(now);
            Assert.Equal(2, points.Count);
            Assert.Equal(0.2d, points[0].Left);
            Assert.Equal(0.3d, points[1].Left);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void PlayerPathTrailStore_KeepsThePathWhenItIsOlderThan3Hours()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IsleLiveMap.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "player-path-trail.json");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var store = new PlayerPathTrailStore(path);
            store.AddPoint(new MapPoint(0.1d, 0.1d), null, now.AddHours(-5));
            store.AddPoint(new MapPoint(0.2d, 0.2d), null, now.AddHours(-4.9));
            Assert.Equal(2, store.GetPoints(now).Count);

            // Reopening after a break must keep the recorded path, not wipe it.
            var reopened = new PlayerPathTrailStore(path);
            var kept = reopened.GetPoints(now);
            Assert.Equal(2, kept.Count);
            Assert.Equal(0.1d, kept[0].Left);
            Assert.Equal(0.2d, kept[1].Left);

            // Moving again rolls the window forward and drops the stale stretch.
            Assert.True(reopened.AddPoint(new MapPoint(0.6d, 0.6d), null, now));
            var rolled = reopened.GetPoints(now);
            var remaining = Assert.Single(rolled);
            Assert.Equal(0.6d, remaining.Left);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void PlayerPathTrailStore_ClearRemovesAllPointsAndSaves()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IsleLiveMap.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "player-path-trail.json");
        try
        {
            var store = new PlayerPathTrailStore(path);
            var now = DateTimeOffset.UtcNow;

            store.AddPoint(new MapPoint(0.1d, 0.1d), null, now);
            store.AddPoint(new MapPoint(0.2d, 0.2d), null, now.AddSeconds(5));
            Assert.Equal(2, store.GetPoints(now).Count);

            store.Clear();
            Assert.Empty(store.GetPoints(now));

            var reloadedStore = new PlayerPathTrailStore(path);
            Assert.Empty(reloadedStore.GetPoints(now));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
