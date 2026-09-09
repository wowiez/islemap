using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

public partial class MainWindow : Window
{
    private static readonly Uri GatewayMapResourceUri = new("Assets/GatewayMap.webp", UriKind.Relative);
    private static readonly Uri GatewayZonesResourceUri = new("Assets/GatewayZones.json", UriKind.Relative);
    private static readonly IReadOnlyDictionary<string, Uri> BundledSbtcZoneIcons =
        new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase)
        {
            ["/maps/icons/sanctuary.webp"] = new("Assets/SbtcSanctuary.png", UriKind.Relative),
            ["/maps/icons/migration.webp"] = new("Assets/SbtcMigration.png", UriKind.Relative)
        };
    private static readonly TimeSpan LiveHeadingAnimationDuration = TimeSpan.FromMilliseconds(70);
    private static readonly TimeSpan MovementHeadingAnimationDuration = TimeSpan.FromMilliseconds(220);
    private const double MapZoomShortcutStep = 0.25d;
    private static readonly TimeSpan MinimumMapPanDuration = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan MaximumMapPanDuration = TimeSpan.FromMilliseconds(360);

    private const int SettingsHotkeyId = 0x714;
    private const int ZoomInHotkeyId = 0x715;
    private const int ZoomOutHotkeyId = 0x716;
    private const int LargeMapHotkeyId = 0x717;
    private const int GuideHotkeyId = 0x718;
    private const int WmNcHitTest = 0x0084;
    private const int WmHotkey = 0x0312;
    private const int HtTransparent = -1;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint KeyO = 0x4F;
    private const uint KeyM = 0x4D;
    private const uint KeyF8 = 0x77;
    private const uint KeyOemPlus = 0xBB;
    private const uint KeyOemMinus = 0xBD;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;

    private static readonly SolidColorBrush OnlineBrush = BrushFrom("#37D4C6");
    private static readonly SolidColorBrush WaitingBrush = BrushFrom("#E7B74E");
    private static readonly SolidColorBrush ErrorBrush = BrushFrom("#DC5A56");

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TelemetrySourceDefinition? _requestedSource;
    private readonly string? _providedCookie;
    private readonly ITelemetrySession? _providedSession;
    private readonly GuideGarageApi? _garageApi;
    private readonly OverlayLayoutSettingsStore _layoutSettingsStore = new();
    private readonly Dictionary<string, ImageSource> _sbtcZoneIconSources = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sbtcZoneIconLoads = new(StringComparer.Ordinal);
    private readonly List<RotateTransform> _sbtcZoneLabelRotations = [];
    private readonly List<RotateTransform> _sbtcPlayerLabelRotations = [];
    private RotateTransform? _routeDistanceLabelRotation;
    private readonly IReadOnlyList<SbtcZoneFeature> _bundledSbtcZones;
    private ITelemetrySession? _telemetrySession;
    private Task? _telemetryWatchTask;
    private ClipboardCoordinateMonitor? _clipboardCoordinateMonitor;
    private OverlayLayoutSettings _layoutSettings = new();
    private string _configuredSource = "ERA";
    private WorldLocation? _location;
    private MapPoint? _mapLocation;
    private WorldLocation? _previousLocation;
    private WorldLocation? _previousClipboardLocation;
    private WorldLocation? _clipboardLocation;
    private WorldLocation? _lastAutoDetectedLocation;
    private WorldLocation? _autoDetectLocationAtClipboard;
    private DateTimeOffset? _lastAutoDetectedMapUpdatedAt;
    private DateTimeOffset? _autoDetectMapUpdatedAtClipboard;
    private HwndSource? _windowSource;
    private double _mapZoom = 2.25d;
    private string _mapStyle = OverlayLayoutRules.DefaultMapStyle;
    private bool _showMap = true;
    private bool _showActivity = true;
    private bool _rotateMap;
    private bool _autoDetectLiveMap = true;
    private bool _showPrimeTasks;
    private bool _usesIslePilotMap;
    private double _headingDegrees;
    private double _overlayScale = OverlayLayoutRules.DefaultScale;
    private double _effectiveOverlayScale = OverlayLayoutRules.DefaultScale;
    private double _resizeStartingScale;
    private Point _resizeStartingScreenPoint;
    private bool _clickThrough;
    private bool _resizingOverlay;
    private bool _hasMovementHeading;
    private bool _settingsHotkeyRegistered;
    private bool _zoomInHotkeyRegistered;
    private bool _zoomOutHotkeyRegistered;
    private bool _largeMapHotkeyRegistered;
    private bool _guideHotkeyRegistered;
    private IReadOnlyList<SbtcZoneFeature> _sbtcZoneFeatures = [];
    private IReadOnlyList<SbtcPlayerMarker> _sbtcPlayerMarkers = [];
    private string _sbtcZoneSignature = string.Empty;
    private string _sbtcPlayerSignature = string.Empty;
    private double _renderedZoneWidth;
    private double _renderedZoneHeight;
    private double _renderedPlayerWidth;
    private double _renderedPlayerHeight;
    private double? _lastMapWorldLeft;
    private double? _lastMapWorldTop;
    private MapPoint? _lastPositionedMapPoint;
    private MapPoint? _routeDestination;
    private LargeMapWindow? _largeMapWindow;
    private GuideWindow? _guideWindow;
    private GuidePlayerOverview? _guidePlayerOverview;
    private long _lastLargeMapToggleTick;
    private string? _activeSpeciesName;

    public MainWindow() : this(null, null, null, null, null)
    {
    }

    public MainWindow(TelemetrySourceDefinition? source, string? cookieValue)
        : this(source, cookieValue, null, null, null)
    {
    }

    public MainWindow(ITelemetrySession telemetrySession, string displayName)
        : this(telemetrySession, displayName, null)
    {
    }

    public MainWindow(
        ITelemetrySession telemetrySession,
        string displayName,
        GuideGarageApi? garageApi)
        : this(
            null,
            null,
            telemetrySession ?? throw new ArgumentNullException(nameof(telemetrySession)),
            displayName,
            garageApi)
    {
    }

    private MainWindow(
        TelemetrySourceDefinition? source,
        string? cookieValue,
        ITelemetrySession? telemetrySession,
        string? displayName,
        GuideGarageApi? garageApi)
    {
        _requestedSource = source;
        _providedCookie = cookieValue;
        _providedSession = telemetrySession;
        _garageApi = garageApi;
        _usesIslePilotMap = source?.Kind == TelemetrySourceKind.IslePilot ||
                            telemetrySession is not null &&
                            displayName?.Contains("ISLEPILOT", StringComparison.OrdinalIgnoreCase) == true;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            _configuredSource = displayName;
        }

        InitializeComponent();
        _bundledSbtcZones = LoadBundledSbtcZones();
        _layoutSettings = _layoutSettingsStore.Load();
        _mapZoom = _layoutSettings.MapZoom;
        _mapStyle = _layoutSettings.MapStyle;
        _showMap = _layoutSettings.ShowMap;
        _showActivity = _layoutSettings.ShowActivity;
        _rotateMap = _layoutSettings.RotateMap;
        _autoDetectLiveMap = _layoutSettings.AutoDetectLiveMap;
        _showPrimeTasks = _layoutSettings.ShowPrimeTasks;
        ApplyOverlayScale(_layoutSettings.Scale, persist: false);
        ApplyMapZoom(_mapZoom, persist: false);
        ApplyMapStyle(_mapStyle, persist: false);
        ApplyMapRotationPreference(_rotateMap, persist: false);
        ApplyAutoDetectLiveMapPreference(_autoDetectLiveMap, persist: false);
        RenderPrimeTasks(null);
        ApplyPanelVisibility(persist: false);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PlayerMarker.Visibility = Visibility.Collapsed;
        DirectionNeedle.Opacity = 0.45d;
        _clipboardCoordinateMonitor = new ClipboardCoordinateMonitor(Dispatcher, ApplyClipboardLocation);
        _clipboardCoordinateMonitor.SetEnabled(true);
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        _settingsHotkeyRegistered = RegisterHotKey(handle, SettingsHotkeyId, ModControl | ModShift | ModNoRepeat, KeyO);
        _zoomInHotkeyRegistered = RegisterHotKey(handle, ZoomInHotkeyId, ModAlt, KeyOemPlus);
        _zoomOutHotkeyRegistered = RegisterHotKey(handle, ZoomOutHotkeyId, ModAlt, KeyOemMinus);
        _largeMapHotkeyRegistered = RegisterHotKey(handle, LargeMapHotkeyId, ModAlt | ModNoRepeat, KeyM);
        _guideHotkeyRegistered = RegisterHotKey(handle, GuideHotkeyId, ModNoRepeat, KeyF8);
        if (_settingsHotkeyRegistered)
        {
            SetClickThrough(true);
        }
        else
        {
            SettingsPanel.Visibility = Visibility.Visible;
        }

        RestoreOverlayPosition();

        if (!TryConfigureTelemetrySession())
        {
            SetConnectionState("CHƯA CẤU HÌNH NGUỒN", ErrorBrush);
            PlayerNameLabel.Text = "Set cookie cho Era hoặc DinoVietnam";
            MapStateLabel.Text = "CHƯA CÓ PHIÊN ĐĂNG NHẬP";
            return;
        }

        LoadMap();
        _telemetryWatchTask = WatchTelemetryAsync();
    }

    private bool TryConfigureTelemetrySession()
    {
        if (_providedSession is not null)
        {
            _telemetrySession = _providedSession;
            return true;
        }

        if (_requestedSource is not null && !string.IsNullOrWhiteSpace(_providedCookie))
        {
            ConfigureTelemetrySession(_requestedSource, _providedCookie);
            return true;
        }

        var requestedSourceId = Environment.GetEnvironmentVariable("TELEMETRY_SOURCE")?.Trim().ToLowerInvariant();
        if (requestedSourceId == "islepilot")
        {
            requestedSourceId = "dinovietnam";
        }

        var source = TelemetrySourceDefinition.FromId(requestedSourceId);
        var cookie = source?.Id switch
        {
            "era" => Environment.GetEnvironmentVariable("ERA_SESSION"),
            "dinovietnampremium" => Environment.GetEnvironmentVariable("ISLEPILOT_PREMIUM_PLAYER") ??
                                      Environment.GetEnvironmentVariable("ISLEPILOT_PLAYER"),
            "hoho" => Environment.GetEnvironmentVariable("ISLEPILOT_HOHO_PLAYER") ??
                      Environment.GetEnvironmentVariable("ISLEPILOT_PLAYER"),
            "dinovietnam" => Environment.GetEnvironmentVariable("ISLEPILOT_PLAYER"),
            "pandora" => Environment.GetEnvironmentVariable("PANDORA_SESSION"),
            _ => null
        };

        if (source is not null && !string.IsNullOrWhiteSpace(cookie))
        {
            ConfigureTelemetrySession(source, cookie);
            return true;
        }

        return false;
    }

    private void ConfigureTelemetrySession(TelemetrySourceDefinition source, string cookieValue)
    {
        _usesIslePilotMap = source.Kind == TelemetrySourceKind.IslePilot;
        var provider = source.CreateProvider(_httpClient, cookieValue);
        _telemetrySession = new PollingTelemetrySession(
            provider,
            source.PollingInterval,
            source: source.ShortName);
        _configuredSource = source.ShortName;
    }

    private void LoadMap()
    {
        try
        {
            var resource = Application.GetResourceStream(GatewayMapResourceUri)
                ?? throw new InvalidOperationException("Bundled Gateway map resource was not found.");
            using var stream = resource.Stream;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            MapImage.Source = image;
            DrinkingWaterImage.Source = DrinkingWaterOverlay.Image;
            MapStateLabel.Visibility = Visibility.Collapsed;
            PositionMap();
        }
        catch
        {
            MapStateLabel.Text = "KHÔNG ĐỌC ĐƯỢC BẢN ĐỒ";
        }
    }

    private async Task WatchTelemetryAsync()
    {
        if (_telemetrySession is null)
        {
            return;
        }

        try
        {
            await foreach (var snapshot in _telemetrySession
                               .WatchAsync(_shutdown.Token)
                               .ConfigureAwait(false))
            {
                await Dispatcher.InvokeAsync(() => RenderSnapshot(snapshot));
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await Dispatcher.InvokeAsync(() =>
                ShowTelemetryUnavailable("MẤT KẾT NỐI", "Telemetry session đã dừng"));
        }
    }

    private void RenderSnapshot(TelemetrySnapshot snapshot)
    {
        var activityStatus = MapOverlayPresentation.RequestStatus(
            snapshot.SessionState,
            snapshot.RequestStatus);
        var activeServer = snapshot.PlayerOnline ? snapshot.Player?.Server : null;
        UpdateSbtcZoneData(
            activeServer,
            snapshot.Map?.PointsOfInterest);
        UpdateSbtcPlayerData(activeServer, snapshot.Map?.Markers);
        RenderPrimeTasks(snapshot.PlayerOnline ? snapshot.Player?.Prime : null);

            if (snapshot.SessionState == TelemetrySessionState.AuthenticationRequired)
            {
                ShowTelemetryUnavailable("PHIÊN CẦN XÁC THỰC LẠI", "Tài khoản đã lưu vẫn được giữ; mở lại Home để xác thực");
                RequestStatusLabel.Text = activityStatus;
                return;
            }

            if (snapshot.SessionState == TelemetrySessionState.UnsupportedServer)
            {
                ShowNoActiveDinosaur(
                    snapshot.StatusMessage ?? "ISLEPILOT · CHƯA VÀO SERVER HỖ TRỢ",
                    "Server hiện tại chưa cài IslePilot");
                RequestStatusLabel.Text = activityStatus;
                return;
            }

            if (!snapshot.Success || !snapshot.ServerOnline)
            {
                ShowTelemetryUnavailable("SERVER OFFLINE", "Nguồn telemetry đang ngoại tuyến");
                RequestStatusLabel.Text = activityStatus;
                return;
            }

            if (!snapshot.PlayerOnline || snapshot.Player is null)
            {
                var state = ConnectionText(snapshot.SessionState);
                var detail = snapshot.SessionState switch
                {
                    TelemetrySessionState.Connecting => "Đang khởi tạo phiên telemetry",
                    TelemetrySessionState.Reconnecting => "Mất kết nối, đang thử lại",
                    TelemetrySessionState.Stale => "Dữ liệu realtime đã quá hạn",
                    _ => $"Join server {_configuredSource} để nhận telemetry"
                };
                ShowNoActiveDinosaur(state, detail);
                RequestStatusLabel.Text = activityStatus;
                return;
            }

            var player = snapshot.Player;
            _activeSpeciesName = player.Class;
            var exact = player.ExactVitals;

            var degraded = snapshot.SessionState is TelemetrySessionState.Reconnecting or TelemetrySessionState.Stale;
            SetTelemetryOpacity(degraded ? 0.7d : 1d);
            SetConnectionState(ConnectionText(snapshot.SessionState), degraded ? WaitingBrush : OnlineBrush);
            SpeciesLabel.Text = FriendlySpecies(player.Class);
            PlayerNameLabel.Text = string.IsNullOrWhiteSpace(player.Name) ? "ACTIVE PLAYER" : player.Name;

            var growth = exact?.Growth ?? player.GrowthPercent;
            GrowthLabel.Text = $"{NormalizePercent(growth):0.#}%";

            RenderVital(HealthBar, HealthValue, exact?.Health, exact?.MaxHealth, player.HealthPercent);
            RenderVital(StaminaBar, StaminaValue, exact?.Stamina, exact?.MaxStamina, player.StaminaPercent);

            RenderVital(HungerBar, HungerValue, exact?.Hunger, exact?.MaxHunger, player.HungerPercent);
            RenderVital(WaterBar, WaterValue, exact?.Thirst, exact?.MaxThirst, player.ThirstPercent);

            UpdatedLabel.Text = $"SYNC {(snapshot.UpdatedAt ?? DateTimeOffset.Now).ToLocalTime():HH:mm:ss}";
            RequestStatusLabel.Text = activityStatus;
            _guidePlayerOverview = new GuidePlayerOverview(
                FriendlySpecies(player.Class),
                player.Name ?? "ACTIVE PLAYER",
                player.Server ?? _configuredSource,
                NormalizePercent(growth),
                VitalPercent(exact?.Health, exact?.MaxHealth, player.HealthPercent),
                VitalPercent(exact?.Stamina, exact?.MaxStamina, player.StaminaPercent),
                VitalPercent(exact?.Hunger ?? exact?.FoodValue, exact?.MaxHunger ?? exact?.MaxFoodValue, player.HungerPercent),
                VitalPercent(exact?.Thirst, exact?.MaxThirst, player.ThirstPercent),
                snapshot.UpdatedAt ?? DateTimeOffset.Now);
            _guideWindow?.UpdatePlayerOverview(_guidePlayerOverview);

            var effectiveLocation = ResolveEffectiveLocation(
                player.Location,
                player.MapLocation,
                snapshot.Map?.UpdatedAt ?? snapshot.UpdatedAt,
                out var effectiveMapLocation);
            UpdateHeading(player with
            {
                Location = effectiveLocation,
                MapLocation = effectiveMapLocation
            });
            var mapPositionChanged = HasMapPositionChanged(
                _location,
                _mapLocation,
                effectiveLocation,
                effectiveMapLocation);
            _location = effectiveLocation;
            _mapLocation = effectiveMapLocation;
        if (_location is not null)
        {
            var altitude = _location.Z is null ? "—" : $"{_location.Z.Value / 1000d:0.0}";
            CoordinateLabel.Text = $"X {_location.X / 1000d:0.0}  Y {_location.Y / 1000d:0.0}  Z {altitude}";
            PlayerMarker.Visibility = Visibility.Visible;
            if (mapPositionChanged)
            {
                PositionMap();
            }
        }
    }

    private void UpdateHeading(PlayerTelemetry player)
    {
        if (player.ExactMapHeadingDegrees is not null)
        {
            _headingDegrees = MapHeading.Normalize(player.ExactMapHeadingDegrees.Value);
            AnimateHeadingTo(_headingDegrees, LiveHeadingAnimationDuration);
            _hasMovementHeading = true;
            DirectionNeedle.Opacity = 1d;
            HeadingModeLabel.Text = $"{_headingDegrees:000}°";
            _previousLocation = player.Location;
            return;
        }

        UpdateMovementHeading(player.Location);
    }

    private WorldLocation? ResolveEffectiveLocation(
        WorldLocation? networkLocation,
        MapPoint? networkMapLocation,
        DateTimeOffset? networkMapUpdatedAt,
        out MapPoint? effectiveMapLocation)
    {
        if (networkLocation is not null)
        {
            _lastAutoDetectedLocation = networkLocation;
            _lastAutoDetectedMapUpdatedAt = networkMapUpdatedAt;
        }

        if (_clipboardLocation is not null)
        {
            if (_autoDetectLiveMap &&
                AutoDetectLocationPriority.HasNewLocation(
                    _autoDetectLocationAtClipboard,
                    _autoDetectMapUpdatedAtClipboard,
                    networkLocation,
                    networkMapUpdatedAt))
            {
                _clipboardLocation = null;
                _autoDetectLocationAtClipboard = null;
                _autoDetectMapUpdatedAtClipboard = null;
                effectiveMapLocation = networkMapLocation;
                return networkLocation;
            }

            if (_autoDetectLocationAtClipboard is null && networkLocation is not null)
            {
                _autoDetectLocationAtClipboard = networkLocation;
                _autoDetectMapUpdatedAtClipboard = networkMapUpdatedAt;
            }

            effectiveMapLocation = GatewayMapProjection.Project(_clipboardLocation);
            return _clipboardLocation;
        }

        if (_autoDetectLiveMap && networkLocation is not null)
        {
            effectiveMapLocation = networkMapLocation;
            return networkLocation;
        }

        effectiveMapLocation = _mapLocation;
        return _location;
    }

    private bool ApplyClipboardLocation(WorldLocation location)
    {
        // While an interactive map is open, coordinate clipboard data belongs to
        // Ctrl+V route selection. Do not let the background Copy Asset monitor move
        // the player marker to the destination before the paste command is handled.
        if (_largeMapWindow?.IsVisible == true || _guideWindow?.IsMapPageVisible == true)
        {
            return false;
        }

        _clipboardLocation = location;
        _autoDetectLocationAtClipboard = _lastAutoDetectedLocation;
        _autoDetectMapUpdatedAtClipboard = _lastAutoDetectedMapUpdatedAt;
        if (_previousClipboardLocation is not null &&
            MovementHeading.TryCalculate(
                _previousClipboardLocation,
                location,
                out var clipboardHeading,
                minimumDistance: 2_000d))
        {
            _headingDegrees = clipboardHeading;
            AnimateHeadingTo(_headingDegrees, MovementHeadingAnimationDuration);
            _hasMovementHeading = true;
            DirectionNeedle.Opacity = 1d;
            HeadingModeLabel.Text = $"{_headingDegrees:000}°";
        }

        _previousClipboardLocation = location;
        var projectedLocation = GatewayMapProjection.Project(location);
        var mapPositionChanged = HasMapPositionChanged(
            _location,
            _mapLocation,
            location,
            projectedLocation);
        _location = location;
        _mapLocation = projectedLocation;
        var altitude = location.Z is null ? "—" : $"{location.Z.Value / 1000d:0.0}";
        CoordinateLabel.Text = $"X {location.X / 1000d:0.0}  Y {location.Y / 1000d:0.0}  Z {altitude}";
        PlayerMarker.Visibility = Visibility.Visible;
        if (mapPositionChanged)
        {
            PositionMap();
        }

        return true;
    }

    private static bool HasMapPositionChanged(
        WorldLocation? previousLocation,
        MapPoint? previousMapLocation,
        WorldLocation? currentLocation,
        MapPoint? currentMapLocation)
    {
        if (previousLocation is not null && currentLocation is not null)
        {
            return previousLocation.X != currentLocation.X ||
                   previousLocation.Y != currentLocation.Y;
        }

        if (previousMapLocation is not null && currentMapLocation is not null)
        {
            return previousMapLocation.Value.Left != currentMapLocation.Value.Left ||
                   previousMapLocation.Value.Top != currentMapLocation.Value.Top;
        }

        return previousLocation is null != (currentLocation is null) ||
               previousMapLocation is null != (currentMapLocation is null);
    }

    private void UpdateMovementHeading(WorldLocation? current)
    {
        if (current is null)
        {
            _previousLocation = null;
            return;
        }

        if (_previousLocation is not null && MovementHeading.TryCalculate(_previousLocation, current, out var measuredHeading))
        {
            _headingDegrees = _hasMovementHeading
                ? MovementHeading.Smooth(_headingDegrees, measuredHeading, 0.72d)
                : measuredHeading;
            AnimateHeadingTo(_headingDegrees, MovementHeadingAnimationDuration);
            _hasMovementHeading = true;
            DirectionNeedle.Opacity = 1d;
            HeadingModeLabel.Text = $"{_headingDegrees:000}°";
        }
        else if (!_hasMovementHeading)
        {
            HeadingModeLabel.Text = "CHƯA RÕ HƯỚNG";
        }

        _previousLocation = current;
    }

    private void AnimateHeadingTo(double targetDegrees, TimeSpan duration)
    {
        var target = MapHeading.Normalize(targetDegrees);
        var mapTarget = _rotateMap ? MapHeading.MapRotationForHeadingUp(target) : 0d;
        var needleTarget = _rotateMap ? 0d : target;
        if (!_hasMovementHeading)
        {
            MapRotationTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            MapRotationTransform.Angle = mapTarget;
            DrinkingWaterRotationTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            DrinkingWaterRotationTransform.Angle = mapTarget;
            SetSbtcZoneRotation(mapTarget, animate: false, duration: duration);
            SetCompassRotation(mapTarget, animate: false, duration: duration);
            DirectionNeedleRotationTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            DirectionNeedleRotationTransform.Angle = needleTarget;
            PositionMap();
            return;
        }

        AnimateRotationTo(MapRotationTransform, mapTarget, duration);
        AnimateRotationTo(DrinkingWaterRotationTransform, mapTarget, duration);
        SetSbtcZoneRotation(mapTarget, animate: true, duration: duration);
        SetCompassRotation(mapTarget, animate: true, duration: duration);
        AnimateRotationTo(DirectionNeedleRotationTransform, needleTarget, duration);
        PositionMap();
    }

    private void SetCompassRotation(double mapAngle, bool animate, TimeSpan duration)
    {
        var counterAngle = -mapAngle;
        RotateTransform[] counterRotations =
        [
            NorthLabelCounterRotationTransform,
            EastLabelCounterRotationTransform,
            SouthLabelCounterRotationTransform,
            WestLabelCounterRotationTransform
        ];

        if (animate)
        {
            AnimateRotationTo(CompassRingRotationTransform, mapAngle, duration);
            foreach (var rotation in counterRotations)
            {
                AnimateRotationTo(rotation, counterAngle, duration);
            }

            return;
        }

        CompassRingRotationTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        CompassRingRotationTransform.Angle = mapAngle;
        foreach (var rotation in counterRotations)
        {
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            rotation.Angle = counterAngle;
        }
    }

    private void SetSbtcZoneRotation(double mapAngle, bool animate, TimeSpan duration)
    {
        if (animate)
        {
            AnimateRotationTo(SbtcZoneRotationTransform, mapAngle, duration);
            AnimateRotationTo(SbtcZoneDecorationRotationTransform, mapAngle, duration);
            AnimateRotationTo(RouteRotationTransform, mapAngle, duration);
            AnimateRotationTo(SbtcPlayerRotationTransform, mapAngle, duration);
            foreach (var rotation in _sbtcZoneLabelRotations)
            {
                AnimateRotationTo(rotation, -mapAngle, duration);
            }
            foreach (var rotation in _sbtcPlayerLabelRotations)
            {
                AnimateRotationTo(rotation, -mapAngle, duration);
            }
            if (_routeDistanceLabelRotation is not null)
            {
                AnimateRotationTo(_routeDistanceLabelRotation, -mapAngle, duration);
            }

            return;
        }

        SbtcZoneRotationTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        SbtcZoneRotationTransform.Angle = mapAngle;
        SbtcZoneDecorationRotationTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        SbtcZoneDecorationRotationTransform.Angle = mapAngle;
        RouteRotationTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        RouteRotationTransform.Angle = mapAngle;
        SbtcPlayerRotationTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        SbtcPlayerRotationTransform.Angle = mapAngle;
        foreach (var rotation in _sbtcZoneLabelRotations)
        {
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            rotation.Angle = -mapAngle;
        }
        foreach (var rotation in _sbtcPlayerLabelRotations)
        {
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            rotation.Angle = -mapAngle;
        }
        if (_routeDistanceLabelRotation is not null)
        {
            _routeDistanceLabelRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            _routeDistanceLabelRotation.Angle = -mapAngle;
        }
    }

    private static void AnimateRotationTo(RotateTransform transform, double targetAngle, TimeSpan duration)
    {
        var current = transform.Angle;
        var shortestDelta = (targetAngle - current + 540d) % 360d - 180d;
        transform.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation
        {
            From = current,
                To = current + shortestDelta,
            Duration = duration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void RenderVital(System.Windows.Controls.ProgressBar bar, System.Windows.Controls.TextBlock label, double? current, double? maximum, double? fallback)
    {
        if (current is null && maximum is null && fallback is null)
        {
            bar.Value = 0d;
            label.Text = "—";
            return;
        }

        var percent = VitalMath.Percent(current, maximum, fallback);
        bar.Value = percent;
        label.Text = current is not null && maximum is > 0
            ? $"{FormatNumber(current.Value)} / {FormatNumber(maximum.Value)}"
            : $"{percent:0.#}%";
    }

    private void ClearVitals()
    {
        HealthBar.Value = StaminaBar.Value = HungerBar.Value = WaterBar.Value = 0;
        HealthValue.Text = StaminaValue.Text = HungerValue.Text = WaterValue.Text = "— / —";
        GrowthLabel.Text = "—";
        CoordinateLabel.Text = "X —  Y —  Z —";
        UpdatedLabel.Text = "—";
        RequestStatusLabel.Text = string.Empty;
    }

    private void ShowTelemetryUnavailable(string connectionState, string detail)
    {
        _guidePlayerOverview = null;
        _guideWindow?.UpdatePlayerOverview(null);
        SetConnectionState(connectionState, ErrorBrush);
        SpeciesLabel.Text = "TELEMETRY UNAVAILABLE";
        PlayerNameLabel.Text = detail;
        _location = null;
        _mapLocation = null;
        _previousLocation = null;
        _hasMovementHeading = false;
        PlayerMarker.Visibility = Visibility.Collapsed;
        HeadingModeLabel.Text = "CHƯA RÕ HƯỚNG";
        ClearVitals();
    }

    private void ShowNoActiveDinosaur(string connectionState, string detail)
    {
        _guidePlayerOverview = null;
        _guideWindow?.UpdatePlayerOverview(null);
        SetConnectionState(connectionState, WaitingBrush);
        SpeciesLabel.Text = "NO ACTIVE DINOSAUR";
        PlayerNameLabel.Text = detail;
        _location = null;
        _mapLocation = null;
        _previousLocation = null;
        _hasMovementHeading = false;
        PlayerMarker.Visibility = Visibility.Collapsed;
        HeadingModeLabel.Text = "CHƯA RÕ HƯỚNG";
        ClearVitals();
    }

    private string ConnectionText(TelemetrySessionState state) => state switch
    {
        TelemetrySessionState.Live => $"{_configuredSource} · LIVE",
        TelemetrySessionState.Reconnecting => $"{_configuredSource} · RECONNECTING",
        TelemetrySessionState.Stale => $"{_configuredSource} · DATA STALE",
        TelemetrySessionState.Polling => $"{_configuredSource} · FAST POLL",
        _ => $"{_configuredSource} · {state.ToString().ToUpperInvariant()}"
    };

    private void SetTelemetryOpacity(double opacity)
    {
        PlayerMarker.Opacity = opacity;
        HealthBar.Opacity = StaminaBar.Opacity = HungerBar.Opacity = WaterBar.Opacity = opacity;
        HealthValue.Opacity = StaminaValue.Opacity = HungerValue.Opacity = WaterValue.Opacity = opacity;
        GrowthLabel.Opacity = CoordinateLabel.Opacity = opacity;
    }

    private void PositionMap()
    {
        var viewportWidth = MapViewport.ActualWidth;
        var viewportHeight = MapViewport.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        MapShade.Width = viewportWidth;
        MapShade.Height = viewportHeight;

        if (MapImage.Source is not BitmapSource source || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return;
        }

        var coverScale = Math.Max(viewportWidth / source.PixelWidth, viewportHeight / source.PixelHeight);
        var imageWidth = source.PixelWidth * coverScale * _mapZoom;
        var imageHeight = source.PixelHeight * coverScale * _mapZoom;
        MapImage.Width = imageWidth;
        MapImage.Height = imageHeight;
        DrinkingWaterImage.Width = imageWidth;
        DrinkingWaterImage.Height = imageHeight;

        var point = _mapLocation ??
            (_location is null ? new MapPoint(0.5d, 0.5d) : GatewayMapProjection.Project(_location));
        var left = viewportWidth / 2d - point.Left * imageWidth;
        var top = viewportHeight / 2d - point.Top * imageHeight;
        var targetChanged = _lastMapWorldLeft is null || _lastMapWorldTop is null ||
                            _lastMapWorldLeft.Value != left ||
                            _lastMapWorldTop.Value != top;
        var pointChanged = _lastPositionedMapPoint is not null &&
                           (_lastPositionedMapPoint.Value.Left != point.Left ||
                            _lastPositionedMapPoint.Value.Top != point.Top);
        var panStartX = _lastMapWorldLeft is null
            ? 0d
            : _lastMapWorldLeft.Value + MapImagePanTransform.X - left;
        var panStartY = _lastMapWorldTop is null
            ? 0d
            : _lastMapWorldTop.Value + MapImagePanTransform.Y - top;
        Canvas.SetLeft(MapImage, left);
        Canvas.SetTop(MapImage, top);
        MapRotationTransform.CenterX = viewportWidth / 2d - left;
        MapRotationTransform.CenterY = viewportHeight / 2d - top;
        Canvas.SetLeft(DrinkingWaterImage, left);
        Canvas.SetTop(DrinkingWaterImage, top);
        DrinkingWaterRotationTransform.CenterX = viewportWidth / 2d - left;
        DrinkingWaterRotationTransform.CenterY = viewportHeight / 2d - top;

        SbtcZoneLayer.Width = imageWidth;
        SbtcZoneLayer.Height = imageHeight;
        Canvas.SetLeft(SbtcZoneLayer, left);
        Canvas.SetTop(SbtcZoneLayer, top);
        SbtcZoneRotationTransform.CenterX = viewportWidth / 2d - left;
        SbtcZoneRotationTransform.CenterY = viewportHeight / 2d - top;
        SbtcZoneDecorationLayer.Width = imageWidth;
        SbtcZoneDecorationLayer.Height = imageHeight;
        Canvas.SetLeft(SbtcZoneDecorationLayer, left);
        Canvas.SetTop(SbtcZoneDecorationLayer, top);
        SbtcZoneDecorationRotationTransform.CenterX = viewportWidth / 2d - left;
        SbtcZoneDecorationRotationTransform.CenterY = viewportHeight / 2d - top;
        if (_sbtcZoneFeatures.Count > 0 &&
            (Math.Abs(_renderedZoneWidth - imageWidth) > 0.1d ||
             Math.Abs(_renderedZoneHeight - imageHeight) > 0.1d))
        {
            RenderSbtcZoneOverlay(imageWidth, imageHeight);
        }

        RouteLayer.Width = imageWidth;
        RouteLayer.Height = imageHeight;
        Canvas.SetLeft(RouteLayer, left);
        Canvas.SetTop(RouteLayer, top);
        RouteRotationTransform.CenterX = viewportWidth / 2d - left;
        RouteRotationTransform.CenterY = viewportHeight / 2d - top;
        RenderRouteOverlay(imageWidth, imageHeight, point);

        SbtcPlayerLayer.Width = imageWidth;
        SbtcPlayerLayer.Height = imageHeight;
        Canvas.SetLeft(SbtcPlayerLayer, left);
        Canvas.SetTop(SbtcPlayerLayer, top);
        SbtcPlayerRotationTransform.CenterX = viewportWidth / 2d - left;
        SbtcPlayerRotationTransform.CenterY = viewportHeight / 2d - top;
        if (_sbtcPlayerMarkers.Count > 0 &&
            (pointChanged ||
             Math.Abs(_renderedPlayerWidth - imageWidth) > 0.1d ||
             Math.Abs(_renderedPlayerHeight - imageHeight) > 0.1d))
        {
            RenderSbtcPlayerOverlay(imageWidth, imageHeight);
        }

        Canvas.SetLeft(PlayerMarker, viewportWidth / 2d - PlayerMarker.Width / 2d);
        Canvas.SetTop(PlayerMarker, viewportHeight / 2d - PlayerMarker.Height / 2d);

        if (targetChanged)
        {
            if (IsLoaded && pointChanged)
            {
                AnimateMapPan(panStartX, panStartY);
            }
            else
            {
                ResetMapPan();
            }
        }

        _lastMapWorldLeft = left;
        _lastMapWorldTop = top;
        _lastPositionedMapPoint = point;
        _largeMapWindow?.UpdateCurrentLocation(CurrentMapPoint());
        _guideWindow?.UpdateCurrentLocation(CurrentMapPoint());
    }

    private void AnimateMapPan(double offsetX, double offsetY)
    {
        var distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
        var durationMilliseconds = Math.Clamp(
            110d + distance * 0.8d,
            MinimumMapPanDuration.TotalMilliseconds,
            MaximumMapPanDuration.TotalMilliseconds);
        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);

        foreach (var transform in MapPanTransforms())
        {
            AnimatePanAxis(transform, TranslateTransform.XProperty, offsetX, duration);
            AnimatePanAxis(transform, TranslateTransform.YProperty, offsetY, duration);
        }
    }

    private static void AnimatePanAxis(
        TranslateTransform transform,
        DependencyProperty property,
        double from,
        TimeSpan duration)
    {
        transform.BeginAnimation(property, null);
        transform.SetValue(property, from);
        var animation = new DoubleAnimation
        {
            From = from,
            To = 0d,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        transform.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        transform.SetValue(property, 0d);
    }

    private void ResetMapPan()
    {
        foreach (var transform in MapPanTransforms())
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.X = 0d;
            transform.Y = 0d;
        }
    }

    private IEnumerable<TranslateTransform> MapPanTransforms()
    {
        yield return MapImagePanTransform;
        yield return DrinkingWaterPanTransform;
        yield return SbtcZonePanTransform;
        yield return SbtcZoneDecorationPanTransform;
        yield return RoutePanTransform;
        yield return SbtcPlayerPanTransform;
    }

    private void RenderRouteOverlay(double imageWidth, double imageHeight, MapPoint currentLocation)
    {
        RouteLayer.Children.Clear();
        _routeDistanceLabelRotation = null;
        if (_routeDestination is not { } destination)
        {
            RouteLayer.Visibility = Visibility.Collapsed;
            return;
        }

        RouteLayer.Visibility = Visibility.Visible;
        var start = new Point(currentLocation.Left * imageWidth, currentLocation.Top * imageHeight);
        var end = new Point(destination.Left * imageWidth, destination.Top * imageHeight);
        RouteLayer.Children.Add(new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = Brushes.Black,
            StrokeThickness = 7d,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0.72d,
            IsHitTestVisible = false
        });
        var route = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = BrushFrom("#37D4C6"),
            StrokeThickness = 3d,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(55, 212, 198),
                BlurRadius = 9d,
                ShadowDepth = 0d,
                Opacity = 1d
            },
            IsHitTestVisible = false
        };
        RouteLayer.Children.Add(route);

        var target = new Ellipse
        {
            Width = 14d,
            Height = 14d,
            Fill = BrushFrom("#37D4C6"),
            Stroke = Brushes.White,
            StrokeThickness = 2d,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(target, end.X - target.Width / 2d);
        Canvas.SetTop(target, end.Y - target.Height / 2d);
        RouteLayer.Children.Add(target);

        _routeDistanceLabelRotation = new RotateTransform
        {
            Angle = -(_rotateMap ? MapHeading.MapRotationForHeadingUp(_headingDegrees) : 0d)
        };
        var distanceLabel = CreateMapLabel(
            MapOverlayPresentation.Distance(currentLocation, destination),
            BrushFrom("#37D4C6"),
            10d,
            _routeDistanceLabelRotation);
        distanceLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(distanceLabel, end.X - distanceLabel.DesiredSize.Width / 2d);
        Canvas.SetTop(distanceLabel, end.Y - target.Height / 2d - distanceLabel.DesiredSize.Height - 5d);
        RouteLayer.Children.Add(distanceLabel);
    }

    private MapPoint? CurrentMapPoint() =>
        _mapLocation ?? (_location is null ? null : GatewayMapProjection.Project(_location));

    private void ToggleLargeMap()
    {
        var now = Environment.TickCount64;
        if (now - _lastLargeMapToggleTick < 300L)
        {
            return;
        }

        _lastLargeMapToggleTick = now;
        if (_largeMapWindow is not null)
        {
            if (_largeMapWindow.IsVisible)
            {
                _largeMapWindow.Hide();
            }
            else
            {
                _largeMapWindow.UpdateState(
                    CurrentMapPoint(),
                    _routeDestination,
                    _sbtcZoneFeatures,
                    _sbtcPlayerMarkers);
                _largeMapWindow.ShowCentered();
            }

            return;
        }

        var mapWindow = new LargeMapWindow(MapImage.Source) { Owner = this };
        mapWindow.DestinationChanged += LargeMapWindow_DestinationChanged;
        mapWindow.Closed += LargeMapWindow_Closed;
        _largeMapWindow = mapWindow;
        mapWindow.UpdateState(
            CurrentMapPoint(),
            _routeDestination,
            _sbtcZoneFeatures,
            _sbtcPlayerMarkers);
        mapWindow.ShowCentered();
    }

    private void LargeMapWindow_DestinationChanged(MapPoint? destination)
    {
        _routeDestination = destination;
        _guideWindow?.UpdateDestination(destination);
        PositionMap();
    }

    private void LargeMapWindow_Closed(object? sender, EventArgs e)
    {
        if (_largeMapWindow is not null)
        {
            _largeMapWindow.DestinationChanged -= LargeMapWindow_DestinationChanged;
            _largeMapWindow.Closed -= LargeMapWindow_Closed;
            _largeMapWindow = null;
        }
    }

    private void ToggleGuideWindow()
    {
        if (_guideWindow is not null)
        {
            _guideWindow.Close();
            return;
        }

        var guideWindow = new GuideWindow(
            _activeSpeciesName,
            MapImage.Source,
            CurrentMapPoint(),
            _routeDestination,
            _sbtcZoneFeatures,
            _sbtcPlayerMarkers,
            _guidePlayerOverview,
            _garageApi)
        { Owner = this };
        guideWindow.DestinationChanged += GuideWindow_DestinationChanged;
        guideWindow.Closed += GuideWindow_Closed;
        _guideWindow = guideWindow;
        guideWindow.Show();
        guideWindow.Activate();
    }

    private void GuideWindow_Closed(object? sender, EventArgs e)
    {
        if (_guideWindow is not null)
        {
            _guideWindow.DestinationChanged -= GuideWindow_DestinationChanged;
            _guideWindow.Closed -= GuideWindow_Closed;
            _guideWindow = null;
        }
    }

    private void GuideWindow_DestinationChanged(MapPoint? destination)
    {
        _routeDestination = destination;
        _largeMapWindow?.UpdateDestination(destination);
        PositionMap();
    }

    private void UpdateSbtcPlayerData(
        string? serverName,
        IReadOnlyList<MapMarkerTelemetry>? markers)
    {
        var players = SbtcPlayerOverlay.Create(serverName, markers, _usesIslePilotMap);
        var signature = SbtcPlayerOverlay.Signature(players);
        if (string.Equals(signature, _sbtcPlayerSignature, StringComparison.Ordinal))
        {
            return;
        }

        _sbtcPlayerMarkers = players;
        _sbtcPlayerSignature = signature;
        _renderedPlayerWidth = 0d;
        _renderedPlayerHeight = 0d;
        SbtcPlayerLayer.Children.Clear();
        _sbtcPlayerLabelRotations.Clear();
        SbtcPlayerLayer.Visibility = players.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _largeMapWindow?.UpdatePlayers(players);
        _guideWindow?.UpdatePlayers(players);
        if (IsLoaded)
        {
            PositionMap();
        }
    }

    private void RenderSbtcPlayerOverlay(double imageWidth, double imageHeight)
    {
        SbtcPlayerLayer.Children.Clear();
        _sbtcPlayerLabelRotations.Clear();
        _renderedPlayerWidth = imageWidth;
        _renderedPlayerHeight = imageHeight;

        foreach (var player in _sbtcPlayerMarkers)
        {
            var center = new Point(
                player.Location.Left * imageWidth,
                player.Location.Top * imageHeight);
            var color = player.Group ? BrushFrom("#E879F9") : BrushFrom("#34D399");
            FrameworkElement marker;
            if (player.HeadingDegrees is { } heading)
            {
                marker = new Polygon
                {
                    Width = 16d,
                    Height = 16d,
                    Points = [new Point(8d, 0d), new Point(14d, 14d), new Point(8d, 11d), new Point(2d, 14d)],
                    Fill = color,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.2d,
                    Stretch = Stretch.None,
                    RenderTransformOrigin = new Point(0.5d, 0.5d),
                    RenderTransform = new RotateTransform(MapHeading.Normalize(heading)),
                    IsHitTestVisible = false
                };
            }
            else
            {
                marker = new Ellipse
                {
                    Width = 11d,
                    Height = 11d,
                    Fill = color,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.2d,
                    IsHitTestVisible = false
                };
            }

            Canvas.SetLeft(marker, center.X - marker.Width / 2d);
            Canvas.SetTop(marker, center.Y - marker.Height / 2d);
            SbtcPlayerLayer.Children.Add(marker);

            var labelRotation = new RotateTransform
            {
                Angle = -(_rotateMap ? MapHeading.MapRotationForHeadingUp(_headingDegrees) : 0d)
            };
            _sbtcPlayerLabelRotations.Add(labelRotation);
            var label = new Border
            {
                Background = BrushFrom("#D20A1110"),
                BorderBrush = color,
                BorderThickness = new Thickness(1d),
                CornerRadius = new CornerRadius(4d),
                Padding = new Thickness(4d, 1d, 4d, 1d),
                RenderTransformOrigin = new Point(0.5d, 0.5d),
                RenderTransform = labelRotation,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = MapOverlayPresentation.PlayerLabel(
                        player.Label,
                        player.Location,
                        CurrentMapPoint()),
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
                    FontSize = 10d,
                    FontWeight = FontWeights.SemiBold,
                    MaxWidth = 120d,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, center.X - label.DesiredSize.Width / 2d);
            Canvas.SetTop(label, center.Y - marker.Height / 2d - label.DesiredSize.Height - 4d);
            SbtcPlayerLayer.Children.Add(label);
        }
    }

    private static Border CreateMapLabel(
        string text,
        Brush accent,
        double fontSize,
        Transform? transform = null) => new()
    {
        Background = BrushFrom("#E60A1110"),
        BorderBrush = accent,
        BorderThickness = new Thickness(1d),
        CornerRadius = new CornerRadius(4d),
        Padding = new Thickness(4d, 1d, 4d, 1d),
        RenderTransformOrigin = new Point(0.5d, 0.5d),
        RenderTransform = transform,
        IsHitTestVisible = false,
        Child = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold
        }
    };

    private void UpdateSbtcZoneData(
        string? serverName,
        IReadOnlyList<MapPointOfInterestTelemetry>? pointsOfInterest)
    {
        var features = SbtcZoneOverlay.Create(
            serverName,
            pointsOfInterest,
            _bundledSbtcZones,
            _usesIslePilotMap);
        var signature = SbtcZoneOverlay.Signature(features);
        if (string.Equals(signature, _sbtcZoneSignature, StringComparison.Ordinal))
        {
            return;
        }

        _sbtcZoneFeatures = features;
        _sbtcZoneSignature = signature;
        _renderedZoneWidth = 0d;
        _renderedZoneHeight = 0d;
        SbtcZoneLayer.Children.Clear();
        SbtcZoneDecorationLayer.Children.Clear();
        _sbtcZoneLabelRotations.Clear();
        SbtcZoneLayer.Visibility = features.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SbtcZoneDecorationLayer.Visibility = SbtcZoneLayer.Visibility;
        _largeMapWindow?.UpdateZones(features);
        _guideWindow?.UpdateZones(features);
        if (IsLoaded)
        {
            PositionMap();
        }
    }

    private static IReadOnlyList<SbtcZoneFeature> LoadBundledSbtcZones()
    {
        try
        {
            var resource = Application.GetResourceStream(GatewayZonesResourceUri);
            if (resource is null)
            {
                return [];
            }

            using var stream = resource.Stream;
            return GatewayZoneCatalog.Load(stream);
        }
        catch
        {
            return [];
        }
    }

    private void RenderSbtcZoneOverlay(double imageWidth, double imageHeight)
    {
        SbtcZoneLayer.Children.Clear();
        SbtcZoneDecorationLayer.Children.Clear();
        _sbtcZoneLabelRotations.Clear();
        _renderedZoneWidth = imageWidth;
        _renderedZoneHeight = imageHeight;

        foreach (var feature in _sbtcZoneFeatures)
        {
            var points = new PointCollection(feature.Points.Select(point =>
                new Point(point.Left * imageWidth, point.Top * imageHeight)));
            var stroke = ZoneStroke(feature);
            var fill = ZoneFill(feature);
            var isPolygon = string.Equals(feature.Shape, "polygon", StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrWhiteSpace(feature.Shape) && points.Count >= 3;

            var hasOfficialIcon = TryGetSbtcZoneIcon(feature.Icon, out var iconSource);
            if (isPolygon && points.Count >= 3)
            {
                SbtcZoneLayer.Children.Add(new Polygon
                {
                    Points = points,
                    Stroke = stroke,
                    Fill = fill,
                    StrokeThickness = 1.6d,
                    StrokeLineJoin = PenLineJoin.Round,
                    IsHitTestVisible = false
                });
            }
            else if (!hasOfficialIcon)
            {
                var center = new Point(
                    points.Average(point => point.X),
                    points.Average(point => point.Y));
                var radius = Math.Max(
                    3d,
                    (feature.Size ?? 0.008d) * imageWidth);
                var marker = new Ellipse
                {
                    Width = radius * 2d,
                    Height = radius * 2d,
                    Fill = fill,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.2d,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(marker, center.X - radius);
                Canvas.SetTop(marker, center.Y - radius);
                SbtcZoneLayer.Children.Add(marker);
            }

            if (hasOfficialIcon)
            {
                var center = new Point(
                    points.Average(point => point.X),
                    points.Average(point => point.Y));
                var radius = Math.Max(5d, (feature.Size ?? 0.02d) * imageWidth);
                var icon = new System.Windows.Controls.Image
                {
                    Width = radius * 2d,
                    Height = radius * 2d,
                    Source = iconSource,
                    Stretch = Stretch.Uniform,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(icon, center.X - radius);
                Canvas.SetTop(icon, center.Y - radius);
                SbtcZoneDecorationLayer.Children.Add(icon);
            }

        }

        foreach (var zoneLabel in SbtcZoneOverlay.CreateLabels(_sbtcZoneFeatures))
        {
            var center = new Point(
                zoneLabel.Center.Left * imageWidth,
                zoneLabel.Center.Top * imageHeight);
            var stroke = TryBrushFrom(zoneLabel.Color, 0xFF) ?? ZoneStroke(zoneLabel.Kind);
            var labelRotation = new RotateTransform
            {
                Angle = -(_rotateMap ? MapHeading.MapRotationForHeadingUp(_headingDegrees) : 0d)
            };
            _sbtcZoneLabelRotations.Add(labelRotation);
            var label = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = stroke,
                BorderThickness = new Thickness(0d, 0d, 0d, 1d),
                CornerRadius = new CornerRadius(3d),
                Padding = new Thickness(4d, 1d, 4d, 1d),
                RenderTransformOrigin = new Point(0.5d, 0.5d),
                RenderTransform = labelRotation,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = zoneLabel.Name,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
                    FontSize = 9.5d,
                    FontWeight = FontWeights.SemiBold
                }
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var isPolygon = string.Equals(zoneLabel.Shape, "polygon", StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrWhiteSpace(zoneLabel.Shape);
            var labelCenterY = isPolygon
                ? center.Y
                : center.Y - Math.Max(3d, (zoneLabel.Size ?? 0.008d) * imageWidth) - 10d;
            Canvas.SetLeft(label, center.X - label.DesiredSize.Width / 2d);
            Canvas.SetTop(label, labelCenterY - label.DesiredSize.Height / 2d);
            SbtcZoneDecorationLayer.Children.Add(label);
        }
    }

    private static Brush ZoneStroke(SbtcZoneKind kind) => kind switch
    {
        SbtcZoneKind.Sanctuary => BrushFrom("#42DDB1"),
        SbtcZoneKind.Migration => BrushFrom("#FFA91F"),
        SbtcZoneKind.Patrol => BrushFrom("#A97BF3"),
        SbtcZoneKind.Location => BrushFrom("#E6ECF2"),
        _ => BrushFrom("#B8A8D8")
    };

    private static Brush ZoneStroke(SbtcZoneFeature feature) =>
        TryBrushFrom(feature.Color, 0xFF) ?? ZoneStroke(feature.Kind);

    private static Brush ZoneFill(SbtcZoneKind kind) => kind switch
    {
        SbtcZoneKind.Sanctuary => BrushFrom("#2842DDB1"),
        SbtcZoneKind.Migration => BrushFrom("#2EFFA91F"),
        SbtcZoneKind.Patrol => BrushFrom("#28A97BF3"),
        SbtcZoneKind.Location => BrushFrom("#18E6ECF2"),
        _ => BrushFrom("#20B8A8D8")
    };

    private static Brush ZoneFill(SbtcZoneFeature feature)
    {
        var alpha = string.Equals(feature.Shape, "polygon", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(feature.Shape) && feature.Points.Count >= 3
            ? (byte)0x4D
            : (byte)0x8C;
        return TryBrushFrom(feature.Color, alpha) ?? ZoneFill(feature.Kind);
    }

    private static Brush? TryBrushFrom(string? color, byte alpha)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(color);
            parsed.A = alpha;
            var brush = new SolidColorBrush(parsed);
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private bool TryGetSbtcZoneIcon(string? iconPath, out ImageSource? source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(iconPath) ||
            !iconPath.StartsWith("/maps/icons/", StringComparison.Ordinal) ||
            iconPath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (_sbtcZoneIconSources.TryGetValue(iconPath, out var cachedSource))
        {
            source = cachedSource;
            return true;
        }

        if (BundledSbtcZoneIcons.TryGetValue(iconPath, out var resourceUri))
        {
            var resource = Application.GetResourceStream(resourceUri);
            if (resource is not null)
            {
                using var stream = resource.Stream;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                _sbtcZoneIconSources[iconPath] = bitmap;
                source = bitmap;
                return true;
            }
        }

        if (_sbtcZoneIconLoads.Add(iconPath))
        {
            _ = LoadSbtcZoneIconAsync(iconPath);
        }

        return false;
    }

    private async Task LoadSbtcZoneIconAsync(string iconPath)
    {
        try
        {
            var uri = new Uri(new Uri("https://islepilot.eu/"), iconPath);
            var bytes = await _httpClient.GetByteArrayAsync(uri, _shutdown.Token);
            using var stream = new System.IO.MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();

            _sbtcZoneIconSources[iconPath] = ZoneIconTransparency.CutEdgeBackground(bitmap);
            _renderedZoneWidth = 0d;
            _renderedZoneHeight = 0d;
            PositionMap();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // Keep the vector fallback for this render. A later zone refresh can
            // request the icon again when the endpoint or decoder recovers.
        }
        finally
        {
            _sbtcZoneIconLoads.Remove(iconPath);
        }
    }

    private void SetConnectionState(string text, Brush brush)
    {
        ConnectionLabel.Text = text;
        ConnectionDot.Fill = brush;
    }

    private static double NormalizePercent(double? value)
    {
        if (value is null) return 0d;
        return Math.Clamp(value.Value is >= 0d and <= 1d ? value.Value * 100d : value.Value, 0d, 100d);
    }

    private static double VitalPercent(double? current, double? maximum, double? fallback)
    {
        if (current is { } currentValue && maximum is > 0d)
        {
            return Math.Clamp(currentValue / maximum.Value * 100d, 0d, 100d);
        }

        return NormalizePercent(fallback);
    }

    private static string FriendlySpecies(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return "ACTIVE DINOSAUR";
        var value = className.Replace("BP_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_C", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Character", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('_', ' ');
        return value.Replace('_', ' ').ToUpperInvariant();
    }

    private static string FormatNumber(double value) => Math.Abs(value) >= 100d ? value.ToString("0") : value.ToString("0.#");

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private void MapViewport_SizeChanged(object sender, SizeChangedEventArgs e) => PositionMap();

    private void DragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Button)
        {
            DragMove();
            SaveOverlayLayout();
        }
    }

    private void OverlayScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || Math.Abs(e.NewValue / 100d - _overlayScale) < 0.001d)
        {
            return;
        }

        ApplyOverlayScale(e.NewValue / 100d, persist: IsLoaded);
    }

    private void MapZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || Math.Abs(e.NewValue / 100d - _mapZoom) < 0.001d)
        {
            return;
        }

        ApplyMapZoom(e.NewValue / 100d, persist: IsLoaded);
    }

    private void ZoomInMap() =>
        ApplyMapZoom(_mapZoom + MapZoomShortcutStep, persist: true);

    private void ZoomOutMap() =>
        ApplyMapZoom(_mapZoom - MapZoomShortcutStep, persist: true);

    private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyOverlayScale(OverlayLayoutRules.DefaultScale, persist: false);
        ApplyMapZoom(OverlayLayoutRules.DefaultMapZoom, persist: false);
        ApplyMapStyle(OverlayLayoutRules.DefaultMapStyle, persist: false);
        _showMap = true;
        _showActivity = true;
        _showPrimeTasks = false;
        RenderPrimeTasks(null);
        ApplyMapRotationPreference(rotateMap: true, persist: false);
        ApplyAutoDetectLiveMapPreference(autoDetectLiveMap: true, persist: false);
        ApplyPanelVisibility(persist: false);
        SaveOverlayLayout();
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e) => CloseSettings();

    private void CircleStyleButton_Click(object sender, RoutedEventArgs e) =>
        ApplyMapStyle(OverlayLayoutRules.DefaultMapStyle, persist: true);

    private void SquareStyleButton_Click(object sender, RoutedEventArgs e) =>
        ApplyMapStyle(OverlayLayoutRules.SquareMapStyle, persist: true);

    private void MapVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_showMap && !_showActivity)
        {
            return;
        }

        _showMap = !_showMap;
        ApplyPanelVisibility(persist: true);
    }

    private void ActivityVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_showActivity && !_showMap)
        {
            return;
        }

        _showActivity = !_showActivity;
        ApplyPanelVisibility(persist: true);
    }

    private void PrimeTasksVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        _showPrimeTasks = !_showPrimeTasks;
        ApplyPanelVisibility(persist: true);
    }

    private void RenderPrimeTasks(PrimeTelemetry? prime)
    {
        var quests = prime?.Quests ?? [];
        if (quests.Count == 0)
        {
            PrimeTasksList.ItemsSource = new[]
            {
                new
                {
                    Marker = "◇",
                    Text = "Đang chờ dữ liệu Prime từ server",
                    MarkerBrush = WaitingBrush,
                    TextBrush = BrushFrom("#91A39D")
                }
            };
            PrimeTasksProgressLabel.Text = prime?.Done is { } done
                ? $"{done} / {prime.Required ?? 5}"
                : "— / 5";
            return;
        }

        PrimeTasksList.ItemsSource = quests.Select(quest =>
        {
            var display = PrimeQuestPresentation.Create(quest);
            return new
            {
                Marker = display.Completed ? "✓" : "◇",
                Text = display.Text,
                MarkerBrush = display.Completed ? OnlineBrush : WaitingBrush,
                TextBrush = display.Completed ? BrushFrom("#9CB7AE") : BrushFrom("#E1E8E3")
            };
        }).ToArray();
        var completed = prime?.Done ?? quests.Count(quest => quest.Done == true);
        PrimeTasksProgressLabel.Text = $"{completed} / {prime?.Required ?? 5}";
    }

    private void MapRotationButton_Click(object sender, RoutedEventArgs e) =>
        ApplyMapRotationPreference(!_rotateMap, persist: true);

    private void AutoDetectLiveMapButton_Click(object sender, RoutedEventArgs e) =>
        ApplyAutoDetectLiveMapPreference(!_autoDetectLiveMap, persist: true);

    private void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        _resizeStartingScale = _overlayScale;
        _resizeStartingScreenPoint = PointToScreen(Mouse.GetPosition(this));
        _resizingOverlay = true;
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_resizingOverlay)
        {
            return;
        }

        var current = PointToScreen(Mouse.GetPosition(this));
        var dpiScale = Math.Max(0.01d, VisualTreeHelper.GetDpi(this).DpiScaleX);
        var deltaDip = (current.X - _resizeStartingScreenPoint.X) / dpiScale;
        ApplyOverlayScale(
            OverlayLayoutRules.ScaleFromHorizontalDrag(_resizeStartingScale, deltaDip),
            persist: false);
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _resizingOverlay = false;
        SaveOverlayLayout();
    }

    private void FinishOverlayResize()
    {
        if (!_resizingOverlay)
        {
            return;
        }

        _resizingOverlay = false;
        if (ResizeGrip.IsDragging)
        {
            ResizeGrip.CancelDrag();
        }
        SaveOverlayLayout();
    }

    private void ApplyOverlayScale(double scale, bool persist)
    {
        _overlayScale = OverlayLayoutRules.NormalizeScale(scale);
        var dpiScale = IsInitialized
            ? Math.Max(0.01d, VisualTreeHelper.GetDpi(this).DpiScaleX)
            : 1d;
        var pixelAlignedScale = OverlayLayoutRules.PixelAlignedScale(_overlayScale, dpiScale);
        _effectiveOverlayScale = pixelAlignedScale;
        OverlayScaleTransform.ScaleX = pixelAlignedScale;
        OverlayScaleTransform.ScaleY = pixelAlignedScale;
        TextOptions.SetTextRenderingMode(OverlayScaleRoot, TextRenderingMode.Grayscale);
        RenderOptions.SetBitmapScalingMode(
            MapImage,
            pixelAlignedScale < 1d ? BitmapScalingMode.Linear : BitmapScalingMode.HighQuality);
        ApplyReadabilityScales();
        OverlayScaleLabel.Text = OverlayLayoutRules.FormatScale(_overlayScale);
        if (Math.Abs(OverlayScaleSlider.Value - _overlayScale * 100d) > 0.01d)
        {
            OverlayScaleSlider.Value = _overlayScale * 100d;
        }
        OverlayScaleRoot.InvalidateMeasure();
        InvalidateMeasure();

        if (IsLoaded)
        {
            UpdateLayout();
            KeepOverlayVisible();
            PositionMap();
        }

        if (persist)
        {
            SaveOverlayLayout();
        }
    }

    private void ApplyMapZoom(double zoom, bool persist)
    {
        _mapZoom = OverlayLayoutRules.NormalizeMapZoom(zoom);
        MapZoomLabel.Text = OverlayLayoutRules.FormatMapZoom(_mapZoom);
        if (Math.Abs(MapZoomSlider.Value - _mapZoom * 100d) > 0.01d)
        {
            MapZoomSlider.Value = _mapZoom * 100d;
        }

        if (IsLoaded)
        {
            PositionMap();
        }

        if (persist)
        {
            SaveOverlayLayout();
        }
    }

    private void ApplyMapStyle(string? style, bool persist)
    {
        _mapStyle = OverlayLayoutRules.NormalizeMapStyle(style);
        var isCircle = string.Equals(
            _mapStyle,
            OverlayLayoutRules.DefaultMapStyle,
            StringComparison.Ordinal);

        CircularMapOuterFrame.Visibility = isCircle ? Visibility.Visible : Visibility.Collapsed;
        CircularMapInnerFrame.Visibility = isCircle ? Visibility.Visible : Visibility.Collapsed;
        SquareMapFrame.Visibility = isCircle ? Visibility.Collapsed : Visibility.Visible;
        MapViewport.Clip = isCircle
            ? new EllipseGeometry(new Point(141d, 141d), 141d, 141d)
            : new RectangleGeometry(new Rect(0d, 0d, 282d, 282d), 4d, 4d);
        CircleStyleButton.Background = isCircle ? BrushFrom("#3A1D514B") : Brushes.Transparent;
        SquareStyleButton.Background = isCircle ? Brushes.Transparent : BrushFrom("#3A1D514B");

        if (persist)
        {
            SaveOverlayLayout();
        }
    }

    private void ApplyMapRotationPreference(bool rotateMap, bool persist)
    {
        _rotateMap = rotateMap;
        MapRotationButton.Content = _rotateMap ? "ROTATE · ON" : "ROTATE · OFF";
        MapRotationButton.Background = _rotateMap ? BrushFrom("#3A1D514B") : Brushes.Transparent;
        AnimateHeadingTo(_headingDegrees, LiveHeadingAnimationDuration);

        if (persist)
        {
            SaveOverlayLayout();
        }
    }

    private void ApplyAutoDetectLiveMapPreference(bool autoDetectLiveMap, bool persist)
    {
        _autoDetectLiveMap = autoDetectLiveMap;
        AutoDetectLiveMapButton.Content = _autoDetectLiveMap ? "LIVE MAP · ON" : "LIVE MAP · OFF";
        AutoDetectLiveMapButton.Background = _autoDetectLiveMap ? BrushFrom("#3A3B4A21") : Brushes.Transparent;

        if (persist)
        {
            SaveOverlayLayout();
        }
    }

    private void ApplyPanelVisibility(bool persist)
    {
        if (!_showMap && !_showActivity)
        {
            _showMap = true;
        }

        MapPanel.Visibility = _showMap ? Visibility.Visible : Visibility.Collapsed;
        ActivityPanel.Visibility = _showActivity ? Visibility.Visible : Visibility.Collapsed;
        MapActivitySpacer.Visibility = _showMap && _showActivity
            ? Visibility.Visible
            : Visibility.Collapsed;
        MapVisibilityButton.Content = _showMap ? "MAP · ON" : "MAP · OFF";
        ActivityVisibilityButton.Content = _showActivity ? "ACTIVITY · ON" : "ACTIVITY · OFF";
        MapVisibilityButton.Background = _showMap ? BrushFrom("#3A1D514B") : Brushes.Transparent;
        ActivityVisibilityButton.Background = _showActivity ? BrushFrom("#3A1D514B") : Brushes.Transparent;
        PrimeTasksPanel.Visibility = _showPrimeTasks ? Visibility.Visible : Visibility.Collapsed;
        PrimeTasksVisibilityButton.Content = _showPrimeTasks ? "PRIME · ON" : "PRIME · OFF";
        PrimeTasksVisibilityButton.Background = _showPrimeTasks ? BrushFrom("#3A3B4A21") : Brushes.Transparent;

        if (_showMap && IsLoaded)
        {
            Dispatcher.BeginInvoke(PositionMap, DispatcherPriority.Loaded);
        }

        if (IsLoaded)
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    RefreshWindowSizeToContent();
                    KeepOverlayVisible();
                },
                DispatcherPriority.Loaded);
        }

        if (persist)
        {
            SaveOverlayLayout();
        }
    }

    private void Window_DpiChanged(object sender, DpiChangedEventArgs e) =>
        ApplyOverlayScale(_overlayScale, persist: false);

    private void RestoreOverlayPosition()
    {
        UpdateLayout();
        var primaryWorkArea = SystemParameters.WorkArea;
        Left = _layoutSettings.Left
            ?? Math.Max(primaryWorkArea.Left, primaryWorkArea.Right - ActualWidth - 24d);
        Top = _layoutSettings.Top ?? primaryWorkArea.Top + 70d;
        KeepOverlayVisible();
    }

    private void KeepOverlayVisible()
    {
        const double minimumVisible = 80d;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;
        if (virtualWidth <= 0d || virtualHeight <= 0d)
        {
            return;
        }

        var width = Math.Max(1d, ActualWidth);
        var height = Math.Max(1d, ActualHeight);
        Left = KeepCoordinateVisible(
            Left,
            width,
            virtualLeft,
            virtualWidth,
            minimumVisible);
        Top = KeepCoordinateVisible(
            Top,
            height,
            virtualTop,
            virtualHeight,
            minimumVisible);
    }

    private static double KeepCoordinateVisible(
        double coordinate,
        double windowSize,
        double virtualStart,
        double virtualSize,
        double minimumVisible)
    {
        if (!double.IsFinite(coordinate))
        {
            return virtualStart;
        }

        if (windowSize <= virtualSize)
        {
            return Math.Clamp(coordinate, virtualStart, virtualStart + virtualSize - windowSize);
        }

        var visible = Math.Min(minimumVisible, virtualSize);
        return Math.Clamp(
            coordinate,
            virtualStart - windowSize + visible,
            virtualStart + virtualSize - visible);
    }

    private void SaveOverlayLayout()
    {
        KeepOverlayVisible();
        _layoutSettings = OverlayLayoutRules.Normalize(_layoutSettings with
        {
            Scale = _overlayScale,
            MapZoom = _mapZoom,
            MapStyle = _mapStyle,
            ShowMap = _showMap,
            ShowActivity = _showActivity,
            RotateMap = _rotateMap,
            AutoDetectLiveMap = _autoDetectLiveMap,
            ShowPrimeTasks = _showPrimeTasks,
            Left = Left,
            Top = Top
        });
        _layoutSettingsStore.Save(_layoutSettings);
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        var home = new HomeWindow();
        Application.Current.MainWindow = home;
        home.Show();
        Close();
    }

    private void SetClickThrough(bool enabled)
    {
        if (enabled)
        {
            FinishOverlayResize();
        }

        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        style = enabled ? style | WsExTransparent | WsExNoActivate : style & ~(WsExTransparent | WsExNoActivate);
        _clickThrough = enabled;
        OverlayScaleRoot.IsHitTestVisible = !enabled;
        SetWindowLong(handle, GwlExStyle, style);
        if (!enabled)
        {
            Activate();
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                RefreshWindowSizeToContent();
                KeepOverlayVisible();
                PositionMap();
            },
            DispatcherPriority.Loaded);
    }

    private void ToggleSettings()
    {
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            CloseSettings();
            return;
        }

        SetClickThrough(false);
        SettingsPanel.Visibility = Visibility.Visible;
        ApplyReadabilityScales();
        Dispatcher.BeginInvoke(RefreshWindowSizeToContent, DispatcherPriority.Loaded);
    }

    private void CloseSettings()
    {
        FinishOverlayResize();
        SettingsPanel.Visibility = Visibility.Collapsed;
        ApplyReadabilityScales();
        SaveOverlayLayout();
        SetClickThrough(true);
    }

    private void ApplyReadabilityScales()
    {
        var hudReadabilityScale = _effectiveOverlayScale < 1d
            ? Math.Min(1d / _effectiveOverlayScale, 1.18d)
            : 1d;
        ActivityReadabilityScaleTransform.ScaleX = hudReadabilityScale;
        ActivityReadabilityScaleTransform.ScaleY = hudReadabilityScale;
        PrimeTasksReadabilityScaleTransform.ScaleX = hudReadabilityScale;
        PrimeTasksReadabilityScaleTransform.ScaleY = hudReadabilityScale;

        var compensate = SettingsPanel.Visibility == Visibility.Visible && _effectiveOverlayScale < 1d;
        var inverseScale = compensate ? 1d / _effectiveOverlayScale : 1d;
        SettingsReadabilityScaleTransform.ScaleX = inverseScale;
        SettingsReadabilityScaleTransform.ScaleY = inverseScale;
        OverlayScaleRoot.Width = compensate
            ? OverlayLayoutRules.BaseWidth / _effectiveOverlayScale
            : OverlayLayoutRules.BaseWidth;
    }

    private void RefreshWindowSizeToContent()
    {
        // WPF can retain the smaller click-through window bounds after controls
        // transition from Collapsed to Visible under a LayoutTransform. Toggling
        // the sizing mode forces the native HWND to match the newly measured HUD.
        SizeToContent = System.Windows.SizeToContent.Manual;
        SizeToContent = System.Windows.SizeToContent.WidthAndHeight;
        OverlayScaleRoot.InvalidateMeasure();
        InvalidateMeasure();
        UpdateLayout();
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcHitTest && _clickThrough)
        {
            handled = true;
            return new IntPtr(HtTransparent);
        }

        if (message == WmHotkey && wParam.ToInt32() == SettingsHotkeyId)
        {
            ToggleSettings();
            handled = true;
        }
        else if (message == WmHotkey && wParam.ToInt32() == ZoomInHotkeyId)
        {
            ZoomInMap();
            handled = true;
        }
        else if (message == WmHotkey && wParam.ToInt32() == ZoomOutHotkeyId)
        {
            ZoomOutMap();
            handled = true;
        }
        else if (message == WmHotkey && wParam.ToInt32() == LargeMapHotkeyId)
        {
            ToggleLargeMap();
            handled = true;
        }
        else if (message == WmHotkey && wParam.ToInt32() == GuideHotkeyId)
        {
            ToggleGuideWindow();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _largeMapWindow?.ClosePermanently();
        _guideWindow?.Close();
        _clipboardCoordinateMonitor?.Dispose();
        SaveOverlayLayout();
        _shutdown.Cancel();
        if (_telemetryWatchTask is not null)
        {
            await _telemetryWatchTask;
        }

        if (_telemetrySession is not null)
        {
            await _telemetrySession.DisposeAsync();
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (_settingsHotkeyRegistered) UnregisterHotKey(handle, SettingsHotkeyId);
        if (_zoomInHotkeyRegistered) UnregisterHotKey(handle, ZoomInHotkeyId);
        if (_zoomOutHotkeyRegistered) UnregisterHotKey(handle, ZoomOutHotkeyId);
        if (_largeMapHotkeyRegistered) UnregisterHotKey(handle, LargeMapHotkeyId);
        if (_guideHotkeyRegistered) UnregisterHotKey(handle, GuideHotkeyId);
        _windowSource?.RemoveHook(WindowMessageHook);
        _httpClient.Dispose();
        _shutdown.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);
}
