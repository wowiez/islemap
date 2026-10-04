using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.Sbtc;

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
    DateTimeOffset UpdatedAt,
    string? ServerId = null,
    bool? Female = null);

internal static class IslePilotServerIds
{
    // IslePilot's skin endpoint expects the internal server id, not the public slug.
    // SBTC ISLAND is the server used by the current skin editor route.
    public const string SbtcIsland = "cmsxo6wv70nl7o101w2ae292k";

    public static string? ForSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return SbtcIsland;
        if (slug.Contains("sbtc", StringComparison.OrdinalIgnoreCase)) return SbtcIsland;
        return null;
    }
}

public sealed record GuideGarageApi(
    Func<CancellationToken, Task<IslePilotOverlayGarageDto>> Load,
    Func<SbtcVaultParkTerms, CancellationToken, Task<IslePilotOverlayGarageCommandDto>> Park,
    Func<string, CancellationToken, Task<IslePilotOverlayGarageCommandDto>> Restore,
    Func<string, CancellationToken, Task<IslePilotOverlayGarageCommandStatusDto>> Status,
    Func<string, CancellationToken, Task<IslePilotOverlaySkinDraftsDto>> LoadSkinDrafts,
    Func<string, string, string, IslePilotOverlayGaragePaletteDto, bool, int, int, int, CancellationToken, Task<IslePilotOverlaySkinDraftDto>> SaveSkinDraft,
    Func<string, IslePilotOverlayGaragePaletteDto, int, int, int, CancellationToken, Task<SbtcPreparedSkinApply>> PrepareSkinApply,
    Func<SbtcPreparedSkinApply, CancellationToken, Task<IslePilotOverlaySkinApplyDto>> ApplySkin,
    Func<CancellationToken, Task<SbtcSkinAccess>> SkinAccess,
    Func<CancellationToken, Task<IslePilotOverlaySkinApplyDto>> CheckSkinDelivery,
    Func<Uri, CancellationToken, Task<byte[]>> DownloadAsset,
    Func<CancellationToken, Task<SbtcVaultParkTerms>> ParkTerms,
    Func<string, int, CancellationToken, Task<SbtcSkinPreview>>? SkinPreview = null);

public sealed record SkinDraftPresentation(
    string Name,
    IslePilotOverlayGaragePaletteDto? Palette,
    IReadOnlyList<string> Colors,
    IslePilotOverlaySkinDraftPayloadDto? Payload)
{
    public string Description => Payload?.RenderMode == "glitch"
        ? "Thiết kế glitch · xem và chỉnh trong Studio SBTC"
        : Palette is null ? "Server chưa cung cấp bảng màu · xem trong Studio SBTC" : "Thiết kế màu thường";
    public string ActionLabel => Payload?.RenderMode == "glitch" || Palette is null ? "MỞ STUDIO" : "CHỌN";
}

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
    public int Pattern { get; init; }
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
            colors) { SkinPalette = dino.Palette, Pattern = dino.Payload?.Pattern ?? 0 };
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
    internal const bool VoiceFeatureEnabled = false;
    private MutationTrack _selectedTrack = MutationTrack.Survival;
    private readonly LargeMapWindow _embeddedMapWindow;
    private readonly GuideGarageApi? _garageApi;
    private readonly CancellationTokenSource _garageCancellation = new();
    private bool _garageLoaded;
    private bool _garageLoading;
    private GarageCommandKind _pendingGarageCommand;
    private string? _pendingGarageDinoId;
    private SbtcVaultParkTerms? _pendingParkTerms;
    private string _pendingGarageSuccessTitle = string.Empty;
    private CancellationTokenSource? _activeGarageCommandCancellation;
    private bool _npcapEnabled;
    private bool _autoHideOutsideGame;
    private bool _showPathTrail = true;
    private NpcapSourceState _npcapState;
    private bool _settingSkinInputs;
    private string? _skinEditorSpecies;
    private bool _sbtcVoiceAvailable;
    private string _skinEditorServerSlug = "sbtcisland";
    private string? _skinEditorServerId = IslePilotServerIds.SbtcIsland;
    private bool _skinEditorFemale = true;
    private int _skinEditorTheme;
    private int _skinEditorPattern;
    private int _skinEditorVariation = 8;
    private bool _skinDraftLoading;
    private bool _showAllSkinDrafts = true;
    private bool _skinApplyBusy;

    public GuideWindow(
        string? activeSpecies = null,
        ImageSource? mapSource = null,
        MapPoint? currentLocation = null,
        MapPoint? destination = null,
        IReadOnlyList<SbtcZoneFeature>? zones = null,
        IReadOnlyList<SbtcPlayerMarker>? players = null,
        GuidePlayerOverview? playerOverview = null,
        GuideGarageApi? garageApi = null,
        bool npcapEnabled = true,
        bool copyAssetEnabled = true,
        NpcapSourceState? npcapState = null,
        bool autoHideOutsideGame = true)
    {
        InitializeComponent();
        _embeddedMapWindow = new LargeMapWindow(mapSource);
        _garageApi = garageApi;
        if (garageApi is not null)
        {
            SetValue(GarageModelControl.AssetDownloaderProperty, garageApi.DownloadAsset);
            SetValue(GarageModelControl.PreviewProviderProperty, garageApi.SkinPreview);
        }
        _npcapEnabled = npcapEnabled;
        _autoHideOutsideGame = autoHideOutsideGame;
        _skinEditorSpecies = activeSpecies;
        _npcapState = npcapState ?? new NpcapSourceState(NpcapSourceStatus.Unavailable, "Chưa cài Npcap");
        var mapContent = _embeddedMapWindow.Content;
        _embeddedMapWindow.Content = null;
        GuideMapHost.Content = mapContent;
        _embeddedMapWindow.UpdateState(currentLocation, destination, zones ?? [], players ?? []);
        _embeddedMapWindow.DestinationChanged += destinationValue => DestinationChanged?.Invoke(destinationValue);
        _embeddedMapWindow.ClearPathTrailRequested += () => ClearPathTrailRequested?.Invoke();
        _embeddedMapWindow.MapLayerFiltersChanged += (showZones, showWildlife) =>
            MapLayerFiltersChanged?.Invoke(showZones, showWildlife);
        SpeciesComboBox.ItemsSource = MutationGuideCatalog.Species;
        SpeciesComboBox.DisplayMemberPath = nameof(SpeciesMutationGuide.Name);
        SelectSpecies(activeSpecies);
        ShowPage(OverviewPage, OverviewNavButton);
        UpdatePlayerOverview(playerOverview);
        RenderRecommendations();
        GarageNavButton.Visibility = Visibility.Collapsed;
        SkinEditorNavButton.Visibility = garageApi is null ? Visibility.Collapsed : Visibility.Visible;
        ApplySkinPaletteToInputs(DefaultSkinPalette());
        RenderCaptureSettings();
        UpdateMapMarkerSettings(OverlayLayoutRules.DefaultPlayerMarkerScale, wildlifeAbovePlayer: false);
        InitializeKillFeed();
    }

    public event Action<MapPoint?>? DestinationChanged;
    public event Action<bool>? NpcapEnabledChanged;
    public event Action<bool>? AutoHideOutsideGameChanged;
    public event Action<bool>? PathTrailToggleRequested;
    public event Action? ClearPathTrailRequested;
    public event Action? RetryNpcapRequested;
    public event Action? ZoneSnifferRequested;
    public event Action? DownloadNpcapRequested;

    public bool IsMapPageVisible => MapPage.Visibility == Visibility.Visible;

    public void UpdateCurrentLocation(MapPoint? currentLocation) =>
        _embeddedMapWindow.UpdateCurrentLocation(currentLocation);

    public void UpdateZones(IReadOnlyList<SbtcZoneFeature> zones) =>
        _embeddedMapWindow.UpdateZones(zones);

    public void UpdateDestination(MapPoint? destination) =>
        _embeddedMapWindow.UpdateDestination(destination);

    public void UpdatePlayers(IReadOnlyList<SbtcPlayerMarker> players) =>
        _embeddedMapWindow.UpdatePlayers(players);

    public void UpdatePathTrail(IReadOnlyList<PlayerPathTrailPoint>? points, bool showTrail)
    {
        _showPathTrail = showTrail;
        GuidePathTrailToggleButton.Content = _showPathTrail ? "BẬT" : "TẮT";
        _embeddedMapWindow.UpdatePathTrail(points, showTrail);
    }

    public void UpdatePlayerOverview(GuidePlayerOverview? player)
    {
        if (player is null)
        {
            SetSbtcVoiceAvailability(false);
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

        var speciesChanged = !string.Equals(_skinEditorSpecies, player.Species, StringComparison.OrdinalIgnoreCase);
        if (speciesChanged)
        {
            _skinEditorSpecies = player.Species;
            _skinEditorTheme = 0;
            _skinEditorPattern = 0;
            _skinEditorVariation = 8;
            _skinEditorFemale = player.Female ?? true;
            RefreshSkinEditorModel();
        }
        else if (string.IsNullOrWhiteSpace(_skinEditorSpecies))
        {
            _skinEditorSpecies = player.Species;
            _skinEditorFemale = player.Female ?? true;
            RefreshSkinEditorModel();
        }

        _skinEditorServerSlug = SlugForServer(player.Server);
        _skinEditorServerId = player.ServerId ?? IslePilotServerIds.ForSlug(_skinEditorServerSlug);
        SetSbtcVoiceAvailability(SbtcZoneOverlay.IsSbtcServer(player.Server));

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

    private static string SlugForServer(string? server)
    {
        var slug = new string((server ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return string.IsNullOrWhiteSpace(slug) ? "sbtcisland" : slug;
    }

    public void UpdateNpcapState(NpcapSourceState state)
    {
        _npcapState = state;
        RenderCaptureSettings();
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

    private async void SkinEditorNavButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShowPage(SkinEditorPage, SkinEditorNavButton);
            await RefreshSkinAccessAsync();
            RefreshSkinEditorModel();
            await LoadSkinDraftsAsync();
        }
        catch (Exception exception)
        {
            CrashReporter.Write("GuideWindow.SkinEditorNav", exception);
        }
    }

    private void VoiceNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_sbtcVoiceAvailable) return;
        ShowPage(VoicePage, VoiceNavButton);
        VoiceControl.SetEnabled(true);
    }

    private void VoiceRetryButton_Click(object sender, RoutedEventArgs e) => VoiceControl.Retry();

    private void MutationNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(MutationPage, MutationNavButton);

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(SettingsPage, SettingsNavButton);

    /// <summary>Shows where the zone sniffer saves its files.</summary>
    public void UpdateSnifferTarget(string directory) =>
        NpcapDiagnosticsLabel.Text = "Vùng IslePilot sẽ được lưu vào: " + directory;

    private void ZoneSnifferButton_Click(object sender, RoutedEventArgs e)
    {
        ZoneSnifferRequested?.Invoke();
    }

    private void NpcapToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _npcapEnabled = !_npcapEnabled;
        RenderCaptureSettings();
        NpcapEnabledChanged?.Invoke(_npcapEnabled);
    }

    private void NpcapActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_npcapState.Status == NpcapSourceStatus.Unavailable)
        {
            DownloadNpcapRequested?.Invoke();
        }
        else
        {
            RetryNpcapRequested?.Invoke();
        }
    }

    private void AutoHideOutsideGameButton_Click(object sender, RoutedEventArgs e)
    {
        _autoHideOutsideGame = !_autoHideOutsideGame;
        RenderCaptureSettings();
        AutoHideOutsideGameChanged?.Invoke(_autoHideOutsideGame);
    }

    private void GuidePathTrailToggleButton_Click(object sender, RoutedEventArgs e)
    {
        PathTrailToggleRequested?.Invoke(!_showPathTrail);
    }

    private void GuideClearPathTrailButton_Click(object sender, RoutedEventArgs e)
    {
        ClearPathTrailRequested?.Invoke();
    }

    private void ResetHotkeysButton_Click(object sender, RoutedEventArgs e)
    {
        GuideHotkeyButton.Content = "F8";
        LargeMapHotkeyButton.Content = "Alt + M";
        SettingsHotkeyButton.Content = "Ctrl + Shift + O";
        ZoomHotkeyButton.Content = "Alt +  /  Alt −";
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var previous = button.Content;
        button.Content = "ENTER THE KEY";
        button.Focus();
        KeyEventHandler? capture = null;
        capture = (_, keyEvent) =>
        {
            keyEvent.Handled = true;
            button.Content = keyEvent.Key == Key.Escape ? previous : FormatHotkey(keyEvent);
            button.PreviewKeyDown -= capture;
            button.ReleaseMouseCapture();
        };
        button.PreviewKeyDown += capture;
        button.CaptureMouse();
    }

    private static string FormatHotkey(KeyEventArgs e)
    {
        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)) parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    private async void SkinEditorRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshSkinAccessAsync();
        await LoadSkinDraftsAsync();
        RefreshSkinEditorModel();
        SkinEditorModel.Reload();
    }

    private void SkinColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key) return;
        var input = SkinInputs().FirstOrDefault(item => string.Equals(item.Input.Tag as string, key, StringComparison.Ordinal));
        if (input.Input is null) return;
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var panel = new UniformGrid { Columns = 8, Width = 224, Margin = new Thickness(2) };
        var colors = new[]
        {
            "#FFFFFF", "#F4CCCC", "#FCE5CD", "#FFF2CC", "#D9EAD3", "#D0E0E3", "#CFE2F3", "#D9D2E9",
            "#EA9999", "#F9CB9C", "#FFE599", "#B6D7A8", "#A2C4C9", "#9FC5E8", "#B4A7D6", "#D5A6BD",
            "#E06666", "#F6B26B", "#FFD966", "#93C47D", "#76A5AF", "#6FA8DC", "#8E7CC3", "#C27BA0",
            "#CC0000", "#E69138", "#F1C232", "#6AA84F", "#45818E", "#3D85C6", "#674EA7", "#A64D79",
            "#990000", "#B45F06", "#BF9000", "#38761D", "#134F5C", "#1155CC", "#351C75", "#741B47",
            "#222222", "#444444", "#666666", "#888888", "#AAAAAA", "#CCCCCC", "#EEEEEE", "#17191B"
        };
        foreach (var color in colors)
        {
            var colorButton = new Button { Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(1), Background = BrushFrom(color), BorderBrush = BrushFrom("#606761"), ToolTip = color };
            colorButton.Click += (_, _) => { input.Input.Text = color; popup.IsOpen = false; };
            panel.Children.Add(colorButton);
        }
        popup.Child = new Border { Background = BrushFrom("#202326"), BorderBrush = BrushFrom("#606761"), BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = panel };
        popup.IsOpen = true;
    }

    private void SkinSaveButton_Click(object sender, RoutedEventArgs e)
        => _ = SaveSkinDraftAsync();

    private void SkinPasteJsonButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var palette = JsonSerializer.Deserialize<IslePilotOverlayGaragePaletteDto>(
                Clipboard.GetText(), IslePilotOverlayJson.Options);
            if (palette is null) throw new JsonException();
            ApplySkinPaletteToInputs(palette);
            SkinEditorStatusLabel.Text = "ĐÃ PASTE JSON BẢNG MÀU";
            SkinEditorStatusLabel.Foreground = BrushFrom("#8FC7A5");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            SkinEditorStatusLabel.Text = "JSON KHÔNG HỢP LỆ";
            SkinEditorStatusLabel.Foreground = BrushFrom("#D2726F");
        }
    }

    private async void SkinShowAllDraftsButton_Click(object sender, RoutedEventArgs e)
    {
        _showAllSkinDrafts = !_showAllSkinDrafts;
        SkinShowAllDraftsButton.Content = _showAllSkinDrafts ? "CHỈ LOÀI HIỆN TẠI" : "TẤT CẢ ĐÃ LƯU";
        await LoadSkinDraftsAsync();
    }

    private async void SkinDraftsRefreshButton_Click(object sender, RoutedEventArgs e) => await LoadSkinDraftsAsync();

    private async Task SaveSkinDraftAsync()
    {
        if (!TryReadSkinPalette(out var palette))
        {
            SkinEditorStatusLabel.Text = "KHÔNG THỂ LƯU · KIỂM TRA MÃ HEX";
            SkinEditorStatusLabel.Foreground = BrushFrom("#D2726F");
            return;
        }
        if (_garageApi is null || string.IsNullOrWhiteSpace(_skinEditorSpecies))
        {
            SkinEditorStatusLabel.Text = "CHƯA CÓ DINO ĐỂ LƯU DRAFT";
            SkinEditorStatusLabel.Foreground = BrushFrom("#D2726F");
            return;
        }
        var name = PromptDraftName();
        if (name is null) return;
        try
        {
            await _garageApi.SaveSkinDraft(_skinEditorServerSlug, _skinEditorSpecies, name, palette, _skinEditorFemale, _skinEditorTheme, _skinEditorPattern, _skinEditorVariation, _garageCancellation.Token);
            SkinEditorStatusLabel.Text = "ĐÃ LƯU VÀO THIẾT KẾ SBTC";
            SkinEditorStatusLabel.Foreground = BrushFrom("#8FC7A5");
            await LoadSkinDraftsAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException or TelemetryAuthenticationException or IslePilotOverlayAuthenticationException)
        {
            SkinEditorStatusLabel.Text = exception is IslePilotOverlayAuthenticationException or TelemetryAuthenticationException
                ? "CHƯA ĐĂNG NHẬP SBTC ISLAND HOẶC PHIÊN ĐÃ HẾT HẠN"
                : $"KHÔNG LƯU ĐƯỢC THIẾT KẾ: {exception.Message}";
            SkinEditorStatusLabel.Foreground = BrushFrom("#D2726F");
        }
        catch (Exception exception)
        {
            SkinEditorStatusLabel.Text = $"LỖI LƯU DRAFT: {exception.Message}";
            SkinEditorStatusLabel.Foreground = BrushFrom("#D2726F");
        }
    }

    private string? PromptDraftName()
    {
        var dialog = new Window
        {
            Title = "Lưu skin draft",
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = BrushFrom("#151918"),
            Foreground = BrushFrom("#E8EAE9")
        };
        var input = new TextBox { Text = $"{_skinEditorSpecies} skin", Margin = new Thickness(0, 8, 0, 12), MinWidth = 290 };
        var save = new Button { Content = "LƯU", IsDefault = true, Padding = new Thickness(18, 6, 18, 6), HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "HỦY", IsCancel = true, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = "Tên draft", FontWeight = FontWeights.SemiBold });
        content.Children.Add(input); content.Children.Add(buttons);
        dialog.Content = content;
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private async void SkinApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_skinApplyBusy || _garageApi is null || string.IsNullOrWhiteSpace(_skinEditorSpecies)) return;
        if (!TryReadSkinPalette(out var palette)) { SkinEditorStatusLabel.Text = "KIỂM TRA MÃ MÀU HEX"; return; }
        _skinApplyBusy = true;
        try
        {
            var prepared = await _garageApi.PrepareSkinApply(_skinEditorSpecies, palette,
                _skinEditorTheme, _skinEditorPattern, _skinEditorVariation, _garageCancellation.Token);
            SkinAccessLabel.Text = prepared.Access.Message;
            SkinEditorStatusLabel.Text = "ĐANG GỬI SKIN TỚI SBTC…";
            var result = await _garageApi.ApplySkin(prepared, _garageCancellation.Token);
            ShowSkinDelivery(result);
            // Accepted is only an acknowledgement. Keep checking the island's result.
            for (var poll = 0; result.Pending == true && result.Accepted && poll < 15; poll++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _garageCancellation.Token);
                result = await _garageApi.CheckSkinDelivery(_garageCancellation.Token);
                ShowSkinDelivery(result);
            }
        }
        catch (OperationCanceledException) when (_garageCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            SkinEditorStatusLabel.Text = $"CHƯA XÁC NHẬN SKIN: {exception.Message}";
            SkinEditorStatusLabel.Foreground = BrushFrom("#D2726F");
        }
        finally { _skinApplyBusy = false; }
    }

    private void ShowSkinDelivery(IslePilotOverlaySkinApplyDto result)
    {
        SkinEditorStatusLabel.Text = result.Message ?? result.Error ?? "Server chưa xác nhận skin.";
        SkinEditorStatusLabel.Foreground = BrushFrom(result.Pending == true ? "#E7B74E" : result.Accepted ? "#8FC7A5" : "#D2726F");
    }

    private async void SkinDeliveryCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_garageApi is null || _skinApplyBusy) return;
        _skinApplyBusy = true;
        try { ShowSkinDelivery(await _garageApi.CheckSkinDelivery(_garageCancellation.Token)); }
        catch (OperationCanceledException) when (_garageCancellation.IsCancellationRequested) { }
        catch (Exception exception) { SkinEditorStatusLabel.Text = exception.Message; }
        finally { _skinApplyBusy = false; }
    }

    private async Task RefreshSkinAccessAsync()
    {
        if (_garageApi is null) return;
        try
        {
            var access = await _garageApi.SkinAccess(_garageCancellation.Token);
            SkinAccessLabel.Text = access.Message;
            foreach (var control in new Control[] { SkinTeethHex, SkinTeethSwatch, SkinMouthHex, SkinMouthSwatch, SkinClawsHex, SkinClawsSwatch })
                control.IsEnabled = access.Advanced;
        }
        catch (OperationCanceledException) when (_garageCancellation.IsCancellationRequested) { }
        catch (Exception exception) { SkinAccessLabel.Text = exception.Message; }
    }

    private void SkinDraftApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SkinDraftPresentation draft })
        {
            return;
        }

        try
        {
            if (draft.Payload?.RenderMode == "glitch")
            {
                SkinEditorStatusLabel.Text = "Skin glitch cần trình studio trên web SBTC; bảng HEX này dùng cho skin màu thường.";
                OpenSkinStudio();
                return;
            }
            if (draft.Palette is null)
            {
                SkinEditorStatusLabel.Text = "Thiết kế này chưa có bảng màu trong phản hồi server. Xem trong Studio SBTC.";
                OpenSkinStudio();
                return;
            }
            if (draft.Payload?.Species is { } draftSpecies && !string.Equals(SpeciesKey(draftSpecies), SpeciesKey(_skinEditorSpecies), StringComparison.OrdinalIgnoreCase))
            {
                SkinEditorStatusLabel.Text = "Skin này thuộc loài khác. Chọn skin đúng Dino đang chơi.";
                return;
            }
            ApplySkinPaletteToInputs(draft.Palette);
            if (draft.Payload is not null)
            {
                _skinEditorTheme = draft.Payload.Theme;
                _skinEditorPattern = draft.Payload.Pattern;
                _skinEditorVariation = draft.Payload.Variation;
                if (draft.Payload.Female.HasValue)
                {
                    _skinEditorFemale = draft.Payload.Female.Value;
                }
                else if (!string.IsNullOrWhiteSpace(draft.Payload.Sex))
                {
                    _skinEditorFemale = string.Equals(draft.Payload.Sex, "female", StringComparison.OrdinalIgnoreCase);
                }
            }
            RefreshSkinEditorModel();
            SkinEditorStatusLabel.Text = $"ĐÃ NẠP DRAFT {draft.Name.ToUpperInvariant()} · BẤM ÁP DỤNG ĐỂ GỬI TỚI GAME";
            SkinEditorStatusLabel.Foreground = BrushFrom("#8FC7A5");
        }
        catch (Exception exception)
        {
            SkinEditorStatusLabel.Text = $"LỖI NẠP DRAFT: {exception.Message}";
            SkinEditorStatusLabel.Foreground = BrushFrom("#D2726F");
        }
    }

    private static void OpenSkinStudio() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = "https://sbtcislandd.com/studio", UseShellExecute = true
    });

    private async Task LoadSkinDraftsAsync()
    {
        if (_garageApi is null)
        {
            SkinDraftsStatusLabel.Text = "Đăng nhập nguồn SBTC Island để tải thiết kế đã lưu.";
            return;
        }
        if (_skinDraftLoading) return;
        _skinDraftLoading = true;
        SkinDraftsRefreshButton.IsEnabled = false;
        SkinDraftsStatusLabel.Text = "Đang tải thiết kế đã lưu từ Studio SBTC…";
        SkinDraftsStatusLabel.Foreground = BrushFrom("#B9C8C1");
        try
        {
            var result = await _garageApi.LoadSkinDrafts(_skinEditorServerSlug, _garageCancellation.Token);
            var currentSpecies = SpeciesKey(_skinEditorSpecies);
            var rows = result.Drafts
                .Where(draft => _showAllSkinDrafts || string.IsNullOrWhiteSpace(draft.GetSpecies()) ||
                    (currentSpecies.Length > 0 && string.Equals(SpeciesKey(draft.GetSpecies()), currentSpecies, StringComparison.OrdinalIgnoreCase)))
                .Select(draft => (Draft: draft, Palette: draft.GetPalette(), Species: draft.GetSpecies()))
                .Select(draft =>
                {
                    var draftName = string.IsNullOrWhiteSpace(draft.Draft.Name) ? "Skin draft" : draft.Draft.Name!;
                    var speciesLabel = string.IsNullOrWhiteSpace(draft.Species) ? string.Empty : SpeciesKey(draft.Species);
                    var displayName = _showAllSkinDrafts && !string.IsNullOrWhiteSpace(speciesLabel)
                        ? $"{draftName} ({speciesLabel})"
                        : draftName;
                    return new SkinDraftPresentation(
                        displayName,
                        draft.Palette,
                        draft.Palette is null ? Array.Empty<string>() : PaletteColors(draft.Palette),
                        draft.Draft.GetPayload());
                })
                .ToArray();
            SkinDraftsList.ItemsSource = rows;
            var partial = !string.IsNullOrWhiteSpace(result.Message);
            var message = result.Drafts.Count == 0 ? (partial ? "Chưa đọc được đầy đủ kho thiết kế." : "Chưa có thiết kế tự lưu trong kho Studio SBTC.")
                : rows.Length == 0 ? "Không có thiết kế cho loài hiện tại. Bấm TẤT CẢ ĐÃ LƯU để xem kho."
                : $"Đang hiển thị {rows.Length} / {result.Drafts.Count} thiết kế{(partial ? " đã nhận" : " đã lưu")}.";
            SkinDraftsStatusLabel.Text = string.IsNullOrWhiteSpace(result.Message) ? message : message + " " + result.Message;
            SkinDraftsStatusLabel.Foreground = BrushFrom(string.IsNullOrWhiteSpace(result.Message) ? "#8FC7A5" : "#E7B74E");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or TelemetryAuthenticationException or IslePilotOverlayAuthenticationException)
        {
            var authenticationError = exception is IslePilotOverlayAuthenticationException or TelemetryAuthenticationException;
            if (authenticationError) SkinDraftsList.ItemsSource = Array.Empty<SkinDraftPresentation>();
            SkinDraftsStatusLabel.Text = authenticationError
                ? "CHƯA ĐĂNG NHẬP SBTC ISLAND HOẶC PHIÊN ĐÃ HẾT HẠN"
                : $"Không tải được thiết kế đã lưu: {exception.Message}";
            SkinDraftsStatusLabel.Foreground = BrushFrom("#D2726F");
        }
        catch (Exception exception)
        {
            SkinDraftsStatusLabel.Text = $"LỖI TẢI DRAFT: {exception.Message}";
            SkinDraftsStatusLabel.Foreground = BrushFrom("#D2726F");
        }
        finally { _skinDraftLoading = false; SkinDraftsRefreshButton.IsEnabled = true; }
    }

    private static string SpeciesKey(string? species)
    {
        var value = species?.Trim() ?? string.Empty;
        if (value.StartsWith("BP_", StringComparison.OrdinalIgnoreCase)) value = value[3..];
        if (value.EndsWith("_C", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        return value.Replace('_', ' ');
    }

    private void SkinDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        _skinEditorTheme = 0;
        _skinEditorPattern = 0;
        _skinEditorVariation = 8;
        ApplySkinPaletteToInputs(DefaultSkinPalette());
    }

    private void SkinRandomButton_Click(object sender, RoutedEventArgs e)
    {
        _skinEditorTheme = 0;
        _skinEditorPattern = 0;
        _skinEditorVariation = 8;
        string Color() => $"#{Random.Shared.Next(28, 232):X2}{Random.Shared.Next(28, 232):X2}{Random.Shared.Next(28, 232):X2}";
        ApplySkinPaletteToInputs(new IslePilotOverlayGaragePaletteDto
        {
            Body = Color(), Markings = Color(), Flank = Color(), Underbelly = Color(), Detail = Color(),
            Display = Color(), Eyes = Color(), Teeth = Color(), Mouth = Color(), Claws = Color()
        });
    }

    private void SkinCopyJsonButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryReadSkinPalette(out var palette))
        {
            Clipboard.SetText(JsonSerializer.Serialize(palette, IslePilotOverlayJson.Options));
            SkinEditorStatusLabel.Text = "ĐÃ COPY JSON BẢNG MÀU";
            SkinEditorStatusLabel.Foreground = BrushFrom("#8FC7A5");
        }
    }

    private void SkinHexInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingSkinInputs || !IsInitialized) return;
        RenderSkinInputs();
    }

    private void ApplySkinPaletteToInputs(IslePilotOverlayGaragePaletteDto palette)
    {
        if (!IsInitialized) return;
        _settingSkinInputs = true;
        SkinBodyHex.Text = palette.Body ?? DefaultSkinPalette().Body;
        SkinMarkingsHex.Text = palette.Markings ?? DefaultSkinPalette().Markings;
        SkinFlankHex.Text = palette.Flank ?? DefaultSkinPalette().Flank;
        SkinUnderbellyHex.Text = palette.Underbelly ?? DefaultSkinPalette().Underbelly;
        SkinDetailHex.Text = palette.Detail ?? DefaultSkinPalette().Detail;
        SkinDisplayHex.Text = palette.Display ?? DefaultSkinPalette().Display;
        SkinEyesHex.Text = palette.Eyes ?? DefaultSkinPalette().Eyes;
        SkinTeethHex.Text = palette.Teeth ?? DefaultSkinPalette().Teeth;
        SkinMouthHex.Text = palette.Mouth ?? DefaultSkinPalette().Mouth;
        SkinClawsHex.Text = palette.Claws ?? DefaultSkinPalette().Claws;
        _settingSkinInputs = false;
        RenderSkinInputs();
    }

    private void RenderSkinInputs()
    {
        var valid = true;
        foreach (var (input, swatch) in SkinInputs())
        {
            var color = NormalizeHex(input.Text);
            var inputValid = color is not null;
            valid &= inputValid;
            input.BorderBrush = BrushFrom(inputValid ? "#45484C" : "#B95252");
            swatch.Background = BrushFrom(color ?? "#17191B");
        }

        if (valid && TryReadSkinPalette(out var palette))
        {
            SkinEditorModel.SetModel(_skinEditorSpecies, palette, _skinEditorPattern);
            SkinEditorStatusLabel.Text = "BẢNG MÀU HỢP LỆ · #RRGGBB";
            SkinEditorStatusLabel.Foreground = BrushFrom("#7F8C84");
        }
        else
        {
            SkinEditorStatusLabel.Text = "MÃ HEX KHÔNG HỢP LỆ · DÙNG #RRGGBB";
            SkinEditorStatusLabel.Foreground = BrushFrom("#D2726F");
        }
    }

    private void RefreshSkinEditorModel()
    {
        var species = string.IsNullOrWhiteSpace(_skinEditorSpecies) ? null : _skinEditorSpecies;
        SkinEditorModelLabel.Text = species is null
            ? "CHƯA CÓ DINO HIỆN TẠI"
            : $"ĐANG CHỈNH: {species.ToUpperInvariant()}";
        if (TryReadSkinPalette(out var palette)) SkinEditorModel.SetModel(species, palette, _skinEditorPattern);
    }

    private bool TryReadSkinPalette(out IslePilotOverlayGaragePaletteDto palette)
    {
        palette = new IslePilotOverlayGaragePaletteDto
        {
            Body = NormalizeHex(SkinBodyHex.Text), Markings = NormalizeHex(SkinMarkingsHex.Text),
            Flank = NormalizeHex(SkinFlankHex.Text), Underbelly = NormalizeHex(SkinUnderbellyHex.Text),
            Detail = NormalizeHex(SkinDetailHex.Text), Display = NormalizeHex(SkinDisplayHex.Text),
            Eyes = NormalizeHex(SkinEyesHex.Text), Teeth = NormalizeHex(SkinTeethHex.Text),
            Mouth = NormalizeHex(SkinMouthHex.Text), Claws = NormalizeHex(SkinClawsHex.Text)
        };
        return new[] { palette.Body, palette.Markings, palette.Flank, palette.Underbelly, palette.Detail,
            palette.Display, palette.Eyes, palette.Teeth, palette.Mouth, palette.Claws }.All(value => value is not null);
    }

    private (TextBox Input, Button Swatch)[] SkinInputs() =>
    [
        (SkinBodyHex, SkinBodySwatch), (SkinMarkingsHex, SkinMarkingsSwatch),
        (SkinFlankHex, SkinFlankSwatch), (SkinUnderbellyHex, SkinUnderbellySwatch),
        (SkinDetailHex, SkinDetailSwatch), (SkinDisplayHex, SkinDisplaySwatch),
        (SkinEyesHex, SkinEyesSwatch), (SkinTeethHex, SkinTeethSwatch),
        (SkinMouthHex, SkinMouthSwatch), (SkinClawsHex, SkinClawsSwatch)
    ];

    private static string? NormalizeHex(string? value)
    {
        var normalized = value?.Trim();
        if (normalized is { Length: 6 }) normalized = "#" + normalized;
        return normalized is { Length: 7 } && normalized[0] == '#' && normalized.Skip(1).All(Uri.IsHexDigit)
            ? normalized.ToUpperInvariant()
            : null;
    }

    private static IslePilotOverlayGaragePaletteDto DefaultSkinPalette() => new()
    {
        Body = "#4B5D36", Markings = "#364725", Flank = "#7B6A42", Underbelly = "#B2B08E",
        Detail = "#71815D", Display = "#D5F38F", Eyes = "#FFD76B", Teeth = "#E8E2D0",
        Mouth = "#7A3B3B", Claws = "#3A3A38"
    };

    private static IReadOnlyList<string> PaletteColors(IslePilotOverlayGaragePaletteDto palette) =>
        new[] { palette.Display, palette.Body, palette.Markings, palette.Flank, palette.Underbelly,
            palette.Detail, palette.Eyes, palette.Mouth, palette.Claws, palette.Teeth }
        .Where(value => value is not null)
        .Select(value => value!.ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();


    private void RenderCaptureSettings()
    {
        if (!IsInitialized) return;

        NpcapToggleButton.Content = _npcapEnabled ? "BẬT" : "TẮT";
        NpcapToggleButton.Background = _npcapEnabled ? BrushFrom("#34363A") : Brushes.Transparent;
        NpcapToggleButton.Foreground = BrushFrom(_npcapEnabled ? "#F2F3F4" : "#85888D");
        NpcapDiagnosticsLabel.Text = _npcapEnabled ? NpcapDiagnostics.Describe(_npcapState) : string.Empty;
        NpcapStatusLabel.Text = NpcapSourcePresentation.StatusText(_npcapEnabled, _npcapState.Status);
        NpcapStatusLabel.Foreground = BrushFrom(!_npcapEnabled
            ? "#85888D"
            : _npcapState.Status switch
            {
                NpcapSourceStatus.Live => "#37D4C6",
                NpcapSourceStatus.Faulted => "#DC5A56",
                NpcapSourceStatus.Unavailable => "#E7B74E",
                _ => "#C6A85C"
            });
        NpcapActionButton.Content = NpcapSourcePresentation.ActionText(_npcapState.Status);
        NpcapActionButton.Visibility = NpcapSourcePresentation.ShouldShowAction(_npcapEnabled, _npcapState.Status)
                ? Visibility.Visible
                : Visibility.Collapsed;
        AutoHideOutsideGameButton.Content = _autoHideOutsideGame ? "BẬT" : "TẮT";
        AutoHideOutsideGameButton.Background = _autoHideOutsideGame ? BrushFrom("#34363A") : Brushes.Transparent;
        AutoHideOutsideGameButton.Foreground = BrushFrom(_autoHideOutsideGame ? "#F2F3F4" : "#85888D");
    }

    private void ShowPage(FrameworkElement page, Button activeButton)
    {
        foreach (var candidate in new FrameworkElement[] { OverviewPage, MapPage, GaragePage, SkinEditorPage, VoicePage, MutationPage, KillFeedPage, SettingsPage })
        {
            candidate.Visibility = candidate == page ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var button in new[] { OverviewNavButton, MapNavButton, GarageNavButton, SkinEditorNavButton, VoiceNavButton, MutationNavButton, KillFeedNavButton, SettingsNavButton })
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
        UpdateKillFeedPolling();
    }

    private void SetSbtcVoiceAvailability(bool available)
    {
        var enabled = VoiceFeatureEnabled && available;
        _sbtcVoiceAvailable = enabled;
        VoiceNavButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled) return;

        VoiceControl.SetEnabled(false);
        if (VoicePage.Visibility == Visibility.Visible)
        {
            ShowPage(OverviewPage, OverviewNavButton);
        }
    }

    private async void GarageRefreshButton_Click(object sender, RoutedEventArgs e) =>
        await LoadGarageAsync(force: true);

    private async void GarageParkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_garageApi is null || _activeGarageCommandCancellation is not null) return;
        try
        {
            _pendingParkTerms = await _garageApi.ParkTerms(_garageCancellation.Token);
            ShowGarageConfirmation(
                GarageCommandKind.Park,
                dinoId: null,
                "Đã cất Dino",
                "Cất Dino hiện tại",
                _pendingParkTerms.Message);
        }
        catch (OperationCanceledException) when (_garageCancellation.IsCancellationRequested) { }
        catch (Exception exception) { ShowGarageError("Chưa kiểm tra được điều kiện cất Dino", exception.Message); }
    }

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
                : $"Lấy {card.DisplayName} ({card.Species}) từ kho SBTC? Bạn cần spawn cùng loài để server giao Dino. Kho chỉ giải phóng slot khi server xác nhận.");
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
                ShowGarageCommandResult(false, "Đã hủy", "Việc theo dõi đã dừng. Yêu cầu có thể vẫn được server xử lý; hãy kiểm tra Garage trước khi gửi lại.");
            }
        }
        catch (Exception exception) when (exception is IslePilotOverlayAuthenticationException or SbtcIslandAuthenticationException)
        {
            ShowGarageCommandResult(false, "Phiên Steam đã hết hạn", "Đăng nhập lại Steam SBTC rồi thử lại.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException ||
            exception is OperationCanceledException)
        {
            ShowGarageCommandResult(false, "Không gửi được lệnh", "Chưa xác nhận kết quả. Server có thể vẫn xử lý yêu cầu; kiểm tra Garage trước khi gửi lại.");
        }
        finally
        {
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
        GarageCommandProgress.Text = "ĐANG GỬI YÊU CẦU CẤT DINO…";
        if (_pendingParkTerms is null) return;
        var command = await _garageApi!.Park(_pendingParkTerms, cancellationToken);
        if (!command.Ok) { ShowGarageCommandResult(false, "Không thể cất Dino", ErrorMessage(command.Error)); return; }
        await CompleteGarageCommandAsync(command, "Đã cất Dino", cancellationToken);
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
            if (command.Pending == true)
            {
                ShowGarageCommandResult(false, "Đang chờ server", "Yêu cầu đã gửi nhưng chưa xác nhận. Kiểm tra kho Dino trước khi gửi lại.");
                return;
            }
            ShowGarageCommandResult(true, successTitle, "Garage đã được cập nhật.");
            await LoadGarageAsync(force: true);
            return;
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(45);
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

        ShowGarageCommandResult(false, "Đang chờ xác nhận từ server", "Yêu cầu có thể vẫn đang chạy. Hãy kiểm tra Garage; lấy Dino cần spawn cùng loài. Đừng gửi lại khi lệnh chưa rõ kết quả.");
    }

    private void GarageCommandCancelButton_Click(object sender, RoutedEventArgs e) => CloseGarageCommandOverlay();

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
        string.IsNullOrWhiteSpace(value) ? "SBTC từ chối lệnh. Hãy kiểm tra trạng thái trong game." : value;

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
        GarageStateMessage.Text = "Đang đọc dữ liệu trực tiếp từ SBTC Island…";
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
        catch (Exception exception) when (exception is IslePilotOverlayAuthenticationException or SbtcIslandAuthenticationException)
        {
            ShowGarageError("Phiên Steam đã hết hạn", "Đăng nhập lại Steam SBTC để đọc Dino Garage.");
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
            Hide();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosed(EventArgs e)
    {
        DisposeKillFeed();
        _garageCancellation.Cancel();
        _garageCancellation.Dispose();
        VoiceControl.Dispose();
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
