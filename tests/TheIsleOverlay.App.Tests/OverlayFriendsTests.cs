using System.Net;
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class OverlayFriendsTests
{
    [Fact]
    public void FriendDistance_UsesCalibratedMeters_AndNeverShowsHiddenOrOtherServerPosition()
    {
        var point = new TheIsleOverlay.Core.MapPoint(.5, .5);
        var friend = new FriendPresence("user", true, "Name", "Ptera", new(.50000899283152195387, .5, null), 0, true);
        Assert.Equal("Name · 0 m", FriendPresentation.NameAndDistance("Name", friend with { Position = new(.5, .5, null) }, point));
        Assert.Equal("Name · 0 m", FriendPresentation.NameAndDistance("Name", friend, point));
        friend = friend with { Position = new(.50899283152195387, .5, null) };
        Assert.Equal("Name · 100 m", FriendPresentation.NameAndDistance("Name", friend, point));
        Assert.Equal("Name · 1.0 km", FriendPresentation.NameAndDistance("Name", friend with { Position = new(.5899283152195387, .5, null) }, point));
        Assert.Equal("Name", FriendPresentation.NameAndDistance("Name", friend with { SameServer = false }, point));
        Assert.Equal("Name", FriendPresentation.NameAndDistance("Name", friend with { Position = null }, point));
        Assert.Equal("Name", FriendPresentation.NameAndDistance("Name", friend, null));
        Assert.Equal("Name", FriendPresentation.NameAndDistance("Name", friend with { Position = new(double.NaN, .5, null) }, point));
        Assert.Equal("Ptera", FriendPresentation.Detail(friend));
    }
    [Fact]
    public void Identity_IsProtectedAtRestAndRestoredWithoutChangingAccount()
    {
        var directory = Path.Combine(Path.GetTempPath(), "IsleLiveMap-tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "identity.dat");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var identity = new FriendIdentity("user", "device", "Name", "Code", key.ExportPkcs8PrivateKeyPem(), "Secret recovery");
            var store = new FriendIdentityStore(path);
            store.Save(identity);
            Assert.Equal(identity, store.Load());
            Assert.DoesNotContain("PRIVATE KEY", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.DoesNotContain("Secret recovery", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            using var client = new OverlayFriendsClient(path);
            client.LoadIdentity();
            Assert.True(client.Sharing);
            Assert.Equal("user", client.Identity!.UserId);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void CorruptIdentity_IsNotSilentlyOverwritten()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "invalid identity");
            Assert.Throws<CryptographicException>(() => new FriendIdentityStore(path).Load());
            Assert.Equal("invalid identity", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(true, true, 9, true)]
    [InlineData(true, true, 10, false)]
    [InlineData(true, false, 0, false)]
    [InlineData(false, true, 0, false)]
    public void Markers_RequireFreshSameServerOnlinePresence(bool online, bool sameServer, int age, bool expected)
    {
        var received = DateTimeOffset.UtcNow;
        var friend = new FriendPresence("user", online, "Name", "Ptera", new FriendPosition(.5, .5, null), received.ToUnixTimeMilliseconds(), sameServer);
        Assert.Equal(expected, FriendPresenceRules.Visible(friend, received, received.AddSeconds(age)));
        Assert.False(FriendPresenceRules.Visible(friend with { Position = null }, received, received));
        Assert.False(FriendPresenceRules.Visible(friend with { Position = new FriendPosition(double.NaN, .5, null) }, received, received));
    }
    [Fact]
    public async Task SignedRequest_UsesDeviceKeyAndExactPayload_NoPrivateKeyTransmission()
    {
        var directory = Path.Combine(Path.GetTempPath(), "IsleLiveMap-tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "identity.dat");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            new FriendIdentityStore(path).Save(new FriendIdentity("user", "device", "Name", "Code", key.ExportPkcs8PrivateKeyPem(), "secret"));
            using var handler = new InspectHandler(key);
            using var client = new OverlayFriendsClient(path, handler);
            client.LoadIdentity();
            client.Sharing = false;
            var response = await client.SyncAsync(new FriendContext("sbtc"), new FriendPosition(.4, .6, 30), "Ptera", CancellationToken.None);
            Assert.Empty(response.Friends);
            Assert.Empty((await client.FriendsAsync("request", "Name With More Than 12 Chars", CancellationToken.None, byName: true)).Friends);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class InspectHandler(ECDsa key) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var envelope = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var payload = envelope.RootElement.GetProperty("payload").GetString()!;
            string Header(string name) => request.Headers.GetValues(name).Single();
            var canonical = OverlayFriendsClient.SignatureText("POST", request.RequestUri!.AbsolutePath, Header("X-ILM-Time"), Header("X-ILM-Nonce"), payload);
            Assert.True(key.VerifyData(Encoding.UTF8.GetBytes(canonical), Convert.FromBase64String(Header("X-ILM-Signature")), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
            Assert.DoesNotContain("PRIVATE KEY", payload);
            Assert.DoesNotContain("secret", payload);
            if (request.RequestUri.AbsolutePath == "/api/friends")
            {
                using var body = JsonDocument.Parse(payload);
                Assert.Equal("Name With More Than 12 Chars", body.RootElement.GetProperty("name").GetString());
                Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("code").ValueKind);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"userId\":\"user\",\"name\":\"Name\",\"friendCode\":\"Code\",\"friends\":[]}", Encoding.UTF8, "application/json") };
            }
            Assert.False(JsonDocument.Parse(payload).RootElement.GetProperty("sharing").GetBoolean());
            Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(payload).RootElement.GetProperty("position").ValueKind);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"serverTime\":1,\"friends\":[]}", Encoding.UTF8, "application/json") };
        }
    }
}
