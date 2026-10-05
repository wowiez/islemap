using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.App;

public sealed record MapKillFeedRowPresentation(string Summary, string Detail)
{
    public static MapKillFeedRowPresentation From(SbtcKillFeedRow row)
    {
        var value = KillFeedRowPresentation.From(row);
        return new($"{value.Killer} → {value.Victim}",
            $"{row.At.ToLocalTime():HH:mm:ss} · {value.KillerDetail} → {value.VictimDetail}");
    }
}

public partial class MainWindow
{
    private readonly HttpClient _mapKillFeedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private SbtcKillFeedClient? _mapKillFeedClient;
    private DispatcherTimer? _mapKillFeedTimer;
    private CancellationTokenSource? _mapKillFeedRequest;
    private bool _mapKillFeedLoading;
    private bool _mapKillFeedDisposed;
    private DateTimeOffset? _mapKillFeedLastSuccess;

    private void InitializeMapKillFeed()
    {
        _mapKillFeedClient = new SbtcKillFeedClient(_mapKillFeedHttp);
        _mapKillFeedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _mapKillFeedTimer.Tick += async (_, _) => await RefreshMapKillFeedAsync();
        UpdateMapKillFeedPolling();
    }

    private void MapKillFeedToggleButton_Click(object sender, RoutedEventArgs e) =>
        SetMapKillFeedEnabled(!_layoutSettings.ShowMapKillFeed);

    private void GuideWindow_MapKillFeedEnabledChanged(bool enabled) => SetMapKillFeedEnabled(enabled);

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
        var enabled = _layoutSettings.ShowMapKillFeed && _showMap;
        MapKillFeedToggleButton.Content = _layoutSettings.ShowMapKillFeed ? "KILL FEED · ON" : "KILL FEED · OFF";
        MapKillFeedPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (_mapKillFeedTimer is null || _mapKillFeedDisposed) return;
        if (enabled && IsLoaded)
        {
            _mapKillFeedTimer.Start();
            if (_mapKillFeedLastSuccess is null || DateTimeOffset.UtcNow - _mapKillFeedLastSuccess >= TimeSpan.FromSeconds(5))
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
        if (_mapKillFeedLoading || _mapKillFeedDisposed || !_layoutSettings.ShowMapKillFeed || !_showMap || _mapKillFeedClient is null) return;
        _mapKillFeedLoading = true;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _mapKillFeedRequest = request;
        try
        {
            var feed = await _mapKillFeedClient.LoadAsync(cancellationToken: request.Token);
            if (!request.IsCancellationRequested && !_mapKillFeedDisposed)
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
    }

    internal void RenderMapKillFeed(SbtcKillFeed feed, DateTimeOffset loadedAt)
    {
        var available = feed.Available && !feed.Gated;
        var rows = available ? feed.Rows.OrderByDescending(row => row.At).Take(5).Select(MapKillFeedRowPresentation.From).ToArray() : [];
        MapKillFeedRows.ItemsSource = rows;
        MapKillFeedEmpty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        MapKillFeedEmpty.Text = feed.Gated ? "Server đang giới hạn quyền xem." : available ? "Chưa có bản ghi." : "Kill Feed tạm thời chưa khả dụng.";
        MapKillFeedStatus.Text = available ? $"{loadedAt.ToLocalTime():HH:mm:ss} · 5s" : "Chưa có dữ liệu";
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
