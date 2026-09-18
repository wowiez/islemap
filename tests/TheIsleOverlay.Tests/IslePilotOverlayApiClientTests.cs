using System.Net;
using System.Text;
using System.Text.Json;
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

    [Fact]
    public async Task SkinDrafts_UsesPlayerCookieWithoutDuplicatingCookieName()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"drafts\":[]}");
        var httpClient = new HttpClient(handler);
        var client = new IslePilotOverlayApiClient(httpClient,
            new IslePilotOverlayOptions { OverlayToken = "islepilot_player=" + Token });

        await client.GetSkinDraftsAsync("sbtcisland");

        Assert.Equal("islepilot_player=" + Token, handler.Cookie);
        Assert.EndsWith("/api/player/skin-drafts?slug=sbtcisland", handler.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Null(handler.AuthorizationScheme);
        Assert.Equal("https://islepilot.eu", handler.Origin);
    }

    [Fact]
    public async Task ApplySkinPalette_PostsToLiveGameEndpointWithCookieOnly()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, "{\"ok\":true}");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.ApplySkinPaletteAsync("cmsxo6wv70nl7o101w2ae292k", "BP_Allosaurus_C", new IslePilotOverlayGaragePaletteDto
        {
            Body = "#112233",
            Display = "#AABBCC",
            Claws = "#0A0B0C"
        });

        Assert.True(result.Accepted);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri("https://islepilot.eu/api/skin/set"), handler.RequestUri);
        Assert.Null(handler.AuthorizationScheme);
        Assert.Equal($"islepilot_player={Token}", handler.Cookie);
        Assert.Contains("\"serverId\":\"cmsxo6wv70nl7o101w2ae292k\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"payload\":{", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"class\":\"BP_Allosaurus_C\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"female\":true", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"body\":[0.005605,0.015996,0.033105,1]", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"male_display\":[0.401978,0.496933,0.603827,1]", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SrgbToLinear_MatchesIslePilotWebColorFormula()
    {
        var palette = new IslePilotOverlayGaragePaletteDto
        {
            Body = "#0a1208",
            Claws = "#124913",
            Detail = "#1c3527",
            Eyes = "#27451c",
            Flank = "#0b2806",
            Display = "#27451c",
            Markings = "#223404",
            Mouth = "#10450d",
            Teeth = "#143d1b",
            Underbelly = "#0b2806"
        };

        var payload = IslePilotOverlaySkinSetPayloadDto.FromPalette("BP_Tyrannosaurus_C", palette, female: true, theme: 1);

        Assert.Equal("BP_Tyrannosaurus_C", payload.Class);
        Assert.Equal([0.003035, 0.006049, 0.002428, 1], payload.Body);
        Assert.Equal([0.006049, 0.066626, 0.006512, 1], payload.Claws);
        Assert.Equal([0.011612, 0.035601, 0.020289, 1], payload.Detail1);
        Assert.Equal([0.020289, 0.059511, 0.011612, 1], payload.Eyes);
        Assert.Equal([0.003347, 0.021219, 0.001821, 1], payload.Flank);
        Assert.Equal([0.020289, 0.059511, 0.011612, 1], payload.MaleDisplay);
        Assert.Equal([0.015996, 0.03434, 0.001214, 1], payload.Markings);
        Assert.Equal([0.005182, 0.059511, 0.004025, 1], payload.Mouth);
        Assert.Equal([0.006995, 0.046665, 0.01096, 1], payload.Teeth);
        Assert.Equal([0.003347, 0.021219, 0.001821, 1], payload.Underbelly);
        Assert.Equal(1, payload.Theme);
        Assert.True(payload.Female);

        var uppercasePayload = IslePilotOverlaySkinSetPayloadDto.FromPalette("TYRANNOSAURUS", palette, female: true, theme: 1);
        Assert.Equal("BP_Tyrannosaurus_C", uppercasePayload.Class);

        var json = JsonSerializer.Serialize(uppercasePayload, IslePilotOverlayJson.Options);
        Assert.DoesNotContain("\"female_display\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"display\"", json, StringComparison.Ordinal);
        Assert.Contains("\"male_display\"", json, StringComparison.Ordinal);
        Assert.Contains("\"class\":\"BP_Tyrannosaurus_C\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveSkinDraft_UsesWebPostShapeWithoutQueryString()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, "{\"id\":\"draft-1\",\"name\":\"test\"}");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await client.SaveSkinDraftAsync("sbtcisland", "Tyrannosaurus", "test",
            new IslePilotOverlayGaragePaletteDto { Body = "#112233" });

        Assert.Equal(new Uri("https://islepilot.eu/api/player/skin-drafts"), handler.RequestUri);
        Assert.Contains("\"slug\":\"sbtcisland\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"drafts\":[{\"name\":\"test\",\"species\":\"Tyrannosaurus\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"payload\":{", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"glitchLab\":{", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"renderMode\":\"standard\"", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplySkinDraft_PreservesWebDraftVariantFields()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, "{\"ok\":true}");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.ApplySkinDraftAsync("cmsxo6wv70nl7o101w2ae292k", "Tyrannosaurus",
            new IslePilotOverlaySkinDraftPayloadDto
            {
                Sex = "female", Variation = 2, Pattern = 3, Theme = 4,
                Palette = new IslePilotOverlayGaragePaletteDto { Body = "#112233" }
            });

        Assert.True(result.Accepted);
        Assert.Contains("\"class\":\"BP_Tyrannosaurus_C\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"female\":true", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"variation\":2", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"pattern\":3", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"theme\":4", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkinDrafts_ReadsSavedColorsFromWebResponse()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            { "drafts": [
              { "id": "draft-1", "name": "hong nhat", "species": "Triceratops",
                "payload": { "species": "Triceratops", "palette": { "body": "#112233", "display": "#AABBCC" } } }
            ] }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.GetSkinDraftsAsync("sbtcisland");
        var draft = Assert.Single(result.Drafts);

        Assert.Equal("hong nhat", draft.Name);
        Assert.Equal("#112233", draft.GetPalette()?.Body);
        Assert.Equal("#AABBCC", draft.GetPalette()?.Display);
    }

    [Fact]
    public async Task Unauthorized_RequiresLoginWithoutLeakingToken()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Unauthorized, "unauthorized");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAnyAsync<TelemetryAuthenticationException>(
            () => client.GetMeAsync());

        Assert.DoesNotContain(Token, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ServerAndDdosFailures_DoNotClassifyTheSavedCredentialAsExpired(
        HttpStatusCode statusCode)
    {
        using var handler = new RecordingHandler(statusCode, "temporarily unavailable");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetMeAsync());

        Assert.IsNotAssignableFrom<TelemetryAuthenticationException>(exception);
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
        public string? Referrer { get; private set; }
        public string? Origin { get; private set; }

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
            Referrer = request.Headers.Referrer?.AbsoluteUri;
            Origin = request.Headers.TryGetValues("Origin", out var origins)
                ? origins.Single()
                : null;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
