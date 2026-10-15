using System.Net;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotWebGarageClientTests
{
    private const string Slug = "sdvn3123123123";
    private const string ActionId = "704d13b05fdc0ebe1d7e379b2641b448b6c86a6006";

    // Shape of a Next.js garage page: the card props travel inside a pushed flight string.
    private static string GaragePage(string extra = "") =>
        "<html><script src=\"/_next/static/chunks/0abc~garage.js\"></script>" + extra +
        "<script>self.__next_f.push([1," + JsonSerializer.Serialize(
            "1a:[\"$\",\"div\",null,{\"children\":[[\"$\",\"$L18\",\"d1\",{\"slug\":\"" + Slug + "\"," +
            "\"dino\":{\"id\":\"d1\",\"customName\":null,\"className\":\"Diabloceratops\",\"gender\":\"Male\",\"growth\":0.5," +
            "\"parkedAt\":\"$D2026-10-12T15:52:25.487Z\",\"state\":{\"healthFrac\":0.9,\"hunger\":50,\"maxHunger\":100," +
            "\"thirst\":25,\"maxThirst\":100,\"stamina\":10,\"maxStamina\":100,\"isPrimeElder\":true}}," +
            "\"palette\":{\"body\":\"#19ffff\",\"eyes\":\"#ffffff\"},\"liveSwap\":true,\"mutationEligible\":false}]]}]\n") +
        "])</script></html>";

    [Fact]
    public void ParseGarage_ReadsTheCardPropsFromTheFlightData()
    {
        var garage = IslePilotWebGarageClient.ParseGarage(GaragePage());

        Assert.True(garage.Settings?.LiveSwap);
        var dino = Assert.Single(garage.Dinos);
        Assert.Equal("d1", dino.Id);
        Assert.Null(dino.Name);
        Assert.Equal("Diabloceratops", dino.Species);
        Assert.Equal("Male", dino.Gender);
        Assert.Equal(0.5, dino.Growth);
        Assert.Equal(0.9, dino.Health);
        Assert.Equal(0.5, dino.Hunger);
        Assert.Equal(0.25, dino.Thirst);
        Assert.Equal(0.1, dino.Stamina);
        Assert.True(dino.IsPrimeElder);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 15, 52, 25, 487, TimeSpan.Zero), dino.ParkedAt);
        Assert.Equal("#19ffff", dino.Palette?.Body);
    }

    [Fact]
    public void ParseGarage_WithoutCardsIsAnEmptyGarage()
    {
        var garage = IslePilotWebGarageClient.ParseGarage("<html><script>self.__next_f.push([1,\"0:{}\\n\"])</script></html>");

        Assert.Empty(garage.Dinos);
        Assert.False(garage.Settings?.LiveSwap);
    }

    [Fact]
    public async Task SignedOutGaragePage_IsAnAuthenticationFailure()
    {
        var handler = new RouteHandler(request =>
            Text("<a href=\"/api/player/steam/login?redirect=%2Fgarage\">Sign in</a>"));
        var client = Client(handler);

        await Assert.ThrowsAsync<IslePilotOverlayAuthenticationException>(() => client.GetGarageAsync());
        Assert.Equal("islepilot_player=synthetic-cookie", handler.Requests[0].Cookie);
    }

    [Fact]
    public void ServerAction_IdAndResultAreReadFromTheBundleAndRscReply()
    {
        const string bundle = "let t=(0,a.createServerReference)(\"" + ActionId +
            "\",a.callServer,void 0,a.findSourceMapURL,\"restoreDino\");let y=(0,a.createServerReference)(\"40472eb75799390627e65f4cabc0bd9f622b444f0e\",a.callServer,void 0,a.findSourceMapURL,\"slayDino\")";

        Assert.Equal(ActionId, IslePilotWebGarageClient.FindServerActionId(bundle, "restoreDino"));
        Assert.Equal("40472eb75799390627e65f4cabc0bd9f622b444f0e", IslePilotWebGarageClient.FindServerActionId(bundle, "slayDino"));
        Assert.Null(IslePilotWebGarageClient.FindServerActionId(bundle, "deleteDino"));

        var result = IslePilotWebGarageClient.ParseActionResult<IslePilotOverlayGarageCommandDto>(
            "0:{\"a\":\"$@1\",\"f\":\"\",\"b\":\"build\"}\n1:{\"ok\":true,\"commandId\":\"c1\"}\n");
        Assert.True(result?.Ok);
        Assert.Equal("c1", result?.CommandId);
    }

    [Fact]
    public async Task Restore_CallsTheDiscoveredServerActionWithSlugAndDino()
    {
        var handler = new RouteHandler(request => request.Path switch
        {
            "/garage" when request.Method == "GET" => Text(GaragePage()),
            "/_next/static/chunks/0abc~garage.js" => Text(
                "(0,a.createServerReference)(\"" + ActionId + "\",a.callServer,void 0,a.findSourceMapURL,\"restoreDino\")"),
            "/garage" => Text("0:{\"a\":\"$@1\"}\n1:{\"ok\":false,\"error\":\"Spawn the same species first\"}\n"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await Client(handler).RestoreAsync("d1");

        Assert.False(result.Ok);
        Assert.Equal("Spawn the same species first", result.Error);
        var action = handler.Requests.Single(request => request.Method == "POST");
        Assert.Equal(ActionId, action.NextAction);
        Assert.Equal("[\"" + Slug + "\",\"d1\"]", action.Body);
    }

    [Fact]
    public async Task Park_WaitsOutTheHoldStillDelayThenFinalizes()
    {
        var handler = new RouteHandler(request => request.Body switch
        {
            var body when body.Contains("\"start\"") => Text("{\"ok\":true,\"pending\":true,\"delaySec\":1}"),
            var body when body.Contains("\"finalize\"") => Text("{\"ok\":true,\"commandId\":\"c9\"}"),
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        });

        var result = await Client(handler).ParkAsync();

        Assert.Equal("c9", result.CommandId);
        Assert.Equal(
            ["{\"slug\":\"" + Slug + "\",\"step\":\"start\"}", "{\"slug\":\"" + Slug + "\",\"step\":\"finalize\"}"],
            handler.Requests.Select(request => request.Body));
    }

    [Fact]
    public async Task Park_CancelledDuringTheDelayTellsTheServerToStop()
    {
        var handler = new RouteHandler(request => Text(request.Body.Contains("\"start\"")
            ? "{\"ok\":true,\"pending\":true,\"delaySec\":30}"
            : "{\"ok\":true}"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).ParkAsync(cancellation.Token));

        Assert.Contains(handler.Requests, request => request.Body.Contains("\"cancel\""));
        Assert.DoesNotContain(handler.Requests, request => request.Body.Contains("\"finalize\""));
    }

    [Fact]
    public async Task ApplySkin_UsesTheServerIdFromTheSkinPage()
    {
        var skinPage = "<script>self.__next_f.push([1," + JsonSerializer.Serialize(
            "5:[\"$\",\"$L1\",null,{\"canSetSkin\":true,\"isAdmin\":false,\"serverId\":\"srv1\",\"slug\":\"" + Slug + "\"}]\n") + "])</script>";
        var handler = new RouteHandler(request => request.Path switch
        {
            "/skin" => Text(skinPage),
            "/api/skin/set" => Text("{\"ok\":true}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await Client(handler).ApplySkinAsync("Diabloceratops",
            new IslePilotOverlayGaragePaletteDto { Body = "#FFFFFF" }, female: false, theme: 0, pattern: 1, variation: 8);

        Assert.True(result.Accepted);
        using var body = JsonDocument.Parse(handler.Requests.Single(request => request.Path == "/api/skin/set").Body);
        Assert.Equal("srv1", body.RootElement.GetProperty("serverId").GetString());
        var payload = body.RootElement.GetProperty("payload");
        Assert.Equal("BP_Diabloceratops_C", payload.GetProperty("class").GetString());
        Assert.False(payload.GetProperty("female").GetBoolean());
        Assert.Equal(1, payload.GetProperty("pattern").GetInt32());
        Assert.Equal(1d, payload.GetProperty("body")[0].GetDouble());
    }

    [Fact]
    public async Task ApplyGlitchDraft_SendsTheLayersLikeTheWebStudio()
    {
        var skinPage = "<script>self.__next_f.push([1," + JsonSerializer.Serialize(
            "5:[\"$\",\"$L1\",null,{\"canSetSkin\":true,\"serverId\":\"srv1\",\"slug\":\"" + Slug + "\"}]\n") + "])</script>";
        var handler = new RouteHandler(request => request.Path switch
        {
            "/skin" => Text(skinPage),
            "/api/skin/set" => Text("{\"ok\":true}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var draft = new IslePilotOverlaySkinDraftPayloadDto
        {
            Sex = "male", RenderMode = "glitched", Pattern = 0,
            Palette = new IslePilotOverlayGaragePaletteDto { Body = "#000000", Teeth = "#FFFFFF" },
            GlitchLab = new IslePilotOverlaySkinGlitchLabDto
            {
                Layers = new Dictionary<string, IslePilotOverlaySkinGlitchLayerDto>
                {
                    ["m"] = new() { X = -10, Y = 15, Z = 120, A = 1 },
                    ["b"] = new() { X = 80, Y = 0, Z = -5 }
                }
            }
        };

        await Client(handler).ApplySkinDraftAsync("Triceratops", draft);

        using var body = JsonDocument.Parse(handler.Requests.Single(request => request.Path == "/api/skin/set").Body);
        var payload = body.RootElement.GetProperty("payload");
        Assert.True(payload.GetProperty("glitch").GetBoolean());
        Assert.False(payload.GetProperty("female").GetBoolean());
        Assert.Equal([-10d, 15d, 120d, 1d], payload.GetProperty("body").EnumerateArray().Select(v => v.GetDouble()));
        // Zero becomes 1e-4 and a missing alpha 1, as the studio sends them.
        Assert.Equal([80d, 1e-4, -5d, 1d], payload.GetProperty("flank").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal(1d, payload.GetProperty("teeth")[0].GetDouble());
    }

    [Fact]
    public async Task GlitchDraftWithoutLayers_IsRefusedInsteadOfSentAsFlatColours()
    {
        var handler = new RouteHandler(_ => Text("{\"ok\":true}"));
        var draft = new IslePilotOverlaySkinDraftPayloadDto
        {
            RenderMode = "glitched", Palette = new IslePilotOverlayGaragePaletteDto { Body = "#112233" }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).ApplySkinDraftAsync("Triceratops", draft));
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/api/skin/set");
    }

    [Fact]
    public void GlitchDraftFromAnotherSpecies_IsSentAsTheLiveDino()
    {
        var draft = new IslePilotOverlaySkinDraftPayloadDto
        {
            Species = "Triceratops", Sex = "female", RenderMode = "glitched",
            GlitchLab = new IslePilotOverlaySkinGlitchLabDto
            {
                Layers = new Dictionary<string, IslePilotOverlaySkinGlitchLayerDto> { ["m"] = new() { X = 80, Y = -5, Z = -5 } }
            }
        };

        var payload = JsonSerializer.SerializeToElement(
            IslePilotOverlaySkinSetPayloadDto.FromDraft("Diabloceratops", draft with { Sex = "male" }), IslePilotOverlayJson.Options);

        Assert.Equal("BP_Diabloceratops_C", payload.GetProperty("class").GetString());
        Assert.False(payload.GetProperty("female").GetBoolean());
        Assert.True(payload.GetProperty("glitch").GetBoolean());
        Assert.Equal(80d, payload.GetProperty("body")[0].GetDouble());
    }

    [Fact]
    public void StandardPayload_NeverSendsPureBlack()
    {
        var payload = JsonSerializer.SerializeToElement(
            IslePilotOverlaySkinSetPayloadDto.FromPalette("Triceratops", new IslePilotOverlayGaragePaletteDto { Body = "#000000" }),
            IslePilotOverlayJson.Options);

        Assert.True(payload.GetProperty("body")[0].GetDouble() > 0);
        Assert.False(payload.TryGetProperty("glitch", out _));
    }

    private static IslePilotWebGarageClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new Uri("https://3.sdvn.org/"), Slug, "synthetic-cookie");

    private static HttpResponseMessage Text(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8)
    };

    private sealed record SeenRequest(string Method, string Path, string? Cookie, string? NextAction, string Body);

    private sealed class RouteHandler(Func<SeenRequest, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<SeenRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var seen = new SeenRequest(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("Cookie", out var cookies) ? cookies.Single() : null,
                request.Headers.TryGetValues("Next-Action", out var actions) ? actions.Single() : null,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(seen);
            return route(seen);
        }
    }
}
