using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotOverlayStateReducerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OverlayOptions_RefreshGrowthAndMapQuickly()
    {
        var options = new IslePilotOverlayOptions { OverlayToken = "token" };

        Assert.Equal(TimeSpan.FromSeconds(5), options.MeRefreshInterval);
        Assert.Equal(TimeSpan.FromSeconds(2), options.MapRefreshInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), options.MarkersRefreshInterval);
        Assert.Equal(TimeSpan.FromSeconds(6), options.PositionFallbackAfter);
        Assert.Equal(TimeSpan.FromSeconds(3), options.WebSocketConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RestRequestTimeout);
        Assert.Equal(1, options.RestTimeoutRetryCount);
    }

    [Fact]
    public void PartialLiveFrames_DoNotEraseBaselineOrPreviousLiveValues()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Health = 8,
            Nutrition = new IslePilotNutritionDto { Protein = 4 }
        }, Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Thirst = 500,
            Nutrition = new IslePilotNutritionDto { Lipid = 5 }
        }, Now.AddSeconds(1));

        var player = reducer.BuildSnapshot(Now.AddSeconds(1)).Player;

        Assert.Equal(8, player?.ExactVitals?.Health);
        Assert.Equal(20, player?.ExactVitals?.MaxHealth);
        Assert.Equal(7, player?.ExactVitals?.Hunger);
        Assert.Equal(2, player?.Nutrition?.Carb);
        Assert.Equal(4, player?.Nutrition?.Protein);
        Assert.Equal(5, player?.Nutrition?.Lipid);
    }

    [Fact]
    public void HasDinoFalse_HidesPlayerButKeepsMapState()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto { HasDino = false }, Now);

        var snapshot = reducer.BuildSnapshot(Now);

        Assert.False(snapshot.PlayerOnline);
        Assert.Null(snapshot.Player);
        Assert.NotNull(snapshot.Map);
    }

    [Fact]
    public void StaleRealtimePosition_UsesNewerChangedRestMarkerWhileVitalsFallBack()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Health = 8,
            Position = new IslePilotOverlayPositionDto { X = 80, Y = 90, Yaw = 0 }
        }, Now);
        reducer.ApplyMe(Baseline() with { Health = 12 }, Now.AddSeconds(5));
        reducer.ApplyMap(MapWithSelfMarker(30, 40), Now.AddSeconds(5));

        var snapshot = reducer.BuildSnapshot(Now.AddSeconds(5));

        Assert.Equal(TelemetrySessionState.Polling, snapshot.SessionState);
        Assert.True(snapshot.LiveDataStale);
        Assert.Equal(12, snapshot.Player?.ExactVitals?.Health);
        Assert.Equal(30, snapshot.Player?.Location?.X);
        Assert.Equal(40, snapshot.Player?.Location?.Y);
    }

    [Fact]
    public void PositionRemainsAtLastRealtimeCoordinateAfterLifetimeBoundary()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(30, 40), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 80, Y = 90 }
        }, Now);

        Assert.Equal(80, reducer.BuildSnapshot(Now.AddSeconds(4)).Player?.Location?.X);
        Assert.Equal(80, reducer.BuildSnapshot(Now.AddSeconds(4).AddTicks(1)).Player?.Location?.X);
    }

    [Fact]
    public void StatsOnlyFrames_DoNotReplaceLastRealtimePositionWithRestMarker()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(30, 40), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 80, Y = 90 }
        }, Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Health = 8
        }, Now.AddSeconds(5));

        var player = reducer.BuildSnapshot(Now.AddSeconds(5)).Player;

        Assert.Equal(80, player?.Location?.X);
        Assert.Equal(90, player?.Location?.Y);
        Assert.Equal(8, player?.ExactVitals?.Health);
    }

    [Fact]
    public void MapMarker_IsUsedBeforeAnyRealtimePositionHasArrived()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(30, 40), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Health = 8
        }, Now.AddSeconds(1));

        var player = reducer.BuildSnapshot(Now.AddSeconds(1)).Player;

        Assert.Equal(30, player?.Location?.X);
        Assert.Equal(40, player?.Location?.Y);
    }

    [Fact]
    public void PositionOnlyLiveFrames_DoNotKeepOldGrowthAheadOfNewerMeRefresh()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Growth = 0.51,
            Position = new IslePilotOverlayPositionDto { X = 10, Y = 20 }
        }, Now.AddSeconds(1));
        reducer.ApplyMe(Baseline() with { Growth = 0.62 }, Now.AddSeconds(5));
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 11, Y = 21 }
        }, Now.AddSeconds(6));

        var snapshot = reducer.BuildSnapshot(Now.AddSeconds(6));

        Assert.Equal(62, snapshot.Player?.GrowthPercent);
        Assert.Equal(11, snapshot.Player?.Location?.X);
        Assert.Equal(21, snapshot.Player?.Location?.Y);
    }

    [Fact]
    public void FreshRealtimeStats_AreNotOverwrittenByLaterStaleRestResponse()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline() with
        {
            Growth = 0.4,
            Health = 10,
            Hunger = 6,
            Thirst = 7,
            Stamina = 8
        }, Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Growth = 0.51,
            Health = 8,
            Hunger = 5,
            Thirst = 6,
            Stamina = 7
        }, Now.AddSeconds(1));

        // Simulate a slow /me request completing later with an older cached payload.
        reducer.ApplyMe(Baseline() with
        {
            Growth = 0.4,
            Health = 10,
            Hunger = 6,
            Thirst = 7,
            Stamina = 8
        }, Now.AddSeconds(2));

        var player = reducer.BuildSnapshot(Now.AddSeconds(2)).Player;

        Assert.Equal(51, player?.GrowthPercent);
        Assert.Equal(8, player?.ExactVitals?.Health);
        Assert.Equal(5, player?.ExactVitals?.Hunger);
        Assert.Equal(6, player?.ExactVitals?.Thirst);
        Assert.Equal(7, player?.ExactVitals?.Stamina);
    }

    [Fact]
    public void OlderCompletedResponse_IsIgnoredEvenIfItArrivesAfterNewerState()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline() with { Health = 10 }, Now.AddSeconds(2));
        reducer.ApplyMe(Baseline() with { Health = 18 }, Now.AddSeconds(1));

        var player = reducer.BuildSnapshot(Now.AddSeconds(2)).Player;

        Assert.Equal(10, player?.ExactVitals?.Health);
        Assert.Equal(Now.AddSeconds(2), reducer.BuildSnapshot(Now.AddSeconds(2)).UpdatedAt);
    }

    [Fact]
    public void LiveDataBecomesStaleImmediatelyAfterConfiguredLifetime()
    {
        var lifetime = TimeSpan.FromSeconds(4);
        var reducer = new IslePilotOverlayStateReducer(lifetime);
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Health = 8
        }, Now);

        Assert.False(reducer.BuildSnapshot(Now.Add(lifetime)).LiveDataStale);
        Assert.True(reducer.BuildSnapshot(Now.Add(lifetime).AddTicks(1)).LiveDataStale);
    }

    [Fact]
    public void StaleRealtimeStats_FallBackToRestValues()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Health = 8
        }, Now.AddSeconds(1));
        reducer.ApplyMe(Baseline() with { Health = 12 }, Now.AddSeconds(3));

        var player = reducer.BuildSnapshot(Now.AddSeconds(6)).Player;

        Assert.Equal(12, player?.ExactVitals?.Health);
    }

    [Fact]
    public void FreshSocketPosition_WinsOverSlowerMapSelfMarker()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(30, 40), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 80, Y = 90, Yaw = 0 }
        }, Now.AddSeconds(1));

        var player = reducer.BuildSnapshot(Now.AddSeconds(1)).Player;

        Assert.Equal(80, player?.Location?.X);
        Assert.Equal(90, player?.Location?.Y);
    }

    [Fact]
    public void NewMapResponse_DoesNotPullFreshRealtimePositionBackwards()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 80, Y = 90, Yaw = 45 }
        }, Now.AddSeconds(1));
        reducer.ApplyMap(MapWithSelfMarker(30, 40), Now.AddSeconds(2));

        var player = reducer.BuildSnapshot(Now.AddSeconds(2)).Player;

        Assert.Equal(80, player?.Location?.X);
        Assert.Equal(90, player?.Location?.Y);
    }

    [Fact]
    public void MatchingSteamId_WinsOverIncorrectSelfFlag()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(30, 40) with
        {
            Markers =
            [
                new IslePilotOverlayMapMarkerDto
                {
                    SteamId = "another-player",
                    Label = "Wrong self",
                    X = 900,
                    Y = 900,
                    Self = true
                },
                new IslePilotOverlayMapMarkerDto
                {
                    SteamId = "76561198000000000",
                    Label = "Correct player",
                    X = 30,
                    Y = 40
                }
            ]
        }, Now);

        var player = reducer.BuildSnapshot(Now).Player;

        Assert.Equal(30, player?.Location?.X);
        Assert.Equal(40, player?.Location?.Y);
    }

    [Fact]
    public void MeRefresh_UpdatesIdentityServerAndPrimeState()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMe(Baseline() with
        {
            Species = "Tyrannosaurus",
            Server = "Second IslePilot Server",
            Prime = new IslePilotPrimeDto
            {
                Elder = true,
                Eligible = true,
                Done = 3,
                Required = 3,
                Quests = [new IslePilotPrimeQuestDto { Name = "Survive", Done = true }]
            }
        }, Now.AddSeconds(10));

        var player = reducer.BuildSnapshot(Now.AddSeconds(10)).Player;

        Assert.Equal("Tyrannosaurus", player?.Class);
        Assert.Equal("Second IslePilot Server", player?.Server);
        Assert.True(player?.Prime?.Elder);
        Assert.True(player?.Prime?.Eligible);
        Assert.Equal(3, player?.Prime?.Done);
        Assert.Equal(3, player?.Prime?.Required);
        var quest = Assert.Single(player?.Prime?.Quests ?? []);
        Assert.Equal("Survive", quest.Name);
        Assert.True(quest.Done);
    }

    [Fact]
    public void LiveFrame_UpdatesPrimeImmediatelyAndPartialFrameKeepsIt()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Prime = new IslePilotPrimeDto
            {
                Done = 2,
                Required = 5,
                Quests = [new IslePilotPrimeQuestDto { Name = "Visit 4 Patrol zones", Done = false }]
            }
        }, Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto { HasDino = true, Health = 8 }, Now.AddSeconds(1));

        var prime = reducer.BuildSnapshot(Now.AddSeconds(1)).Player?.Prime;

        Assert.Equal(2, prime?.Done);
        Assert.Equal(5, prime?.Required);
        var quest = Assert.Single(prime?.Quests ?? []);
        Assert.False(quest.Done);

        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Prime = new IslePilotPrimeDto
            {
                Done = 3,
                Required = 5,
                Quests = [new IslePilotPrimeQuestDto { Name = "Visit 4 Patrol zones", Done = true }]
            }
        }, Now.AddSeconds(2));

        var updatedPrime = reducer.BuildSnapshot(Now.AddSeconds(2)).Player?.Prime;
        Assert.Equal(3, updatedPrime?.Done);
        Assert.True(Assert.Single(updatedPrime?.Quests ?? []).Done);
    }

    [Fact]
    public void NewerMeRefresh_ReplacesOlderLivePrimeState()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Prime = new IslePilotPrimeDto
            {
                Done = 1,
                Required = 5,
                Quests = [new IslePilotPrimeQuestDto { Name = "Visit a Sanctuary", Done = false }]
            }
        }, Now.AddSeconds(1));

        reducer.ApplyMe(Baseline() with
        {
            Prime = new IslePilotPrimeDto
            {
                Done = 2,
                Required = 5,
                Quests = [new IslePilotPrimeQuestDto { Name = "Visit a Sanctuary", Done = true }]
            }
        }, Now.AddSeconds(10));

        var prime = reducer.BuildSnapshot(Now.AddSeconds(10)).Player?.Prime;
        Assert.Equal(2, prime?.Done);
        Assert.True(Assert.Single(prime?.Quests ?? []).Done);
    }

    [Fact]
    public void ApplyLive_UpdatesHeadingWhileTheDinosaurIsStationary()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(50, 50), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 50, Y = 50, Yaw = 0 }
        }, Now);
        var firstHeading = reducer.BuildSnapshot(Now).Player?.ExactMapHeadingDegrees;

        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 50, Y = 50, Yaw = 90 }
        }, Now.AddMilliseconds(50));
        var secondHeading = reducer.BuildSnapshot(Now.AddMilliseconds(50))
            .Player?.ExactMapHeadingDegrees;

        Assert.NotNull(firstHeading);
        Assert.NotNull(secondHeading);
        Assert.NotEqual(firstHeading, secondHeading);
    }

    [Fact]
    public void MapPois_PreserveOfficialShapeSizeAndColorMetadata()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20) with
        {
            Pois =
            [
                new IslePilotOverlayMapPoiDto
                {
                    Id = "hunting-ground",
                    Name = "Northern Hunting Grounds",
                    Shape = "circle",
                    Size = 0.1,
                    Color = "#38bdf8",
                    Icon = "/maps/icons/sanctuary.webp",
                    Enabled = true,
                    HideLabel = false,
                    Points = [new IslePilotOverlayWorldPointDto { X = 25, Y = 75 }]
                }
            ]
        }, Now);

        var poi = Assert.Single(reducer.BuildSnapshot(Now).Map?.PointsOfInterest ?? []);

        Assert.Equal("circle", poi.Shape);
        Assert.Equal(0.1, poi.Size);
        Assert.Equal("#38bdf8", poi.Color);
        Assert.Equal("/maps/icons/sanctuary.webp", poi.Icon);
        Assert.True(poi.Enabled);
        Assert.False(poi.HideLabel);
        Assert.Equal(0.25, Assert.Single(poi.Points).Left, 6);
        Assert.Equal(0.75, Assert.Single(poi.Points).Top, 6);
    }

    [Fact]
    public void MapMarkers_PreserveOfficialGroupFlag()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20) with
        {
            Markers =
            [
                new IslePilotOverlayMapMarkerDto
                {
                    SteamId = "group-player",
                    Label = "Group mate",
                    X = 30,
                    Y = 40,
                    Group = true
                }
            ]
        }, Now);

        var marker = Assert.Single(reducer.BuildSnapshot(Now).Map?.Markers ?? []);

        Assert.True(marker.Group);
        Assert.False(marker.Self);
        Assert.Equal("Group mate", marker.Label);
        Assert.Equal(0.3, marker.MapLocation!.Value.Left, 6);
        Assert.Equal(0.4, marker.MapLocation!.Value.Top, 6);
    }

    [Fact]
    public void PartialMapRefresh_KeepsLastGoodFixedZoneMetadata()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20) with
        {
            Categories = [new IslePilotOverlayMapCategoryDto { Id = "patrol", Name = "Patrol Zones" }],
            Pois =
            [
                new IslePilotOverlayMapPoiDto
                {
                    Id = "delta",
                    Name = "Delta",
                    CategoryId = "patrol",
                    Shape = "polygon",
                    Points =
                    [
                        new IslePilotOverlayWorldPointDto { X = 10, Y = 10 },
                        new IslePilotOverlayWorldPointDto { X = 20, Y = 10 },
                        new IslePilotOverlayWorldPointDto { X = 20, Y = 20 }
                    ]
                }
            ]
        }, Now);

        reducer.ApplyMap(new IslePilotOverlayMapDto
        {
            Allowed = true,
            Markers = []
        }, Now.AddSeconds(3));

        var snapshot = reducer.BuildSnapshot(Now.AddSeconds(3));
        var poi = Assert.Single(snapshot.Map?.PointsOfInterest ?? []);
        Assert.Equal("Delta", poi.Name);
        Assert.Equal("Patrol Zones", poi.CategoryName);
        Assert.Equal(3, poi.Points.Count);
        Assert.Empty(snapshot.Map?.Markers ?? []);
    }

    [Fact]
    public void DedicatedSbtcMarkers_OverrideEmbeddedMapMarkersAndKeepLastGoodResponse()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline() with { Server = "SBTC ISLAND" }, Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20), Now);
        reducer.ApplyMarkers(new IslePilotOverlayMarkersDto
        {
            Ok = true,
            Markers =
            [
                new IslePilotOverlayMapMarkerDto
                {
                    SteamId = "friend",
                    Label = "Dedicated friend",
                    X = 30,
                    Y = 40,
                    Group = true
                }
            ]
        }, Now.AddSeconds(1));
        reducer.ApplyMap(MapWithSelfMarker(50, 60), Now.AddSeconds(2));
        reducer.ApplyMarkers(new IslePilotOverlayMarkersDto { Ok = false }, Now.AddSeconds(3));

        var snapshot = reducer.BuildSnapshot(Now.AddSeconds(3));
        var marker = Assert.Single(snapshot.Map?.Markers ?? []);

        Assert.Equal("Dedicated friend", marker.Label);
        Assert.True(marker.Group);
        Assert.Equal(Now.AddSeconds(2), snapshot.Map?.UpdatedAt);
    }

    [Fact]
    public void StalledWebSocketPosition_FallsBackToNewMarkerUntilWebSocketActuallyMoves()
    {
        var reducer = new IslePilotOverlayStateReducer(
            liveDataLifetime: TimeSpan.FromSeconds(4),
            positionFallbackAfter: TimeSpan.FromSeconds(6));
        reducer.ApplyMe(Baseline() with { Server = "SBTC ISLAND" }, Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 80, Y = 90, Yaw = 10 }
        }, Now);
        reducer.ApplyMarkers(new IslePilotOverlayMarkersDto
        {
            Ok = true,
            Markers =
            [
                new IslePilotOverlayMapMarkerDto
                {
                    SteamId = "76561198000000000",
                    Self = true,
                    X = 30,
                    Y = 40,
                    Yaw = 20
                }
            ]
        }, Now.AddSeconds(7));

        var markerFallback = reducer.BuildSnapshot(Now.AddSeconds(7)).Player;
        Assert.Equal(30, markerFallback?.Location?.X);
        Assert.Equal(40, markerFallback?.Location?.Y);
        var markerHeading = markerFallback?.ExactMapHeadingDegrees;

        // A repeated WS coordinate may carry a fresh yaw, but must not reclaim
        // position ownership and pull the map back to its old X/Y.
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 80, Y = 90, Yaw = 45 }
        }, Now.AddSeconds(8));
        var repeatedSocket = reducer.BuildSnapshot(Now.AddSeconds(8)).Player;
        Assert.Equal(30, repeatedSocket?.Location?.X);
        Assert.Equal(40, repeatedSocket?.Location?.Y);
        Assert.NotEqual(markerHeading, repeatedSocket?.ExactMapHeadingDegrees);

        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 81, Y = 91, Yaw = 50 }
        }, Now.AddSeconds(9));
        var recoveredSocket = reducer.BuildSnapshot(Now.AddSeconds(9)).Player;
        Assert.Equal(81, recoveredSocket?.Location?.X);
        Assert.Equal(91, recoveredSocket?.Location?.Y);
    }

    [Fact]
    public void ReconnectingWebSocket_ImmediatelyUsesNewSelfMarkerFromMapEndpoint()
    {
        var reducer = new IslePilotOverlayStateReducer(
            liveDataLifetime: TimeSpan.FromSeconds(4),
            positionFallbackAfter: TimeSpan.FromSeconds(6));
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 80, Y = 90, Yaw = 10 }
        }, Now.AddSeconds(1));
        reducer.ApplyMap(MapWithSelfMarker(30, 40), Now.AddSeconds(2));
        reducer.SetSessionState(TelemetrySessionState.Reconnecting);

        var fallback = reducer.BuildSnapshot(Now.AddSeconds(2)).Player;

        Assert.Equal(30, fallback?.Location?.X);
        Assert.Equal(40, fallback?.Location?.Y);
        Assert.Equal(TelemetrySessionState.Polling, reducer.BuildSnapshot(Now.AddSeconds(2)).SessionState);
    }

    [Fact]
    public void StalledWebSocketYaw_FallsBackToLatestMarkerYaw()
    {
        var reducer = new IslePilotOverlayStateReducer(
            liveDataLifetime: TimeSpan.FromSeconds(4));
        reducer.ApplyMe(Baseline() with { Server = "SBTC ISLAND" }, Now);
        reducer.ApplyMap(MapWithSelfMarker(50, 50), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 50, Y = 50, Yaw = 10 }
        }, Now);
        var liveHeading = reducer.BuildSnapshot(Now).Player?.ExactMapHeadingDegrees;

        reducer.ApplyMarkers(new IslePilotOverlayMarkersDto
        {
            Ok = true,
            Markers =
            [
                new IslePilotOverlayMapMarkerDto
                {
                    SteamId = "76561198000000000",
                    Self = true,
                    X = 50,
                    Y = 50,
                    Yaw = 80
                }
            ]
        }, Now.AddSeconds(5));

        var fallbackHeading = reducer.BuildSnapshot(Now.AddSeconds(5))
            .Player?.ExactMapHeadingDegrees;

        Assert.NotNull(liveHeading);
        Assert.NotNull(fallbackHeading);
        Assert.NotEqual(liveHeading, fallbackHeading);
    }

    private static IslePilotOverlayMeDto Baseline() => new()
    {
        HasData = true,
        Online = true,
        SteamId = "76561198000000000",
        PersonaName = "Player",
        Species = "Utahraptor",
        Server = "IslePilot Server",
        Female = true,
        Growth = 0.5,
        Health = 10,
        MaxHealth = 20,
        Hunger = 7,
        MaxHunger = 10,
        Thirst = 8,
        MaxThirst = 10,
        Stamina = 9,
        MaxStamina = 10,
        Nutrition = new IslePilotNutritionDto { Carb = 2, Protein = 3, Lipid = 4 }
    };

    private static IslePilotOverlayMapDto MapWithSelfMarker(double x, double y) => new()
    {
        Calibration = new IslePilotMapCalibrationDto
        {
            A = new IslePilotMapCalibrationPointDto { WorldX = 0, WorldY = 0, U = 0, V = 0 },
            B = new IslePilotMapCalibrationPointDto { WorldX = 100, WorldY = 100, U = 1, V = 1 }
        },
        Markers =
        [
            new IslePilotOverlayMapMarkerDto
            {
                SteamId = "76561198000000000",
                Label = "You",
                X = x,
                Y = y,
                Yaw = 90,
                Self = true
            }
        ]
    };
}
