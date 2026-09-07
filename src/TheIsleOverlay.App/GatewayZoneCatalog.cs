using System.IO;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public static class GatewayZoneCatalog
{
    public static IReadOnlyList<SbtcZoneFeature> Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var document = JsonSerializer.Deserialize<CatalogDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Gateway zone catalog is empty.");

        return document.Zones
            .Where(zone => !string.IsNullOrWhiteSpace(zone.Name) && zone.Points.Count >= 3)
            .Select(zone => new SbtcZoneFeature(
                zone.Name!.Trim(),
                ParseKind(zone.Kind),
                zone.Points.Select(point => new MapPoint(point.Left, point.Top)).ToArray()))
            .ToArray();
    }

    private static SbtcZoneKind ParseKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "sanctuary" => SbtcZoneKind.Sanctuary,
        "migration" => SbtcZoneKind.Migration,
        "patrol" => SbtcZoneKind.Patrol,
        "location" => SbtcZoneKind.Location,
        _ => SbtcZoneKind.Uncategorized
    };

    private sealed record CatalogDocument
    {
        public IReadOnlyList<CatalogZone> Zones { get; init; } = [];
    }

    private sealed record CatalogZone
    {
        public string? Name { get; init; }
        public string? Kind { get; init; }
        public IReadOnlyList<CatalogPoint> Points { get; init; } = [];
    }

    private sealed record CatalogPoint
    {
        public double Left { get; init; }
        public double Top { get; init; }
    }
}
