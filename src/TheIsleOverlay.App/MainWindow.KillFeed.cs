using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.App;

public sealed record MapKillFeedRowPresentation(string Summary, string Detail, string Accent)
{
    public static MapKillFeedRowPresentation From(SbtcKillFeedRow row)
    {
        var value = KillFeedRowPresentation.From(row);
        return new($"{value.Killer} → {value.Victim}",
            $"{row.At.ToLocalTime():HH:mm:ss} · {value.KillerDetail} → {value.VictimDetail}", value.Accent);
    }
}

public partial class MainWindow
{
    private readonly HttpClient _mapKillFeedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private SbtcKillFeedClient? _mapKillFeedClient;
    private DispatcherTimer? _mapKillFeedTimer;
    private CancellationTokenSource? _mapKillFeedRequest;
    private bool _mapKillFeedLoading;
    private bool _mapKillFeedStale;
    private bool _mapKillFeedDisposed;
    private DateTimeOffset? _mapKillFeedLastSuccess;
    private bool _killFeedAvailable;
    private string? _killFeedServer;

    // The kill feed only exists on SBTC Island: offer it only while playing there.
    private void UpdateKillFeedAvailability(string? server)
    {
        // A brief offline snapshot reports no server: keep the last one instead of
        // hiding/re-showing the kill feed (and resizing the HUD) on every reconnect.
        _killFeedServer = server ?? _killFeedServer;
        var available = !_guest &&
            (_requestedSource?.Kind == TelemetrySourceKind.SbtcIsland || SbtcZoneOverlay.IsSbtcServer(_killFeedServer));
        if (available == _killFeedAvailable) return;
        _killFeedAvailable = available;
        MapKillFeedSettingRow.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        _guideWindow?.SetKillFeedAvailable(available);
        UpdateMapKillFeedPolling();
        if (IsLoaded) RefreshWindowSizeToContent();
    }

    private void InitializeMapKillFeed()
    {
        _mapKillFeedClient = new SbtcKillFeedClient(_mapKillFeedHttp);
        _mapKillFeedTimer = new DispatcherTimer { Interval = KillFeedPolling.Interval };
        _mapKillFeedTimer.Tick += async (_, _) => await RefreshMapKillFeedAsync();
        RenderMapKillFeedTitle();
        UpdateMapKillFeedPolling();
    }

    private void MapKillFeedToggleButton_Click(object sender, RoutedEventArgs e) =>
        SetMapKillFeedEnabled(!_layoutSettings.ShowMapKillFeed);

    private void GuideWindow_MapKillFeedEnabledChanged(bool enabled) => SetMapKillFeedEnabled(enabled);

    // Same filter as the F8 tab: picking a species there filters this panel too.
    private void GuideWindow_KillFeedSpeciesChanged(string? species)
    {
        if (string.Equals(species, _layoutSettings.KillFeedSpecies, StringComparison.OrdinalIgnoreCase)) return;
        _layoutSettings = _layoutSettings with { KillFeedSpecies = species };
        _layoutSettingsStore.Save(_layoutSettings);
        RenderMapKillFeedTitle();
        MapKillFeedRows.ItemsSource = Array.Empty<MapKillFeedRowPresentation>();
        MapKillFeedEmpty.Visibility = Visibility.Visible;
        MapKillFeedEmpty.Text = "Đang tải…";
        _mapKillFeedLastSuccess = null;
        if (_mapKillFeedLoading) _mapKillFeedStale = true;
        else _ = RefreshMapKillFeedAsync();
    }

    private void RenderMapKillFeedTitle() =>
        MapKillFeedTitle.Text = string.IsNullOrWhiteSpace(_layoutSettings.KillFeedSpecies)
            ? "KILL FEED · SBTC"
            : $"KILL FEED · {_layoutSettings.KillFeedSpecies.ToUpperInvariant()}";

    private void SetMapKillFeedEnabled(bool enabled)
    {
        _layoutSettings = _layoutSettings with { ShowMapKillFeed = enabled };
        _layoutSettingsStore.Save(_layoutSettings);
        _guideWindow?.UpdateMapKillFeedSetting(enabled);
        UpdateMapKillFeedPolling();
        if (IsLoaded)
        {
            RefreshWindowSizeToContent();
            KeepOverlayVisible();
        }
    }

    private void UpdateMapKillFeedPolling()
    {
        var enabled = _layoutSettings.ShowMapKillFeed && _showMap && _killFeedAvailable;
        MapKillFeedToggleButton.Content = _layoutSettings.ShowMapKillFeed ? "KILL FEED · ON" : "KILL FEED · OFF";
        MapKillFeedPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (_mapKillFeedTimer is null || _mapKillFeedDisposed) return;
        if (enabled && IsLoaded)
        {
            _mapKillFeedTimer.Start();
            if (_mapKillFeedLastSuccess is null || DateTimeOffset.UtcNow - _mapKillFeedLastSuccess >= KillFeedPolling.Interval)
                _ = RefreshMapKillFeedAsync();
        }
        else
        {
            _mapKillFeedTimer.Stop();
            _mapKillFeedRequest?.Cancel();
        }
    }

    private async Task RefreshMapKillFeedAsync()
    {
        if (_mapKillFeedLoading || _mapKillFeedDisposed || !_layoutSettings.ShowMapKillFeed || !_showMap || !_killFeedAvailable || _mapKillFeedClient is null) return;
        _mapKillFeedLoading = true;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _mapKillFeedRequest = request;
        var species = _layoutSettings.KillFeedSpecies;
        try
        {
            var feed = await _mapKillFeedClient.LoadAsync(species, request.Token);
            // Drop a result fetched for a filter that has since changed.
            if (!request.IsCancellationRequested && !_mapKillFeedDisposed &&
                string.Equals(species, _layoutSettings.KillFeedSpecies, StringComparison.OrdinalIgnoreCase))
                RenderMapKillFeed(feed, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            if (!request.IsCancellationRequested && !_mapKillFeedDisposed)
            {
                MapKillFeedStatus.Text = _mapKillFeedLastSuccess is null ? "Chưa tải được" : "Dữ liệu cũ";
                MapKillFeedEmpty.Text = "SBTC chưa phản hồi · sẽ thử lại.";
            }
        }
        finally
        {
            _mapKillFeedRequest = null;
            _mapKillFeedLoading = false;
        }

        if (_mapKillFeedStale && !_mapKillFeedDisposed)
        {
            _mapKillFeedStale = false;
            await RefreshMapKillFeedAsync();
        }
    }

    internal void RenderMapKillFeed(SbtcKillFeed feed, DateTimeOffset loadedAt)
    {
        var available = feed.Available && !feed.Gated;
        var rows = available ? feed.Rows.OrderByDescending(row => row.At).Take(5).Select(MapKillFeedRowPresentation.From).ToArray() : [];
        MapKillFeedRows.ItemsSource = rows;
        MapKillFeedEmpty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        MapKillFeedEmpty.Text = feed.Gated ? "Server đang giới hạn quyền xem."
            : available ? string.IsNullOrWhiteSpace(_layoutSettings.KillFeedSpecies) ? "Chưa có bản ghi." : $"Chưa có bản ghi cho {_layoutSettings.KillFeedSpecies}."
            : "Kill Feed tạm thời chưa khả dụng.";
        MapKillFeedStatus.Text = available ? $"{loadedAt.ToLocalTime():HH:mm:ss} · 2s" : "Chưa có dữ liệu";
        if (available) _mapKillFeedLastSuccess = loadedAt;
    }

    private void DisposeMapKillFeed()
    {
        _mapKillFeedDisposed = true;
        _mapKillFeedTimer?.Stop();
        _mapKillFeedRequest?.Cancel();
        _mapKillFeedHttp.Dispose();
    }
}
