using System.Globalization;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public static class MapOverlayPresentation
{
    public static string Distance(MapPoint current, MapPoint target)
    {
        var meters = Math.Max(0d, GatewayMapProjection.DistanceMeters(current, target));
        return $"{Math.Round(meters, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)} m";
    }

    public static string PlayerLabel(
        string name,
        MapPoint playerLocation,
        MapPoint? currentLocation) => currentLocation is { } current
        ? $"{name} ({Distance(current, playerLocation)})"
        : name;

    public static string RequestStatus(
        TelemetrySessionState sessionState,
        string? requestStatus) => requestStatus ?? sessionState switch
    {
        TelemetrySessionState.Polling => "REST POLLING",
        TelemetrySessionState.Stale => "WS · DATA STALE · REST FALLBACK",
        TelemetrySessionState.Reconnecting => "WS · RECONNECTING · REST FALLBACK",
        _ => string.Empty
    };
}
