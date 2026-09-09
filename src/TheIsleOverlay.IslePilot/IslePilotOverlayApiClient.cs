using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotOverlayApiClient : IIslePilotOverlayApiClient
{
    private static readonly Uri MeUri = new(IslePilotOverlayOptions.ServiceBaseUri, "api/overlay/me");
    private static readonly Uri MapUri = new(IslePilotOverlayOptions.ServiceBaseUri, "api/overlay/map");
    private static readonly Uri GarageUri = new(IslePilotOverlayOptions.ServiceBaseUri, "api/overlay/garage");
    private static readonly Uri GarageParkUri = new(IslePilotOverlayOptions.ServiceBaseUri, "api/overlay/garage/park");
    private static readonly Uri GarageStatusUri = new(IslePilotOverlayOptions.ServiceBaseUri, "api/overlay/garage/status");
    private static readonly Uri SbtcMarkersUri = new(
        IslePilotOverlayOptions.ServiceBaseUri,
        "api/p/sbtcisland/map/markers");

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

        _httpClient = httpClient;
        _overlayToken = options.OverlayToken;
    }

    public Task<IslePilotOverlayMeDto> GetMeAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IslePilotOverlayMeDto>(MeUri, cancellationToken);

    public Task<IslePilotOverlayMapDto> GetMapAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IslePilotOverlayMapDto>(MapUri, cancellationToken);

    public Task<IslePilotOverlayGarageDto> GetGarageAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<IslePilotOverlayGarageDto>(GarageUri, cancellationToken);

    public Task<IslePilotOverlayGarageCommandDto> ParkGarageDinoAsync(
        string step,
        CancellationToken cancellationToken = default)
    {
        if (step is not ("start" or "finalize" or "cancel"))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        return PostAsync<IslePilotOverlayGarageCommandDto>(
            GarageParkUri,
            new IslePilotOverlayGarageParkRequest(step),
            cancellationToken);
    }

    public Task<IslePilotOverlayGarageCommandDto> RestoreGarageDinoAsync(
        string dinoId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(dinoId, nameof(dinoId));
        var uri = new Uri(
            IslePilotOverlayOptions.ServiceBaseUri,
            $"api/overlay/garage/{Uri.EscapeDataString(dinoId)}/restore");
        return PostAsync<IslePilotOverlayGarageCommandDto>(uri, body: null, cancellationToken);
    }

    public Task<IslePilotOverlayGarageCommandStatusDto> GetGarageCommandStatusAsync(
        string commandId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(commandId, nameof(commandId));
        var uri = new UriBuilder(GarageStatusUri)
        {
            Query = $"id={Uri.EscapeDataString(commandId)}"
        }.Uri;
        return GetAsync<IslePilotOverlayGarageCommandStatusDto>(uri, cancellationToken);
    }

    public async Task<IslePilotOverlayMarkersDto> GetMarkersAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SbtcMarkersUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.TryAddWithoutValidation("Cookie", $"islepilot_player={_overlayToken}");

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

    private async Task<T> PostAsync<T>(
        Uri uri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _overlayToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Overlay-Version", "2");
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
}

public sealed class IslePilotOverlayAuthenticationException(string message)
    : TelemetryAuthenticationException(message);
