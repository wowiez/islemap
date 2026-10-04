using System.IO;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App.Tests;

public sealed class HomeServerSelectionTests
{
    [Fact]
    public void Selection_RoundTripsTheServerWithoutSavingAccountSecrets()
    {
        var root = Path.Combine(Path.GetTempPath(), "isle-server-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "selection.json");
        try
        {
            var store = new HomeServerSelectionStore(path);
            Assert.Null(store.Load());
            store.Save("SBTC");
            Assert.Equal("sbtc", new HomeServerSelectionStore(path).Load());
            Assert.Equal("{\"SourceId\":\"sbtc\"}", File.ReadAllText(path));
            store.Save("islepilot");
            Assert.Equal("islepilot", store.Load());
            store.Save("unknown");
            Assert.Equal("islepilot", store.Load());
            File.WriteAllText(path, "{invalid json");
            Assert.Null(store.Load());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Selection_RestoresWebsiteAndHostedServerOrFallsBackWhenRemoved()
    {
        var account = new IslePilotOverlayAuthResult("synthetic-account", "synthetic-token", "Tester");
        var choices = HomeWindow.BuildAccountChoices([account], new Dictionary<string, string> { ["sbtc"] = "session=synthetic" });
        Assert.True(HomeWindow.SelectSavedServer(choices, "sbtc", account.SteamId)!.IsWebsiteSession);
        Assert.Equal("sdvn3", HomeWindow.SelectSavedServer(choices, "sdvn3", account.SteamId)!.Source!.Id);
        Assert.Null(HomeWindow.SelectSavedServer(choices, "islepilot", account.SteamId)!.Source);
        Assert.Equal(choices[0], HomeWindow.SelectSavedServer(choices, "removed", account.SteamId));
        Assert.Null(HomeWindow.SelectSavedServer([], "sbtc", account.SteamId));
    }
}
