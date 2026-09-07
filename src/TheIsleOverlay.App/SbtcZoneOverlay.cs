using System.Globalization;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public enum SbtcZoneKind
{
    Location,
    Sanctuary,
    Migration,
    Patrol,
    Uncategorized
}

public sealed record SbtcZoneFeature(
    string Name,
    SbtcZoneKind Kind,
    IReadOnlyList<MapPoint> Points,
    string? Shape = null,
    double? Size = null,
    string? Color = null,
    string? Icon = null,
    bool HideLabel = false);

public sealed record SbtcZoneLabel(
    string Name,
    SbtcZoneKind Kind,
    MapPoint Center,
    string? Shape,
    double? Size,
    string? Color);

public static class SbtcZoneOverlay
{
    public static IReadOnlyList<SbtcZoneFeature> Create(
        string? serverName,
        IReadOnlyList<MapPointOfInterestTelemetry>? pointsOfInterest,
        IReadOnlyList<SbtcZoneFeature>? fallback = null,
        bool isIslePilotServer = false)
    {
        if (!isIslePilotServer && !IsSbtcServer(serverName))
        {
            return [];
        }

        var liveFeatures = (pointsOfInterest ?? [])
            .Select(CreateFeature)
            .Where(feature => feature is not null)
            .Select(feature => feature!)
            .ToArray();
        return liveFeatures.Length > 0 ? liveFeatures : fallback ?? [];
    }

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
        features.Select(feature => string.Create(
            CultureInfo.InvariantCulture,
            $"{feature.Kind}:{feature.Name}:{feature.Shape}:{feature.Size:0.######}:{feature.Color}:{feature.Icon}:{feature.HideLabel}:{feature.Points.Count}:{feature.Points.FirstOrDefault().Left:0.######}:{feature.Points.FirstOrDefault().Top:0.######}")));

    public static IReadOnlyList<SbtcZoneLabel> CreateLabels(IReadOnlyList<SbtcZoneFeature> features) => features
        .Where(feature => feature.Points.Count > 0 &&
                          !feature.HideLabel &&
                          !string.IsNullOrWhiteSpace(feature.Name))
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
            var value when value.Contains("sanctuar") => SbtcZoneKind.Sanctuary,
            var value when value.Contains("migration") || value.Contains("mmz") => SbtcZoneKind.Migration,
            var value when value.Contains("patrol") => SbtcZoneKind.Patrol,
            var value when value.Contains("location") => SbtcZoneKind.Location,
            var value when value.Contains("uncategor") => SbtcZoneKind.Uncategorized,
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
            poi.Color,
            poi.Icon,
            poi.HideLabel == true);
    }

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
