using System.Net;
using System.Text;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.Tests;

public sealed class SbtcIslandMapPoiTests
{
    // The zones the server draws on its own website come from public files; the overlay
    // has to read them instead of falling back to the bundled Gateway set.
    [Fact]
    public async Task Snapshot_CarriesTheSitesOwnZonesWithTheirColours()
    {
        var handler = new RoutingHandler(
            ("/api/positions", """{ "ok": true, "signed_in": true, "fresh": true, "island": "Haven" }"""),
            ("/api/live", """{ "ok": true, "signed_in": true, "island_online": true }"""),
            ("/assets/maps/mapconfig.json", """
                {
                  "default_on": ["areas", "waters", "landmarks", "sanctuaries"],
                  "categories": { "waters": { "color": "#5ec5f0", "label": true }, "sanctuaries": { "color": "#6fd79a", "zone": true } },
                  "labels": { "waters": "Water", "sanctuaries": "Sanctuaries" }
                }
                """),
            ("/assets/data/map_pois.json", """
                {
                  "version": 4,
                  "categories": {
                    "waters": { "label": "Water", "items": [ { "x": 620.7, "y": 405.1, "n": "Cascades" } ] },
                    "sanctuaries": { "label": "Sanctuaries", "items": [ { "x": 667.2, "y": 539.1, "n": "Delta side", "r": 9.6 } ] },
                    "animals": { "label": "Animals", "items": [ { "x": 668.9, "y": 259.8, "n": "Boar" } ] },
                    "patrol_zones": { "style":"zone", "items":[ {"x":500,"y":500,"n":"Patrol","poly":[[-10,-10],[10,-10],[0,10]]} ] }
                  }
                }
                """));

        var provider = new SbtcIslandTelemetryProvider(
            new HttpClient(handler),
            new SbtcIslandOptions { SessionCookieHeader = "session=abc" });

        var snapshot = await provider.GetSnapshotAsync();

        var points = snapshot.Map!.PointsOfInterest;
        Assert.Equal(3, points.Count);

        var water = Assert.Single(points, point => point.CategoryId == "waters");
        Assert.Equal("Cascades", water.Name);
        Assert.Equal("Water", water.CategoryName);
        Assert.Equal("#5EC5F0", water.Color);
        Assert.Equal("label", water.Shape);
        var waterPoint = Assert.Single(water.Points);
        // Canvas pixel 620.7 on the 8192 canvas maps into the 7800x7817 picture at (196,187).
        Assert.Equal(0.6114d, waterPoint.Left, precision: 3);
        Assert.Equal(0.3907d, waterPoint.Top, precision: 3);

        // Match the web's 40-point circle and account for the image's padding.
        var sanctuary = Assert.Single(points, point => point.CategoryId == "sanctuaries");
        Assert.Equal("polygon", sanctuary.Shape);
        Assert.Null(sanctuary.Size);
        Assert.Equal(40, sanctuary.Points.Count);
        Assert.Equal(((667.2 + 9.6) * 8 - 196) / 7800, sanctuary.Points[0].Left, precision: 12);
        Assert.Equal((539.1 * 8 - 187) / 7817, sanctuary.Points[0].Top, precision: 12);
        Assert.Equal((667.2 * 8 - 196) / 7800, sanctuary.Points[10].Left, precision: 12);
        Assert.Equal(((539.1 + 9.6) * 8 - 187) / 7817, sanctuary.Points[10].Top, precision: 12);
        Assert.True(sanctuary.HideLabel);
        Assert.False(water.HideLabel);
        Assert.NotNull(water.LabelLocation);
        Assert.Single(points, point => point.CategoryId == "patrol_zones");

        // Categories the site keeps off by default are not sent either.
        Assert.DoesNotContain(points, point => point.CategoryId == "animals");
    }

    [Fact]
    public async Task Snapshot_UsesPolygonOffsetsCategoryRadiusAndIndependentMapCache()
    {
        var routes = new[]
        {
            ("/api/positions", """{"signed_in":true}"""),
            ("/api/live", """{"signed_in":true}"""),
            ("/assets/maps/mapconfig.json", """{"default_on":["sanctuaries"],"categories":{"sanctuaries":{"zone":true}}}"""),
            ("/assets/data/map_pois.json", """
                {"categories":{"sanctuaries":{"radius":12,"labels":"always","items":[
                  {"x":500,"y":500,"n":"Polygon","poly":[[-8,-4],[8,-4],[0,8]]},
                  {"x":300,"y":200,"n":"Circle"}
                ]}}}
                """)
        };
        var provider = new SbtcIslandTelemetryProvider(new HttpClient(new RoutingHandler(routes)),
            new SbtcIslandOptions { SessionCookieHeader = "session=synthetic" });
        var points = (await provider.GetSnapshotAsync()).Map!.PointsOfInterest;
        Assert.Equal(2, points.Count);
        Assert.Equal(3, points[0].Points.Count);
        Assert.Equal((492d * 8 - 196) / 7800, points[0].Points[0].Left, 12);
        Assert.Equal((496d * 8 - 187) / 7817, points[0].Points[0].Top, 12);
        Assert.False(points[0].HideLabel);
        Assert.Equal((500d * 8 - 196) / 7800, points[0].LabelLocation!.Value.Left, 12);
        Assert.Equal(40, points[1].Points.Count);
        Assert.Equal((312d * 8 - 196) / 7800, points[1].Points[0].Left, 12);

        // A second provider/host must not inherit another provider's cached zones.
        routes[3] = (routes[3].Item1, """{"categories":{"sanctuaries":{"items":[]}}}""");
        var other = new SbtcIslandTelemetryProvider(new HttpClient(new RoutingHandler(routes)),
            new SbtcIslandOptions { SessionCookieHeader = "session=synthetic-other" });
        Assert.Empty((await other.GetSnapshotAsync()).Map!.PointsOfInterest);
    }

    [Theory]
    [InlineData("http-error")]
    [InlineData("{broken")]
    [InlineData("null")]
    public async Task Snapshot_KeepsItsCachedStaticRegionsWhenRefreshFailsAndAcceptsRecovery(string failure)
    {
        var clock = new Clock();
        var handler = new RoutingHandler(
            ("/api/positions", """{"signed_in":true}"""),
            ("/api/live", """{"signed_in":true}"""),
            ("/assets/maps/mapconfig.json", """{"default_on":["patrol_zones"],"categories":{"patrol_zones":{"zone":true}}}"""),
            ("/assets/data/map_pois.json", """{"categories":{"patrol_zones":{"items":[{"x":500,"y":500,"n":"Patrol","poly":[[-10,-10],[10,-10],[0,10]]}]}}}"""),
            ("/api/ai_positions", """{"status":"ok","ai":[{"species":"Deer","ue_x":100,"ue_y":200}]}"""));
        var provider = new SbtcIslandTelemetryProvider(new HttpClient(handler),
            new SbtcIslandOptions { SessionCookieHeader = "session=synthetic" }, clock);
        var initial = (await provider.GetSnapshotAsync()).Map!.PointsOfInterest;
        var patrol = Assert.Single(initial, point => point.CategoryId == "patrol_zones");

        clock.Now += TimeSpan.FromMinutes(11);
        handler.PoiOverride = failure;
        var interrupted = await provider.GetSnapshotAsync();
        Assert.True(interrupted.Success);
        Assert.Same(patrol, Assert.Single(interrupted.Map!.PointsOfInterest, point => point.CategoryId == "patrol_zones"));
        Assert.Single(interrupted.Map.PointsOfInterest, point => point.CategoryId == "wildlife");

        handler.PoiOverride = """{"categories":{"migrations":{"items":[{"x":600,"y":600,"n":"Recovered","poly":[[-10,-10],[10,-10],[0,10]]}]}}}""";
        var recovered = (await provider.GetSnapshotAsync()).Map!.PointsOfInterest;
        Assert.DoesNotContain(recovered, point => point.CategoryId == "patrol_zones");
        Assert.Single(recovered, point => point.CategoryId == "migrations");
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 2, 7, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RoutingHandler(params (string Path, string Payload)[] routes) : HttpMessageHandler
    {
        public string? PoiOverride { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/assets/data/map_pois.json" && PoiOverride is { } replacement)
                return Task.FromResult(new HttpResponseMessage(replacement == "http-error"
                    ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                    { Content = new StringContent(replacement, Encoding.UTF8, "application/json") });
            foreach (var (route, payload) in routes)
            {
                if (string.Equals(route, path, StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"ok":false}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
