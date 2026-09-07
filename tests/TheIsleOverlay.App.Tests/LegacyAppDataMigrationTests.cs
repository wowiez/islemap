using System.IO;
using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class LegacyAppDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IsleLiveMap.App.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Run_CopiesCookieProfilesAndSettingsWithoutOverwritingCurrentData()
    {
        var legacy = Path.Combine(_root, "legacy");
        var current = Path.Combine(_root, "current");
        WriteFile(legacy, "WebView2", "cookie.db", "legacy-cookie");
        WriteFile(legacy, "WebView2-IslePilot", "steam.db", "legacy-steam");
        WriteFile(legacy, "overlay-layout.json", "legacy-layout");
        WriteFile(current, "overlay-layout.json", "current-layout");

        LegacyAppDataMigration.Run(legacy, current);

        Assert.Equal("legacy-cookie", File.ReadAllText(Path.Combine(current, "WebView2", "cookie.db")));
        Assert.Equal("legacy-steam", File.ReadAllText(Path.Combine(current, "WebView2-IslePilot", "steam.db")));
        Assert.Equal("current-layout", File.ReadAllText(Path.Combine(current, "overlay-layout.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void WriteFile(string root, params string[] parts)
    {
        var content = parts[^1];
        var path = parts[..^1].Aggregate(root, Path.Combine);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
