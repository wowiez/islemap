using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TheIsleOverlay.EraGaming;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.Pandora;

namespace TheIsleOverlay.App;

public partial class HomeWindow : Window
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly GitHubUpdateService _updateService = new();
    private bool _connecting;
    private bool _updateOperationActive;
    private string? _availableUpdateVersion;

    public HomeWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {

        if (string.Equals(
                Environment.GetEnvironmentVariable("ISLELIVEMAP_DEV_AUTO_CONNECT"),
                "1",
                StringComparison.Ordinal))
        {
            var overlay = new MainWindow();
            Application.Current.MainWindow = overlay;
            overlay.Show();
            Close();
            return;
        }

        VersionStatusLabel.Text = $"ISLE LIVE MAP · v{CurrentVersion()} · ĐANG KIỂM TRA UPDATE";
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var result = await _updateService.CheckForUpdateAsync(_shutdown.Token);

            switch (result.State)
            {
                case UpdateCheckState.Available:
                    _availableUpdateVersion = result.Version;
                    ShowUpdateNeeded();
                    var prompt = new UpdateAvailableWindow(CurrentVersion(), result.Version ?? "bản mới")
                    {
                        Owner = this
                    };
                    if (prompt.ShowDialog() == true)
                    {
                        await DownloadAndApplyUpdateAsync();
                    }
                    break;
                case UpdateCheckState.DevelopmentBuild:
                    VersionStatusLabel.Text = $"ISLE LIVE MAP · v{CurrentVersion()} · PORTABLE/DEV";
                    break;
                case UpdateCheckState.Unavailable:
                    VersionStatusLabel.Text = $"ISLE LIVE MAP · v{CurrentVersion()} · KHÔNG CHECK ĐƯỢC UPDATE";
                    break;
                default:
                    VersionStatusLabel.Text = $"ISLE LIVE MAP · v{CurrentVersion()} · MỚI NHẤT";
                    break;
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private async void ApplyUpdateButton_Click(object sender, RoutedEventArgs e) =>
        await DownloadAndApplyUpdateAsync();

    private void ShowUpdateNeeded()
    {
        var version = string.IsNullOrWhiteSpace(_availableUpdateVersion)
            ? "BẢN MỚI"
            : $"v{_availableUpdateVersion}";
        VersionStatusLabel.Text = $"ISLE LIVE MAP · v{CurrentVersion()} · CẦN UPDATE {version}";
        ApplyUpdateButton.Content = "UPDATE";
        ApplyUpdateButton.IsEnabled = true;
        ApplyUpdateButton.Visibility = Visibility.Visible;
    }

    private async Task DownloadAndApplyUpdateAsync()
    {
        if (_updateOperationActive)
        {
            return;
        }

        _updateOperationActive = true;
        ApplyUpdateButton.IsEnabled = false;
        ApplyUpdateButton.Content = "ĐANG TẢI…";
        try
        {
            var downloaded = await _updateService.DownloadPendingUpdateAsync(
                progress => Dispatcher.Invoke(() =>
                    VersionStatusLabel.Text = $"ISLE LIVE MAP · ĐANG TẢI UPDATE {progress}%"),
                _shutdown.Token);
            if (!downloaded)
            {
                ShowUpdateNeeded();
                return;
            }

            VersionStatusLabel.Text = "ĐANG CHUẨN BỊ KHỞI ĐỘNG LẠI…";
            if (_updateService.ScheduleApplyAndRestart())
            {
                Application.Current.Shutdown();
                return;
            }

            ShowUpdateNeeded();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            _updateOperationActive = false;
        }
    }

    private void SourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_connecting || sender is not Button { Tag: string sourceId })
        {
            return;
        }

        var source = TelemetrySourceDefinition.FromId(sourceId);
        if (source is null)
        {
            return;
        }

        _connecting = true;
        SetSourceButtonsEnabled(false);
        SourceStatusLabel.Text = $"ĐANG MỞ PHIÊN {source.DisplayName.ToUpperInvariant()}…";

        try
        {
            var loginWindow = new LoginWindow(source, (cookie, token) => ValidateSessionAsync(source, cookie, token))
            {
                Owner = this
            };
            if (loginWindow.ShowDialog() != true || string.IsNullOrWhiteSpace(loginWindow.CookieValue))
            {
                SourceStatusLabel.Text = "Chưa nhận được phiên. Đăng nhập trong cửa sổ vừa mở rồi bấm KIỂM TRA PHIÊN.";
                return;
            }

            var overlay = new MainWindow(source, loginWindow.CookieValue);
            Application.Current.MainWindow = overlay;
            overlay.Show();
            Close();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SourceStatusLabel.Text = $"Không kết nối được: {FriendlyError(exception)}";
        }
        finally
        {
            _connecting = false;
            SetSourceButtonsEnabled(true);
        }
    }

    private static async Task<LoginSessionValidationState> ValidateSessionAsync(
        TelemetrySourceDefinition source,
        string cookie,
        CancellationToken cancellationToken)
    {
        try
        {
            using var validationClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var provider = source.CreateProvider(validationClient, cookie);
            var snapshot = await provider.GetSnapshotAsync(cancellationToken);
            return snapshot.Success
                ? LoginSessionValidationState.Valid
                : LoginSessionValidationState.Invalid;
        }
        catch (EraGamingAuthenticationException)
        {
            return LoginSessionValidationState.Invalid;
        }
        catch (IslePilotAuthenticationException)
        {
            return LoginSessionValidationState.Invalid;
        }
        catch (PandoraAuthenticationException)
        {
            return LoginSessionValidationState.Invalid;
        }
        catch
        {
            // A slow or temporarily unavailable API must not destroy a valid
            // browser session. The overlay will keep retrying telemetry.
            return LoginSessionValidationState.Unavailable;
        }
    }

    private void SetSourceButtonsEnabled(bool enabled)
    {
        EraSourceButton.IsEnabled = enabled;
        PandoraSourceButton.IsEnabled = enabled;
    }

    private static string FriendlyError(Exception exception) => exception switch
    {
        Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException => "Máy chưa có Microsoft Edge WebView2 Runtime.",
        HttpRequestException => "website/API không phản hồi.",
        _ => exception.Message
    };

    private static string CurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
