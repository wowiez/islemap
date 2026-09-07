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

        LegacyAppDataMigration.Run([legacy], current);

        Assert.Equal("legacy-cookie", File.ReadAllText(Path.Combine(current, "WebView2", "cookie.db")));
        Assert.Equal("legacy-steam", File.ReadAllText(Path.Combine(current, "WebView2-IslePilot", "steam.db")));
        Assert.Equal("current-layout", File.ReadAllText(Path.Combine(current, "overlay-layout.json")));
        Assert.False(Directory.Exists(legacy));
    }

    [Fact]
    public void Run_DoesNotDeleteSourceWhenEncryptedCredentialCannotBeRecovered()
    {
        var legacy = Path.Combine(_root, "legacy-corrupt");
        var current = Path.Combine(_root, "current-corrupt");
        WriteFile(legacy, "islepilot-overlay.credential", "not-a-valid-credential");

        LegacyAppDataMigration.Run([legacy], current);

        Assert.True(Directory.Exists(legacy));
        Assert.True(File.Exists(Path.Combine(legacy, "islepilot-overlay.credential")));
    }

    [Fact]
    public void Run_NeverDeletesInstallerOwnedSource()
    {
        var installerRoot = Path.Combine(_root, "installer-owned");
        var current = Path.Combine(_root, "safe-data");
        WriteFile(installerRoot, "overlay-layout.json", "layout");

        LegacyAppDataMigration.Run([installerRoot], current, deleteSources: false);

        Assert.True(Directory.Exists(installerRoot));
        Assert.Equal("layout", File.ReadAllText(Path.Combine(current, "overlay-layout.json")));
    }

    [Fact]
    public void AppDataRoot_HasNoVendorOrPersonalNamespace()
    {
        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IsleLiveMapData"),
            AppPaths.Root);
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
