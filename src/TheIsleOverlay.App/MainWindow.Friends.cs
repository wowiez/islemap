using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Threading;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public partial class MainWindow
{
    private OverlayFriendsClient? _friends;
    private DispatcherTimer? _friendsTimer;
    private bool _friendSyncBusy;
    private DateTimeOffset _friendRetryAfter;
    private DateTimeOffset _friendPresenceReceivedAt;
    private FriendContext? _friendReceivedContext;
    private string? _friendGameServer;
    private IReadOnlyList<FriendPresence> _overlayFriendPresence = [];
    private IReadOnlyList<SbtcPlayerMarker> _serverPlayerMarkers = [];
    private int _friendFailures;
    private MapPoint? _friendLabelPoint;
    private void InitializeFriends()
    {
        _friends = new OverlayFriendsClient(Path.Combine(AppPaths.Root, "friends-identity.dat"));
        try { _friends.LoadIdentity(); }
        catch (Exception error) when (error is IOException or CryptographicException or System.Text.Json.JsonException)
        { /* Existing identity remains untouched; F8 offers recovery. */ }
        _friendsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _friendsTimer.Tick += async (_, _) =>
        {
            ApplyOverlayFriendMarkers(); // Expiry also runs while requests fail.
            await SyncFriendsAsync();
        };
        _friendsTimer.Start();
    }
    private FriendContext? CurrentFriendContext()
    {
        // Never share a stale last-known point from the menu or after disconnect.
        return FriendServerIdentity.Resolve(_friendGameServer, _guidePlayerOverview?.UpdatedAt,
            _npcapPosition?.ServerEndpoint, _npcapPosition?.CapturedAt,
            CurrentNpcapStatus == NpcapSourceStatus.Live, DateTimeOffset.UtcNow);
    }
    private async Task SyncFriendsAsync()
    {
        if (_friends?.Identity is null || _friendSyncBusy || DateTimeOffset.UtcNow < _friendRetryAfter || _shutdown.IsCancellationRequested) return;
        _friendSyncBusy = true;
        try
        {
            var context = CurrentFriendContext();
            var point = context is null ? null : CurrentMapPoint();
            var position = point is { } location ? new FriendPosition(location.Left, location.Top, _headingDegrees) : null;
            var capturedContext = context;
            var result = await _friends.SyncAsync(context, position, _guidePlayerOverview is null ? null : _activeSpeciesName, _shutdown.Token);
            // A response to the old server may not render after switching server.
            var received = DateTimeOffset.UtcNow;
            _overlayFriendPresence = Equals(capturedContext, CurrentFriendContext()) ? result.Friends.Select(friend => friend with
            {
                At = friend.At is { } at ? received.ToUnixTimeMilliseconds() - Math.Max(0, result.ServerTime - at) : null
            }).ToArray() : [];
            _friendPresenceReceivedAt = DateTimeOffset.UtcNow;
            _friendReceivedContext = capturedContext;
            _friendFailures = 0;
            _friendRetryAfter = default;
            ApplyOverlayFriendMarkers();
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException || (error is ObjectDisposedException && _shutdown.IsCancellationRequested))
        {
            _friendFailures++;
            _friendRetryAfter = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, Math.Min(_friendFailures, 5))));
        }
        finally { _friendSyncBusy = false; }
    }
    private void ApplyOverlayFriendMarkers()
    {
        var now = DateTimeOffset.UtcNow;
        var ownContext = CurrentFriendContext();
        SbtcPlayerMarker[] friends = ownContext is null || !Equals(ownContext, _friendReceivedContext) ? [] : _overlayFriendPresence.Where(friend => FriendPresenceRules.Visible(friend, _friendPresenceReceivedAt, now))
            .Select(friend => new SbtcPlayerMarker("overlay:" + friend.UserId, $"{friend.Name ?? "Bạn bè"}\n{friend.Species}", false, new MapPoint(friend.Position!.X, friend.Position.Y), friend.Position.Heading, true)).ToArray();
        ApplyCombinedPlayerMarkers(_serverPlayerMarkers.Concat(friends).ToArray());
        var labelPoint = CurrentMapPoint();
        if (_sbtcPlayerMarkers.Count > 0 && !Equals(labelPoint, _friendLabelPoint) && _renderedPlayerWidth > 0)
            RenderSbtcPlayerOverlay(_renderedPlayerWidth, _renderedPlayerHeight);
        _friendLabelPoint = labelPoint;
        var fresh = now - _friendPresenceReceivedAt < TimeSpan.FromSeconds(10);
        _guideWindow?.UpdateFriendPresence(fresh ? _overlayFriendPresence.Select(friend => FriendPresenceRules.Visible(friend, _friendPresenceReceivedAt, now)
            && ownContext is not null && Equals(ownContext, _friendReceivedContext) ? friend : friend with { Position = null }).ToArray() : [], ownContext is null ? null : CurrentMapPoint());
    }
    private void DisposeFriends()
    {
        _friendsTimer?.Stop();
        _shutdown.Cancel();
        _friends?.Dispose();
    }
}
