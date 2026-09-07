using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotOverlayStateReducer
{
    public static readonly TimeSpan LiveDataLifetime = TimeSpan.FromSeconds(4);

    private readonly TimeSpan _liveDataLifetime;
    private readonly TimeSpan _positionFallbackAfter;
    private readonly TimeSpan _restDataLifetime;
    private DateTimeOffset? _lastMeRequestAt;
    private readonly Dictionary<string, DateTimeOffset> _restFieldTimes = new();
    private IslePilotOverlayMeDto? _me;
    private IslePilotOverlayMapDto? _map;
    private IslePilotOverlayLiveDataDto? _live;
    private IReadOnlyList<IslePilotOverlayMapMarkerDto>? _dedicatedMarkers;
    private IslePilotMapCalibration? _calibration;
    private DateTimeOffset? _lastMeAt;
    private DateTimeOffset? _lastMapAt;
    private DateTimeOffset? _lastMarkersAt;
    private DateTimeOffset? _lastLiveAt;
    private DateTimeOffset? _lastLivePositionAt;
    private DateTimeOffset? _lastLiveYawAt;
    private DateTimeOffset? _lastLivePositionChangedAt;
    private DateTimeOffset? _lastMarkerPositionChangedAt;
    private DateTimeOffset? _mapSelfChangedAt;
    private DateTimeOffset? _dedicatedSelfChangedAt;
    private DateTimeOffset? _mapRequestAt;
    private DateTimeOffset? _markersRequestAt;
    private bool _useMapSelf;
    private DateTimeOffset? _selectedMarkerAt;
    private DateTimeOffset? _lastLiveGrowthAt;
    private DateTimeOffset? _lastLiveHealthAt;
    private DateTimeOffset? _lastLiveMaxHealthAt;
    private DateTimeOffset? _lastLiveHungerAt;
    private DateTimeOffset? _lastLiveMaxHungerAt;
    private DateTimeOffset? _lastLiveThirstAt;
    private DateTimeOffset? _lastLiveMaxThirstAt;
    private DateTimeOffset? _lastLiveStaminaAt;
    private DateTimeOffset? _lastLiveMaxStaminaAt;
    private DateTimeOffset? _lastLivePrimeAt;
    private DateTimeOffset? _lastLiveHasDinoAt;
    private TelemetrySessionState _sessionState = TelemetrySessionState.Connecting;

    public IslePilotOverlayStateReducer(
        TimeSpan? liveDataLifetime = null,
        TimeSpan? positionFallbackAfter = null,
        TimeSpan? restDataLifetime = null)
    {
        _liveDataLifetime = liveDataLifetime ?? LiveDataLifetime;
        _positionFallbackAfter = positionFallbackAfter ?? TimeSpan.FromSeconds(6);
        _restDataLifetime = restDataLifetime ?? TimeSpan.FromSeconds(15);
        if (_restDataLifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(restDataLifetime));
        if (_liveDataLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(liveDataLifetime));
        }
        if (_positionFallbackAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(positionFallbackAfter));
        }
    }

    public string? PersonaName => _me?.PersonaName ?? _me?.Name;

    public bool UsesDedicatedMarkers =>
        _me?.Server?.Contains("sbtc", StringComparison.OrdinalIgnoreCase) == true;

    public void ApplyMe(IslePilotOverlayMeDto me, DateTimeOffset receivedAt, DateTimeOffset? requestStartedAt = null)
    {
        ArgumentNullException.ThrowIfNull(me);

        // A late completion from an older request must never move the state
        // backwards. The realtime session already serializes requests, but
        // keeping this guard in the reducer also protects direct callers and
        // future refresh paths that may become concurrent.
        if (_lastMeAt is { } lastMeAt && receivedAt < lastMeAt)
        {
            return;
        }

        var previousServer = _me?.Server;
        _me = Merge(_me, me);
        if (!string.Equals(previousServer, _me.Server, StringComparison.OrdinalIgnoreCase) &&
            !UsesDedicatedMarkers)
        {
            _dedicatedMarkers = null;
            _lastMarkersAt = null;
            _dedicatedSelfChangedAt = null;
            _useMapSelf = false;
        }
        _lastMeAt = receivedAt;
        _lastMeRequestAt = requestStartedAt ?? receivedAt;
        if (me.Growth is not null) _restFieldTimes[nameof(me.Growth)] = _lastMeRequestAt.Value;
        if (me.Health is not null) _restFieldTimes[nameof(me.Health)] = _lastMeRequestAt.Value;
        if (me.MaxHealth is not null) _restFieldTimes[nameof(me.MaxHealth)] = _lastMeRequestAt.Value;
        if (me.Hunger is not null) _restFieldTimes[nameof(me.Hunger)] = _lastMeRequestAt.Value;
        if (me.MaxHunger is not null) _restFieldTimes[nameof(me.MaxHunger)] = _lastMeRequestAt.Value;
        if (me.Thirst is not null) _restFieldTimes[nameof(me.Thirst)] = _lastMeRequestAt.Value;
        if (me.MaxThirst is not null) _restFieldTimes[nameof(me.MaxThirst)] = _lastMeRequestAt.Value;
        if (me.Stamina is not null) _restFieldTimes[nameof(me.Stamina)] = _lastMeRequestAt.Value;
        if (me.MaxStamina is not null) _restFieldTimes[nameof(me.MaxStamina)] = _lastMeRequestAt.Value;
        if (me.Prime is not null) _restFieldTimes[nameof(me.Prime)] = _lastMeRequestAt.Value;
        if (me.Online is not null) _restFieldTimes[nameof(me.Online)] = _lastMeRequestAt.Value;
    }

    public void ApplyMarkers(IslePilotOverlayMarkersDto markers, DateTimeOffset receivedAt, DateTimeOffset? requestStartedAt = null)
    {
        ArgumentNullException.ThrowIfNull(markers);
        if (_lastMarkersAt is { } lastMarkersAt && receivedAt < lastMarkersAt)
        {
            return;
        }

        if (!UsesDedicatedMarkers || !markers.Ok)
        {
            return;
        }

        var previousSelf = FindSelfMarker(_dedicatedMarkers ?? []);
        var nextSelf = FindSelfMarker(markers.Markers);
        if (HasChangedLocation(previousSelf, nextSelf))
        {
            _dedicatedSelfChangedAt = receivedAt;
            _markersRequestAt = requestStartedAt ?? receivedAt;
        }

        _dedicatedMarkers = markers.Markers;
        _lastMarkersAt = receivedAt;
    }

    public void ApplyMap(IslePilotOverlayMapDto map, DateTimeOffset receivedAt, DateTimeOffset? requestStartedAt = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (_lastMapAt is { } lastMapAt && receivedAt < lastMapAt)
        {
            return;
        }

        var previousSelf = FindSelfMarker(_map?.Markers ?? []);

        // Validate calibration before committing any state so a malformed
        // response cannot partially replace the last good map.
        var calibrationDto = map.Calibration ?? _map?.Calibration;
        var nextCalibration = map.Calibration is not null
            ? new IslePilotMapCalibration(map.Calibration)
            : _calibration;
        if (nextCalibration is null && calibrationDto is not null)
        {
            nextCalibration = new IslePilotMapCalibration(calibrationDto);
        }

        // Markers are dynamic and may legitimately become empty. Categories,
        // POIs and calibration describe the fixed Gateway/SBTC map, so retain
        // the last good copy when a temporary/partial API response omits them.
        var nextMap = map with
        {
            Calibration = calibrationDto,
            Markers = map.Markers ?? [],
            Categories = map.Categories is { Count: > 0 } ? map.Categories : _map?.Categories ?? [],
            Pois = map.Pois is { Count: > 0 } ? map.Pois : _map?.Pois ?? []
        };

        _map = nextMap;
        _calibration = nextCalibration;
        var nextSelf = FindSelfMarker(_map.Markers);
        if (HasChangedLocation(previousSelf, nextSelf))
        {
            _mapSelfChangedAt = receivedAt;
            _mapRequestAt = requestStartedAt ?? receivedAt;
        }
        _lastMapAt = receivedAt;
    }

    public void ApplyLive(IslePilotOverlayLiveDataDto live, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (_lastLiveAt is { } lastLiveAt && receivedAt < lastLiveAt)
        {
            return;
        }

        var previousPosition = ToLocation(_live?.Position);
        var incomingPosition = ToLocation(live.Position);
        if (HasChangedLocation(previousPosition, incomingPosition))
        {
            _lastLivePositionChangedAt = receivedAt;
        }

        _live = Merge(_live, live);
        _lastLiveAt = receivedAt;
        SetTimestampIf(HasLocation(live.Position), receivedAt, ref _lastLivePositionAt);
        SetTimestampIf(live.Position?.Yaw is not null, receivedAt, ref _lastLiveYawAt);
        SetTimestampIf(live.Growth is not null, receivedAt, ref _lastLiveGrowthAt);
        SetTimestampIf(live.HasDino is not null, receivedAt, ref _lastLiveHasDinoAt);
        SetTimestampIf(live.Health is not null, receivedAt, ref _lastLiveHealthAt);
        SetTimestampIf(live.MaxHealth is not null, receivedAt, ref _lastLiveMaxHealthAt);
        SetTimestampIf(live.Hunger is not null, receivedAt, ref _lastLiveHungerAt);
        SetTimestampIf(live.MaxHunger is not null, receivedAt, ref _lastLiveMaxHungerAt);
        SetTimestampIf(live.Thirst is not null, receivedAt, ref _lastLiveThirstAt);
        SetTimestampIf(live.MaxThirst is not null, receivedAt, ref _lastLiveMaxThirstAt);
        SetTimestampIf(live.Stamina is not null, receivedAt, ref _lastLiveStaminaAt);
        SetTimestampIf(live.MaxStamina is not null, receivedAt, ref _lastLiveMaxStaminaAt);
        if (live.Prime is not null)
        {
            _lastLivePrimeAt = receivedAt;
        }
        _sessionState = TelemetrySessionState.Live;
    }

    public void SetSessionState(TelemetrySessionState state) => _sessionState = state;

    public TelemetrySnapshot BuildSnapshot(DateTimeOffset now)
    {
        var liveDataStale = _lastLiveAt is not null && now - _lastLiveAt > _liveDataLifetime;
        var selfMarker = SelectSelfMarker(now);
        var sessionState = ResolveSessionState(liveDataStale, now);
        var playerOnline = ResolvePlayerOnline(selfMarker, now);
        var player = playerOnline ? BuildPlayer(selfMarker, now) : null;

        return new TelemetrySnapshot
        {
            Source = "IslePilot",
            Success = sessionState != TelemetrySessionState.AuthenticationRequired,
            ServerOnline = sessionState is not TelemetrySessionState.AuthenticationRequired and
                not TelemetrySessionState.Stopped,
            PlayerOnline = playerOnline,
            UpdatedAt = LatestTimestamp(),
            Player = player,
            Map = BuildMap(),
            SessionState = sessionState,
            LiveDataStale = liveDataStale,
            StatusMessage = ResolveStatusMessage(sessionState, playerOnline)
        };
    }

    private TelemetrySessionState ResolveSessionState(bool liveDataStale, DateTimeOffset now)
    {
        if (_sessionState is TelemetrySessionState.AuthenticationRequired or TelemetrySessionState.Stopped)
        {
            return _sessionState;
        }

        if (_me?.HasData == false)
        {
            return TelemetrySessionState.UnsupportedServer;
        }

        // Socket silence is not a failure of REST telemetry. Require both
        // status and the active marker stream; map POIs cannot refresh stats.
        var markerAt = UsesDedicatedMarkers ? _selectedMarkerAt : _lastMapAt;
        var restFresh = _me?.Online is not null && _map?.Allowed != false &&
                        _lastMeAt is { } meAt && now - meAt <= _restDataLifetime &&
                        markerAt is { } positionAt && now - positionAt <= _restDataLifetime;
        if (_sessionState != TelemetrySessionState.Reconnecting &&
            _lastLiveAt is not null && !liveDataStale)
        {
            return TelemetrySessionState.Live;
        }
        if (restFresh)
        {
            return TelemetrySessionState.Polling;
        }
        if (_sessionState == TelemetrySessionState.Reconnecting)
        {
            return TelemetrySessionState.Reconnecting;
        }

        if (liveDataStale)
        {
            return TelemetrySessionState.Stale;
        }

        return _lastMeAt is not null || _lastMapAt is not null
            ? TelemetrySessionState.Stale : _sessionState;
    }

    private bool ResolvePlayerOnline(IslePilotOverlayMapMarkerDto? selfMarker, DateTimeOffset now)
    {
        if (_me?.HasData == false)
        {
            return false;
        }

        if (_lastLiveHasDinoAt is { } hasDinoAt &&
            (now - hasDinoAt <= _liveDataLifetime || !RestFieldIsNewer("Online", hasDinoAt)))
        {
            return _live?.HasDino == true;
        }
        if (_me?.Online is { } online) return online;

        return _live?.HasDino == true || _me?.Online == true || selfMarker is not null;
    }

    private PlayerTelemetry BuildPlayer(
        IslePilotOverlayMapMarkerDto? selfMarker,
        DateTimeOffset now)
    {
        var liveLocation = ToLocation(_live?.Position);
        var markerLocation = ToLocation(selfMarker);
        var webSocketUnavailable = _sessionState == TelemetrySessionState.Reconnecting ||
                                   _lastLiveAt is null ||
                                   now - _lastLiveAt > _liveDataLifetime;
        var livePositionFresh = !webSocketUnavailable &&
                                _lastLivePositionChangedAt is not null &&
                                now - _lastLivePositionChangedAt <= _positionFallbackAfter;
        var markerChangedAfterLive = _lastMarkerPositionChangedAt is not null &&
                                     (_lastLivePositionChangedAt is null ||
                                      (_lastMarkerPositionChangedAt > _lastLivePositionChangedAt &&
                                       (_useMapSelf ? _mapRequestAt : _markersRequestAt) > _lastLivePositionChangedAt));
        var useMarkerFallback = liveLocation is not null &&
                                markerLocation is not null &&
                                !livePositionFresh &&
                                markerChangedAfterLive &&
                                HasChangedLocation(liveLocation, markerLocation);
        var location = liveLocation is null || useMarkerFallback
            ? markerLocation
            : liveLocation;
        // A live yaw is authoritative while WS is actually producing position
        // frames. Once that field becomes stale, let the REST marker keep the
        // heading moving instead of pinning the map to the last WS direction.
        var liveYawFresh = _lastLiveYawAt is not null &&
                           now - _lastLiveYawAt <= _liveDataLifetime;
        var yaw = liveYawFresh
            ? _live?.Position?.Yaw ?? selfMarker?.Yaw
            : selfMarker?.Yaw ?? _live?.Position?.Yaw;
        MapPoint? mapLocation = location is null || _calibration is null
            ? null
            : _calibration.Project(location.X, location.Y);
        double? mapHeading = location is null || yaw is null || _calibration is null
            ? null
            : _calibration.ProjectHeading(location.X, location.Y, yaw.Value);

        // REST responses can complete after a WebSocket frame while still
        // containing an older server snapshot. Keep each fresh realtime field
        // authoritative; use /me only after that particular field becomes stale.
        var growth = LatestValue(_live?.Growth, _lastLiveGrowthAt, _me?.Growth, now, nameof(IslePilotOverlayMeDto.Growth));
        var health = LatestValue(_live?.Health, _lastLiveHealthAt, _me?.Health, now, nameof(IslePilotOverlayMeDto.Health));
        var maxHealth = LatestValue(_live?.MaxHealth, _lastLiveMaxHealthAt, _me?.MaxHealth, now, nameof(IslePilotOverlayMeDto.MaxHealth));
        var hunger = LatestValue(_live?.Hunger, _lastLiveHungerAt, _me?.Hunger, now, nameof(IslePilotOverlayMeDto.Hunger));
        var maxHunger = LatestValue(_live?.MaxHunger, _lastLiveMaxHungerAt, _me?.MaxHunger, now, nameof(IslePilotOverlayMeDto.MaxHunger));
        var thirst = LatestValue(_live?.Thirst, _lastLiveThirstAt, _me?.Thirst, now, nameof(IslePilotOverlayMeDto.Thirst));
        var maxThirst = LatestValue(_live?.MaxThirst, _lastLiveMaxThirstAt, _me?.MaxThirst, now, nameof(IslePilotOverlayMeDto.MaxThirst));
        var stamina = LatestValue(_live?.Stamina, _lastLiveStaminaAt, _me?.Stamina, now, nameof(IslePilotOverlayMeDto.Stamina));
        var maxStamina = LatestValue(_live?.MaxStamina, _lastLiveMaxStaminaAt, _me?.MaxStamina, now, nameof(IslePilotOverlayMeDto.MaxStamina));

        return new PlayerTelemetry
        {
            SteamId = _live?.SteamId ?? _me?.SteamId,
            Name = _me?.PersonaName ?? _me?.Name,
            Class = _me?.Species,
            Server = _me?.Server,
            Female = _me?.Female,
            GrowthPercent = FractionToPercent(growth),
            HealthPercent = Percent(health, maxHealth),
            StaminaPercent = Percent(stamina, maxStamina),
            HungerPercent = Percent(hunger, maxHunger),
            ThirstPercent = Percent(thirst, maxThirst),
            ExactVitalsSource = "IslePilotOverlayV2",
            ExactVitals = new ExactVitals
            {
                Growth = growth,
                Health = health,
                MaxHealth = maxHealth,
                Stamina = stamina,
                MaxStamina = maxStamina,
                Hunger = hunger,
                MaxHunger = maxHunger,
                Thirst = thirst,
                MaxThirst = maxThirst
            },
            Nutrition = ToNutrition(Merge(_me?.Nutrition, _live?.Nutrition)),
            Location = location,
            MapLocation = mapLocation,
            ExactMapHeadingDegrees = mapHeading,
            Prime = ToPrime(LatestPrime(now))
        };
    }

    private IslePilotPrimeDto? LatestPrime(DateTimeOffset now)
    {
        if (_lastLivePrimeAt is not null &&
            (now - _lastLivePrimeAt <= _liveDataLifetime || !RestFieldIsNewer("Prime", _lastLivePrimeAt)))
        {
            return _live?.Prime ?? _me?.Prime;
        }

        return _me?.Prime ?? _live?.Prime;
    }

    private MapTelemetry? BuildMap()
    {
        if (_map is null)
        {
            return null;
        }

        return new MapTelemetry
        {
            UpdatedAt = Latest(_lastMapAt, _lastMarkersAt),
            Markers = CurrentMarkers().Select(ToMarker).ToArray(),
            PointsOfInterest = _map.Pois.Select(ToPointOfInterest).ToArray()
        };
    }

    private MapMarkerTelemetry ToMarker(IslePilotOverlayMapMarkerDto marker)
    {
        var location = ToLocation(marker);
        return new MapMarkerTelemetry
        {
            SteamId = marker.SteamId,
            Label = marker.Label,
            Self = marker.Self,
            Group = marker.Group,
            Location = location,
            MapLocation = location is null || _calibration is null
                ? null
                : _calibration.Project(location.X, location.Y),
            ExactMapHeadingDegrees = location is null || marker.Yaw is null || _calibration is null
                ? null
                : _calibration.ProjectHeading(location.X, location.Y, marker.Yaw.Value),
            Path = marker.Path
                .Where(HasLocation)
                .Select(point => _calibration?.Project(point.X!.Value, point.Y!.Value))
                .Where(point => point is not null)
                .Select(point => point!.Value)
                .ToArray()
        };
    }

    private MapPointOfInterestTelemetry ToPointOfInterest(IslePilotOverlayMapPoiDto poi) => new()
    {
        Id = poi.Id,
        Name = poi.Name,
        CategoryId = poi.CategoryId,
        CategoryName = _map?.Categories.FirstOrDefault(category =>
            string.Equals(category.Id, poi.CategoryId, StringComparison.OrdinalIgnoreCase))?.Name,
        Shape = poi.Shape,
        Size = poi.Size,
        Color = poi.Color,
        Icon = poi.Icon,
        Enabled = poi.Enabled,
        HideLabel = poi.HideLabel,
        Points = poi.Points
            .Where(HasLocation)
            .Select(point => _calibration?.Project(point.X!.Value, point.Y!.Value))
            .Where(point => point is not null)
            .Select(point => point!.Value)
            .ToArray()
    };

    private IslePilotOverlayMapMarkerDto? SelectSelfMarker(DateTimeOffset now)
    {
        var mapSelf = _map?.Allowed == false ? null : FindSelfMarker(_map?.Markers ?? []);
        var dedicatedSelf = UsesDedicatedMarkers ? FindSelfMarker(_dedicatedMarkers ?? []) : null;
        var mapValid = ToLocation(mapSelf) is not null;
        var dedicatedValid = ToLocation(dedicatedSelf) is not null;
        if (!UsesDedicatedMarkers)
        {
            _useMapSelf = true;
        }
        else if (!_useMapSelf)
        {
            var dedicatedExpired = _lastMarkersAt is null || now - _lastMarkersAt > _positionFallbackAfter;
            // Only promote a changed map coordinate observed after the dedicated
            // source. Repeated cached responses are not evidence of movement.
            if (mapValid && _lastMapAt is { } mapAt && now - mapAt <= _restDataLifetime && (!dedicatedValid ||
                (dedicatedExpired && _mapSelfChangedAt > _dedicatedSelfChangedAt &&
                 _mapRequestAt > _dedicatedSelfChangedAt))) _useMapSelf = true;
        }
        else if (dedicatedValid && (!mapValid ||
                 (_dedicatedSelfChangedAt > _mapSelfChangedAt && _markersRequestAt > _mapSelfChangedAt)))
        {
            // Stay on /map until /markers actually advances. An old in-flight
            // request or repeated coordinate must not reclaim ownership.
            _useMapSelf = false;
        }
        _selectedMarkerAt = _useMapSelf ? _lastMapAt : _lastMarkersAt;
        _lastMarkerPositionChangedAt = _useMapSelf ? _mapSelfChangedAt : _dedicatedSelfChangedAt;
        return _useMapSelf ? mapSelf : dedicatedSelf;
    }

    private IslePilotOverlayMapMarkerDto? FindSelfMarker(
        IReadOnlyList<IslePilotOverlayMapMarkerDto> markers)
    {
        var steamId = _live?.SteamId ?? _me?.SteamId;
        return markers.FirstOrDefault(marker =>
                   steamId is not null && string.Equals(marker.SteamId, steamId, StringComparison.Ordinal)) ??
               markers.FirstOrDefault(marker => marker.Self);
    }

    private IReadOnlyList<IslePilotOverlayMapMarkerDto> CurrentMarkers() =>
        _dedicatedMarkers ?? _map?.Markers ?? [];

    private DateTimeOffset? LatestTimestamp()
    {
        var values = new[] { _lastMeAt, _lastMapAt, _lastMarkersAt, _lastLiveAt }
            .Where(value => value is not null)
            .Select(value => value!.Value);
        return values.Any() ? values.Max() : null;
    }

    private static DateTimeOffset? Latest(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null ? left : left > right ? left : right;

    private static string ResolveStatusMessage(TelemetrySessionState state, bool playerOnline) => state switch
    {
        TelemetrySessionState.AuthenticationRequired => "PHIÊN ĐÃ HẾT HẠN",
        TelemetrySessionState.UnsupportedServer => "ISLEPILOT · CHƯA VÀO SERVER HỖ TRỢ",
        TelemetrySessionState.Reconnecting => "ISLEPILOT · RECONNECTING",
        TelemetrySessionState.Stale => "ISLEPILOT · DATA STALE",
        TelemetrySessionState.Live when !playerOnline => "NO ACTIVE DINOSAUR",
        TelemetrySessionState.Live => "ISLEPILOT · LIVE",
        TelemetrySessionState.Connecting => "ISLEPILOT · CONNECTING",
        _ => $"ISLEPILOT · {state.ToString().ToUpperInvariant()}"
    };

    private static WorldLocation? ToLocation(IslePilotOverlayPositionDto? position) =>
        HasLocation(position)
            ? new WorldLocation { X = position!.X!.Value, Y = position.Y!.Value, Z = position.Z }
            : null;

    private static WorldLocation? ToLocation(IslePilotOverlayMapMarkerDto? marker) =>
        marker is not null && marker.X is not null && marker.Y is not null
            ? new WorldLocation { X = marker.X.Value, Y = marker.Y.Value, Z = marker.Z }
            : null;

    private static bool HasLocation(IslePilotOverlayPositionDto? position) =>
        position?.X is not null && position.Y is not null;

    private static bool HasLocation(IslePilotOverlayWorldPointDto point) =>
        point.X is not null && point.Y is not null;

    private static bool HasChangedLocation(
        WorldLocation? previous,
        WorldLocation? current) =>
        current is not null &&
        (previous is null || previous.X != current.X || previous.Y != current.Y);

    private static bool HasChangedLocation(
        IslePilotOverlayMapMarkerDto? previous,
        IslePilotOverlayMapMarkerDto? current) =>
        current?.X is not null && current.Y is not null &&
        (previous?.X is null || previous.Y is null ||
         previous.X != current.X || previous.Y != current.Y);

    private static NutritionTelemetry? ToNutrition(IslePilotNutritionDto? nutrition) => nutrition is null
        ? null
        : new NutritionTelemetry
        {
            Carb = nutrition.Carb,
            Protein = nutrition.Protein,
            Lipid = nutrition.Lipid
        };

    private static PrimeTelemetry? ToPrime(IslePilotPrimeDto? prime) => prime is null
        ? null
        : new PrimeTelemetry
        {
            IsPrime = prime.Elder,
            Progress = Percent(prime.Done, prime.Required),
            Elder = prime.Elder,
            Eligible = prime.Eligible,
            Done = prime.Done,
            Required = prime.Required,
            Quests = prime.Quests.Select(quest => new PrimeQuestTelemetry
            {
                Name = quest.Name,
                Done = quest.Done
            }).ToArray()
        };

    private static IslePilotOverlayMeDto Merge(IslePilotOverlayMeDto? previous, IslePilotOverlayMeDto current)
    {
        if (previous is null)
        {
            return current;
        }

        return new IslePilotOverlayMeDto
        {
            HasData = current.HasData ?? previous.HasData,
            Online = current.Online ?? previous.Online,
            SteamId = current.SteamId ?? previous.SteamId,
            PersonaName = current.PersonaName ?? previous.PersonaName,
            Name = current.Name ?? previous.Name,
            Species = current.Species ?? previous.Species,
            Server = current.Server ?? previous.Server,
            Female = current.Female ?? previous.Female,
            Growth = current.Growth ?? previous.Growth,
            Health = current.Health ?? previous.Health,
            MaxHealth = current.MaxHealth ?? previous.MaxHealth,
            Hunger = current.Hunger ?? previous.Hunger,
            MaxHunger = current.MaxHunger ?? previous.MaxHunger,
            Thirst = current.Thirst ?? previous.Thirst,
            MaxThirst = current.MaxThirst ?? previous.MaxThirst,
            Stamina = current.Stamina ?? previous.Stamina,
            MaxStamina = current.MaxStamina ?? previous.MaxStamina,
            Nutrition = Merge(previous.Nutrition, current.Nutrition),
            Prime = current.Prime ?? previous.Prime
        };
    }

    private static IslePilotOverlayLiveDataDto Merge(
        IslePilotOverlayLiveDataDto? previous,
        IslePilotOverlayLiveDataDto current)
    {
        if (previous is null)
        {
            return current;
        }

        return new IslePilotOverlayLiveDataDto
        {
            HasDino = current.HasDino ?? previous.HasDino,
            SteamId = current.SteamId ?? previous.SteamId,
            Growth = current.Growth ?? previous.Growth,
            Health = current.Health ?? previous.Health,
            MaxHealth = current.MaxHealth ?? previous.MaxHealth,
            Hunger = current.Hunger ?? previous.Hunger,
            MaxHunger = current.MaxHunger ?? previous.MaxHunger,
            Thirst = current.Thirst ?? previous.Thirst,
            MaxThirst = current.MaxThirst ?? previous.MaxThirst,
            Stamina = current.Stamina ?? previous.Stamina,
            MaxStamina = current.MaxStamina ?? previous.MaxStamina,
            Nutrition = Merge(previous.Nutrition, current.Nutrition),
            Position = Merge(previous.Position, current.Position),
            Prime = current.Prime ?? previous.Prime
        };
    }

    private static IslePilotNutritionDto? Merge(
        IslePilotNutritionDto? previous,
        IslePilotNutritionDto? current)
    {
        if (current is null)
        {
            return previous;
        }

        return new IslePilotNutritionDto
        {
            Carb = current.Carb ?? previous?.Carb,
            Protein = current.Protein ?? previous?.Protein,
            Lipid = current.Lipid ?? previous?.Lipid
        };
    }

    private static IslePilotOverlayPositionDto? Merge(
        IslePilotOverlayPositionDto? previous,
        IslePilotOverlayPositionDto? current)
    {
        if (current is null)
        {
            return previous;
        }

        return new IslePilotOverlayPositionDto
        {
            X = current.X ?? previous?.X,
            Y = current.Y ?? previous?.Y,
            Z = current.Z ?? previous?.Z,
            Yaw = current.Yaw ?? previous?.Yaw
        };
    }

    private static double? FractionToPercent(double? value) => value is null
        ? null
        : Math.Clamp(value.Value <= 1d ? value.Value * 100d : value.Value, 0d, 100d);

    private double? LatestValue(
        double? liveValue,
        DateTimeOffset? liveReceivedAt,
        double? restValue,
        DateTimeOffset now, string field) =>
        liveReceivedAt is not null &&
        (now - liveReceivedAt <= _liveDataLifetime || !RestFieldIsNewer(field, liveReceivedAt))
            ? liveValue ?? restValue
            : restValue ?? liveValue;

    private bool RestFieldIsNewer(string field, DateTimeOffset? liveAt) =>
        _restFieldTimes.TryGetValue(field, out var restAt) && (liveAt is null || restAt > liveAt);

    private static void SetTimestampIf(
        bool condition,
        DateTimeOffset timestamp,
        ref DateTimeOffset? target)
    {
        if (condition)
        {
            target = timestamp;
        }
    }

    private static double? Percent(double? current, double? maximum) =>
        current is not null && maximum is > 0d
            ? Math.Clamp(current.Value / maximum.Value * 100d, 0d, 100d)
            : null;

    private static double? Percent(int? current, int? maximum) =>
        current is not null && maximum is > 0
            ? Math.Clamp((double)current.Value / maximum.Value * 100d, 0d, 100d)
            : null;
}
