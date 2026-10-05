using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public partial class GuideWindow
{
    private OverlayFriendsClient? _friendsClient;
    private Func<Task>? _friendSync;
    private bool _friendsBusy;
    private readonly Dictionary<string, string> _friendNames = new();
    private readonly Dictionary<string, (TextBlock Name, TextBlock Detail)> _friendLabels = new();
    private IReadOnlyDictionary<string, FriendPresence> _friendPresence = new Dictionary<string, FriendPresence>();
    private MapPoint? _friendOwnPoint;
    public void ConnectFriends(OverlayFriendsClient client, Func<Task> sync)
    {
        _friendsClient = client;
        _friendSync = sync;
        UpdateFriendAccount();
    }
    private async void FriendsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(FriendsPage, FriendsNavButton);
        await RunFriendAction(async () => await LoadFriendsAsync());
    }
    private void UpdateFriendAccount()
    {
        var identity = _friendsClient?.Identity;
        FriendSetupPanel.Visibility = identity is null ? Visibility.Visible : Visibility.Collapsed;
        FriendAccountPanel.Visibility = identity is null ? Visibility.Collapsed : Visibility.Visible;
        FriendNameInput.Text = identity?.Name ?? string.Empty;
        FriendCodeLabel.Text = identity is null ? string.Empty : $"Mã kết bạn: {identity.FriendCode}";
        FriendShareButton.Content = _friendsClient?.Sharing == true ? "CHIA SẺ VỊ TRÍ: BẬT" : "CHIA SẺ VỊ TRÍ: TẮT";
    }
    private async Task RunFriendAction(Func<Task> action)
    {
        if (_friendsBusy || _friendsClient is null) return;
        _friendsBusy = true;
        FriendsActions.IsEnabled = false;
        FriendsStatus.Text = "Đang xử lý…";
        try { await action(); FriendsStatus.Text = "Đã cập nhật · chỉ bạn cùng server mới được xem vị trí khi bạn bật chia sẻ."; }
        catch (Exception error) when (error is HttpRequestException or IOException or CryptographicException or InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException or ObjectDisposedException)
        { FriendsStatus.Text = error is HttpRequestException ? error.Message : "Chưa thực hiện được. Kiểm tra kết nối hoặc dùng mã khôi phục nếu danh tính bị hỏng."; }
        finally { _friendsBusy = false; FriendsActions.IsEnabled = true; UpdateFriendAccount(); }
    }
    private async void FriendCreate_Click(object sender, RoutedEventArgs e) => await RunFriendAction(async () =>
    {
        await _friendsClient!.RegisterAsync(FriendNewName.Text, null, CancellationToken.None);
        await LoadFriendsAsync();
    });
    private async void FriendRecover_Click(object sender, RoutedEventArgs e) => await RunFriendAction(async () =>
    {
        await _friendsClient!.RegisterAsync(string.Empty, FriendRecoveryInput.Password, CancellationToken.None);
        FriendRecoveryInput.Clear();
        await LoadFriendsAsync();
    });
    private async void FriendRename_Click(object sender, RoutedEventArgs e) => await RunFriendAction(async () =>
    {
        await _friendsClient!.RenameAsync(FriendNameInput.Text, CancellationToken.None);
        await LoadFriendsAsync();
    });
    private async void FriendInvite_Click(object sender, RoutedEventArgs e) => await RunFriendAction(async () =>
    {
        RenderFriends(await _friendsClient!.FriendsAsync("request", FriendInviteCode.Text, CancellationToken.None, byName: FriendInviteLookup.SelectedIndex == 0));
        FriendInviteCode.Clear();
    });
    private async void FriendRefresh_Click(object sender, RoutedEventArgs e) => await RunFriendAction(async () => await LoadFriendsAsync());
    private async void FriendShare_Click(object sender, RoutedEventArgs e) => await RunFriendAction(async () =>
    {
        // Turning off is immediate locally; if offline Redis expires the old position within 10s.
        _friendsClient!.Sharing = !_friendsClient.Sharing;
        UpdateFriendAccount();
        if (_friendSync is not null) await _friendSync();
    });
    private void FriendCopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_friendsClient?.Identity is { } identity) Clipboard.SetText(identity.FriendCode);
    }
    private void FriendBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_friendsClient?.Identity is not { } identity) return;
        var dialog = new SaveFileDialog { Title = "Lưu mã khôi phục bí mật · giữ file riêng để chuyển máy", FileName = "IsleLiveMap-recovery.txt", Filter = "Text file|*.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, identity.RecoveryCode); FriendsStatus.Text = "Đã lưu mã khôi phục. Giữ file riêng; ai có mã có thể khôi phục tài khoản. Mã sẽ đổi sau khi khôi phục."; }
        catch (IOException) { FriendsStatus.Text = "Không lưu được file khôi phục."; }
    }
    private async Task LoadFriendsAsync()
    {
        if (_friendsClient?.Identity is null) return;
        RenderFriends(await _friendsClient.FriendsAsync("list", null, CancellationToken.None));
    }
    private void RenderFriends(FriendSnapshot snapshot)
    {
        FriendRows.Items.Clear();
        _friendNames.Clear();
        _friendLabels.Clear();
        FriendCountLabel.Text = $"BẠN BÈ · {snapshot.Friends.Count(friend => friend.State == "accepted")}    LỜI MỜI · {snapshot.Friends.Count(friend => friend.State != "accepted")}";
        foreach (var friend in snapshot.Friends.OrderBy(friend => friend.State == "incoming" ? 0 : friend.State == "accepted" ? 1 : 2).ThenBy(friend => friend.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            _friendNames[friend.UserId] = friend.Name;
            var row = new DockPanel { LastChildFill = true };
            var card = new Border { Background = new SolidColorBrush(Color.FromArgb(230, 20, 25, 27)), BorderBrush = new SolidColorBrush(Color.FromRgb(52,61,64)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 8), Child = row };
            void ActionButton(string title, string action)
            {
                var button = new Button { Content = title, Style = (Style)FindResource("NavButton"), Margin = new Thickness(6, 0, 0, 0) };
                DockPanel.SetDock(button, Dock.Right);
                button.Click += async (_, _) => await RunFriendAction(async () =>
                {
                    RenderFriends(await _friendsClient!.FriendsAsync(action, friend.UserId, CancellationToken.None));
                    if (_friendSync is not null) await _friendSync();
                });
                row.Children.Add(button);
            }
            if (friend.State == "incoming") { ActionButton("CHẤP THUẬN", "accept"); ActionButton("TỪ CHỐI", "reject"); }
            else ActionButton(friend.State == "accepted" ? "HỦY KẾT BẠN" : "HỦY LỜI MỜI", friend.State == "accepted" ? "remove" : "cancel");
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) };
            var name = new TextBlock { Text = friend.Name, Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var detail = new TextBlock { Text = friend.State == "incoming" ? "Lời mời đến" : friend.State == "outgoing" ? "Chờ chấp thuận" : "Ngoại tuyến", Foreground = new SolidColorBrush(Color.FromRgb(146,156,159)), FontSize = 12, Margin = new Thickness(0,4,0,0), TextTrimming = TextTrimming.CharacterEllipsis };
            labels.Children.Add(name); labels.Children.Add(detail); row.Children.Add(labels);
            if (friend.State == "accepted") _friendLabels[friend.UserId] = (name, detail);
            FriendRows.Items.Add(card);
        }
        if (snapshot.Friends.Count == 0) FriendRows.Items.Add(new TextBlock { Text = "Chưa có bạn bè hoặc lời mời.", Foreground = Brushes.Gray });
        RefreshFriendLabels();
    }
    public void UpdateFriendPresence(IReadOnlyList<FriendPresence> friends, MapPoint? own = null)
    {
        _friendPresence = friends.DistinctBy(friend => friend.UserId).ToDictionary(friend => friend.UserId);
        _friendOwnPoint = own;
        RefreshFriendLabels();
    }
    private void RefreshFriendLabels()
    {
        foreach (var (id, labels) in _friendLabels)
        {
            var friend = _friendPresence.GetValueOrDefault(id);
            labels.Name.Text = FriendPresentation.NameAndDistance(friend?.Name ?? _friendNames[id], friend, _friendOwnPoint);
            labels.Detail.Text = FriendPresentation.Detail(friend);
        }
    }
}
