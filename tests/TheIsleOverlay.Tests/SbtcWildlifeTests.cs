using System.Net;
using System.Text;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.Tests;

public sealed class SbtcWildlifeTests
{
    [Fact]
    public async Task LiveAnimals_MatchTheWebProjectionAndUsePublicPlatformFeed()
    {
        var handler = new WildlifeHandler();
        var provider = Create(handler);
        var snapshot = await provider.GetSnapshotAsync();
        var animal = Assert.Single(snapshot.Map!.PointsOfInterest, point => point.CategoryId == "wildlife");
        Assert.Equal("Goat", animal.Name);
        Assert.Equal("#E0B95E", animal.Color);
        Assert.True(animal.HideLabel);
        var point = Assert.Single(animal.Points);
        Assert.Equal((0.007014388489208633d * -324974d + 3738.26618705036d - 196d) / 7800d, point.Left, 12);
        Assert.Equal((0.007004480286738351d * 218212d + 4438.719534050179d - 187d) / 7817d, point.Top, 12);
        Assert.Equal("?platform=steam", handler.AiQuery);
        Assert.False(handler.AiHadCookie);
        Assert.Single(snapshot.Map.Markers);
        Assert.Equal(50d, snapshot.Player!.HealthPercent);
        Assert.Contains(snapshot.Map.PointsOfInterest, poi => poi.CategoryId == "waters");
    }

    [Fact]
    public async Task InvalidAnimals_AreExcludedAndUnknownSpeciesUsesOtherColour()
    {
        var handler = new WildlifeHandler { AiPayload = """
            {"status":"ok","ai":[null,{},
              {"species":"","ue_x":100,"ue_y":200},
              {"species":"Deer","ue_x":0,"ue_y":0},
              {"species":"Boar","ue_x":1e20,"ue_y":200},
              {"species":"Goat","ue_x":100},
              {"species":" Mystery animal ","ue_x":100,"ue_y":200}]}
            """ };
        var animals = (await Create(handler).GetSnapshotAsync()).Map!.PointsOfInterest.Where(p => p.CategoryId == "wildlife");
        var animal = Assert.Single(animals);
        Assert.Equal("Mystery animal", animal.Name);
        Assert.Equal("#9D95C9", animal.Color);
    }

    [Fact]
    public async Task Polling_UsesEightSecondCacheThenReplacesOldAnimals()
    {
        var clock = new Clock();
        var handler = new WildlifeHandler();
        var provider = Create(handler, clock);
        await provider.GetSnapshotAsync();
        handler.AiPayload = """{"status":"ok","ai":[]}""";
        clock.Advance(7);
        Assert.Contains((await provider.GetSnapshotAsync()).Map!.PointsOfInterest, p => p.CategoryId == "wildlife");
        Assert.Equal(1, handler.AiCalls);
        clock.Advance(1);
        Assert.DoesNotContain((await provider.GetSnapshotAsync()).Map!.PointsOfInterest, p => p.CategoryId == "wildlife");
        Assert.Equal(2, handler.AiCalls);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"status\":\"no_source\",\"ai\":[{\"species\":\"Goat\",\"ue_x\":100,\"ue_y\":200}]}")]
    [InlineData("null")]
    public async Task MissingOrMalformedSource_ClearsOldAnimalsWithoutLosingOtherData(string payload)
    {
        var clock = new Clock();
        var handler = new WildlifeHandler();
        var provider = Create(handler, clock);
        await provider.GetSnapshotAsync();
        handler.AiPayload = payload;
        clock.Advance(8);
        var snapshot = await provider.GetSnapshotAsync();
        Assert.DoesNotContain(snapshot.Map!.PointsOfInterest, p => p.CategoryId == "wildlife");
        Assert.Contains(snapshot.Map.PointsOfInterest, p => p.CategoryId == "waters");
        Assert.Single(snapshot.Map.Markers);
        Assert.Equal(50d, snapshot.Player!.HealthPercent);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(408)]
    public async Task AiHttpFailure_DoesNotExpireTheSteamSessionAndIsThrottled(int status)
    {
        var handler = new WildlifeHandler { AiStatus = (HttpStatusCode)status };
        var provider = Create(handler);
        Assert.True((await provider.GetSnapshotAsync()).Success);
        Assert.True((await provider.GetSnapshotAsync()).Success);
        Assert.Equal(1, handler.AiCalls);
    }

    [Fact]
    public async Task AiTimeout_DoesNotInterruptTheLiveCard()
    {
        var snapshot = await Create(new WildlifeHandler { TimeoutAi = true }).GetSnapshotAsync();
        Assert.True(snapshot.Success);
        Assert.Equal(50d, snapshot.Player!.HealthPercent);
    }

    [Fact]
    public async Task WildlifeDisabledByServer_DoesNotRequestOrRenderAnimals()
    {
        var handler = new WildlifeHandler { WildlifeEnabled = false };
        var snapshot = await Create(handler).GetSnapshotAsync();
        Assert.DoesNotContain(snapshot.Map!.PointsOfInterest, p => p.CategoryId == "wildlife");
        Assert.Equal(0, handler.AiCalls);
    }

    private static SbtcIslandTelemetryProvider Create(WildlifeHandler handler, TimeProvider? clock = null) =>
        new(new HttpClient(handler), new SbtcIslandOptions { SessionCookieHeader = "session=synthetic" }, clock);

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }

    private sealed class WildlifeHandler : HttpMessageHandler
    {
        public string AiPayload { get; set; } = """{"status":"ok","ai":[{"species":"Goat","ue_x":-324974,"ue_y":218212}]}""";
        public HttpStatusCode AiStatus { get; init; } = HttpStatusCode.OK;
        public bool TimeoutAi { get; init; }
        public bool WildlifeEnabled { get; init; } = true;
        public int AiCalls { get; private set; }
        public string? AiQuery { get; private set; }
        public bool AiHadCookie { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = HttpStatusCode.OK;
            string payload;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/positions":
                    payload = """{"signed_in":true,"you":{"you":true,"steam_id":"synthetic","x":500,"y":500}}""";
                    break;
                case "/api/live":
                    payload = """{"signed_in":true,"dino":{"species":"Ceratosaurus","vitals":[{"key":"health","known":true,"percent":50}]}}""";
                    break;
                case "/assets/maps/mapconfig.json":
                    payload = """{"default_on":["waters"],"ai_groups":{"Goat":"crit"},"ai_colors":{"crit":"#e0b95e","other":"#9d95c9"},"wildlife":WILDLIFE}"""
                        .Replace("WILDLIFE", WildlifeEnabled ? "true" : "false", StringComparison.Ordinal);
                    break;
                case "/assets/data/map_pois.json":
                    payload = """{"categories":{"waters":{"items":[{"n":"Water","x":500,"y":500}]}}}""";
                    break;
                case "/api/ai_positions":
                    AiCalls++;
                    AiQuery = request.RequestUri.Query;
                    AiHadCookie = request.Headers.Contains("Cookie");
                    if (TimeoutAi) throw new TaskCanceledException("Synthetic AI timeout");
                    payload = AiPayload;
                    status = AiStatus;
                    break;
                default: throw new InvalidOperationException("Unexpected fixture route.");
            }
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
        }
    }
}
