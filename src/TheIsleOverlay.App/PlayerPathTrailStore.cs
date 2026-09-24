using System.IO;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public sealed record PlayerPathTrailPoint(
    double Left,
    double Top,
    DateTimeOffset Timestamp,
    double? WorldX = null,
    double? WorldY = null);

public sealed class PlayerPathTrailStore
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);
    private const double MinMovementDistanceSquared = 0.0008 * 0.0008;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _lock = new();
    private readonly string _path;
    private readonly List<PlayerPathTrailPoint> _points = [];
    private bool _hasUnsavedChanges;

    public PlayerPathTrailStore(string? path = null)
    {
        var overridePath = Environment.GetEnvironmentVariable("ISLELIVEMAP_PATH_TRAIL_PATH");
        _path = string.IsNullOrWhiteSpace(path)
            ? string.IsNullOrWhiteSpace(overridePath)
                ? AppPaths.PlayerPathTrail
                : Path.GetFullPath(overridePath)
            : Path.GetFullPath(path);

        Load();
    }

    public bool AddPoint(MapPoint mapPoint, WorldLocation? worldLocation = null, DateTimeOffset? timestamp = null)
    {
        if (!double.IsFinite(mapPoint.Left) || !double.IsFinite(mapPoint.Top))
        {
            return false;
        }

        var now = timestamp ?? DateTimeOffset.UtcNow;
        lock (_lock)
        {
            PruneInternal();

            if (_points.Count > 0)
            {
                var last = _points[^1];
                var dx = mapPoint.Left - last.Left;
                var dy = mapPoint.Top - last.Top;
                var distSq = dx * dx + dy * dy;

                if (distSq < MinMovementDistanceSquared && (now - last.Timestamp) < TimeSpan.FromSeconds(30))
                {
                    return false;
                }
            }

            _points.Add(new PlayerPathTrailPoint(
                Math.Clamp(mapPoint.Left, 0d, 1d),
                Math.Clamp(mapPoint.Top, 0d, 1d),
                now,
                worldLocation?.X,
                worldLocation?.Y));

            _hasUnsavedChanges = true;
            SaveInternal();
            return true;
        }
    }

    public IReadOnlyList<PlayerPathTrailPoint> GetPoints(DateTimeOffset? now = null)
    {
        lock (_lock)
        {
            PruneInternal();
            return _points.ToArray();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _points.Clear();
            _hasUnsavedChanges = true;
            SaveInternal();
        }
    }

    // The window rolls with the trail itself instead of the wall clock: only
    // stretches older than MaxAge behind the newest recorded point are dropped, so
    // reopening the app after a break keeps the path instead of wiping it whole.
    private void PruneInternal()
    {
        if (_points.Count == 0)
        {
            return;
        }

        var cutoff = _points[^1].Timestamp - MaxAge;
        var removed = _points.RemoveAll(p => p.Timestamp < cutoff);
        if (removed > 0)
        {
            _hasUnsavedChanges = true;
        }
    }

    public void Load()
    {
        lock (_lock)
        {
            _points.Clear();
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                var loaded = JsonSerializer.Deserialize<List<PlayerPathTrailPoint>>(
                    File.ReadAllText(_path),
                    JsonOptions);

                if (loaded is not null)
                {
                    _points.AddRange(loaded);
                    PruneInternal();
                }
            }
            catch
            {
                // Ignore load errors and start fresh
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            SaveInternal();
        }
    }

    private void SaveInternal()
    {
        if (!_hasUnsavedChanges)
        {
            return;
        }

        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Path trail store path has no parent directory.");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(_points, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
            _hasUnsavedChanges = false;
        }
        catch
        {
            // Windows lock fallback
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }
}
