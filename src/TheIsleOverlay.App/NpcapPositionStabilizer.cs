using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

internal sealed class NpcapPositionStabilizer
{
    internal static readonly TimeSpan SourceHoldDuration = TimeSpan.FromSeconds(8);
    private const double MinimumJumpAllowance = 5_000d;
    private const double MaximumSpeedAllowance = 15_000d;
    private const double SmoothingTimeSeconds = 0.08d;

    private readonly object _syncRoot = new();
    private NpcapPositionSample? _lastRaw;
    private WorldLocation? _smoothed;

    public void Seed(WorldLocation location)
    {
        lock (_syncRoot)
        {
            _smoothed = location;
            if (_lastRaw is { } raw)
            {
                _lastRaw = raw with { Location = location };
            }
            else
            {
                _lastRaw = new NpcapPositionSample(location, DateTimeOffset.UtcNow, 0f);
            }
        }
    }

    public bool TryStabilize(NpcapPositionSample sample, out NpcapPositionSample stabilized)
    {
        lock (_syncRoot)
        {
            stabilized = default;
            if (_lastRaw is not { } previous || _smoothed is null)
            {
                _lastRaw = sample;
                _smoothed = sample.Location;
                stabilized = sample;
                return true;
            }

            var elapsed = (sample.CapturedAt - previous.CapturedAt).TotalSeconds;
            if (elapsed <= 0d)
            {
                return false;
            }

            // A packet with an uninitialized/zero location is a replication gap,
            // not a real teleport to the world origin. Keep the last stable point
            // while the player is standing still or the game briefly omits a field.
            if (Math.Abs(sample.Location.X) < 5_000d && Math.Abs(sample.Location.Y) < 5_000d &&
                (Math.Abs(previous.Location.X) >= 5_000d || Math.Abs(previous.Location.Y) >= 5_000d))
            {
                return false;
            }

            if (elapsed <= SourceHoldDuration.TotalSeconds)
            {
                var distance = PlanarDistance(previous.Location, sample.Location);
                var allowed = Math.Max(MinimumJumpAllowance, elapsed * MaximumSpeedAllowance);
                if (distance > allowed)
                {
                    return false;
                }
            }

            _lastRaw = sample;
            if (elapsed > SourceHoldDuration.TotalSeconds)
            {
                _smoothed = sample.Location;
            }
            else
            {
                var alpha = 1d - Math.Exp(-elapsed / SmoothingTimeSeconds);
                _smoothed = new WorldLocation
                {
                    X = Lerp(_smoothed.X, sample.Location.X, alpha),
                    Y = Lerp(_smoothed.Y, sample.Location.Y, alpha),
                    Z = sample.Location.Z is { } z
                        ? Lerp(_smoothed.Z ?? z, z, alpha)
                        : _smoothed.Z
                };
            }

            stabilized = sample with { Location = _smoothed };
            return true;
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            _lastRaw = null;
            _smoothed = null;
        }
    }

    private static double PlanarDistance(WorldLocation first, WorldLocation second) =>
        Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));

    private static double Lerp(double from, double to, double amount) =>
        from + (to - from) * amount;
}
