using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.App;

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
            natural ? "#939A9D" : "#E79591");
    }
}

public partial class GuideWindow
{
    private const string AllKillFeedSpecies = "TẤT CẢ LOÀI";
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
        _killFeedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _killFeedTimer.Tick += async (_, _) => await RefreshKillFeedAsync();
        IsVisibleChanged += (_, _) => UpdateKillFeedPolling();
    }

    private void KillFeedNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(KillFeedPage, KillFeedNavButton);

    private async void KillFeedRefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshKillFeedAsync();

    private async void KillFeedSpeciesFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingKillFeedSpecies && _killFeedClient is not null) await RefreshKillFeedAsync();
    }

    private void UpdateKillFeedPolling()
    {
        if (_killFeedTimer is null) return;
        if (IsVisible && KillFeedPage.Visibility == Visibility.Visible)
        {
            _killFeedTimer.Start();
            if (_killFeedLastSuccess is null || DateTimeOffset.UtcNow - _killFeedLastSuccess >= TimeSpan.FromSeconds(30))
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
        KillFeedRefreshButton.IsEnabled = KillFeedSpeciesFilter.IsEnabled = false;
        KillFeedStatus.Text = "Đang tải Kill Feed SBTC…";
        try
        {
            var species = KillFeedSpeciesFilter.SelectedItem as string;
            var feed = await _killFeedClient.LoadAsync(species == AllKillFeedSpecies ? null : species, request.Token);
            RenderKillFeed(feed, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            KillFeedStatus.Text = _killFeedLastSuccess is { } last
                ? $"SBTC chưa phản hồi sau 3 lần thử · dữ liệu cũ từ {last.ToLocalTime():HH:mm:ss}."
                : "Chưa tải được Kill Feed sau 3 lần thử. Bấm TẢI LẠI hoặc chờ lần cập nhật tiếp theo.";
            KillFeedStatus.Foreground = BrushFrom("#E7B74E");
        }
        finally
        {
            _killFeedRequest = null;
            _killFeedLoading = false;
            KillFeedRefreshButton.IsEnabled = KillFeedSpeciesFilter.IsEnabled = true;
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
            KillFeedStatus.Foreground = BrushFrom("#E7B74E");
            return;
        }
        _killFeedLastSuccess = loadedAt;
        var selected = KillFeedSpeciesFilter.SelectedItem as string;
        var species = new[] { AllKillFeedSpecies }.Concat(feed.Species).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _updatingKillFeedSpecies = true;
        KillFeedSpeciesFilter.ItemsSource = species;
        KillFeedSpeciesFilter.SelectedItem = species.Contains(selected, StringComparer.OrdinalIgnoreCase) ? selected : AllKillFeedSpecies;
        _updatingKillFeedSpecies = false;
        KillFeedRows.ItemsSource = feed.Rows.Select(KillFeedRowPresentation.From).ToArray();
        KillFeedCount.Text = $"{feed.Rows.Count} BẢN GHI GẦN NHẤT";
        KillFeedEmpty.Visibility = feed.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        KillFeedEmpty.Text = "Chưa có bản ghi cho loài đã chọn.";
        KillFeedStatus.Text = $"Cập nhật {loadedAt.ToLocalTime():HH:mm:ss} · tự cập nhật mỗi 30 giây khi mở tab.";
        KillFeedStatus.Foreground = BrushFrom("#8FC7A5");
    }

    private void DisposeKillFeed()
    {
        _killFeedTimer?.Stop();
        _killFeedRequest?.Cancel();
        _killFeedHttp.Dispose();
    }
}
