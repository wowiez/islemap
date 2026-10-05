using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheIsleOverlay.App;

public sealed record FriendIdentity(string UserId, string DeviceId, string Name, string FriendCode, string PrivateKey, string RecoveryCode);
public sealed record OverlayFriend(string UserId, string Name, string State);
public sealed record FriendPosition(double X, double Y, double? Heading);
public sealed record FriendPresence(string UserId, bool Online, string? Name, string? Species, FriendPosition? Position, long? At, bool SameServer);
public sealed record FriendSnapshot(string UserId, string Name, string FriendCode, IReadOnlyList<OverlayFriend> Friends);
public sealed record PresenceSnapshot(long ServerTime, IReadOnlyList<FriendPresence> Friends);
public sealed record FriendContext(string Server, string Map = "gateway");
internal sealed record AccountResponse(string UserId, string Name, string FriendCode, string? RecoveryCode);

public sealed class FriendIdentityStore(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IsleLiveMap.Friends.v1");
    public FriendIdentity? Load()
    {
        if (!File.Exists(path)) return null;
        // A damaged identity must never silently create a new account.
        return JsonSerializer.Deserialize<FriendIdentity>(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser))
            ?? throw new InvalidDataException("Danh tính bị hỏng. Hãy khôi phục tài khoản bằng mã dự phòng.");
    }
    public void Save(FriendIdentity identity)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(identity), Entropy, DataProtectionScope.CurrentUser);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, data);
        File.Move(temporary, path, overwrite: true);
    }
}

public sealed class OverlayFriendsClient : IDisposable
{
    public static readonly Uri DefaultApiUri = new("https://southtampanailsfl.com/api/islemap/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly FriendIdentityStore _store;
    private readonly FriendSocketClient? _socket;
    private readonly SemaphoreSlim _accountGate = new(1);
    public FriendIdentity? Identity { get; private set; }
    public bool Sharing { get; set; } = true;
    public event Action? IdentityChanged;
    public OverlayFriendsClient(string identityPath, HttpMessageHandler? handler = null, Uri? apiUri = null)
    {
        _store = new(identityPath);
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = apiUri ?? DefaultApiUri;
        _http.Timeout = TimeSpan.FromSeconds(8);
        if (handler is null) _socket = new FriendSocketClient(_http.BaseAddress);
    }
    public void LoadIdentity() { Identity = _store.Load(); IdentityChanged?.Invoke(); }
    public async Task RegisterAsync(string name, string? recovery, CancellationToken token)
    {
        await _accountGate.WaitAsync(token);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = Guid.NewGuid().ToString("N");
            var response = await SendAsync<AccountResponse>("account", new { action = string.IsNullOrWhiteSpace(recovery) ? "create" : "recover", name, recoveryCode = recovery?.Trim(), deviceId = device, publicKey = key.ExportSubjectPublicKeyInfoPem() }, false, token);
            var identity = new FriendIdentity(response.UserId, device, response.Name, response.FriendCode, key.ExportPkcs8PrivateKeyPem(), response.RecoveryCode!);
            _store.Save(identity);
            Identity = identity;
            _socket?.Reset();
            Sharing = true;
            IdentityChanged?.Invoke();
        }
        finally { _accountGate.Release(); }
    }
    public async Task RenameAsync(string name, CancellationToken token)
    {
        await _accountGate.WaitAsync(token);
        try
        {
            var response = await SendAsync<AccountResponse>("account", new { action = "rename", name }, true, token);
            var identity = Identity! with { Name = response.Name };
            _store.Save(identity);
            Identity = identity;
            IdentityChanged?.Invoke();
        }
        finally { _accountGate.Release(); }
    }
    public async Task<FriendSnapshot> FriendsAsync(string action, string? value, CancellationToken token, bool byName = false)
    {
        var result = await SendAsync<FriendSnapshot>("friends", new { action, code = byName ? null : value, name = byName ? value : null, userId = value }, true, token);
        if (Identity is { } identity && identity.Name != result.Name)
        {
            Identity = identity with { Name = result.Name };
            _store.Save(Identity);
            IdentityChanged?.Invoke();
        }
        return result;
    }
    public Task<PresenceSnapshot> SyncAsync(FriendContext? context, FriendPosition? position, string? species, CancellationToken token)
    {
        var body = new { sharing = Sharing, context, position = Sharing ? position : null, species = context is null ? null : species };
        return _socket is null ? SendAsync<PresenceSnapshot>("presence", body, true, token)
            : _socket.SyncAsync(Identity ?? throw new InvalidOperationException("Hãy tạo hoặc khôi phục tài khoản trước."), JsonSerializer.Serialize(body, JsonOptions), token);
    }

    internal static string SignatureText(string method, string path, string timestamp, string nonce, string payload) =>
        $"{method}\n{path}\n{timestamp}\n{nonce}\n{payload}";
    private async Task<T> SendAsync<T>(string route, object body, bool authenticated, CancellationToken token)
    {
        var payload = JsonSerializer.Serialize(body, JsonOptions);
        var target = new Uri(_http.BaseAddress!, route);
        using var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = JsonContent.Create(new { payload }, options: JsonOptions) };
        if (authenticated)
        {
            var identity = Identity ?? throw new InvalidOperationException("Hãy tạo hoặc khôi phục tài khoản trước.");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var nonce = Guid.NewGuid().ToString("N");
            using var key = ECDsa.Create();
            key.ImportFromPem(identity.PrivateKey);
            var signature = key.SignData(Encoding.UTF8.GetBytes(SignatureText("POST", target.AbsolutePath, timestamp, nonce, payload)), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            request.Headers.Add("X-ILM-User", identity.UserId);
            request.Headers.Add("X-ILM-Device", identity.DeviceId);
            request.Headers.Add("X-ILM-Time", timestamp);
            request.Headers.Add("X-ILM-Nonce", nonce);
            request.Headers.Add("X-ILM-Signature", Convert.ToBase64String(signature));
        }
        using var response = await _http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            // Show only a known API message, never arbitrary HTML or server diagnostics.
            string message = "API bạn bè chưa sẵn sàng hoặc kết nối bị gián đoạn.";
            try
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (error.RootElement.TryGetProperty("error", out var value) && value.GetString() is { Length: > 0 and < 200 } text) message = text;
            }
            catch (JsonException) { }
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, token) ?? throw new HttpRequestException("API trả dữ liệu trống.");
    }
    public void Dispose() { _socket?.Dispose(); _http.Dispose(); }
}

public static class FriendPresenceRules
{
    public static bool Visible(FriendPresence friend, DateTimeOffset receivedAt, DateTimeOffset now) =>
        friend.Online && friend.SameServer && friend.Position is { } point &&
        friend.At is { } at && now.ToUnixTimeMilliseconds() >= at && now.ToUnixTimeMilliseconds() - at < 10000 &&
        double.IsFinite(point.X) && double.IsFinite(point.Y) && point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1 &&
        now >= receivedAt && now - receivedAt < TimeSpan.FromSeconds(10);
}
