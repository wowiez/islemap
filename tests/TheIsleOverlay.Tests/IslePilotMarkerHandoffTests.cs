using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotMarkerHandoffTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-05T00:00:00Z");
    private static IslePilotOverlayMapMarkerDto Self(double x) => new() { SteamId = "self", Self = true, X = x, Y = x, Yaw = x };
    private static IslePilotOverlayMapDto Map(double x) => new() { Allowed = true, Markers = [Self(x)] };
    private static IslePilotOverlayMarkersDto Markers(double x) => new() { Ok = true, Markers = [Self(x)] };
    private static IslePilotOverlayStateReducer Create()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(new() { HasData = true, Online = true, SteamId = "self", Server = "SBTC ISLAND" }, Start);
        reducer.ApplyMap(Map(10), Start);
        reducer.ApplyMarkers(Markers(20), Start.AddSeconds(1));
        return reducer;
    }

    [Fact]
    public void MapTakesOverAfterDedicatedStreamExpires_AndKeepsUpdating()
    {
        var r = Create();
        r.ApplyMap(Map(30), Start.AddSeconds(4));
        Assert.Equal(20, r.BuildSnapshot(Start.AddSeconds(4)).Player?.Location?.X);
        Assert.Equal(30, r.BuildSnapshot(Start.AddSeconds(8)).Player?.Location?.X);
        r.ApplyMap(Map(40), Start.AddSeconds(10));
        Assert.Equal(40, r.BuildSnapshot(Start.AddSeconds(10)).Player?.Location?.X);
        Assert.Equal(TelemetrySessionState.Polling, r.BuildSnapshot(Start.AddSeconds(10)).SessionState);
    }

    [Fact]
    public void CachedDedicatedResponseCannotReclaimMap_ButNewMovementCan()
    {
        var r = Create();
        r.ApplyMap(Map(30), Start.AddSeconds(8));
        Assert.Equal(30, r.BuildSnapshot(Start.AddSeconds(8)).Player?.Location?.X);
        r.ApplyMarkers(Markers(20), Start.AddSeconds(9));
        Assert.Equal(30, r.BuildSnapshot(Start.AddSeconds(9)).Player?.Location?.X);
        r.ApplyMarkers(Markers(40), Start.AddSeconds(10));
        Assert.Equal(40, r.BuildSnapshot(Start.AddSeconds(10)).Player?.Location?.X);
    }

    [Fact]
    public void OldInFlightDedicatedResponseAndItsRepeatsCannotReclaimMap()
    {
        var r = Create();
        r.ApplyMap(Map(30), Start.AddSeconds(8));
        Assert.Equal(30, r.BuildSnapshot(Start.AddSeconds(8)).Player?.Location?.X);
        r.ApplyMarkers(Markers(25), Start.AddSeconds(9), Start.AddSeconds(3));
        Assert.Equal(30, r.BuildSnapshot(Start.AddSeconds(9)).Player?.Location?.X);
        r.ApplyMarkers(Markers(25), Start.AddSeconds(12), Start.AddSeconds(11));
        Assert.Equal(30, r.BuildSnapshot(Start.AddSeconds(12)).Player?.Location?.X);
    }

    [Fact]
    public void CachedMapCannotPullPositionBackWhenDedicatedStops()
    {
        var r = Create();
        r.ApplyMap(Map(10), Start.AddSeconds(10));
        Assert.Equal(20, r.BuildSnapshot(Start.AddSeconds(10)).Player?.Location?.X);
    }

    [Fact]
    public void FreshWebSocketStillWinsOverMapHandoff()
    {
        var r = Create();
        r.ApplyLive(new() { HasDino = true, Position = new() { X = 80, Y = 80 } }, Start.AddSeconds(7));
        r.ApplyMap(Map(30), Start.AddSeconds(8));
        Assert.Equal(80, r.BuildSnapshot(Start.AddSeconds(8)).Player?.Location?.X);
        Assert.Equal(30, r.BuildSnapshot(Start.AddSeconds(12)).Player?.Location?.X);
    }

    [Fact]
    public void MissingDedicatedSelfUsesMapImmediately()
    {
        var r = Create();
        r.ApplyMarkers(new() { Ok = true, Markers = [] }, Start.AddSeconds(2));
        Assert.Equal(10, r.BuildSnapshot(Start.AddSeconds(2)).Player?.Location?.X);
    }

    [Fact]
    public void SlowMapStartedBeforeWebSocketCannotOverwriteLivePositionAfterExpiry()
    {
        var r = Create();
        r.ApplyLive(new() { HasDino = true, Position = new() { X = 80, Y = 80 } }, Start.AddSeconds(7));
        r.ApplyMap(Map(30), Start.AddSeconds(8), Start.AddSeconds(5));
        Assert.Equal(80, r.BuildSnapshot(Start.AddSeconds(12)).Player?.Location?.X);
        r.ApplyMap(Map(40), Start.AddSeconds(13), Start.AddSeconds(12));
        Assert.Equal(40, r.BuildSnapshot(Start.AddSeconds(13)).Player?.Location?.X);
    }

    [Fact]
    public void LongDedicatedOutageDoesNotFreezeMapOrKeepHealthyFallbackStale()
    {
        var r = Create();
        foreach (var second in new[] { 34, 46, 79, 101, 123, 131 })
        {
            var now = Start.AddSeconds(second);
            r.ApplyMe(new() { Online = true }, now);
            r.ApplyMap(Map(second), now);
            var snapshot = r.BuildSnapshot(now);
            Assert.Equal(second, snapshot.Player?.Location?.X);
            Assert.Equal(TelemetrySessionState.Polling, snapshot.SessionState);
        }
        Assert.Equal(TelemetrySessionState.Stale, r.BuildSnapshot(Start.AddSeconds(160)).SessionState);
    }
}
