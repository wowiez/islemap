using System.IO;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

/// <summary>
/// Keeps the Steam cookie of every server that runs its own website (SBTC Island,
/// EraGaming, PANDORA) so those servers can appear as saved entries in the account list.
///
/// The sessions are credentials, so the file is DPAPI protected exactly like the
/// IslePilot vault; nothing here is ever logged or shown.
/// </summary>
public sealed class WebsiteSessionStore
{
    private static readonly byte[] Header = "ILM2"u8.ToArray();
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IsleLiveMap.WebsiteSessions.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string _path;

    public WebsiteSessionStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var raw = await File.ReadAllBytesAsync(_path, cancellationToken);
            if (raw.Length <= Header.Length || !raw.AsSpan(0, Header.Length).SequenceEqual(Header))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var cleartext = WindowsDataProtection.Unprotect(raw[Header.Length..], Entropy);
            var sessions = JsonSerializer.Deserialize<Dictionary<string, string>>(cleartext, JsonOptions)
                ?? [];
            return new Dictionary<string, string>(sessions, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            // A vault that cannot be read must not stop the app from starting.
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task SaveAsync(
        string sourceId,
        string cookie,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);

        var sessions = new Dictionary<string, string>(await LoadAsync(cancellationToken), StringComparer.OrdinalIgnoreCase)
        {
            [sourceId] = cookie
        };
        await WriteAsync(sessions, cancellationToken);
    }

    public async Task RemoveAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var sessions = new Dictionary<string, string>(await LoadAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        if (sessions.Remove(sourceId))
        {
            await WriteAsync(sessions, cancellationToken);
        }
    }

    private async Task WriteAsync(
        IReadOnlyDictionary<string, string> sessions,
        CancellationToken cancellationToken)
    {
        var cleartext = JsonSerializer.SerializeToUtf8Bytes(sessions, JsonOptions);
        var protectedData = WindowsDataProtection.Protect(cleartext, Entropy);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new byte[Header.Length + protectedData.Length];
        Header.CopyTo(payload, 0);
        protectedData.CopyTo(payload, Header.Length);
        await File.WriteAllBytesAsync(_path, payload, cancellationToken);
    }
}
