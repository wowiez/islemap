using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.App;

internal static class SbtcGuideGarageApi
{
    public static GuideGarageApi Create(HttpClient httpClient, TelemetrySourceDefinition source, string cookie)
    {
        var accountKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cookie)))[..24];
        var client = new SbtcIslandVaultClient(httpClient, new SbtcIslandOptions
        {
            BaseUri = source.BaseUri, SessionCookieHeader = cookie
        }) { SkinAttemptStore = new SkinAttemptFile(Path.Combine(AppPaths.Root, "PendingSkins", accountKey + ".json")) };
        return new(
            client.GetVaultAsync,
            client.ParkPreparedAsync,
            client.RestoreAsync,
            client.GetCommandStatusAsync,
            (_, token) => client.GetSkinLibraryAsync(token),
            (_, species, name, palette, _, theme, pattern, variation, token) =>
                client.SaveSkinDesignAsync(species, name, palette, pattern, variation, theme, token),
            (species, palette, theme, pattern, variation, token) =>
                client.PrepareSkinApplyAsync(species, palette, pattern, variation, theme, token),
            client.ApplyPreparedSkinAsync,
            client.GetSkinAccessAsync,
            client.CheckSkinDeliveryAsync,
            client.DownloadModelAssetAsync,
            client.GetParkTermsAsync,
            client.GetSkinPreviewAsync);
    }

    // No cookies or login tokens: only the delivery reference and an intent hash.
    private sealed class SkinAttemptFile(string path) : ISbtcSkinAttemptStore
    {
        public SbtcSkinAttempt? Load()
        {
            if (!File.Exists(path)) return null;
            try { return JsonSerializer.Deserialize<SbtcSkinAttempt>(File.ReadAllText(path)); }
            catch (JsonException exception) { throw new IOException("Không đọc được yêu cầu skin đang chờ. Kiểm tra giao dịch trên web SBTC trước khi đổi skin tiếp.", exception); }
        }
        public void Save(SbtcSkinAttempt? attempt)
        {
            if (attempt is null) { if (File.Exists(path)) File.Delete(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(attempt));
            File.Move(temp, path, overwrite: true);
        }
    }
}
