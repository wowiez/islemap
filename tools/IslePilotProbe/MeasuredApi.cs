using System.Diagnostics;
using TheIsleOverlay.IslePilot;

internal sealed class MeasuredApi(IIslePilotOverlayApiClient inner, Stopwatch clock) : IIslePilotOverlayApiClient
{
    private string? _steamId;
    private readonly Dictionary<string, (double?, double?, double?)> _positions = new();
    private readonly Dictionary<string, double> _lastChanges = new();
    private readonly object _gate = new();

    public async Task<IslePilotOverlayMeDto> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.GetMeAsync(cancellationToken);
        _steamId = result.SteamId;
        Console.WriteLine($"raw t={clock.Elapsed.TotalSeconds:F3} endpoint=me rttMs={sw.Elapsed.TotalMilliseconds:F0} online={result.Online}");
        return result;
    }

    public async Task<IslePilotOverlayMapDto> GetMapAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.GetMapAsync(cancellationToken);
        Report("map", result.Markers, sw.Elapsed.TotalMilliseconds);
        return result;
    }

    public async Task<IslePilotOverlayMarkersDto> GetMarkersAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.GetMarkersAsync(cancellationToken);
        Report("markers", result.Markers, sw.Elapsed.TotalMilliseconds);
        return result;
    }

    private void Report(string endpoint, IReadOnlyList<IslePilotOverlayMapMarkerDto> markers, double rtt)
    {
        var self = markers.FirstOrDefault(m => _steamId is not null && m.SteamId == _steamId) ?? markers.FirstOrDefault(m => m.Self);
        lock (_gate)
        {
            var now = clock.Elapsed.TotalSeconds;
            var pos = (self?.X, self?.Y, self?.Yaw);
            var had = _positions.TryGetValue(endpoint, out var old);
            var changed = had && self?.X is not null && self.Y is not null && (pos.Item1 != old.Item1 || pos.Item2 != old.Item2);
            var gap = changed && _lastChanges.TryGetValue(endpoint, out var last) ? (now - last).ToString("F3") : "na";
            if (changed) _lastChanges[endpoint] = now;
            _positions[endpoint] = pos;
            Console.WriteLine($"raw t={now:F3} endpoint={endpoint} rttMs={rtt:F0} self={self is not null} xyChanged={changed} yawChanged={had && pos.Item3 != old.Item3} changeGap={gap}");
        }
    }
}
