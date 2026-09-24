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

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task RevokedOrUnknownToken_IsReportedAsAnAuthenticationFailure(HttpStatusCode statusCode)
    {
        // 410 Gone is what IslePilot answers once a token existed and was replaced;
        // treating it as a transport error left the overlay polling with a dead
        // session instead of asking for a new Steam login.
        using var handler = new RecordingHandler(statusCode, "{}");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<IslePilotOverlayAuthenticationException>(() => client.GetMeAsync());
    }

    [Fact]
    public async Task HostedServer_UsesItsOwnDomainForOverlayRequests()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "hasData": true,
              "steamId": "76561198000000000",
              "personaName": "Player",
              "species": "Tyrannosaurus"
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateHostedClient(httpClient, "3.sdvn.org");

        var me = await client.GetMeAsync();

        Assert.Equal("Tyrannosaurus", me.Species);
        Assert.Equal(new Uri("https://3.sdvn.org/api/overlay/me"), handler.RequestUri);
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
    public async Task SkinDrafts_ParsesWebDraftPayloadWithLinearRgbaArraysAndClass()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "drafts": [
                {
                  "id": "draft-trex",
                  "name": "trexcano",
                  "payload": {
                    "class": "BP_Tyrannosaurus_C",
                    "body": [0.003035, 0.006049, 0.002428, 1],
                    "claws": [0.006049, 0.066626, 0.006512, 1],
                    "detail1": [0.011612, 0.035601, 0.020289, 1],
                    "eyes": [0.020289, 0.059511, 0.011612, 1],
                    "female": true,
                    "flank": [0.003347, 0.021219, 0.001821, 1],
                    "male_display": [0.020289, 0.059511, 0.011612, 1],
                    "markings": [0.015996, 0.03434, 0.001214, 1],
                    "mouth": [0.005182, 0.059511, 0.004025, 1],
                    "pattern": 0,
                    "teeth": [0.006995, 0.046665, 0.01096, 1],
                    "theme": 1,
                    "underbelly": [0.003347, 0.021219, 0.001821, 1]
                  }
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.GetSkinDraftsAsync("sbtcisland");
        var draft = Assert.Single(result.Drafts);

        Assert.Equal("trexcano", draft.Name);
        Assert.Equal("BP_Tyrannosaurus_C", draft.GetSpecies());

        var palette = draft.GetPalette();
        Assert.NotNull(palette);
        Assert.Equal(IslePilotOverlaySkinDraftDto.LinearRgbaToHex(0.003035, 0.006049, 0.002428), palette.Body);
        Assert.Equal("#0A1208", palette.Body);
        Assert.Equal(IslePilotOverlaySkinDraftDto.LinearRgbaToHex(0.006049, 0.066626, 0.006512), palette.Claws);
        Assert.Equal(IslePilotOverlaySkinDraftDto.LinearRgbaToHex(0.011612, 0.035601, 0.020289), palette.Detail);
        Assert.Equal(IslePilotOverlaySkinDraftDto.LinearRgbaToHex(0.020289, 0.059511, 0.011612), palette.Display);
        Assert.Equal(IslePilotOverlaySkinDraftDto.LinearRgbaToHex(0.003347, 0.021219, 0.001821), palette.Flank);
        Assert.Equal(IslePilotOverlaySkinDraftDto.LinearRgbaToHex(0.015996, 0.03434, 0.001214), palette.Markings);

        var payload = draft.GetPayload();
        Assert.NotNull(payload);
        Assert.Equal("BP_Tyrannosaurus_C", payload.Species);
        Assert.True(payload.Female);
        Assert.Equal(1, payload.Theme);
    }

    [Fact]
    public async Task SkinDrafts_ParsesGlitchLabWithFloatCoordinatesWithoutThrowing()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "drafts": [
                {
                  "id": "draft-glitch",
                  "name": "glitched-trex",
                  "payload": {
                    "species": "Tyrannosaurus",
                    "palette": { "body": "#112233" },
                    "glitchLab": {
                      "pi": 0.5,
                      "sv": 1.2,
                      "layers": {
                        "m": {
                          "x": 0.003035,
                          "y": 0.006049,
                          "z": 0.002428,
                          "a": 1
                        }
                      }
                    }
                  }
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.GetSkinDraftsAsync("sbtcisland");
        var draft = Assert.Single(result.Drafts);

        Assert.NotNull(draft.Payload?.GlitchLab);
        Assert.Equal(0.5, draft.Payload.GlitchLab.Pi);
        Assert.Equal(1.2, draft.Payload.GlitchLab.Sv);

        Assert.NotNull(draft.Payload.GlitchLab.Layers);
        Assert.True(draft.Payload.GlitchLab.Layers.TryGetValue("m", out var mLayer));
        Assert.Equal(0.003035, mLayer.X);
        Assert.Equal(0.006049, mLayer.Y);
        Assert.Equal(0.002428, mLayer.Z);
        Assert.Equal(1.0, mLayer.A);
    }

    [Fact]
    public async Task SkinDrafts_FlexibleNumberParsing_HandlesStringsFloatsAndNulls()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "drafts": [
                {
                  "id": "draft-flexible",
                  "name": "flex-dino",
                  "payload": {
                    "species": "Tyrannosaurus",
                    "variation": 2.0,
                    "pattern": "3",
                    "theme": null,
                    "createdAt": 1726712345000,
                    "palette": { "body": "#AABBCC" }
                  }
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.GetSkinDraftsAsync("sbtcisland");
        var draft = Assert.Single(result.Drafts);

        Assert.NotNull(draft.Payload);
        Assert.Equal(2, draft.Payload.Variation);
        Assert.Equal(3, draft.Payload.Pattern);
        Assert.Equal(0, draft.Payload.Theme);
        Assert.NotNull(draft.Payload.CreatedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1726712345000), draft.Payload.CreatedAt);
    }

    [Fact]
    public async Task SkinDrafts_ResilientDraftList_SkipsCorruptedDraft()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "drafts": [
                {
                  "id": "draft-1",
                  "name": "valid 1",
                  "payload": { "species": "Carnotaurus", "palette": { "body": "#111111" } }
                },
                {
                  "id": 12345,
                  "name": "corrupt draft",
                  "payload": "invalid-payload-structure"
                },
                {
                  "id": "draft-3",
                  "name": "valid 3",
                  "payload": { "species": "Ceratosaurus", "palette": { "body": "#333333" } }
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.GetSkinDraftsAsync("sbtcisland");
        Assert.NotNull(result.Drafts);
        Assert.Equal(2, result.Drafts.Count);
        Assert.Equal("valid 1", result.Drafts[0].Name);
        Assert.Equal("valid 3", result.Drafts[1].Name);
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

    [Fact]
    public async Task SaveSkinDraftAsync_MergesWithExistingDrafts_AppendingNewDraft()
    {
        // Arrange: server has one existing draft; saving a new one should keep both.
        var getResponse = """
            {
              "drafts": [
                { "name": "OldDraft", "species": "Allosaurus", "payload": { "id": "aaa", "sex": "male", "name": "OldDraft", "species": "Allosaurus", "theme": 0, "pattern": 0, "variation": 0, "renderMode": "standard", "glitchLab": { "pi": 0, "sv": 0, "layers": {} }, "createdAt": "2025-01-01T00:00:00Z" } }
              ]
            }
            """;
        var postResponse = """{ "name": "NewDraft", "species": "Utahraptor" }""";
        var responses = new Queue<(HttpStatusCode, string)>(
        [
            (HttpStatusCode.OK, getResponse),
            (HttpStatusCode.OK, postResponse)
        ]);
        using var handler = new SequenceHandler(responses);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var palette = new IslePilotOverlayGaragePaletteDto();
        await client.SaveSkinDraftAsync("sbtcisland", "Utahraptor", "NewDraft", palette);

        // The POST body should include both OldDraft and NewDraft.
        Assert.Equal(2, handler.RequestCount);
        var postBody = handler.LastRequestBody;
        Assert.NotNull(postBody);
        Assert.Contains("OldDraft", postBody, StringComparison.Ordinal);
        Assert.Contains("NewDraft", postBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveSkinDraftAsync_ReplacesByName_NoDuplicateWhenNameMatches()
    {
        // Arrange: server already has a draft named "MySkin"; saving with same name replaces it.
        var getResponse = """
            {
              "drafts": [
                { "name": "MySkin", "species": "Ceratosaurus", "payload": { "id": "bbb", "sex": "female", "name": "MySkin", "species": "Ceratosaurus", "theme": 0, "pattern": 0, "variation": 0, "renderMode": "standard", "glitchLab": { "pi": 0, "sv": 0, "layers": {} }, "createdAt": "2025-01-01T00:00:00Z" } },
                { "name": "Other", "species": "Allosaurus", "payload": { "id": "ccc", "sex": "male", "name": "Other", "species": "Allosaurus", "theme": 0, "pattern": 0, "variation": 0, "renderMode": "standard", "glitchLab": { "pi": 0, "sv": 0, "layers": {} }, "createdAt": "2025-01-01T00:00:00Z" } }
              ]
            }
            """;
        var postResponse = """{ "name": "MySkin", "species": "Allosaurus" }""";
        var responses = new Queue<(HttpStatusCode, string)>(
        [
            (HttpStatusCode.OK, getResponse),
            (HttpStatusCode.OK, postResponse)
        ]);
        using var handler = new SequenceHandler(responses);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var palette = new IslePilotOverlayGaragePaletteDto();
        await client.SaveSkinDraftAsync("sbtcisland", "Allosaurus", "MySkin", palette);

        Assert.Equal(2, handler.RequestCount);
        var postBody = handler.LastRequestBody;
        Assert.NotNull(postBody);
        // Parse the posted body to verify exactly one draft item is named "MySkin" and "Other" is preserved.
        using var doc = JsonDocument.Parse(postBody!);
        var drafts = doc.RootElement.GetProperty("drafts").EnumerateArray().ToList();
        Assert.Equal(2, drafts.Count); // "MySkin" (replaced) + "Other" (kept)
        Assert.Single(drafts, d => d.GetProperty("name").GetString() == "MySkin");
        Assert.Single(drafts, d => d.GetProperty("name").GetString() == "Other");
    }

    private static int CountOccurrences(string text, string token)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    private static IslePilotOverlayApiClient CreateClient(HttpClient httpClient) => new(
        httpClient,
        new IslePilotOverlayOptions { OverlayToken = Token });

    private static IslePilotOverlayApiClient CreateHostedClient(HttpClient httpClient, string host) => new(
        httpClient,
        new IslePilotOverlayOptions
        {
            OverlayToken = Token,
            ServiceBaseUri = new Uri($"https://{host}/"),
            WebSocketUri = new Uri($"wss://{host}/ows")
        });

    private sealed class SequenceHandler(Queue<(HttpStatusCode Status, string Body)> responses) : HttpMessageHandler
    {
        private int _count;
        public int RequestCount => _count;
        public string? LastRequestBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var (status, body) = responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

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
