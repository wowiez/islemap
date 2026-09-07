using System.Text;
using System.Text.Json;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotCredentialStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IsleLiveMap.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsForTheCurrentWindowsUser()
    {
        var path = Path.Combine(_directory, "islepilot.credential");
        var store = new IslePilotCredentialStore(path);
        var expected = new IslePilotOverlayAuthResult(
            "76561198000000000",
            "header.payload.signature");

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Save_DoesNotWriteTheTokenOrSteamIdInPlaintext()
    {
        var path = Path.Combine(_directory, "islepilot.credential");
        var store = new IslePilotCredentialStore(path);
        const string steamId = "76561198000000000";
        const string token = "plain-text-token-must-not-leak";

        await store.SaveAsync(new IslePilotOverlayAuthResult(steamId, token));
        var stored = await File.ReadAllBytesAsync(path);

        Assert.Equal(-1, stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(token)));
        Assert.Equal(-1, stored.AsSpan().IndexOf(Encoding.Unicode.GetBytes(token)));
        Assert.Equal(-1, stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(steamId)));
    }

    [Fact]
    public async Task Vault_StoresSelectsAndRemovesMultipleSteamAccounts()
    {
        var path = Path.Combine(_directory, "islepilot.credential");
        var store = new IslePilotCredentialStore(path);
        var first = new IslePilotOverlayAuthResult(
            "76561198000000000",
            "first-token",
            "old_fox2000");
        var second = new IslePilotOverlayAuthResult(
            "76561198000000001",
            "second-token",
            "second_player");

        await store.SaveAsync(first);
        await store.SaveAsync(second);

        Assert.Equal([first, second], await store.LoadAllAsync());
        Assert.Equal(second, await store.LoadAsync());

        await store.SelectAsync(first.SteamId);
        Assert.Equal(first, await store.LoadAsync());

        await store.RemoveAsync(first.SteamId);
        Assert.Equal([second], await store.LoadAllAsync());
        Assert.Equal(second, await store.LoadAsync());
    }

    [Fact]
    public async Task SavingAnExistingSteamId_UpdatesItsNameAndTokenWithoutDuplication()
    {
        var path = Path.Combine(_directory, "islepilot.credential");
        var store = new IslePilotCredentialStore(path);
        const string steamId = "76561198000000000";

        await store.SaveAsync(new IslePilotOverlayAuthResult(steamId, "old-token"));
        var updated = new IslePilotOverlayAuthResult(steamId, "new-token", "Steam Name");
        await store.SaveAsync(updated);

        Assert.Equal(updated, Assert.Single(await store.LoadAllAsync()));
    }

    [Fact]
    public async Task MigrateLegacy_DecryptsAndReprotectsCredentialsForCurrentStore()
    {
        var legacyPath = Path.Combine(_directory, "legacy", "islepilot.credential");
        var currentPath = Path.Combine(_directory, "current", "islepilot.credential");
        const string steamId = "76561198000000000";
        const string token = "legacy-token";
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 2,
            SelectedSteamId = steamId,
            Accounts = new[] { new { SteamId = steamId, OverlayToken = token, PersonaName = "Player" } }
        });
        var entropy = Encoding.UTF8.GetBytes(string.Join(
            string.Empty,
            "K", "Long", "Dev", ".IsleLiveMap.IslePilotOverlay.v1"));
        var encrypted = WindowsDataProtection.Protect(payload, entropy);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        await File.WriteAllBytesAsync(legacyPath, "ILM1"u8.ToArray().Concat(encrypted).ToArray());

        var store = new IslePilotCredentialStore(currentPath);
        Assert.True(await store.MigrateLegacyAsync(legacyPath));
        Assert.Equal(
            new IslePilotOverlayAuthResult(steamId, token, "Player"),
            await store.LoadAsync());
        Assert.True(File.Exists(legacyPath));
        Assert.False(await store.MigrateLegacyAsync(legacyPath));
    }

    [Fact]
    public async Task Clear_RemovesTheSavedCredential()
    {
        var path = Path.Combine(_directory, "islepilot.credential");
        var store = new IslePilotCredentialStore(path);
        await store.SaveAsync(new IslePilotOverlayAuthResult(
            "76561198000000000",
            "secret"));

        store.Clear();

        Assert.False(File.Exists(path));
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task Load_ReturnsNullForCorruptData()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "islepilot.credential");
        await File.WriteAllBytesAsync(path, [0x49, 0x4C, 0x4D, 0x31, 0x01]);
        var store = new IslePilotCredentialStore(path);

        Assert.Null(await store.LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
