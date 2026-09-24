using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotOverlayApiClient : IIslePilotOverlayApiClient
{
    private readonly Uri _serviceBaseUri;
    private readonly Uri _meUri;
    private readonly Uri _mapUri;
    private readonly Uri _garageUri;
    private readonly Uri _garageParkUri;
    private readonly Uri _garageStatusUri;
    private readonly Uri _skinDraftsUri;
    private readonly Uri _skinApplyUri;
    private readonly Uri _sbtcMarkersUri;
    private readonly HttpClient _httpClient;
    private readonly string _overlayToken;

    public IslePilotOverlayApiClient(HttpClient httpClient, IslePilotOverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.OverlayToken))
        {
            throw new ArgumentException("An IslePilot overlay token is required.", nameof(options));
        }

        if (options.OverlayToken.Contains('\r') || options.OverlayToken.Contains('\n'))
        {
            throw new ArgumentException("The IslePilot overlay token is invalid.", nameof(options));
        }

        _serviceBaseUri = options.ServiceBaseUri;
        _meUri = new Uri(_serviceBaseUri, "api/overlay/me");
        _mapUri = new Uri(_serviceBaseUri, "api/overlay/map");
        _garageUri = new Uri(_serviceBaseUri, "api/overlay/garage");
        _garageParkUri = new Uri(_serviceBaseUri, "api/overlay/garage/park");
        _garageStatusUri = new Uri(_serviceBaseUri, "api/overlay/garage/status");
        _skinDraftsUri = new Uri(_serviceBaseUri, "api/player/skin-drafts");
        _skinApplyUri = new Uri(_serviceBaseUri, "api/skin/set");
        _sbtcMarkersUri = new Uri(_serviceBaseUri, "api/p/sbtcisland/map/markers");
        _httpClient = httpClient;
        _overlayToken = NormalizeOverlayToken(options.OverlayToken);
    }

    public Task<IslePilotOverlayMeDto> GetMeAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IslePilotOverlayMeDto>(_meUri, cancellationToken);

    public Task<IslePilotOverlayMapDto> GetMapAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IslePilotOverlayMapDto>(_mapUri, cancellationToken);

    public Task<IslePilotOverlayGarageDto> GetGarageAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<IslePilotOverlayGarageDto>(_garageUri, cancellationToken);

    public Task<IslePilotOverlayGarageCommandDto> ParkGarageDinoAsync(
        string step,
        CancellationToken cancellationToken = default)
    {
        if (step is not ("start" or "finalize" or "cancel"))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        return PostAsync<IslePilotOverlayGarageCommandDto>(
            _garageParkUri,
            new IslePilotOverlayGarageParkRequest(step),
            cancellationToken);
    }

    public Task<IslePilotOverlayGarageCommandDto> RestoreGarageDinoAsync(
        string dinoId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(dinoId, nameof(dinoId));
        var uri = new Uri(
            _serviceBaseUri,
            $"api/overlay/garage/{Uri.EscapeDataString(dinoId)}/restore");
        return PostAsync<IslePilotOverlayGarageCommandDto>(uri, body: null, cancellationToken);
    }

    public Task<IslePilotOverlayGarageCommandStatusDto> GetGarageCommandStatusAsync(
        string commandId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(commandId, nameof(commandId));
        var uri = new UriBuilder(_garageStatusUri)
        {
            Query = $"id={Uri.EscapeDataString(commandId)}"
        }.Uri;
        return GetAsync<IslePilotOverlayGarageCommandStatusDto>(uri, cancellationToken);
    }

    public Task<IslePilotOverlaySkinDraftsDto> GetSkinDraftsAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(slug, nameof(slug));
        var uri = new UriBuilder(_skinDraftsUri)
        {
            Query = $"slug={Uri.EscapeDataString(slug)}"
        }.Uri;
        return GetCookieOnlyAsync<IslePilotOverlaySkinDraftsDto>(uri, cancellationToken);
    }

    public async Task<IslePilotOverlaySkinDraftDto> SaveSkinDraftAsync(
        string slug, string species, string name, IslePilotOverlayGaragePaletteDto palette, bool female = true,
        int theme = 0, int pattern = 0, int variation = 0,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(slug, nameof(slug));
        ValidateIdentifier(species, nameof(species));
        ValidateIdentifier(name, nameof(name));
        ArgumentNullException.ThrowIfNull(palette);

        // Fetch existing drafts so we don't overwrite them.
        // The IslePilot API replaces the entire list on POST, so we must
        // always send the full merged list.
        IReadOnlyList<IslePilotOverlaySkinDraftDto> existing = [];
        try
        {
            var current = await GetSkinDraftsAsync(slug, cancellationToken).ConfigureAwait(false);
            existing = current.Drafts;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // If we can't fetch existing drafts (network error, auth, etc.),
            // continue anyway — losing other drafts is bad, but blocking the
            // save entirely is worse. The merge will just contain the new draft.
        }

        var newPayload = CreateWebDraftPayload(species, name, palette, female, theme, pattern, variation);
        var newItem = new IslePilotOverlaySkinDraftSaveItem(name, species, newPayload);

        // Merge: replace existing draft with the same name (case-insensitive), else append.
        var merged = existing
            .Where(d => !string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(d => new IslePilotOverlaySkinDraftSaveItem(
                d.Name ?? string.Empty,
                d.GetSpecies() ?? string.Empty,
                d.Payload ?? new IslePilotOverlaySkinDraftPayloadDto()))
            .Append(newItem)
            .ToList();

        return await PostCookieOnlyAsync<IslePilotOverlaySkinDraftDto>(
            _skinDraftsUri,
            new IslePilotOverlaySkinDraftRequest(slug, merged),
            cancellationToken).ConfigureAwait(false);
    }

    private static IslePilotOverlaySkinDraftPayloadDto CreateWebDraftPayload(
        string species, string name, IslePilotOverlayGaragePaletteDto palette, bool female,
        int theme = 0, int pattern = 0, int variation = 0)
    {
        static IslePilotOverlaySkinGlitchLayerDto Layer(double x, double y, double z, double? a = null) =>
            new() { A = a, X = x, Y = y, Z = z };

        return new IslePilotOverlaySkinDraftPayloadDto
        {
            Id = Guid.NewGuid().ToString(),
            Sex = female ? "female" : "male",
            Name = name,
            Species = species,
            Theme = theme,
            Palette = palette,
            Pattern = pattern,
            CreatedAt = DateTimeOffset.UtcNow,
            GlitchLab = new IslePilotOverlaySkinGlitchLabDto
            {
                Pi = 0,
                Sv = 0,
                Layers = new Dictionary<string, IslePilotOverlaySkinGlitchLayerDto>
                {
                    ["b"] = Layer(1, 1, 1), ["e"] = Layer(0, 0, 0),
                    ["f"] = Layer(1, 1, 1), ["m"] = Layer(1, 1, 1),
                    ["u"] = Layer(1, 1, 1), ["d1"] = Layer(0, 0, 0),
                    ["md"] = Layer(1, 1, 1)
                }
            },
            Variation = variation,
            RenderMode = "standard"
        };
    }

    public Task<IslePilotOverlaySkinApplyDto> ApplySkinPaletteAsync(
        string serverId,
        string species,
        IslePilotOverlayGaragePaletteDto palette,
        bool female = true,
        int theme = 0,
        int pattern = 0,
        int variation = 0,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(serverId, nameof(serverId));
        ValidateIdentifier(species, nameof(species));
        ArgumentNullException.ThrowIfNull(palette);
        return PostCookieOnlyAsync<IslePilotOverlaySkinApplyDto>(
            _skinApplyUri,
            new IslePilotOverlaySkinApplyRequest(
                serverId,
                IslePilotOverlaySkinSetPayloadDto.FromPalette(species, palette, female, theme, pattern, variation)),
            cancellationToken);
    }

    public Task<IslePilotOverlaySkinApplyDto> ApplySkinDraftAsync(
        string serverId, string species, IslePilotOverlaySkinDraftPayloadDto payload, bool female = true,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(serverId, nameof(serverId));
        ValidateIdentifier(species, nameof(species));
        ArgumentNullException.ThrowIfNull(payload);
        return PostCookieOnlyAsync<IslePilotOverlaySkinApplyDto>(
            _skinApplyUri,
            new IslePilotOverlaySkinApplyRequest(
                serverId,
                IslePilotOverlaySkinSetPayloadDto.FromDraft(species, payload, female)),
            cancellationToken);
    }

    public async Task<IslePilotOverlayMarkersDto> GetMarkersAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _sbtcMarkersUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.TryAddWithoutValidation("Cookie", PlayerCookieHeader);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new IslePilotOverlayAuthenticationException(
                "Phiên IslePilot đã hết hạn hoặc chưa đăng nhập.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<IslePilotOverlayMarkersDto>(
                stream,
                IslePilotOverlayJson.Options,
                cancellationToken)
            ?? throw new InvalidDataException("SBTC returned an empty markers response.");
    }

    private async Task<T> GetAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _overlayToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // The overlay endpoints are snapshots, not cacheable resources. Send
        // both directives because some proxies only honor the legacy header.
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.TryAddWithoutValidation("X-Overlay-Version", "2");
        request.Headers.TryAddWithoutValidation("Cookie", PlayerCookieHeader);
        request.Headers.Referrer = _serviceBaseUri;

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new IslePilotOverlayAuthenticationException(
                "Phiên IslePilot đã hết hạn hoặc chưa đăng nhập.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(
                stream,
                IslePilotOverlayJson.Options,
                cancellationToken)
            ?? throw new InvalidDataException("IslePilot returned an empty overlay response.");
    }

    private async Task<T> GetCookieOnlyAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddWebCookieHeaders(request);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new IslePilotOverlayAuthenticationException(
                "IslePilot từ chối phiên cookie khi tải skin-drafts.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(
                stream,
                IslePilotOverlayJson.Options,
                cancellationToken)
            ?? throw new InvalidDataException("IslePilot returned an empty skin-drafts response.");
    }

    private async Task<T> PostCookieOnlyAsync<T>(
        Uri uri,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        AddWebCookieHeaders(request);
        request.Content = JsonContent.Create(body, options: IslePilotOverlayJson.Options);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new IslePilotOverlayAuthenticationException(
                "IslePilot từ chối phiên cookie khi áp dụng skin.");
        }
        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(responseBody);
            throw new HttpRequestException(string.IsNullOrWhiteSpace(error)
                ? $"IslePilot skin request failed ({(int)response.StatusCode})."
                : $"IslePilot: {error}");
        }

        return JsonSerializer.Deserialize<T>(responseBody, IslePilotOverlayJson.Options)
            ?? throw new InvalidDataException("IslePilot returned an empty skin response.");
    }

    private void AddWebCookieHeaders(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.TryAddWithoutValidation("Cookie", PlayerCookieHeader);
        request.Headers.TryAddWithoutValidation("Origin", _serviceBaseUri.GetLeftPart(UriPartial.Authority));
        request.Headers.Referrer = _serviceBaseUri;
    }

    private async Task<T> PostAsync<T>(
        Uri uri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _overlayToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Overlay-Version", "2");
        request.Headers.TryAddWithoutValidation("Cookie", PlayerCookieHeader);
        request.Headers.Referrer = _serviceBaseUri;
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: IslePilotOverlayJson.Options);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new IslePilotOverlayAuthenticationException(
                "Phiên IslePilot đã hết hạn hoặc chưa đăng nhập.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<T>(
            stream,
            IslePilotOverlayJson.Options,
            cancellationToken);
        if (result is null)
        {
            throw new InvalidDataException("IslePilot returned an empty garage command response.");
        }

        return result;
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n'))
        {
            throw new ArgumentException("The IslePilot identifier is invalid.", parameterName);
        }
    }

    private string PlayerCookieHeader => $"islepilot_player={_overlayToken}";

    private static string NormalizeOverlayToken(string token) =>
        token.Trim().StartsWith("islepilot_player=", StringComparison.OrdinalIgnoreCase)
            ? token.Trim()["islepilot_player=".Length..]
            : token.Trim();

    private static string? TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            foreach (var name in new[] { "error", "message", "detail" })
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
        }
        catch (JsonException) { }
        return body.Length > 240 ? body[..240] : body;
    }
}

public sealed class IslePilotOverlayAuthenticationException(string message)
    : TelemetryAuthenticationException(message);
