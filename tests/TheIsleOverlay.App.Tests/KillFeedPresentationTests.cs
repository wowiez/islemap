using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.App.Tests;

public sealed class KillFeedPresentationTests
{
    [Fact]
    public void SpeciesFilter_IsSavedSoTheMinimapPanelUsesTheSameOne()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "isle-killfeed-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new OverlayLayoutSettingsStore(path);
            store.Save(store.Load() with { KillFeedSpecies = "Allosaurus" });
            Assert.Equal("Allosaurus", new OverlayLayoutSettingsStore(path).Load().KillFeedSpecies);

            store.Save(store.Load() with { KillFeedSpecies = null });
            Assert.Null(new OverlayLayoutSettingsStore(path).Load().KillFeedSpecies);
        }
        finally { System.IO.File.Delete(path); }
    }

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

    [Theory]
    [InlineData("pvp", true, "#FF7A6E")]
    [InlineData("natural", false, "#D9C58C")]
    [InlineData("fall", false, "#C9A95A")]
    public void Accent_UsesTheLogoPaletteForEachCauseOnBothFeeds(string cause, bool killerKnown, string accent)
    {
        var row = new SbtcKillFeedRow(DateTimeOffset.UtcNow, cause, killerKnown, "Hunter", "Rex", 90, "Victim", "Ptera", 50);
        Assert.Equal(accent, KillFeedRowPresentation.From(row).Accent);
        Assert.Equal(accent, MapKillFeedRowPresentation.From(row).Accent);
    }

    [Fact]
    public void Polling_RefreshesEveryTwoSeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(2), KillFeedPolling.Interval);
}
