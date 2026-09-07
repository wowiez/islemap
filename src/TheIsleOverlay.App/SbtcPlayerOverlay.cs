using System.Globalization;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public sealed record SbtcPlayerMarker(
    string? SteamId,
    string Label,
    bool Group,
    MapPoint Location,
    double? HeadingDegrees);

public static class SbtcPlayerOverlay
{
    public static IReadOnlyList<SbtcPlayerMarker> Create(
        string? serverName,
        IReadOnlyList<MapMarkerTelemetry>? markers,
        bool isIslePilotServer = false)
    {
        if (!isIslePilotServer && !SbtcZoneOverlay.IsSbtcServer(serverName))
        {
            return [];
        }

        return (markers ?? [])
            .Where(marker => !marker.Self &&
                             marker.MapLocation is not null &&
                             !string.IsNullOrWhiteSpace(marker.Label))
            .GroupBy(MarkerKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(marker => new SbtcPlayerMarker(
                marker.SteamId,
                marker.Label!.Trim(),
                marker.Group,
                marker.MapLocation!.Value,
                marker.ExactMapHeadingDegrees))
            .ToArray();
    }

    public static string Signature(IReadOnlyList<SbtcPlayerMarker> markers) => string.Join(
        '|',
        markers.Select(marker => string.Create(
            CultureInfo.InvariantCulture,
            $"{marker.SteamId}:{marker.Label}:{marker.Group}:{marker.Location.Left:0.######}:{marker.Location.Top:0.######}:{marker.HeadingDegrees:0.###}")));

    private static string MarkerKey(MapMarkerTelemetry marker) =>
        !string.IsNullOrWhiteSpace(marker.SteamId)
            ? $"steam:{marker.SteamId}"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"location:{marker.Label?.Trim()}:{marker.MapLocation?.Left:0.######}:{marker.MapLocation?.Top:0.######}");
}
