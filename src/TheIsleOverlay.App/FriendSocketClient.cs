using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheIsleOverlay.App;

// One connection receives server-pushed snapshots. A timer tick never polls HTTP.
internal sealed class FriendSocketClient(Uri apiUri) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _snapshotLock = new();
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifetime;
    private TaskCompletionSource<bool>? _ready;
    private PresenceSnapshot _snapshot = new(0, []);
    private DateTimeOffset _snapshotAt;
    private FriendContext? _expectedContext;
    private string? _lastPayload;
    private DateTimeOffset _lastSent;
    private bool _disposed;

    public async Task<PresenceSnapshot> SyncAsync(FriendIdentity identity, string payload, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var state = JsonDocument.Parse(payload);
            var context = state.RootElement.GetProperty("context").Deserialize<FriendContext>(JsonOptions);
            if (context is not null) context = context with { Server = context.Server.Trim().Normalize().ToLowerInvariant() };
            lock (_snapshotLock)
            {
                if (!Equals(context, _expectedContext)) { _snapshot = new(0, []); _snapshotAt = DateTimeOffset.UtcNow; }
                _expectedContext = context;
            }
            if (_socket?.State != WebSocketState.Open)
            {
                Reset();
                var target = new Uri(apiUri, "ws");
                var wsUri = new UriBuilder(target) { Scheme = target.Scheme == "https" ? "wss" : "ws", Port = target.IsDefaultPort ? -1 : target.Port }.Uri;
                var socket = new ClientWebSocket();
                var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
                _socket = socket; _lifetime = lifetime;
                _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var ready = _ready;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    await socket.ConnectAsync(wsUri, deadline.Token);
                    _ = ReceiveAsync(socket, lifetime.Token);
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var nonce = Guid.NewGuid().ToString("N");
                    using var key = ECDsa.Create(); key.ImportFromPem(identity.PrivateKey);
                    var signature = key.SignData(Encoding.UTF8.GetBytes(OverlayFriendsClient.SignatureText("POST", target.AbsolutePath, timestamp, nonce, payload)), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
                    await SendAsync(socket, new
                    {
                        type = "auth", payload, headers = new Dictionary<string, string>
                        {
                            ["x-ilm-user"] = identity.UserId, ["x-ilm-device"] = identity.DeviceId,
                            ["x-ilm-time"] = timestamp, ["x-ilm-nonce"] = nonce,
                            ["x-ilm-signature"] = Convert.ToBase64String(signature)
                        }
                    }, deadline.Token);
                    await ready.Task.WaitAsync(deadline.Token);
                    _lastPayload = payload; _lastSent = DateTimeOffset.UtcNow;
                }
                catch (Exception error) when (error is WebSocketException or OperationCanceledException or HttpRequestException)
                { Reset(); throw new HttpRequestException("Kết nối vị trí bạn bè bị gián đoạn.", error); }
            }
            else if (_lastPayload != payload || DateTimeOffset.UtcNow - _lastSent >= TimeSpan.FromSeconds(5))
            {
                using var data = JsonDocument.Parse(payload);
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(8));
                    await SendAsync(_socket, new { type = "presence", data = data.RootElement }, deadline.Token);
                    _lastPayload = payload; _lastSent = DateTimeOffset.UtcNow;
                }
                catch (WebSocketException error) { Reset(); throw new HttpRequestException("Kết nối vị trí bạn bè bị gián đoạn.", error); }
            }
            lock (_snapshotLock)
            {
                // Advance server time even without a pushed update, so old positions still expire.
                return _snapshot with { ServerTime = _snapshot.ServerTime + Math.Max(0, (long)(DateTimeOffset.UtcNow - _snapshotAt).TotalMilliseconds) };
            }
        }
        finally { _gate.Release(); }
    }
    private static Task SendAsync(ClientWebSocket socket, object frame, CancellationToken token) =>
        socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions)), WebSocketMessageType.Text, true, token);

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken token)
    {
        try
        {
            var buffer = new byte[16384];
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (part.MessageType != WebSocketMessageType.Text) throw new WebSocketException("Unexpected frame");
                    stream.Write(buffer, 0, part.Count);
                    if (stream.Length > 262144) throw new WebSocketException("Snapshot too large");
                } while (!part.EndOfMessage);
                using var frame = JsonDocument.Parse(stream.ToArray());
                var type = frame.RootElement.GetProperty("type").GetString();
                if (type == "error") throw new WebSocketException("Authentication or stream error");
                if (type != "snapshot") continue;
                var snapshot = frame.RootElement.Deserialize<PresenceSnapshot>(JsonOptions) ?? throw new JsonException();
                var context = frame.RootElement.GetProperty("context").Deserialize<FriendContext>(JsonOptions);
                lock (_snapshotLock)
                {
                    if (!ReferenceEquals(socket, _socket)) return;
                    if (!Equals(context, _expectedContext)) continue; // Discard frames queued before a server switch.
                    _snapshot = snapshot; _snapshotAt = DateTimeOffset.UtcNow;
                }
                _ready?.TrySetResult(true);
            }
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or JsonException or InvalidOperationException or ObjectDisposedException or KeyNotFoundException)
        { if (ReferenceEquals(socket, _socket)) _ready?.TrySetException(new HttpRequestException("Kết nối vị trí bạn bè bị gián đoạn.")); }
        finally { try { socket.Abort(); } catch (ObjectDisposedException) { } }
    }
    public void Reset()
    {
        var previous = _socket; _socket = null;
        _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
        previous?.Abort(); previous?.Dispose();
        _ready?.TrySetCanceled(); _ready = null;
        _lastPayload = null;
        lock (_snapshotLock) { _snapshot = new(0, []); _snapshotAt = DateTimeOffset.UtcNow; }
    }
    public void Dispose() { _disposed = true; Reset(); }
}
