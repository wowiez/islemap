using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.Sbtc;

/// <summary>
/// Telemetry for SBTC Island (sbtcislandd.com).
///
/// The site polls the game server itself, so a signed-in player gets two feeds with the
/// Steam session cookie the login window captured:
/// <list type="bullet">
/// <item>/api/positions — the player's own position and heading, plus friends/group/squad
/// pins when sharing is on. Entries carry either the map pixels the site draws with or the
/// game's own UE coordinates.</item>
/// <item>/api/live — the dino card: species, growth, vitals (health, stamina, hunger,
/// thirst, oxygen, blood), diet sliders, bleeding and fracture.</item>
/// </list>
/// </summary>
public sealed class SbtcIslandTelemetryProvider : ITelemetryProvider
{
    // The site speaks snake_case (signed_in, growth_percent, ue_x, ...), so the naming
    // policy has to follow it or every nested field silently stays null.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // The site's own calibration (assets/maps/gateway_v0217.json): canvas pixels are
    // px = (ax * ue_x + cx) / cal_s against an 8192 canvas that carries the 7800x7817
    // picture at (196, 187). Only used when a feed reports pixels instead of UE
    // coordinates; the values then match GatewayMapProjection exactly.
    private const double CalibrationAx = 0.007014388489208633d;
    private const double CalibrationBx = 0.0d;
    private const double CalibrationCx = 3738.26618705036d;
    private const double CalibrationAy = 0.0d;
    private const double CalibrationBy = 0.007004480286738351d;
    private const double CalibrationCy = 4438.719534050179d;
    private const double CalibrationScale = 8.0d;

    private readonly HttpClient _httpClient;
    private readonly SbtcIslandOptions _options;
    private readonly TimeProvider _timeProvider;

    public SbtcIslandTelemetryProvider(HttpClient httpClient, SbtcIslandOptions options, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (string.IsNullOrWhiteSpace(options.SessionCookieHeader))
        {
            throw new ArgumentException("An SBTC Island Steam session is required.", nameof(options));
        }

        if (options.SessionCookieHeader.Contains('\r') || options.SessionCookieHeader.Contains('\n'))
        {
            throw new ArgumentException("The SBTC Island cookie header is invalid.", nameof(options));
        }
    }

    public async Task<TelemetrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var positions = await GetAsync<SbtcPositionsResponse>("api/positions", includePlatform: true, cancellationToken);
        var live = await GetAsync<SbtcLiveResponse>("api/live", includePlatform: false, cancellationToken);

        if (positions is null && live is null)
        {
            throw new InvalidDataException("SBTC Island returned no usable response.");
        }

        // Both endpoints answer 200 with signed_in=false instead of 401, so a session that
        // expired has to be recognised from the payload.
        var signedIn = (positions?.SignedIn ?? false) || (live?.SignedIn ?? false);
        if (!signedIn && (positions is not null || live is not null))
        {
            throw new SbtcIslandAuthenticationException(
                "Phiên SBTC Island đã hết hạn. Đăng nhập Steam lại để xem chỉ số.");
        }

        var pin = SelfPin(positions);
        var dino = live?.Dino;
        var location = ToWorldLocation(pin);
        var playerOnline = dino is not null || pin is not null;

        return new TelemetrySnapshot
        {
            Source = "SBTC ISLAND",
            Success = true,
            ServerOnline = live?.IslandOnline ?? positions?.Fresh ?? false,
            PlayerOnline = playerOnline,
            UpdatedAt = DateTimeOffset.UtcNow,
            Player = playerOnline
                ? new PlayerTelemetry
                {
                    SteamId = pin?.SteamId,
                    Name = pin?.Name,
                    Class = dino?.Species,
                    Server = ServerName(positions?.Island),
                    GrowthPercent = GrowthPercent(dino),
                    HealthPercent = VitalPercent(dino, "health"),
                    StaminaPercent = VitalPercent(dino, "stamina"),
                    HungerPercent = VitalPercent(dino, "hunger"),
                    ThirstPercent = VitalPercent(dino, "thirst"),
                    OxygenPercent = VitalPercent(dino, "oxygen"),
                    BloodPercent = VitalPercent(dino, "blood"),
                    FracturePercent = dino?.FracturePercent ?? VitalPercent(dino, "fracture"),
                    BleedingStacks = dino?.BleedingStacks ?? (int?)VitalPercent(dino, "bleeding"),
                    LifeStage = dino?.LifeStage,
                    ElderStacks = dino?.ElderStacks,
                    Nutrition = ToNutrition(dino),
                    ExactVitalsSource = "SBTC ISLAND",
                    Location = location,
                    MapLocation = ToMapLocation(pin),
                    ExactMapHeadingDegrees = pin?.Yaw is { } yaw ? MapHeading.FromUnrealYaw(yaw) : null,
                    Prime = ToPrime(dino)
                }
                : null,
            Map = await ToMapAsync(positions, cancellationToken)
        };
    }

    private async Task<T?> GetAsync<T>(string path, bool includePlatform, CancellationToken cancellationToken)
        where T : class
    {
        var query = includePlatform && !string.IsNullOrWhiteSpace(_options.Platform)
            ? $"?platform={Uri.EscapeDataString(_options.Platform)}"
            : string.Empty;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.BaseUri, path + query));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Cookie", _options.SessionCookieHeader.Trim());

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SbtcIslandAuthenticationException(
                "Phiên SBTC Island đã hết hạn. Đăng nhập Steam lại để xem chỉ số.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    // The overlay only draws its player and zone layers when the server name says SBTC,
    // so the island the feed reports is folded into that label.
    private static string ServerName(string? island) =>
        string.IsNullOrWhiteSpace(island) || island.Contains("sbtc", StringComparison.OrdinalIgnoreCase)
            ? "SBTC Island"
            : $"SBTC Island · {island}";

    private static readonly TimeSpan MapDataLifetime = TimeSpan.FromMinutes(10);
    // The visual categories the site turns on by default, plus the three zone layers
    // its own map draws (sanctuaries, migration zones, patrol zones).
    private static readonly string[] DefaultMapCategories =
        ["areas", "waters", "landmarks", "sanctuaries", "migrations", "patrol_zones"];
    private readonly object _mapDataLock = new();
    private IReadOnlyList<MapPointOfInterestTelemetry>? _mapPointsCache;
    private DateTimeOffset _mapPointsAt;
    private SbtcMapConfigResponse? _mapConfig;
    private IReadOnlyList<MapPointOfInterestTelemetry>? _wildlifeCache;
    private DateTimeOffset _wildlifeAt;

    /// <summary>
    /// The site's own map data (assets/data/map_pois.json plus the categories in
    /// mapconfig.json). Both files are public, so the zones the server draws on its own
    /// website are exactly the zones the overlay draws, instead of the bundled set.
    /// </summary>
    private async Task<IReadOnlyList<MapPointOfInterestTelemetry>> GetMapPointsAsync(CancellationToken cancellationToken)
    {
        lock (_mapDataLock)
        {
            if (_mapPointsCache is not null && _timeProvider.GetUtcNow() - _mapPointsAt < MapDataLifetime)
            {
                return _mapPointsCache;
            }
        }

        var config = await GetAsync<SbtcMapConfigResponse>("assets/maps/mapconfig.json", includePlatform: false, cancellationToken);
        _mapConfig = config;
        var pois = await GetAsync<SbtcMapPoisResponse>("assets/data/map_pois.json", includePlatform: false, cancellationToken);
        if (pois?.Categories is null)
        {
            throw new InvalidDataException("SBTC Island returned no map catalogue.");
        }

        var defaults = config?.DefaultOn is { Count: > 0 } on ? on : DefaultMapCategories;
        // Keep the overlay's zone layers present even when the website starts their
        // checkboxes off. Its defaults also include ordinary place/water labels.
        var wanted = defaults.Concat(["sanctuaries", "migrations", "patrol_zones"])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var colors = config?.Categories ?? new Dictionary<string, SbtcMapCategory>();
        var points = new List<MapPointOfInterestTelemetry>();
        foreach (var category in wanted)
        {
            if (!pois.Categories.TryGetValue(category, out var group) || group?.Items is null)
            {
                continue;
            }

            colors.TryGetValue(category, out var meta);
            var color = NormalizeColor(meta?.Color);
            var label = config?.Labels is { } labels && labels.TryGetValue(category, out var text) ? text : category;
            var index = 0;
            foreach (var item in group.Items)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Name) || item.X is not { } px || item.Y is not { } py)
                {
                    continue;
                }

                var point = CanvasToMapPoint(px, py);
                if (point is null)
                {
                    continue;
                }

                // A zone arrives either as a polygon (pixel offsets from x/y) or as a
                // circle with a pixel radius; everything else is a single marker.
                var outline = new List<MapPoint>();
                if (item.Polygon is { Count: >= 3 })
                {
                    foreach (var offset in item.Polygon)
                    {
                        if (offset is { Count: >= 2 } && CanvasToMapPoint(px + offset[0], py + offset[1]) is { } vertex)
                        {
                            outline.Add(vertex);
                        }
                    }
                }

                // map.js zoneRing draws circles as 40 vertices in canvas pixels. Use
                // the same outline so radius, aspect ratio and image padding all match.
                var radius = item.Radius is > 0 ? item.Radius :
                    meta?.Zone == true || group.Style == "zone" ? group.Radius : null;
                if (outline.Count < 3 && radius is > 0 && double.IsFinite(radius.Value))
                {
                    outline.Clear();
                    for (var vertex = 0; vertex < 40; vertex++)
                    {
                        var angle = vertex * Math.PI * 2d / 40d;
                        if (CanvasToMapPoint(px + radius.Value * Math.Cos(angle),
                                py + radius.Value * Math.Sin(angle)) is { } circlePoint)
                            outline.Add(circlePoint);
                    }
                }
                points.Add(new MapPointOfInterestTelemetry
                {
                    Id = category + ":" + index++,
                    Name = item.Name,
                    CategoryId = category,
                    CategoryName = label ?? category,
                    Shape = outline.Count >= 3 ? "polygon" : meta?.Label == true ? "label" : null,
                    Color = color,
                    HideLabel = !(meta?.Label == true || group.Labels == "always"),
                    LabelLocation = point,
                    Points = outline.Count >= 3 ? outline : [point.Value]
                });
            }
        }

        lock (_mapDataLock)
        {
            _mapPointsCache = points;
            _mapPointsAt = _timeProvider.GetUtcNow();
        }

        return points;
    }

    /// <summary>Canvas pixels back to the map fraction the overlay draws in.</summary>
    private static MapPoint? CanvasToMapPoint(double px, double py)
    {
        const double canvasScale = 8d;
        const double pictureLeft = 196d;
        const double pictureTop = 187d;
        const double pictureWidth = 7800d;
        const double pictureHeight = 7817d;
        var left = ((px * canvasScale) - pictureLeft) / pictureWidth;
        var top = ((py * canvasScale) - pictureTop) / pictureHeight;
        return double.IsFinite(left) && double.IsFinite(top) ? new MapPoint(left, top) : null;
    }

    private static string? NormalizeColor(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.StartsWith('#') ? value.ToUpperInvariant() : "#" + value.ToUpperInvariant();

    private async Task<IReadOnlyList<MapPointOfInterestTelemetry>> GetWildlifeAsync(CancellationToken cancellationToken)
    {
        if (_mapConfig?.Wildlife == false) return [];
        var now = _timeProvider.GetUtcNow();
        if (_wildlifeCache is not null && now - _wildlifeAt < TimeSpan.FromSeconds(8)) return _wildlifeCache;

        var points = new List<MapPointOfInterestTelemetry>();
        try
        {
            // This is the public, live wildlife feed used by map.js, rather than
            // the static animal/spawn POIs. No Steam cookie is needed or sent.
            var query = string.IsNullOrWhiteSpace(_options.Platform)
                ? string.Empty : $"?platform={Uri.EscapeDataString(_options.Platform)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.BaseUri, "api/ai_positions" + query));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var body = await JsonSerializer.DeserializeAsync<SbtcWildlifeResponse>(stream, JsonOptions, timeout.Token);
            if (body?.Status == "ok")
            {
                foreach (var animal in body.Ai ?? [])
                {
                    if (animal is null || string.IsNullOrWhiteSpace(animal.Species) ||
                        animal.UeX is not { } x || animal.UeY is not { } y ||
                        !double.IsFinite(x) || !double.IsFinite(y) || x == 0d && y == 0d) continue;
                    var point = ToMapLocation(new SbtcPlayerPin { UeX = x, UeY = y });
                    if (point is null) continue;
                    var species = animal.Species.Trim();
                    var group = _mapConfig?.AiGroups?.GetValueOrDefault(species) ?? "other";
                    var color = _mapConfig?.AiColors?.GetValueOrDefault(group)
                        ?? _mapConfig?.AiColors?.GetValueOrDefault("other") ?? _mapConfig?.FallbackColor ?? "#8A8A8A";
                    points.Add(new MapPointOfInterestTelemetry
                    {
                        Id = $"wildlife:{points.Count}", Name = species, CategoryId = "wildlife",
                        CategoryName = "AI creatures", Shape = "wildlife", Color = NormalizeColor(color),
                        HideLabel = true, Points = [point.Value]
                    });
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException ||
                                          exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // A missing/failed wildlife source must not interrupt vitals, friends
            // or map zones. Clear old animals instead of showing stale positions.
        }
        _wildlifeAt = _timeProvider.GetUtcNow();
        return _wildlifeCache = points;
    }

    private static SbtcPlayerPin? SelfPin(SbtcPositionsResponse? positions)
    {
        if (positions is null)
        {
            return null;
        }

        if (positions.You is { } you)
        {
            return you;
        }

        foreach (var pin in positions.Positions ?? [])
        {
            if (pin.You)
            {
                return pin;
            }
        }

        return null;
    }

    private async Task<MapTelemetry?> ToMapAsync(SbtcPositionsResponse? positions, CancellationToken cancellationToken)
    {
        if (positions is null)
        {
            return null;
        }

        var markers = new List<MapMarkerTelemetry>();
        var merged = new List<SbtcPlayerPin>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var pins = positions.You is { } self
            ? (positions.Positions ?? []).Prepend(self with { You = true })
            : positions.Positions ?? [];
        foreach (var pin in pins
            .Concat((positions.Friends ?? []).Where(pin => pin.Friend))
            .Concat((positions.Group ?? []).Where(pin => pin.Group))
            .Concat((positions.Squad ?? []).Where(pin => pin.Squad)))
        {
            if (!string.IsNullOrWhiteSpace(pin.SteamId) && seen.TryGetValue(pin.SteamId, out var index))
            {
                var previous = merged[index];
                // Merge flags and names like map.js before drawing each Steam ID.
                var basis = ToMapLocation(previous) is null ? pin : previous;
                merged[index] = basis with
                {
                    You = previous.You || pin.You,
                    Friend = previous.Friend || pin.Friend,
                    Group = previous.Group || pin.Group,
                    Squad = previous.Squad || pin.Squad,
                    Name = string.IsNullOrWhiteSpace(previous.Name) ? pin.Name : previous.Name,
                    Species = string.IsNullOrWhiteSpace(previous.Species) ? pin.Species : previous.Species
                };
                continue;
            }
            if (!string.IsNullOrWhiteSpace(pin.SteamId)) seen[pin.SteamId] = merged.Count;
            merged.Add(pin);
        }
        foreach (var pin in merged)
        {
            var mapLocation = ToMapLocation(pin);
            if (mapLocation is null)
            {
                continue;
            }

            markers.Add(new MapMarkerTelemetry
            {
                SteamId = pin.SteamId,
                Label = !string.IsNullOrWhiteSpace(pin.Name) ? pin.Name :
                    !string.IsNullOrWhiteSpace(pin.Species) ? pin.Species : "Người chơi",
                Self = pin.You,
                Group = pin.Friend || pin.Group || pin.Squad,
                Friend = pin.Friend,
                Location = ToWorldLocation(pin),
                MapLocation = mapLocation,
                ExactMapHeadingDegrees = pin.Yaw is { } yaw && double.IsFinite(yaw)
                    ? MapHeading.FromUnrealYaw(yaw) : null
            });
        }

        IReadOnlyList<MapPointOfInterestTelemetry> pointsOfInterest;
        try
        {
            pointsOfInterest = await GetMapPointsAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidDataException)
        {
            // Static map geometry remains usable when its refresh fails. Keep the
            // last successful catalogue for this provider; live AI is independent.
            lock (_mapDataLock) pointsOfInterest = _mapPointsCache ?? [];
        }

        return new MapTelemetry
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            Markers = markers,
            PointsOfInterest = pointsOfInterest.Concat(await GetWildlifeAsync(cancellationToken)).ToArray()
        };
    }

    /// <summary>
    /// Match map.js: canvas pixels take precedence over UE coordinates for drawing.
    /// </summary>
    private static MapPoint? ToMapLocation(SbtcPlayerPin? pin)
    {
        if (pin is null) return null;
        double px, py;
        if (pin.X is { } x && pin.Y is { } y)
        {
            px = x;
            py = y;
        }
        else if (pin.UeX is { } ueX && pin.UeY is { } ueY)
        {
            px = (CalibrationAx * ueX + CalibrationBx * ueY + CalibrationCx) / CalibrationScale;
            py = (CalibrationAy * ueX + CalibrationBy * ueY + CalibrationCy) / CalibrationScale;
        }
        else return null;
        return double.IsFinite(px) && double.IsFinite(py) && px is >= 0 and <= 1024 && py is >= 0 and <= 1024
            ? CanvasToMapPoint(px, py) : null;
    }

    /// <summary>
    /// Prefers the game's own coordinates; falls back to the site's canvas pixels, which
    /// the calibration above turns back into world coordinates.
    /// </summary>
    private static WorldLocation? ToWorldLocation(SbtcPlayerPin? pin)
    {
        if (pin is null)
        {
            return null;
        }

        if (pin.UeX is { } ueX && pin.UeY is { } ueY && double.IsFinite(ueX) && double.IsFinite(ueY))
        {
            return new WorldLocation { X = ueX, Y = ueY };
        }

        if (pin.X is { } px && pin.Y is { } py && double.IsFinite(px) && double.IsFinite(py))
        {
            var rx = (px * CalibrationScale) - CalibrationCx;
            var ry = (py * CalibrationScale) - CalibrationCy;
            var determinant = (CalibrationAx * CalibrationBy) - (CalibrationBx * CalibrationAy);
            if (Math.Abs(determinant) < double.Epsilon)
            {
                return null;
            }

            return new WorldLocation
            {
                X = ((CalibrationBy * rx) - (CalibrationBx * ry)) / determinant,
                Y = ((CalibrationAx * ry) - (CalibrationAy * rx)) / determinant
            };
        }

        return null;
    }

    private static double? GrowthPercent(SbtcDino? dino)
    {
        if (dino is null)
        {
            return null;
        }

        if (dino.GrowthPercent is { } percent)
        {
            return percent;
        }

        return dino.Growth is { } growth ? growth * 100d : null;
    }

    private static double? VitalPercent(SbtcDino? dino, string key)
    {
        if (dino is null)
        {
            return null;
        }

        foreach (var vital in dino.Vitals)
        {
            if (string.Equals(vital.Key, key, StringComparison.OrdinalIgnoreCase) && vital.Known)
            {
                return vital.Percent;
            }
        }

        return null;
    }

    private static NutritionTelemetry? ToNutrition(SbtcDino? dino)
    {
        if (dino is null || dino.Diet.Count == 0)
        {
            return null;
        }

        var nutrition = new NutritionTelemetry
        {
            Carb = DietPercent(dino, "diet_a"),
            Protein = DietPercent(dino, "diet_b"),
            Lipid = DietPercent(dino, "diet_c")
        };

        return nutrition.Carb is null && nutrition.Protein is null && nutrition.Lipid is null
            ? null
            : nutrition;
    }

    private static double? DietPercent(SbtcDino dino, string key)
    {
        foreach (var entry in dino.Diet)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase) && entry.Known)
            {
                return entry.Percent;
            }
        }

        return null;
    }

    private static PrimeTelemetry? ToPrime(SbtcDino? dino)
    {
        if (dino is null || (dino.IsPrime is null && dino.IsElder is null && dino.ElderStacks is null))
        {
            return null;
        }

        return new PrimeTelemetry
        {
            IsPrime = dino.IsPrime,
            Elder = dino.IsElder,
            Done = dino.ElderStacks
        };
    }
}

public sealed class SbtcIslandAuthenticationException(string message)
    : TelemetryAuthenticationException(message);

internal sealed record SbtcPositionsResponse
{
    public bool Ok { get; init; }
    public bool SignedIn { get; init; }
    public string? Island { get; init; }
    public string? Feed { get; init; }
    public bool Fresh { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public SbtcPlayerPin? You { get; init; }
    public IReadOnlyList<SbtcPlayerPin> Positions { get; init; } = [];
    public IReadOnlyList<SbtcPlayerPin> Friends { get; init; } = [];
    public IReadOnlyList<SbtcPlayerPin> Group { get; init; } = [];
    public IReadOnlyList<SbtcPlayerPin> Squad { get; init; } = [];
}

internal sealed record SbtcPlayerPin
{
    public string? SteamId { get; init; }
    public string? Name { get; init; }
    public string? Species { get; init; }

    /// <summary>Map pixels on the site's canvas.</summary>
    public double? X { get; init; }
    public double? Y { get; init; }

    /// <summary>The game's own coordinates, when the feed carries them.</summary>
    public double? UeX { get; init; }
    public double? UeY { get; init; }

    public double? Yaw { get; init; }
    public double? T { get; init; }
    public bool Friend { get; init; }
    public bool Group { get; init; }
    public bool Squad { get; init; }
    public bool You { get; init; }
}

internal sealed record SbtcLiveResponse
{
    public bool Ok { get; init; }
    public bool SignedIn { get; init; }
    public bool IslandOnline { get; init; }
    public double? FeedAgeSeconds { get; init; }
    public int? PollSeconds { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public SbtcDino? Dino { get; init; }
}

internal sealed record SbtcDino
{
    public string? Species { get; init; }
    public string? LifeStage { get; init; }
    public double? GrowthPercent { get; init; }
    public double? Growth { get; init; }
    public double? UpdatedAgeSeconds { get; init; }
    public int? BleedingStacks { get; init; }
    public double? FracturePercent { get; init; }
    public bool? IsPrime { get; init; }
    public bool? IsElder { get; init; }
    public int? ElderStacks { get; init; }
    public IReadOnlyList<SbtcVital> Vitals { get; init; } = [];
    public IReadOnlyList<SbtcVital> Diet { get; init; } = [];
}

internal sealed record SbtcVital
{
    public string? Key { get; init; }
    public bool Known { get; init; }
    public double? Percent { get; init; }
    public double? Value { get; init; }
    public double? Max { get; init; }
}


internal sealed record SbtcMapConfigResponse
{
    public bool? Wildlife { get; init; }
    public Dictionary<string, string>? AiGroups { get; init; }
    public Dictionary<string, string>? AiColors { get; init; }
    public string? FallbackColor { get; init; }
    public IReadOnlyList<string>? DefaultOn { get; init; }
    public Dictionary<string, SbtcMapCategory>? Categories { get; init; }
    public Dictionary<string, string>? Labels { get; init; }
}

internal sealed record SbtcWildlifeResponse
{
    public string? Status { get; init; }
    public IReadOnlyList<SbtcWildlifeAnimal?>? Ai { get; init; }
}

internal sealed record SbtcWildlifeAnimal
{
    public string? Species { get; init; }
    public double? UeX { get; init; }
    public double? UeY { get; init; }
}

internal sealed record SbtcMapCategory
{
    public string? Color { get; init; }
    public bool Zone { get; init; }
    public bool Label { get; init; }
}

internal sealed record SbtcMapPoisResponse
{
    public Dictionary<string, SbtcMapPoiGroup>? Categories { get; init; }
}

internal sealed record SbtcMapPoiGroup
{
    public string? Label { get; init; }
    public string? Labels { get; init; }
    public string? Style { get; init; }
    public double? Radius { get; init; }
    public IReadOnlyList<SbtcMapPoiItem?>? Items { get; init; }
}

internal sealed record SbtcMapPoiItem
{
    [JsonPropertyName("n")] public string? Name { get; init; }
    [JsonPropertyName("x")] public double? X { get; init; }
    [JsonPropertyName("y")] public double? Y { get; init; }
    [JsonPropertyName("r")] public double? Radius { get; init; }
    [JsonPropertyName("poly")] public IReadOnlyList<IReadOnlyList<double>>? Polygon { get; init; }
}
