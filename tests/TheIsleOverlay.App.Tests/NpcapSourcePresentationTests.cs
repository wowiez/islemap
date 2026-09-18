using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class NpcapSourcePresentationTests
{
    [Theory]
    [InlineData(NpcapSourceStatus.WaitingForGame)]
    [InlineData(NpcapSourceStatus.Listening)]
    public void ConnectingStates_HaveOneStableStatus(NpcapSourceStatus status)
    {
        Assert.Equal("NPCAP · CONNECTING", NpcapSourcePresentation.StatusText(true, status));
        Assert.Equal(
            "NPCAP · CONNECTING",
            NpcapSourcePresentation.PositionStatusOrFallback(true, status, "WS · LIVE"));
    }

    [Fact]
    public void LiveState_IsAuthoritativeAndHasStableStatus()
    {
        Assert.True(NpcapSourcePresentation.IsOwnPositionAuthoritative(
            enabled: true,
            NpcapSourceStatus.Live,
            hasPosition: true));
        Assert.Equal("NPCAP · LIVE", NpcapSourcePresentation.StatusText(true, NpcapSourceStatus.Live));
        Assert.Equal(
            "NPCAP · LIVE",
            NpcapSourcePresentation.PositionStatusOrFallback(true, NpcapSourceStatus.Live, "WS · LIVE"));
    }

    [Theory]
    [InlineData(false, NpcapSourceStatus.Live, true)]
    [InlineData(true, NpcapSourceStatus.Live, false)]
    [InlineData(true, NpcapSourceStatus.Listening, true)]
    [InlineData(true, NpcapSourceStatus.Faulted, true)]
    public void NonLiveOrIncompleteSource_DoesNotOwnPlayerPosition(
        bool enabled,
        NpcapSourceStatus status,
        bool hasPosition) =>
        Assert.False(NpcapSourcePresentation.IsOwnPositionAuthoritative(enabled, status, hasPosition));

    [Fact]
    public void DisabledState_HasExplicitStatus() =>
        Assert.Equal("NPCAP · OFF", NpcapSourcePresentation.StatusText(false, NpcapSourceStatus.Live));

    [Theory]
    [InlineData(NpcapSourceStatus.WaitingForGame)]
    [InlineData(NpcapSourceStatus.Listening)]
    [InlineData(NpcapSourceStatus.Live)]
    [InlineData(NpcapSourceStatus.Stopped)]
    public void AutomaticStates_DoNotOfferAConfusingManualRestart(NpcapSourceStatus status) =>
        Assert.False(NpcapSourcePresentation.ShouldShowAction(true, status));

    [Theory]
    [InlineData(NpcapSourceStatus.Unavailable, "TẢI NPCAP")]
    [InlineData(NpcapSourceStatus.Faulted, "MỞ LẠI")]
    public void ActionIsOnlyOfferedWhenUserInterventionCanHelp(NpcapSourceStatus status, string expected)
    {
        Assert.True(NpcapSourcePresentation.ShouldShowAction(true, status));
        Assert.Equal(expected, NpcapSourcePresentation.ActionText(status));
        Assert.False(NpcapSourcePresentation.ShouldShowAction(false, status));
    }
}
