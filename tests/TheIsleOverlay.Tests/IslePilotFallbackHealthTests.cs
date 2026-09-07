using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotFallbackHealthTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-05T00:00:00Z");
    private static IslePilotOverlayMeDto Me => new() { HasData = true, Online = true, Health = 10 };
    private static IslePilotOverlayMapDto Map => new() { Allowed = true };

    [Fact]
    public void SilentSocket_HealthyRestContinuesForTenMinutes_ThenExpires()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.SetSessionState(TelemetrySessionState.Stale);
        for (var second = 0; second <= 600; second += 5)
        {
            var now = Start.AddSeconds(second);
            reducer.ApplyMe(Me, now);
            reducer.ApplyMap(Map, now);
            Assert.Equal(TelemetrySessionState.Polling, reducer.BuildSnapshot(now).SessionState);
        }
        Assert.Equal(TelemetrySessionState.Stale, reducer.BuildSnapshot(Start.AddSeconds(616)).SessionState);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OneHealthyRestEndpoint_CannotHideOtherEndpointFailure(bool onlyMap)
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Me, Start);
        reducer.ApplyMap(Map, Start);
        var now = Start.AddSeconds(30);
        if (onlyMap) reducer.ApplyMap(Map, now);
        else reducer.ApplyMe(Me, now);
        Assert.Equal(TelemetrySessionState.Stale, reducer.BuildSnapshot(now).SessionState);
    }

    [Fact]
    public void SlowRestStartedBeforeLive_CannotRollStatsBackAfterLiveExpires()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Me, Start);
        reducer.ApplyLive(new() { HasDino = true, Health = 8 }, Start.AddSeconds(2));
        reducer.ApplyMe(Me, Start.AddSeconds(4), Start.AddSeconds(1));
        Assert.Equal(8, reducer.BuildSnapshot(Start.AddSeconds(8)).Player?.ExactVitals?.Health);
        reducer.ApplyMe(Me with { Health = 7 }, Start.AddSeconds(10), Start.AddSeconds(9));
        Assert.Equal(7, reducer.BuildSnapshot(Start.AddSeconds(10)).Player?.ExactVitals?.Health);
    }

    [Fact]
    public void PartialRest_CannotRefreshTheAgeOfMissingHealth()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Me, Start);
        reducer.ApplyLive(new() { HasDino = true, Health = 8 }, Start.AddSeconds(2));
        reducer.ApplyMe(new() { Online = true }, Start.AddSeconds(10));
        Assert.Equal(8, reducer.BuildSnapshot(Start.AddSeconds(10)).Player?.ExactVitals?.Health);
    }

    [Fact]
    public void Polling_ReturnsToLiveWhenSocketRecovers()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Me, Start);
        reducer.ApplyMap(Map, Start);
        reducer.SetSessionState(TelemetrySessionState.Reconnecting);
        Assert.Equal(TelemetrySessionState.Polling, reducer.BuildSnapshot(Start).SessionState);
        reducer.ApplyLive(new() { HasDino = true, Health = 8 }, Start.AddSeconds(1));
        Assert.Equal(TelemetrySessionState.Live, reducer.BuildSnapshot(Start.AddSeconds(1)).SessionState);
    }

    [Fact]
    public void NewRestOnlineState_CanRecoverFromOldNoDinosaurFrame()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyLive(new() { HasDino = false }, Start);
        reducer.ApplyMe(Me, Start.AddSeconds(10));
        Assert.True(reducer.BuildSnapshot(Start.AddSeconds(10)).PlayerOnline);
    }
}
