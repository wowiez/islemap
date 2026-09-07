using System.Net;
using System.Text;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotOverlayApiClientTests
{
    private const string Token = "super-secret-overlay-token";

    [Fact]
    public async Task GetMeAsync_UsesFixedServiceHostAndRequiredBearerHeaders()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "hasData": true,
              "steamId": "76561198000000000",
              "personaName": "Player",
              "species": "Utahraptor"
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var me = await client.GetMeAsync();

        Assert.Equal("Utahraptor", me.Species);
        Assert.Equal(new Uri("https://islepilot.eu/api/overlay/me"), handler.RequestUri);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal(Token, handler.AuthorizationParameter);
        Assert.Equal("2", handler.OverlayVersion);
        Assert.Equal("application/json", handler.Accept);
        Assert.True(handler.NoCache);
        Assert.True(handler.NoStore);
    }

    [Fact]
    public async Task GetMapAsync_UsesOverlayMapEndpoint()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "allowed": true,
              "calibration": {
                "a": { "worldX": 0, "worldY": 0, "u": 0, "v": 0 },
                "b": { "worldX": 100, "worldY": -100, "u": 1, "v": 1 }
              },
              "markers": []
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var map = await client.GetMapAsync();

        Assert.True(map.Allowed);
        Assert.Equal(new Uri("https://islepilot.eu/api/overlay/map"), handler.RequestUri);
    }

    [Fact]
    public async Task GetGarageAsync_UsesOverlayGarageEndpointAndReadsVitals()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "settings": { "liveSwap": true },
              "dinos": [
                {
                  "id": "dino-1",
                  "name": "Tank",
                  "species": "Pachycephalosaurus",
                  "gender": "Male",
                  "growth": 100,
                  "health": 95,
                  "hunger": 64.5,
                  "thirst": 72,
                  "stamina": 88,
                  "palette": { "body": "#334455", "display": "#AABBCC" }
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var garage = await client.GetGarageAsync();

        Assert.True(garage.Settings?.LiveSwap);
        var dino = Assert.Single(garage.Dinos);
        Assert.Equal("Pachycephalosaurus", dino.Species);
        Assert.Equal(64.5, dino.Hunger);
        Assert.Equal("#AABBCC", dino.Palette?.Display);
        Assert.Equal(new Uri("https://islepilot.eu/api/overlay/garage"), handler.RequestUri);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("2", handler.OverlayVersion);
    }

    [Fact]
    public async Task ParkGarageDinoAsync_PostsValidatedStep()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            { "ok": true, "pending": true, "delaySec": 5 }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.ParkGarageDinoAsync("start");

        Assert.True(result.Ok);
        Assert.True(result.Pending);
        Assert.Equal(5, result.DelaySec);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri("https://islepilot.eu/api/overlay/garage/park"), handler.RequestUri);
        Assert.Contains("\"step\":\"start\"", handler.RequestBody, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ParkGarageDinoAsync("invalid"));
    }

    [Fact]
    public async Task RestoreAndStatus_UseDinoAndCommandIdentifiersWithoutLeakingToken()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            { "ok": true, "commandId": "command-1", "status": "done" }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var restore = await client.RestoreGarageDinoAsync("dino-1");

        Assert.True(restore.Ok);
        Assert.Equal("command-1", restore.CommandId);
        Assert.Equal(new Uri("https://islepilot.eu/api/overlay/garage/dino-1/restore"), handler.RequestUri);
        Assert.Equal(HttpMethod.Post, handler.Method);

        var status = await client.GetGarageCommandStatusAsync("command-1");

        Assert.Equal("done", status.Status);
        Assert.Equal(
            new Uri("https://islepilot.eu/api/overlay/garage/status?id=command-1"),
            handler.RequestUri);
        Assert.Equal(HttpMethod.Get, handler.Method);
    }

    [Fact]
    public async Task GetMarkersAsync_UsesDedicatedSbtcEndpointAndPlayerCookie()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "ok": true,
              "markers": [
                { "steamId": "1", "label": "Friend", "x": 10, "y": 20, "group": true }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var markers = await client.GetMarkersAsync();

        Assert.True(markers.Ok);
        Assert.Equal("Friend", Assert.Single(markers.Markers).Label);
        Assert.Equal(
            new Uri("https://islepilot.eu/api/p/sbtcisland/map/markers"),
            handler.RequestUri);
        Assert.Equal($"islepilot_player={Token}", handler.Cookie);
        Assert.Null(handler.AuthorizationScheme);
        Assert.True(handler.NoCache);
        Assert.True(handler.NoStore);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthenticationFailure_RequiresLoginWithoutLeakingToken(HttpStatusCode statusCode)
    {
        using var handler = new RecordingHandler(statusCode, "unauthorized");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAnyAsync<TelemetryAuthenticationException>(
            () => client.GetMeAsync());

        Assert.DoesNotContain(Token, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsHeaderInjectionInToken()
    {
        using var httpClient = new HttpClient(new RecordingHandler(HttpStatusCode.OK, "{}"));
        var options = new IslePilotOverlayOptions { OverlayToken = "token\r\nX-Evil: true" };

        Assert.Throws<ArgumentException>(() => new IslePilotOverlayApiClient(httpClient, options));
    }

    private static IslePilotOverlayApiClient CreateClient(HttpClient httpClient) => new(
        httpClient,
        new IslePilotOverlayOptions { OverlayToken = Token });

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? OverlayVersion { get; private set; }
        public string? Accept { get; private set; }
        public string? Cookie { get; private set; }
        public bool NoCache { get; private set; }
        public bool NoStore { get; private set; }
        public string? RequestBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            OverlayVersion = request.Headers.TryGetValues("X-Overlay-Version", out var versions)
                ? versions.Single()
                : null;
            Accept = request.Headers.Accept.SingleOrDefault()?.MediaType;
            Cookie = request.Headers.TryGetValues("Cookie", out var cookies)
                ? cookies.Single()
                : null;
            NoCache = request.Headers.CacheControl?.NoCache == true;
            NoStore = request.Headers.CacheControl?.NoStore == true;
            RequestBody = request.Content?.ReadAsStringAsync(cancellationToken)
                .GetAwaiter()
                .GetResult();

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
