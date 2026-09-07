using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

public sealed record GuidePlayerOverview(
    string Species,
    string PlayerName,
    string Server,
    double Growth,
    double Health,
    double Stamina,
    double Food,
    double Water,
    DateTimeOffset UpdatedAt);

public sealed record GuideGarageApi(
    Func<CancellationToken, Task<IslePilotOverlayGarageDto>> Load,
    Func<string, CancellationToken, Task<IslePilotOverlayGarageCommandDto>> Park,
    Func<string, CancellationToken, Task<IslePilotOverlayGarageCommandDto>> Restore,
    Func<string, CancellationToken, Task<IslePilotOverlayGarageCommandStatusDto>> Status);

public sealed record GarageDinoCardPresentation(
    string DinoId,
    string DisplayName,
    string Species,
    string SpeciesCode,
    string PreviewUrl,
    bool HasPreview,
    bool LiveSwap,
    string ActionLabel,
    string Gender,
    double Growth,
    string GrowthLabel,
    double Health,
    string HealthLabel,
    double Hunger,
    string HungerLabel,
    double Thirst,
    string ThirstLabel,
    double Stamina,
    string StaminaLabel,
    string PrimeLabel,
    string ParkedLabel,
    string MutationLabel,
    string AccentColor,
    string BodyColor,
    IReadOnlyList<string> PaletteColors)
{
    public IslePilotOverlayGaragePaletteDto? SkinPalette { get; init; }
    private static readonly HashSet<string> SupportedPreviewSpecies = new(StringComparer.Ordinal)
    {
        "allosaurus", "austroraptor", "beipiaosaurus", "carnotaurus", "ceratosaurus",
        "deinosuchus", "diabloceratops", "dilophosaurus", "dryosaurus", "gallimimus",
        "herrerasaurus", "hypsilophodon", "kentrosaurus", "maiasaura", "omniraptor",
        "pachycephalosaurus", "pteranodon", "stegosaurus", "tenontosaurus",
        "triceratops", "troodon", "tyrannosaurus"
    };

    public static GarageDinoCardPresentation From(
        IslePilotOverlayGarageDinoDto dino,
        bool liveSwap = false)
    {
        ArgumentNullException.ThrowIfNull(dino);
        var species = string.IsNullOrWhiteSpace(dino.Species) ? "Unknown dinosaur" : dino.Species.Trim();
        var displayName = string.IsNullOrWhiteSpace(dino.Name) ? species : dino.Name.Trim();
        var colors = Palette(dino.Palette);
        var accent = colors.FirstOrDefault() ?? "#8D9095";
        var bodyColor = IsHexColor(dino.Palette?.Body)
            ? dino.Palette!.Body!.ToUpperInvariant()
            : accent;
        var gender = dino.Gender?.Trim().ToLowerInvariant() switch
        {
            "male" => "Đực · Male",
            "female" => "Cái · Female",
            { Length: > 0 } value => value,
            _ => "Không rõ"
        };
        var primeLabel = dino.IsPrimeElder == true
            ? "PRIME ELDER"
            : string.IsNullOrWhiteSpace(dino.ClassName)
                ? "PARKED"
                : dino.ClassName.Trim().ToUpperInvariant();
        var mutationLabel = dino.PickableMutations.Count > 0
            ? $"{dino.PickableMutations.Count} MUTATION"
            : dino.MutationEligible == true ? "MUTATION READY" : "DỮ LIỆU ĐÃ LƯU";
        var parkedLabel = dino.ParkedAt is null
            ? "ĐÃ LƯU TRONG GARAGE"
            : $"PARKED · {dino.ParkedAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}";

        var previewUrl = PreviewUrlFor(species);
        return new GarageDinoCardPresentation(
            dino.Id ?? string.Empty,
            displayName,
            species,
            SpeciesCodeFor(species),
            previewUrl,
            !string.IsNullOrWhiteSpace(previewUrl),
            liveSwap,
            liveSwap ? "ĐỔI SANG" : "LẤY RA",
            gender,
            Percent(dino.Growth), Label(dino.Growth),
            Percent(dino.Health), Label(dino.Health),
            Percent(dino.Hunger), Label(dino.Hunger),
            Percent(dino.Thirst), Label(dino.Thirst),
            Percent(dino.Stamina), Label(dino.Stamina),
            primeLabel,
            parkedLabel,
            mutationLabel,
            accent,
            bodyColor,
            colors) { SkinPalette = dino.Palette };
    }

    private static double Percent(double? value)
    {
        if (value is null)
        {
            return 0d;
        }

        // IslePilot garage stores vitals as normalized 0..1 ratios. Retain
        // compatibility if a future response already uses a 0..100 value.
        var percent = value is >= 0d and <= 1d ? value.Value * 100d : value.Value;
        return Math.Clamp(percent, 0d, 100d);
    }

    private static string Label(double? value) => value is null ? "—" : $"{Percent(value):0.#}%";

    private static string SpeciesCodeFor(string species)
    {
        var letters = new string(species.Where(char.IsLetterOrDigit).Take(3).ToArray());
        return string.IsNullOrWhiteSpace(letters) ? "DINO" : letters.ToUpperInvariant();
    }

    private static string PreviewUrlFor(string species)
    {
        var slug = new string(species
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return !SupportedPreviewSpecies.Contains(slug)
            ? string.Empty
            : $"pack://application:,,,/Assets/DinoThumbnails/{slug}.png";
    }

    private static IReadOnlyList<string> Palette(IslePilotOverlayGaragePaletteDto? palette)
    {
        if (palette is null)
        {
            return ["#8D9095", "#5E6166", "#303236"];
        }

        var values = new[]
        {
            palette.Display, palette.Body, palette.Markings, palette.Flank,
            palette.Underbelly, palette.Detail, palette.Eyes, palette.Mouth,
            palette.Claws, palette.Teeth
        };
        var colors = values
            .Where(IsHexColor)
            .Select(value => value!.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
        return colors.Length > 0 ? colors : ["#8D9095", "#5E6166", "#303236"];
    }

    private static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}

public partial class GuideWindow : Window
{
    private MutationTrack _selectedTrack = MutationTrack.Survival;
    private readonly LargeMapWindow _embeddedMapWindow;
    private readonly GuideGarageApi? _garageApi;
    private readonly CancellationTokenSource _garageCancellation = new();
    private bool _garageLoaded;
    private bool _garageLoading;
    private GarageCommandKind _pendingGarageCommand;
    private string? _pendingGarageDinoId;
    private string _pendingGarageSuccessTitle = string.Empty;
    private CancellationTokenSource? _activeGarageCommandCancellation;
    private bool _parkCountdownActive;
    private Task? _parkCancellationTask;

    public GuideWindow(
        string? activeSpecies = null,
        ImageSource? mapSource = null,
        MapPoint? currentLocation = null,
        MapPoint? destination = null,
        IReadOnlyList<SbtcZoneFeature>? zones = null,
        IReadOnlyList<SbtcPlayerMarker>? players = null,
        GuidePlayerOverview? playerOverview = null,
        GuideGarageApi? garageApi = null)
    {
        InitializeComponent();
        _embeddedMapWindow = new LargeMapWindow(mapSource);
        _garageApi = garageApi;
        var mapContent = _embeddedMapWindow.Content;
        _embeddedMapWindow.Content = null;
        GuideMapHost.Content = mapContent;
        _embeddedMapWindow.UpdateState(currentLocation, destination, zones ?? [], players ?? []);
        _embeddedMapWindow.DestinationChanged += destinationValue => DestinationChanged?.Invoke(destinationValue);
        SpeciesComboBox.ItemsSource = MutationGuideCatalog.Species;
        SpeciesComboBox.DisplayMemberPath = nameof(SpeciesMutationGuide.Name);
        SelectSpecies(activeSpecies);
        ShowPage(OverviewPage, OverviewNavButton);
        UpdatePlayerOverview(playerOverview);
        RenderRecommendations();
        GarageNavButton.Visibility = garageApi is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public event Action<MapPoint?>? DestinationChanged;

    public bool IsMapPageVisible => MapPage.Visibility == Visibility.Visible;

    public void UpdateCurrentLocation(MapPoint? currentLocation) =>
        _embeddedMapWindow.UpdateCurrentLocation(currentLocation);

    public void UpdateZones(IReadOnlyList<SbtcZoneFeature> zones) =>
        _embeddedMapWindow.UpdateZones(zones);

    public void UpdateDestination(MapPoint? destination) =>
        _embeddedMapWindow.UpdateDestination(destination);

    public void UpdatePlayers(IReadOnlyList<SbtcPlayerMarker> players) =>
        _embeddedMapWindow.UpdatePlayers(players);

    public void UpdatePlayerOverview(GuidePlayerOverview? player)
    {
        if (player is null)
        {
            OverviewStateLabel.Text = "Chưa có Dino hoạt động · dữ liệu sẽ tự cập nhật khi vào server";
            OverviewSpeciesLabel.Text = "NO ACTIVE DINOSAUR";
            OverviewPlayerLabel.Text = "—";
            OverviewServerLabel.Text = "Chưa kết nối server";
            OverviewGrowthLabel.Text = "—";
            SetOverviewVital(OverviewGrowthBar, null, null);
            SetOverviewVital(OverviewHealthBar, OverviewHealthLabel, null);
            SetOverviewVital(OverviewStaminaBar, OverviewStaminaLabel, null);
            SetOverviewVital(OverviewFoodBar, OverviewFoodLabel, null);
            SetOverviewVital(OverviewWaterBar, OverviewWaterLabel, null);
            return;
        }

        OverviewStateLabel.Text = $"Dữ liệu trực tiếp · cập nhật {player.UpdatedAt.ToLocalTime():HH:mm:ss}";
        OverviewSpeciesLabel.Text = player.Species;
        OverviewPlayerLabel.Text = string.IsNullOrWhiteSpace(player.PlayerName) ? "ACTIVE PLAYER" : player.PlayerName;
        OverviewServerLabel.Text = player.Server;
        OverviewGrowthLabel.Text = $"{player.Growth:0.#}%";
        SetOverviewVital(OverviewGrowthBar, null, player.Growth);
        SetOverviewVital(OverviewHealthBar, OverviewHealthLabel, player.Health);
        SetOverviewVital(OverviewStaminaBar, OverviewStaminaLabel, player.Stamina);
        SetOverviewVital(OverviewFoodBar, OverviewFoodLabel, player.Food);
        SetOverviewVital(OverviewWaterBar, OverviewWaterLabel, player.Water);
    }

    private static void SetOverviewVital(ProgressBar bar, TextBlock? label, double? value)
    {
        var normalized = Math.Clamp(value ?? 0d, 0d, 100d);
        bar.Value = normalized;
        if (label is not null)
        {
            label.Text = value is null ? "—" : $"{normalized:0.#}%";
        }
    }

    public void SelectSpecies(string? species)
    {
        var normalized = NormalizeSpecies(species);
        var selected = MutationGuideCatalog.Species.FirstOrDefault(item =>
                           string.Equals(NormalizeSpecies(item.Name), normalized, StringComparison.OrdinalIgnoreCase))
                       ?? MutationGuideCatalog.Species.First(item => item.Name == "Triceratops");
        SpeciesComboBox.SelectedItem = selected;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        BeginAnimation(OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

    private void OverviewNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(OverviewPage, OverviewNavButton);

    private void MapNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(MapPage, MapNavButton);

    private async void GarageNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(GaragePage, GarageNavButton);
        await LoadGarageAsync(force: false);
    }

    private void MutationNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(MutationPage, MutationNavButton);

    private void ShowPage(FrameworkElement page, Button activeButton)
    {
        foreach (var candidate in new FrameworkElement[] { OverviewPage, MapPage, GaragePage, MutationPage })
        {
            candidate.Visibility = candidate == page ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var button in new[] { OverviewNavButton, MapNavButton, GarageNavButton, MutationNavButton })
        {
            var active = button == activeButton;
            button.Foreground = BrushFrom(active ? "#111214" : "#9A9DA2");
            button.Background = BrushFrom(active ? "#D9DBDE" : "#00000000");
            button.BorderBrush = BrushFrom(active ? "#F0F1F2" : "#3B3D41");
        }

        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35d, 1d, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private async void GarageRefreshButton_Click(object sender, RoutedEventArgs e) =>
        await LoadGarageAsync(force: true);

    private void GarageParkButton_Click(object sender, RoutedEventArgs e) =>
        ShowGarageConfirmation(
            GarageCommandKind.Park,
            dinoId: null,
            "Đã cất Dino",
            "Cất Dino hiện tại",
            "Dino bạn đang chơi sẽ được chuyển vào Garage. Khi bắt đầu đếm ngược, hãy đứng yên và không nhận sát thương.");

    private void RestoreDinoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string dinoId, DataContext: GarageDinoCardPresentation card } ||
            string.IsNullOrWhiteSpace(dinoId))
        {
            return;
        }

        ShowGarageConfirmation(
            GarageCommandKind.Restore,
            dinoId,
            card.LiveSwap ? "Đã đổi Dino" : "Đã lấy Dino",
            card.LiveSwap ? "Đổi Dino đang chơi" : "Lấy Dino khỏi Garage",
            card.LiveSwap
                ? $"Server này hỗ trợ đổi trực tiếp. {card.DisplayName} ({card.Species}) sẽ được đổi sang Dino đang chơi."
                : $"Server này dùng chế độ lấy ra. {card.DisplayName} ({card.Species}) sẽ được Restore theo quy tắc Garage của server.");
    }

    private void ShowGarageConfirmation(
        GarageCommandKind command,
        string? dinoId,
        string successTitle,
        string title,
        string message)
    {
        if (_garageApi is null || _activeGarageCommandCancellation is not null)
        {
            return;
        }

        _pendingGarageCommand = command;
        _pendingGarageDinoId = dinoId;
        _pendingGarageSuccessTitle = successTitle;
        GarageCommandTitle.Text = title;
        GarageCommandMessage.Text = message;
        GarageCommandProgress.Text = string.Empty;
        GarageCommandConfirmButton.Visibility = Visibility.Visible;
        GarageCommandConfirmButton.IsEnabled = true;
        GarageCommandCancelButton.Visibility = Visibility.Visible;
        GarageCommandCancelButton.IsEnabled = true;
        GarageCommandCloseButton.Visibility = Visibility.Collapsed;
        GarageCommandOverlay.Visibility = Visibility.Visible;
    }

    private async void GarageCommandConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_garageApi is null || _pendingGarageCommand == GarageCommandKind.None ||
            _activeGarageCommandCancellation is not null)
        {
            return;
        }

        _activeGarageCommandCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _garageCancellation.Token);
        GarageCommandConfirmButton.Visibility = Visibility.Collapsed;
        GarageCommandCancelButton.Visibility = Visibility.Collapsed;
        GarageCards.IsEnabled = false;
        GarageParkButton.IsEnabled = false;
        GarageRefreshButton.IsEnabled = false;

        try
        {
            if (_pendingGarageCommand == GarageCommandKind.Park)
            {
                await ExecuteParkAsync(_activeGarageCommandCancellation.Token);
            }
            else
            {
                await ExecuteRestoreAsync(_activeGarageCommandCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (
            _activeGarageCommandCancellation.IsCancellationRequested ||
            _garageCancellation.IsCancellationRequested)
        {
            if (!_garageCancellation.IsCancellationRequested)
            {
                ShowGarageCommandResult(false, "Đã hủy", "Dino chưa được chuyển vào Garage.");
            }
        }
        catch (IslePilotOverlayAuthenticationException)
        {
            ShowGarageCommandResult(false, "Phiên Steam đã hết hạn", "Đăng nhập lại IslePilot rồi thử lại.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException ||
            exception is OperationCanceledException)
        {
            ShowGarageCommandResult(false, "Không gửi được lệnh", "Server phản hồi chậm hoặc tạm thời không khả dụng.");
        }
        finally
        {
            _parkCountdownActive = false;
            _activeGarageCommandCancellation?.Dispose();
            _activeGarageCommandCancellation = null;
            if (!_garageCancellation.IsCancellationRequested)
            {
                GarageCards.IsEnabled = true;
                GarageParkButton.IsEnabled = true;
                GarageRefreshButton.IsEnabled = true;
            }
        }
    }

    private async Task ExecuteParkAsync(CancellationToken cancellationToken)
    {
        var started = await _garageApi!.Park("start", cancellationToken);
        if (!started.Ok)
        {
            ShowGarageCommandResult(false, "Không thể cất Dino", ErrorMessage(started.Error));
            return;
        }

        if (started.Pending == true && started.DelaySec is > 0)
        {
            _parkCountdownActive = true;
            GarageCommandCancelButton.Visibility = Visibility.Visible;
            GarageCommandMessage.Text = "Hãy đứng yên và không nhận sát thương cho tới khi đếm ngược kết thúc.";
            for (var remaining = started.DelaySec.Value; remaining > 0; remaining--)
            {
                GarageCommandProgress.Text = $"ĐANG CẤT · {remaining}s";
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            GarageCommandCancelButton.Visibility = Visibility.Collapsed;
            _parkCountdownActive = false;
            GarageCommandProgress.Text = "ĐANG HOÀN TẤT…";
            started = await _garageApi.Park("finalize", cancellationToken);
            if (!started.Ok)
            {
                ShowGarageCommandResult(false, "Cất Dino bị hủy", ErrorMessage(started.Error));
                return;
            }
        }

        await CompleteGarageCommandAsync(started, "Đã cất Dino", cancellationToken);
    }

    private async Task ExecuteRestoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_pendingGarageDinoId))
        {
            ShowGarageCommandResult(false, "Thiếu Dino ID", "Hãy làm mới Garage rồi thử lại.");
            return;
        }

        GarageCommandProgress.Text = "ĐANG GỬI LỆNH…";
        var command = await _garageApi!.Restore(_pendingGarageDinoId, cancellationToken);
        if (!command.Ok)
        {
            ShowGarageCommandResult(false, "Không thể lấy Dino", ErrorMessage(command.Error));
            return;
        }

        await CompleteGarageCommandAsync(
            command,
            string.IsNullOrWhiteSpace(_pendingGarageSuccessTitle) ? "Đã lấy Dino" : _pendingGarageSuccessTitle,
            cancellationToken);
    }

    private async Task CompleteGarageCommandAsync(
        IslePilotOverlayGarageCommandDto command,
        string successTitle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.CommandId))
        {
            ShowGarageCommandResult(true, successTitle, "Garage đã được cập nhật.");
            await LoadGarageAsync(force: true);
            return;
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(25);
        while (DateTimeOffset.UtcNow < expiresAt)
        {
            GarageCommandProgress.Text = "ĐANG CHỜ SERVER…";
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
            var status = await _garageApi!.Status(command.CommandId, cancellationToken);
            switch (status.Status?.Trim().ToLowerInvariant())
            {
                case "done":
                    ShowGarageCommandResult(true, successTitle, "Server đã xử lý lệnh thành công.");
                    await LoadGarageAsync(force: true);
                    return;
                case "failed":
                    ShowGarageCommandResult(false, "Lệnh thất bại", ErrorMessage(status.Error));
                    return;
                case "claimed":
                    GarageCommandProgress.Text = "SERVER ĐANG XỬ LÝ…";
                    break;
                default:
                    GarageCommandProgress.Text = "ĐANG XẾP HÀNG…";
                    break;
            }
        }

        ShowGarageCommandResult(false, "Server xử lý quá lâu", "Lệnh có thể vẫn đang chạy. Hãy đợi một chút rồi bấm LÀM MỚI.");
    }

    private async void GarageCommandCancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_parkCountdownActive)
        {
            CloseGarageCommandOverlay();
            return;
        }

        await CancelActiveParkAsync();
    }

    private async Task CancelActiveParkAsync()
    {
        _activeGarageCommandCancellation?.Cancel();
        if (GarageCommandCancelButton is not null)
        {
            GarageCommandCancelButton.IsEnabled = false;
            GarageCommandProgress.Text = "ĐANG HỦY…";
        }

        if (_garageApi is not null && !_garageCancellation.IsCancellationRequested)
        {
            var cancellationTask = _parkCancellationTask ??= SendParkCancellationAsync();
            await cancellationTask;
            if (ReferenceEquals(_parkCancellationTask, cancellationTask))
            {
                _parkCancellationTask = null;
            }
        }

    }

    private async Task SendParkCancellationAsync()
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_garageCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await _garageApi!.Park("cancel", timeout.Token);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException ||
            exception is OperationCanceledException)
        {
            // The local countdown is already stopped. A failed best-effort cancel
            // must never keep the overlay open forever.
        }
    }

    private void ShowGarageCommandResult(bool success, string title, string message)
    {
        GarageCommandTitle.Text = title;
        GarageCommandMessage.Text = message;
        GarageCommandProgress.Text = success ? "HOÀN TẤT" : "CHƯA HOÀN TẤT";
        GarageCommandConfirmButton.Visibility = Visibility.Collapsed;
        GarageCommandCancelButton.Visibility = Visibility.Collapsed;
        GarageCommandCloseButton.Visibility = Visibility.Visible;
    }

    private void GarageCommandCloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseGarageCommandOverlay();

    private void CloseGarageCommandOverlay()
    {
        if (_activeGarageCommandCancellation is not null)
        {
            return;
        }

        _pendingGarageCommand = GarageCommandKind.None;
        _pendingGarageDinoId = null;
        _pendingGarageSuccessTitle = string.Empty;
        GarageCommandOverlay.Visibility = Visibility.Collapsed;
    }

    private static string ErrorMessage(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "IslePilot từ chối lệnh. Hãy kiểm tra trạng thái trong game." : value;

    private async Task LoadGarageAsync(bool force)
    {
        if (_garageApi is null || _garageLoading || _garageLoaded && !force)
        {
            return;
        }

        _garageLoading = true;
        GarageRefreshButton.IsEnabled = false;
        GarageStatePanel.Visibility = Visibility.Visible;
        GarageStateTitle.Text = "Đang tải Dino Garage";
        GarageStateMessage.Text = "Đang đọc dữ liệu trực tiếp từ IslePilot…";
        if (!_garageLoaded)
        {
            GarageCards.Visibility = Visibility.Collapsed;
        }

        try
        {
            var garage = await _garageApi.Load(_garageCancellation.Token);
            var liveSwap = garage.Settings?.LiveSwap == true;
            GarageModeLabel.Text = liveSwap ? "CHẾ ĐỘ · ĐỔI TRỰC TIẾP" : "CHẾ ĐỘ · LẤY RA";
            var cards = (garage.Dinos ?? [])
                .Select(dino => GarageDinoCardPresentation.From(dino, liveSwap))
                .OrderByDescending(card => card.Growth)
                .ThenBy(card => card.Species, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            GarageCards.ItemsSource = cards;
            GarageCountLabel.Text = cards.Length == 1 ? "1 DINO ĐÃ LƯU" : $"{cards.Length} DINO ĐÃ LƯU";
            _garageLoaded = true;

            if (cards.Length == 0)
            {
                GarageCards.Visibility = Visibility.Collapsed;
                GarageStatePanel.Visibility = Visibility.Visible;
                GarageStateTitle.Text = "Garage đang trống";
                GarageStateMessage.Text = "Bấm CẤT DINO HIỆN TẠI để đưa Dino đang chơi vào Garage.";
            }
            else
            {
                GarageStatePanel.Visibility = Visibility.Collapsed;
                GarageCards.Visibility = Visibility.Visible;
                GarageCards.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(0.35d, 1d, TimeSpan.FromMilliseconds(170)));
            }
        }
        catch (OperationCanceledException) when (_garageCancellation.IsCancellationRequested)
        {
        }
        catch (IslePilotOverlayAuthenticationException)
        {
            ShowGarageError("Phiên Steam đã hết hạn", "Đăng nhập lại IslePilot để đọc Dino Garage.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException ||
            exception is OperationCanceledException)
        {
            ShowGarageError("Chưa tải được Garage", "Server phản hồi chậm hoặc tạm thời không khả dụng. Bấm LÀM MỚI để thử lại.");
        }
        finally
        {
            _garageLoading = false;
            if (!_garageCancellation.IsCancellationRequested)
            {
                GarageRefreshButton.IsEnabled = true;
            }
        }
    }

    private void ShowGarageError(string title, string message)
    {
        GarageStatePanel.Visibility = Visibility.Visible;
        GarageStateTitle.Text = title;
        GarageStateMessage.Text = message;
        if (!_garageLoaded)
        {
            GarageCards.Visibility = Visibility.Collapsed;
            GarageCountLabel.Text = "CHƯA ĐỒNG BỘ";
        }
    }

    private void SpeciesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RenderRecommendations();

    private void SurvivalTrackButton_Click(object sender, RoutedEventArgs e) => SelectTrack(MutationTrack.Survival);

    private void CombatTrackButton_Click(object sender, RoutedEventArgs e) => SelectTrack(MutationTrack.Combat);

    private void MovementTrackButton_Click(object sender, RoutedEventArgs e) => SelectTrack(MutationTrack.Movement);

    private void SelectTrack(MutationTrack track)
    {
        _selectedTrack = track;
        RenderRecommendations();
    }

    private void RenderRecommendations()
    {
        if (SpeciesComboBox.SelectedItem is not SpeciesMutationGuide species || MutationCards is null)
        {
            return;
        }

        SpeciesSummaryLabel.Text = species.Summary;
        SpeciesProfileLabel.Text = $"{species.Diet.ToUpperInvariant()}  ·  {species.Role.ToUpperInvariant()}";
        var trackName = _selectedTrack switch
        {
            MutationTrack.Survival => "Sinh tồn",
            MutationTrack.Combat => "Combat",
            _ => "Di chuyển"
        };
        RecommendationTitleLabel.Text = $"{trackName} · {species.Name}";
        MutationCards.ItemsSource = species.Recommendations
            .Where(item => item.Track == _selectedTrack)
            .OrderBy(item => item.Priority)
            .ToArray();

        foreach (var pair in new[]
                 {
                     (Button: SurvivalTrackButton, Track: MutationTrack.Survival),
                     (Button: CombatTrackButton, Track: MutationTrack.Combat),
                     (Button: MovementTrackButton, Track: MutationTrack.Movement)
                 })
        {
            var active = pair.Track == _selectedTrack;
            pair.Button.Background = BrushFrom(active ? "#303236" : "#141517");
            pair.Button.Foreground = BrushFrom(active ? "#F0F1F2" : "#92959A");
            pair.Button.BorderBrush = BrushFrom(active ? "#7C7F84" : "#34363A");
        }

        MutationCards.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25d, 1d, TimeSpan.FromMilliseconds(150)));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
        {
            DragMove();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsMapPageVisible && e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = _embeddedMapWindow.PasteDestinationFromClipboard();
            return;
        }

        if (e.Key is Key.Escape or Key.F8)
        {
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _garageCancellation.Cancel();
        _garageCancellation.Dispose();
        base.OnClosed(e);
    }

    private enum GarageCommandKind
    {
        None,
        Park,
        Restore
    }

    private static string NormalizeSpecies(string? value) => new((value ?? string.Empty)
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());

    private static SolidColorBrush BrushFrom(string value) =>
        new((Color)ColorConverter.ConvertFromString(value));
}
