using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class FriendSocketTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Socket_AuthenticatesWithExistingIdentity_ReceivesPush_AdvancesTimeWithoutHttpPolling(bool packetOnly)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start(); var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        var endpoint = new Uri($"http://127.0.0.1:{port}/api/islemap/");
        var gameEndpoint = packetOnly ? "udp:192.0.2.10:7777" : null;
        var switchedContext = packetOnly ? new FriendContext("sbtc", Endpoint: "udp:192.0.2.10:7778") : new FriendContext("other");
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var directory = Path.Combine(Path.GetTempPath(), "IsleMap-socket-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "identity.dat");
        var identity = new FriendIdentity(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "Name", "Code", key.ExportPkcs8PrivateKeyPem(), "secret");
        new FriendIdentityStore(path).Save(identity);
        var clientDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var peer = Task.Run(async () =>
            {
                var request = await listener.GetContextAsync().WaitAsync(lifetime.Token);
                Assert.True(request.Request.IsWebSocketRequest);
                Assert.Equal("/api/islemap/ws", request.Request.Url!.AbsolutePath);
                using var socket = (await request.AcceptWebSocketAsync(null)).WebSocket;
                var bytes = new byte[8192]; var part = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), lifetime.Token);
                using var auth = JsonDocument.Parse(bytes.AsMemory(0, part.Count));
                Assert.Equal("auth", auth.RootElement.GetProperty("type").GetString());
                var payload = auth.RootElement.GetProperty("payload").GetString()!;
                var headers = auth.RootElement.GetProperty("headers");
                Assert.Equal(identity.UserId, headers.GetProperty("x-ilm-user").GetString());
                var canonical = OverlayFriendsClient.SignatureText("POST", "/api/islemap/ws", headers.GetProperty("x-ilm-time").GetString()!, headers.GetProperty("x-ilm-nonce").GetString()!, payload);
                Assert.True(key.VerifyData(Encoding.UTF8.GetBytes(canonical), Convert.FromBase64String(headers.GetProperty("x-ilm-signature").GetString()!), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
                Assert.DoesNotContain("PRIVATE KEY", payload); Assert.DoesNotContain("secret", payload);
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var snapshot = JsonSerializer.SerializeToUtf8Bytes(new { type = "snapshot", serverTime = now, context = new { server = "sbtc", map = "gateway", endpoint = gameEndpoint }, friends = new[] { new { userId = "peer", online = true, sameServer = true, name = "Friend", species = "Ptera", at = now - 5000, position = new { x = .4, y = .6, heading = 30 } } } });
                await socket.SendAsync(new ArraySegment<byte>(snapshot), WebSocketMessageType.Text, true, lifetime.Token);
                // The next changed payload must travel over this same connection.
                part = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), lifetime.Token);
                using var update = JsonDocument.Parse(bytes.AsMemory(0, part.Count));
                Assert.Equal("presence", update.RootElement.GetProperty("type").GetString());
                Assert.False(update.RootElement.GetProperty("data").GetProperty("sharing").GetBoolean());
                Assert.Equal(JsonValueKind.Null, update.RootElement.GetProperty("data").GetProperty("position").ValueKind);
                part = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), lifetime.Token);
                using var switchFrame = JsonDocument.Parse(bytes.AsMemory(0, part.Count));
                Assert.Equal(switchedContext.Server, switchFrame.RootElement.GetProperty("data").GetProperty("context").GetProperty("server").GetString());
                Assert.Equal(switchedContext.Endpoint, switchFrame.RootElement.GetProperty("data").GetProperty("context").GetProperty("endpoint").GetString());
                // A queued frame for the previous server must not restore its coordinates.
                await socket.SendAsync(new ArraySegment<byte>(snapshot), WebSocketMessageType.Text, true, lifetime.Token);
                var changed = JsonSerializer.SerializeToUtf8Bytes(new { type = "snapshot", serverTime = now, context = new { server = switchedContext.Server, map = "gateway", endpoint = switchedContext.Endpoint }, friends = new[] { new { userId = "peer", online = true, sameServer = false, name = "Friend", species = "Ptera", at = now, position = (object?)null } } });
                await socket.SendAsync(new ArraySegment<byte>(changed), WebSocketMessageType.Text, true, lifetime.Token);
                await clientDone.Task.WaitAsync(lifetime.Token);
            }, lifetime.Token);
            using var client = new OverlayFriendsClient(path, apiUri: endpoint); client.LoadIdentity();
            var context = new FriendContext("sbtc", Endpoint: gameEndpoint); var point = new FriendPosition(.5, .6, 10);
            var first = await client.SyncAsync(context, point, "Ptera", lifetime.Token);
            Assert.Equal("Friend", Assert.Single(first.Friends).Name);
            await Task.Delay(100, lifetime.Token);
            var second = await client.SyncAsync(context, point, "Ptera", lifetime.Token);
            Assert.True(second.ServerTime > first.ServerTime);
            Assert.Equal(first.Friends[0].At, second.Friends[0].At);
            client.Sharing = false; await client.SyncAsync(context, point, "Ptera", lifetime.Token);
            var switched = await client.SyncAsync(switchedContext, point, "Ptera", lifetime.Token);
            Assert.True(switched.Friends.Count == 0 || switched.Friends.All(friend => friend.Position is null));
            for (var i = 0; i < 50; i++)
            {
                switched = await client.SyncAsync(switchedContext, point, "Ptera", lifetime.Token);
                Assert.All(switched.Friends, friend => Assert.Null(friend.Position));
                if (switched.Friends.Count == 1) break;
                await Task.Delay(20, lifetime.Token);
            }
            Assert.False(Assert.Single(switched.Friends).SameServer);
            clientDone.TrySetResult(true);
            await peer;
            // No account/presence HTTP request was made; only one WebSocket upgrade.
        }
        finally { listener.Stop(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
