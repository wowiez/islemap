using System.Net;
using System.Text;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.Tests;

public sealed class SbtcIslandTelemetryProviderTests
{
    // The site's own calibration (assets/maps/gateway_v0217.json) maps world to canvas
    // pixels as px = (ax * ue_x + cx) / cal_s and py = (by * ue_y + cy) / cal_s; the
    // provider has to invert exactly that when a feed only carries pixels.
    private const double CalibrationAx = 0.007014388489208633d;
    private const double CalibrationBx = 0.0d;
    private const double CalibrationCx = 3738.26618705036d;
    private const double CalibrationAy = 0.0d;
    private const double CalibrationBy = 0.007004480286738351d;
    private const double CalibrationCy = 4438.719534050179d;
    private const double CalibrationScale = 8.0d;

    private const double HighlandWorldX = -19431.901d;
    private const double HighlandWorldY = -122965.899d;

    [Fact]
    public async Task FriendPins_MergeFlagsAndNamesUseSpeciesFallbackAndMatchWebPixels()
    {
        var handler = new RoutingHandler(
            ("/api/positions", """
                {"signed_in":true,"fresh":true,
                 "you":{"steam_id":"self","x":500,"y":500},
                 "positions":[{"steam_id":"friend","x":512,"y":498.5,"ue_x":0,"ue_y":0},
                   {"steam_id":"repair","name":"Repair"}],
                 "friends":[{"steam_id":"friend","name":"Mate","friend":true,"x":510,"y":490},
                   {"steam_id":"nameless","species":"Ceratosaurus","friend":true,"x":400,"y":400},
                   {"steam_id":"offmap","friend":true,"x":2000,"y":400},
                   {"steam_id":"unflagged","x":450,"y":450}],
                 "group":[{"steam_id":"friend","group":true,"x":512,"y":498.5},
                   {"steam_id":"repair","group":true,"x":300,"y":300}],
                 "squad":[{"steam_id":"squad","squad":true,"x":600,"y":600}]}
                """),
            ("/api/live", """{"signed_in":true,"island_online":true}"""));
        var provider = new SbtcIslandTelemetryProvider(new HttpClient(handler),
            new SbtcIslandOptions { SessionCookieHeader = "session=synthetic" });
        var snapshot = await provider.GetSnapshotAsync();
        var markers = snapshot.Map!.Markers;
        Assert.Equal(5, markers.Count);
        Assert.Equal("self", Assert.Single(markers, marker => marker.Self).SteamId);
        var friend = Assert.Single(markers, marker => marker.SteamId == "friend");
        Assert.True(friend.Friend);
        Assert.True(friend.Group);
        Assert.Equal("Mate", friend.Label);
        Assert.Equal((512d * 8 - 196) / 7800, friend.MapLocation!.Value.Left, 12);
        Assert.Equal((498.5 * 8 - 187) / 7817, friend.MapLocation.Value.Top, 12);
        Assert.Equal("Ceratosaurus", Assert.Single(markers, marker => marker.SteamId == "nameless").Label);
        Assert.Equal("Repair", Assert.Single(markers, marker => marker.SteamId == "repair").Label);
        Assert.DoesNotContain(markers, marker => marker.SteamId is "unflagged" or "offmap");
    }

    private static double PixelX(double worldX) => ((CalibrationAx * worldX) + CalibrationCx) / CalibrationScale;

    private static double PixelY(double worldY) => ((CalibrationBy * worldY) + CalibrationCy) / CalibrationScale;

    [Fact]
    public async Task GetSnapshotAsync_ReadsPositionsAndLiveCardWithTheSessionCookie()
    {
        var handler = new RoutingHandler(
            ("/api/positions", """
                {
                  "ok": true, "feed": "live", "island": "Haven", "signed_in": true,
                  "fresh": true, "scope": "self",
                  "you": {
                    "steam_id": "76561198000000000", "name": "Khang",
                    "species": "Deinosuchus", "ue_x": -19431.901, "ue_y": -122965.899,
                    "yaw": 157.37, "you": true
                  },
                  "positions": [
                    { "steam_id": "76561198000000001", "name": "Mate", "species": "Deinosuchus",
                      "x": 512.0, "y": 498.5, "yaw": 90.0, "friend": true }
                  ],
                  "friends": [], "group": [], "squad": []
                }
                """),
            ("/api/live", """
                {
                  "ok": true, "auth_enabled": true, "poll_seconds": 15, "island_online": true,
                  "feed_age_seconds": 3, "signed_in": true,
                  "dino": {
                    "species": "Deinosuchus", "life_stage": "subadult",
                    "growth_percent": 49.2,
                    "vitals": [
                      { "key": "health", "known": true, "percent": 67.75 },
                      { "key": "stamina", "known": true, "percent": 41.0 },
                      { "key": "hunger", "known": true, "percent": 18.04 },
                      { "key": "thirst", "known": true, "percent": 98.13 },
                      { "key": "oxygen", "known": true, "percent": 88.5 },
                      { "key": "blood", "known": true, "percent": 74.0 }
                    ],
                    "diet": [
                      { "key": "diet_a", "known": true, "percent": 12.5 },
                      { "key": "diet_b", "known": true, "percent": 30.0 },
                      { "key": "diet_c", "known": true, "percent": 57.5 }
                    ],
                    "bleeding_stacks": 0, "fracture_percent": 100.0,
                    "is_prime": false, "is_elder": false, "elder_stacks": 0
                  }
                }
                """));

        var provider = new SbtcIslandTelemetryProvider(
            new HttpClient(handler),
            new SbtcIslandOptions { SessionCookieHeader = "session=abc; cf_clearance=def" });

        var snapshot = await provider.GetSnapshotAsync();

        Assert.Equal("SBTC ISLAND", snapshot.Source);
        Assert.True(snapshot.Success);
        Assert.True(snapshot.ServerOnline);
        Assert.True(snapshot.PlayerOnline);
        Assert.All(handler.Requests.Where(request => request.Path != "/api/ai_positions"),
            request => Assert.Equal("session=abc; cf_clearance=def", request.Cookie));
        Assert.Null(Assert.Single(handler.Requests, request => request.Path == "/api/ai_positions").Cookie);
        Assert.Contains(handler.Requests, request => request.Path == "/api/positions");
        Assert.Contains(handler.Requests, request => request.Path == "/api/live");

        var player = snapshot.Player;
        Assert.NotNull(player);
        Assert.Equal("Deinosuchus", player!.Class);
        // The overlay keys its SBTC layers off the server name, so the site's own
        // island name is carried alongside it.
        Assert.Equal("SBTC Island · Haven", player.Server);
        Assert.Equal("Khang", player.Name);
        Assert.Equal(49.2d, player.GrowthPercent);
        Assert.Equal(67.75d, player.HealthPercent);
        Assert.Equal(41.0d, player.StaminaPercent);
        Assert.Equal(18.04d, player.HungerPercent);
        Assert.Equal(98.13d, player.ThirstPercent);
        Assert.Equal(88.5d, player.OxygenPercent);
        Assert.Equal(74.0d, player.BloodPercent);
        Assert.Equal(100.0d, player.FracturePercent);
        Assert.Equal(0, player.BleedingStacks);
        Assert.Equal("subadult", player.LifeStage);
        Assert.Equal(0, player.ElderStacks);

        // Diet sliders land in the nutrition bars.
        Assert.Equal(12.5d, player.Nutrition!.Carb);
        Assert.Equal(30.0d, player.Nutrition.Protein);
        Assert.Equal(57.5d, player.Nutrition.Lipid);

        // The game's own coordinates are used as-is, and yaw becomes a map heading.
        Assert.Equal(-19431.901d, player.Location!.X);
        Assert.Equal(-122965.899d, player.Location.Y);
        Assert.NotNull(player.ExactMapHeadingDegrees);

        // A friend pin is drawn too, converted from the site's canvas pixels.
        var markers = snapshot.Map!.Markers;
        Assert.Equal(2, markers.Count);
        var self = Assert.Single(markers, marker => marker.Self);
        Assert.Equal(-19431.901d, self.Location!.X, precision: 3);
        var friend = Assert.Single(markers, marker => marker.Group);
        Assert.Equal("Mate", friend.Label);
        var friendWorldX = ((CalibrationBy * ((512.0d * CalibrationScale) - CalibrationCx)) -
                            (CalibrationBx * ((498.5d * CalibrationScale) - CalibrationCy))) /
                           ((CalibrationAx * CalibrationBy) - (CalibrationBx * CalibrationAy));
        var friendWorldY = ((CalibrationAx * ((498.5d * CalibrationScale) - CalibrationCy)) -
                            (CalibrationAy * ((512.0d * CalibrationScale) - CalibrationCx))) /
                           ((CalibrationAx * CalibrationBy) - (CalibrationBx * CalibrationAy));
        Assert.Equal(friendWorldX, friend.Location!.X, precision: 3);
        Assert.Equal(friendWorldY, friend.Location.Y, precision: 3);
    }

    [Fact]
    public async Task PixelOnlyPin_IsConvertedBackToWorldCoordinates()
    {
        var handler = new RoutingHandler(
            ("/api/positions", $$"""
                {
                  "ok": true, "signed_in": true, "fresh": true, "island": "SBTC Island",
                  "you": { "steam_id": "1", "x": {{PixelX(HighlandWorldX)}}, "y": {{PixelY(HighlandWorldY)}}, "you": true }
                }
                """),
            ("/api/live", """{ "ok": true, "signed_in": true, "island_online": true }"""));

        var provider = new SbtcIslandTelemetryProvider(
            new HttpClient(handler),
            new SbtcIslandOptions { SessionCookieHeader = "session=abc" });

        var snapshot = await provider.GetSnapshotAsync();

        Assert.NotNull(snapshot.Player?.Location);
        Assert.Equal(HighlandWorldX, snapshot.Player!.Location!.X, precision: 3);
        Assert.Equal(HighlandWorldY, snapshot.Player.Location.Y, precision: 3);
    }

    [Fact]
    public async Task GrowthFraction_IsReportedAsPercent()
    {
        var handler = new RoutingHandler(
            ("/api/positions", """{ "ok": true, "signed_in": true }"""),
            ("/api/live", """{ "ok": true, "signed_in": true, "island_online": true, "dino": { "species": "Triceratops", "growth": 0.492 } }"""));

        var provider = new SbtcIslandTelemetryProvider(
            new HttpClient(handler),
            new SbtcIslandOptions { SessionCookieHeader = "session=abc" });

        var snapshot = await provider.GetSnapshotAsync();

        Assert.Equal(49.2d, snapshot.Player!.GrowthPercent);
    }

    [Fact]
    public async Task ExpiredSession_IsReportedAsAnAuthenticationFailure()
    {
        // The site answers 200 with signed_in=false instead of 401, so this has to be
        // recognised from the body for the app to offer a fresh Steam sign-in.
        var handler = new RoutingHandler(
            ("/api/positions", """{ "ok": true, "signed_in": false, "reason": "not_signed_in", "positions": [] }"""),
            ("/api/live", """{ "ok": true, "signed_in": false, "reason": "not_signed_in" }"""));

        var provider = new SbtcIslandTelemetryProvider(
            new HttpClient(handler),
            new SbtcIslandOptions { SessionCookieHeader = "session=abc" });

        await Assert.ThrowsAsync<SbtcIslandAuthenticationException>(() => provider.GetSnapshotAsync());
    }

    [Fact]
    public async Task LiveCardWithoutPositions_StillReportsVitals()
    {
        // The island feed can drop out while the player is still in game; the vitals card
        // keeps working, so the HUD must not go blank.
        var handler = new RoutingHandler(
            ("/api/positions", """{ "ok": true, "signed_in": true, "fresh": false, "feed": "no_source" }"""),
            ("/api/live", """
                { "ok": true, "signed_in": true, "island_online": false,
                  "dino": { "species": "Deinosuchus", "growth_percent": 30.0,
                            "vitals": [ { "key": "health", "known": true, "percent": 55.0 } ] } }
                """));

        var provider = new SbtcIslandTelemetryProvider(
            new HttpClient(handler),
            new SbtcIslandOptions { SessionCookieHeader = "session=abc" });

        var snapshot = await provider.GetSnapshotAsync();

        Assert.True(snapshot.PlayerOnline);
        Assert.Equal(55.0d, snapshot.Player!.HealthPercent);
        Assert.False(snapshot.ServerOnline);
    }

    private sealed record RecordedRequest(string Path, string? Cookie);

    private sealed class RoutingHandler(params (string Path, string Payload)[] routes) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            Requests.Add(new RecordedRequest(
                path,
                request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : null));

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
                Content = new StringContent("""{"ok":false,"error":"not_found"}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
