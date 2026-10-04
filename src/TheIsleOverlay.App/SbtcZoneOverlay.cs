using System.Globalization;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public enum SbtcZoneKind
{
    Location,
    Sanctuary,
    Migration,
    Patrol,
    Uncategorized,
    Wildlife
}

public sealed record SbtcZoneFeature(
    string Name,
    SbtcZoneKind Kind,
    IReadOnlyList<MapPoint> Points,
    string? Shape = null,
    double? Size = null,
    string? Color = null,
    string? Icon = null,
    bool HideLabel = false,
    MapPoint? LabelLocation = null);

public sealed record SbtcZoneLabel(
    string Name,
    SbtcZoneKind Kind,
    MapPoint Center,
    string? Shape,
    double? Size,
    string? Color);

public static class SbtcZoneOverlay
{
    // Match IslePilot's patrol palette even when the SBTC feed supplies blue.
    public const string PatrolColor = "#A78BFA";

    public static IReadOnlyList<SbtcZoneFeature> Create(
        string? serverName,
        IReadOnlyList<MapPointOfInterestTelemetry>? pointsOfInterest,
        IReadOnlyList<SbtcZoneFeature>? fallback = null,
        bool isIslePilotServer = false,
        bool hasHostZones = false)
    {
        // Live zones are only drawn while the host map feed is actually delivering them
        // (otherwise a server that stops sending them would leave stale markers on the
        // map). The bundled catalogue is different: it belongs to the map itself, so it
        // is drawn for every server and even before a dinosaur is loaded.
        var hasLiveZones = pointsOfInterest is { Count: > 0 };
        if (hasLiveZones && !hasHostZones)
        {
            return fallback ?? [];
        }

        if (hasLiveZones && !isIslePilotServer && !IsSbtcServer(serverName))
        {
            return fallback ?? [];
        }

        var liveFeatures = (pointsOfInterest ?? [])
            .Select(CreateFeature)
            .Where(feature => feature is not null)
            .Select(feature => feature!)
            .ToArray();
        if (liveFeatures.Length == 0) return fallback ?? [];
        if (liveFeatures.Any(feature => IsFilterableZone(feature.Kind))) return liveFeatures;

        // Live AI and place labels do not replace the map's zone catalogue.
        // A failed zone request can still leave a perfectly usable AI feed.
        return (fallback?.Where(feature => IsFilterableZone(feature.Kind)) ?? [])
            .Concat(liveFeatures).ToArray();
    }

    internal static bool IsFilterableZone(SbtcZoneKind kind) =>
        kind is SbtcZoneKind.Patrol or SbtcZoneKind.Migration or SbtcZoneKind.Sanctuary;

    public static bool IsSbtcServer(string? serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return false;
        }

        var normalized = new string(serverName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalized.Contains("sbtc", StringComparison.Ordinal);
    }

    public static string Signature(IReadOnlyList<SbtcZoneFeature> features) => string.Join(
        '|',
        features.Select(FeatureSignature));

    private static string FeatureSignature(SbtcZoneFeature feature)
    {
        var points = string.Join(';', feature.Points.Select(point => string.Create(
            CultureInfo.InvariantCulture, $"{point.Left:R},{point.Top:R}")));
        return string.Create(CultureInfo.InvariantCulture,
            $"{feature.Kind}:{feature.Name}:{feature.Shape}:{feature.Size:R}:{feature.Color}:{feature.Icon}:{feature.HideLabel}:{feature.LabelLocation?.Left:R}:{feature.LabelLocation?.Top:R}:{points}");
    }

    public static IReadOnlyList<SbtcZoneLabel> CreateLabels(IReadOnlyList<SbtcZoneFeature> features) => features
        .Where(feature => feature.Points.Count > 0 &&
                          feature.Kind != SbtcZoneKind.Wildlife &&
                          !feature.HideLabel &&
                          !string.IsNullOrWhiteSpace(feature.Name) && feature.LabelLocation is null)
        .GroupBy(
            feature => (feature.Kind, Name: CleanName(feature.Name)),
            new ZoneLabelKeyComparer())
        .Select(group =>
        {
            var representative = group.First();
            var centers = group.Select(feature => new MapPoint(
                feature.Points.Average(point => point.Left),
                feature.Points.Average(point => point.Top))).ToArray();
            return new SbtcZoneLabel(
                CleanName(representative.Name),
                group.Key.Kind,
                new MapPoint(
                    centers.Average(center => center.Left),
                    centers.Average(center => center.Top)),
                representative.Shape,
                representative.Size,
                representative.Color);
        })
        .Concat(features.Where(feature => feature.Kind != SbtcZoneKind.Wildlife && !feature.HideLabel && feature.LabelLocation is not null &&
                                          !string.IsNullOrWhiteSpace(feature.Name))
            .Select(feature => new SbtcZoneLabel(CleanName(feature.Name), feature.Kind,
                feature.LabelLocation!.Value, feature.Shape, feature.Size, feature.Color)))
        .ToArray();

    private static SbtcZoneFeature? CreateFeature(MapPointOfInterestTelemetry poi)
    {
        if (string.IsNullOrWhiteSpace(poi.Name))
        {
            return null;
        }

        var points = poi.Points
            .Where(point => double.IsFinite(point.Left) && double.IsFinite(point.Top))
            .ToArray();
        if (points.Length == 0)
        {
            return null;
        }

        var classification = $"{poi.CategoryName} {poi.CategoryId} {poi.Name}".ToLowerInvariant();
        SbtcZoneKind? kind = classification switch
        {
            _ when poi.CategoryId == "wildlife" => SbtcZoneKind.Wildlife,
            var value when value.Contains("sanctuar") => SbtcZoneKind.Sanctuary,
            var value when value.Contains("migration") || value.Contains("mmz") => SbtcZoneKind.Migration,
            var value when value.Contains("patrol") => SbtcZoneKind.Patrol,
            var value when value.Contains("location") => SbtcZoneKind.Location,
            var value when value.Contains("uncategor") => SbtcZoneKind.Uncategorized,
            // The server's own site publishes areas, waters and landmarks next to the
            // zones; those four are the categories it shows by default, so they are drawn
            // as plain markers while anything else stays out to keep the map readable.
            _ when IsSiteCategory(poi.CategoryId) => SbtcZoneKind.Uncategorized,
            _ when !string.IsNullOrWhiteSpace(poi.Shape) && string.IsNullOrWhiteSpace(poi.CategoryId) =>
                SbtcZoneKind.Uncategorized,
            _ when points.Length >= 3 => SbtcZoneKind.Uncategorized,
            _ => null
        };

        // IslePilot's SBTC map starts with Locations disabled. Rendering those
        // point labels together with Patrol polygons creates misleading duplicate
        // names such as West Rail and Delta.
        if (kind is null or SbtcZoneKind.Location)
        {
            return null;
        }

        var size = poi.Size is > 0d and <= 0.5d ? poi.Size : null;
        return new SbtcZoneFeature(
            CleanName(poi.Name),
            kind.Value,
            points,
            poi.Shape?.Trim().ToLowerInvariant(),
            size,
            kind == SbtcZoneKind.Patrol ? PatrolColor : poi.Color,
            poi.Icon,
            poi.HideLabel == true,
            poi.LabelLocation is { } anchor && double.IsFinite(anchor.Left) && double.IsFinite(anchor.Top)
                ? anchor : null);
    }

    private static bool IsSiteCategory(string? categoryId) =>
        categoryId is "areas" or "waters" or "landmarks" or "sanctuaries";

    private static string CleanName(string name) => string.Join(
        ' ',
        name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class ZoneLabelKeyComparer : IEqualityComparer<(SbtcZoneKind Kind, string Name)>
    {
        public bool Equals((SbtcZoneKind Kind, string Name) x, (SbtcZoneKind Kind, string Name) y) =>
            x.Kind == y.Kind && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((SbtcZoneKind Kind, string Name) value) =>
            HashCode.Combine(value.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name));
    }
}
