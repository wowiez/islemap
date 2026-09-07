using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class MapOverlayPresentationTests
{
    [Fact]
    public void Distance_UsesGatewayWorldScaleAndMeters()
    {
        var first = GatewayMapProjection.Project(new WorldLocation { X = 0, Y = 0 });
        var second = GatewayMapProjection.Project(new WorldLocation { X = 30_000, Y = 40_000 });

        Assert.Equal("500 m", MapOverlayPresentation.Distance(first, second));
    }

    [Fact]
    public void PlayerLabel_AppendsDistanceWhenCurrentLocationExists()
    {
        var current = GatewayMapProjection.Project(new WorldLocation { X = 0, Y = 0 });
        var teammate = GatewayMapProjection.Project(new WorldLocation { X = 0, Y = 12_300 });

        Assert.Equal(
            "Raptor (123 m)",
            MapOverlayPresentation.PlayerLabel("Raptor", teammate, current));
        Assert.Equal("Raptor", MapOverlayPresentation.PlayerLabel("Raptor", teammate, null));
    }

    [Theory]
    [InlineData(TelemetrySessionState.Stale, "REQUESTING /MAP…", "REQUESTING /MAP…")]
    [InlineData(TelemetrySessionState.Stale, null, "WS · DATA STALE · REST FALLBACK")]
    [InlineData(TelemetrySessionState.Reconnecting, null, "WS · RECONNECTING · REST FALLBACK")]
    [InlineData(TelemetrySessionState.Live, "REQUESTING /ME…", "REQUESTING /ME…")]
    public void RequestStatus_ShowsRealtimeHealthInTheRequestSlot(
        TelemetrySessionState state,
        string? request,
        string expected)
    {
        Assert.Equal(expected, MapOverlayPresentation.RequestStatus(state, request));
    }
}
