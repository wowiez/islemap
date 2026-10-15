using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.App;

/// <summary>Kill feed refresh cadence for the F8 tab and the minimap panel. A slow
/// request simply skips ticks (_loading guards), so polls never overlap.</summary>
internal static class KillFeedPolling
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
}

public sealed record KillFeedRowPresentation(string Time, string Killer, string KillerDetail,
    string Victim, string VictimDetail, string Cause, string Accent)
{
    public static KillFeedRowPresentation From(SbtcKillFeedRow row)
    {
        var natural = row.Cause == "natural";
        var killer = natural ? "Môi trường" : row.KillerKnown && !string.IsNullOrWhiteSpace(row.KillerName)
            ? row.KillerName : "Không rõ người hạ";
        static string Detail(string species, double? growth) => string.IsNullOrWhiteSpace(species) ? "—" :
            growth is { } percent ? $"{species} · {percent:0.#}%" : species;
        return new(row.At.ToLocalTime().ToString("HH:mm:ss\ndd/MM"), killer,
            natural ? "Nguyên nhân tự nhiên" : Detail(row.KillerSpecies, row.KillerGrowth),
            string.IsNullOrWhiteSpace(row.VictimName) ? "Người chơi" : row.VictimName,
            Detail(row.VictimSpecies, row.VictimGrowth), natural ? "TỰ NHIÊN" : row.Cause == "pvp" ? "PVP" : "KHÁC",
            // Logo palette: live red for PvP, beach sand for natural deaths, ring gold otherwise.
            natural ? "#D9C58C" : row.Cause == "pvp" ? "#FF7A6E" : "#C9A95A");
    }
}

public partial class GuideWindow
{
    public event Action<bool>? MapKillFeedEnabledChanged;
    /// <summary>Raised when the species filter changes (null = all species).</summary>
    public event Action<string?>? KillFeedSpeciesChanged;
    private bool _showMapKillFeed;

    /// <summary>Kill Feed is an SBTC Island API: hide its tab and setting elsewhere.</summary>
    public void SetKillFeedAvailable(bool available)
    {
        var visibility = available ? Visibility.Visible : Visibility.Collapsed;
        KillFeedNavButton.Visibility = GuideMapKillFeedCard.Visibility = visibility;
        if (!available && KillFeedPage.Visibility == Visibility.Visible) ShowPage(OverviewPage, OverviewNavButton);
    }

    public void UpdateMapKillFeedSetting(bool enabled)
    {
        _showMapKillFeed = enabled;
        GuideMapKillFeedButton.Content = enabled ? "BẬT" : "TẮT";
    }

    private void GuideMapKillFeedButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateMapKillFeedSetting(!_showMapKillFeed);
        MapKillFeedEnabledChanged?.Invoke(_showMapKillFeed);
    }

    private const string AllKillFeedSpecies = "TẤT CẢ LOÀI";
    private const string KillFeedOk = "#8DBE5E";
    private const string KillFeedWarning = "#C9A95A";
    private readonly HttpClient _killFeedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private SbtcKillFeedClient? _killFeedClient;
    private DispatcherTimer? _killFeedTimer;
    private CancellationTokenSource? _killFeedRequest;
    private bool _killFeedLoading;
    private bool _updatingKillFeedSpecies;
    private DateTimeOffset? _killFeedLastSuccess;

    private void InitializeKillFeed()
    {
        _killFeedClient = new SbtcKillFeedClient(_killFeedHttp);
        _updatingKillFeedSpecies = true;
        KillFeedSpeciesFilter.ItemsSource = new[] { AllKillFeedSpecies };
        KillFeedSpeciesFilter.SelectedIndex = 0;
        _updatingKillFeedSpecies = false;
        _killFeedTimer = new DispatcherTimer { Interval = KillFeedPolling.Interval };
        _killFeedTimer.Tick += async (_, _) => await RefreshKillFeedAsync();
        IsVisibleChanged += (_, _) => UpdateKillFeedPolling();
    }

    private void KillFeedNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(KillFeedPage, KillFeedNavButton);

    private async void KillFeedRefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshKillFeedAsync();

    private async void KillFeedSpeciesFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingKillFeedSpecies) return;
        KillFeedSpeciesChanged?.Invoke(SelectedKillFeedSpecies);
        if (_killFeedClient is not null) await RefreshKillFeedAsync();
    }

    private string? SelectedKillFeedSpecies =>
        KillFeedSpeciesFilter.SelectedItem is string species && species != AllKillFeedSpecies ? species : null;

    /// <summary>Selects a remembered species before the first load (the list fills in later).</summary>
    public void SetKillFeedSpecies(string? species)
    {
        _updatingKillFeedSpecies = true;
        var items = (KillFeedSpeciesFilter.ItemsSource as string[]) ?? [AllKillFeedSpecies];
        if (!string.IsNullOrWhiteSpace(species) && !items.Contains(species, StringComparer.OrdinalIgnoreCase))
            KillFeedSpeciesFilter.ItemsSource = items = [.. items, species];
        KillFeedSpeciesFilter.SelectedItem = string.IsNullOrWhiteSpace(species)
            ? AllKillFeedSpecies
            : items.First(item => string.Equals(item, species, StringComparison.OrdinalIgnoreCase));
        _updatingKillFeedSpecies = false;
    }

    private void UpdateKillFeedPolling()
    {
        if (_killFeedTimer is null) return;
        if (IsVisible && KillFeedPage.Visibility == Visibility.Visible)
        {
            _killFeedTimer.Start();
            if (_killFeedLastSuccess is null || DateTimeOffset.UtcNow - _killFeedLastSuccess >= KillFeedPolling.Interval)
                _ = RefreshKillFeedAsync();
        }
        else
        {
            _killFeedTimer.Stop();
            _killFeedRequest?.Cancel();
        }
    }

    private async Task RefreshKillFeedAsync()
    {
        if (_killFeedLoading || _killFeedClient is null) return;
        _killFeedLoading = true;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_garageCancellation.Token);
        _killFeedRequest = request;
        // No IsEnabled toggling: disabling the combo on every 5s poll flashed its white
        // system template. _killFeedLoading already blocks overlapping requests.
        KillFeedStatus.Text = "Đang tải Kill Feed SBTC…";
        try
        {
            var species = SelectedKillFeedSpecies;
            var feed = await _killFeedClient.LoadAsync(species, request.Token);
            // The filter may have changed while this request was in flight.
            if (string.Equals(species, SelectedKillFeedSpecies, StringComparison.OrdinalIgnoreCase))
                RenderKillFeed(feed, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            KillFeedStatus.Text = _killFeedLastSuccess is { } last
                ? $"SBTC chưa phản hồi sau 3 lần thử · dữ liệu cũ từ {last.ToLocalTime():HH:mm:ss}."
                : "Chưa tải được Kill Feed sau 3 lần thử. Bấm TẢI LẠI hoặc chờ lần cập nhật tiếp theo.";
            KillFeedStatus.Foreground = BrushFrom(KillFeedWarning);
        }
        finally
        {
            _killFeedRequest = null;
            _killFeedLoading = false;
        }
    }

    internal void RenderKillFeed(SbtcKillFeed feed, DateTimeOffset loadedAt)
    {
        if (!feed.Available || feed.Gated)
        {
            KillFeedRows.ItemsSource = null;
            KillFeedCount.Text = "CHƯA CÓ DỮ LIỆU";
            KillFeedEmpty.Visibility = Visibility.Visible;
            KillFeedEmpty.Text = feed.Gated ? "Server đang giới hạn quyền xem Kill Feed." : "Kill Feed tạm thời chưa khả dụng trên SBTC.";
            KillFeedStatus.Text = "Server chưa cung cấp dữ liệu · có thể thử lại bằng TẢI LẠI.";
            KillFeedStatus.Foreground = BrushFrom(KillFeedWarning);
            return;
        }
        _killFeedLastSuccess = loadedAt;
        var selected = KillFeedSpeciesFilter.SelectedItem as string;
        var species = new[] { AllKillFeedSpecies }.Concat(feed.Species)
            .Concat(selected is null || selected == AllKillFeedSpecies ? [] : [selected])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Replacing ItemsSource on every 5s poll closed an open dropdown; swap it only when the list changed.
        if (KillFeedSpeciesFilter.ItemsSource is not string[] current || !current.SequenceEqual(species, StringComparer.OrdinalIgnoreCase))
        {
            _updatingKillFeedSpecies = true;
            KillFeedSpeciesFilter.ItemsSource = species;
            KillFeedSpeciesFilter.SelectedItem = species.Contains(selected, StringComparer.OrdinalIgnoreCase) ? selected : AllKillFeedSpecies;
            _updatingKillFeedSpecies = false;
        }
        KillFeedRows.ItemsSource = feed.Rows.Select(KillFeedRowPresentation.From).ToArray();
        KillFeedCount.Text = $"{feed.Rows.Count} BẢN GHI GẦN NHẤT";
        KillFeedEmpty.Visibility = feed.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        KillFeedEmpty.Text = "Chưa có bản ghi cho loài đã chọn.";
        KillFeedStatus.Text = $"Cập nhật {loadedAt.ToLocalTime():HH:mm:ss} · tự cập nhật mỗi 2 giây khi mở tab.";
        KillFeedStatus.Foreground = BrushFrom(KillFeedOk);
    }

    private void DisposeKillFeed()
    {
        _killFeedTimer?.Stop();
        _killFeedRequest?.Cancel();
        _killFeedHttp.Dispose();
    }
}
