using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.App.Tests;

public sealed class KillFeedPresentationTests
{
    [Fact]
    public void NaturalDeath_ShowsEnvironmentInsteadOfInventingAKiller()
    {
        var row = KillFeedRowPresentation.From(new(DateTimeOffset.UtcNow, "natural", false,
            "", "", null, "Bạn Ptera", "Pteranodon", 78.4));
        Assert.Equal("Môi trường", row.Killer);
        Assert.Equal("TỰ NHIÊN", row.Cause);
        Assert.Contains("Pteranodon", row.VictimDetail);
        Assert.Contains("78", row.VictimDetail);
    }

    [Fact]
    public void PvpDeath_KeepsBothPlayersAndTheirSpeciesGrowth()
    {
        var row = KillFeedRowPresentation.From(new(DateTimeOffset.UtcNow, "pvp", true,
            "Hunter", "Deinosuchus", 98, "Victim", "Pteranodon", 78.4));
        Assert.Equal("Hunter", row.Killer);
        Assert.Equal("Victim", row.Victim);
        Assert.Equal("PVP", row.Cause);
        Assert.Equal("Deinosuchus · 98%", row.KillerDetail);
    }
}
