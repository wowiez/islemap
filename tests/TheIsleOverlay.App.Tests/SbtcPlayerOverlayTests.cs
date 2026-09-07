using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class SbtcPlayerOverlayTests
{
    [Fact]
    public void Create_ShowsAuthorizedFriendsAndGroupButNotSelf()
    {
        var markers = new[]
        {
            Marker("self", "Me", self: true, group: false, 0.5, 0.5),
            Marker("friend", "Friend", self: false, group: false, 0.4, 0.6),
            Marker("group", "Group mate", self: false, group: true, 0.7, 0.3)
        };

        var players = SbtcPlayerOverlay.Create("SBTC ISLAND", markers);

        Assert.Collection(
            players,
            friend =>
            {
                Assert.Equal("Friend", friend.Label);
                Assert.False(friend.Group);
            },
            group =>
            {
                Assert.Equal("Group mate", group.Label);
                Assert.True(group.Group);
            });
        Assert.NotEmpty(SbtcPlayerOverlay.Signature(players));
    }

    [Fact]
    public void Create_DeduplicatesRepeatedSteamMarkerAndRejectsOtherServers()
    {
        var repeated = new[]
        {
            Marker("same", "Player", false, true, 0.2, 0.3),
            Marker("same", "Player", false, true, 0.4, 0.5)
        };

        Assert.Single(SbtcPlayerOverlay.Create("SBTC Gateway", repeated));
        Assert.Empty(SbtcPlayerOverlay.Create("Another server", repeated));
        Assert.Single(SbtcPlayerOverlay.Create(
            "DinoVietNam",
            repeated,
            isIslePilotServer: true));
    }

    [Fact]
    public void Create_IgnoresMarkersWithoutNameOrProjectedPosition()
    {
        var markers = new[]
        {
            new MapMarkerTelemetry { SteamId = "missing-location", Label = "Player" },
            new MapMarkerTelemetry { SteamId = "missing-label", MapLocation = new MapPoint(0.2, 0.3) }
        };

        Assert.Empty(SbtcPlayerOverlay.Create("SBTC ISLAND", markers));
    }

    private static MapMarkerTelemetry Marker(
        string id,
        string label,
        bool self,
        bool group,
        double left,
        double top) => new()
    {
        SteamId = id,
        Label = label,
        Self = self,
        Group = group,
        MapLocation = new MapPoint(left, top),
        ExactMapHeadingDegrees = 45d
    };
}
