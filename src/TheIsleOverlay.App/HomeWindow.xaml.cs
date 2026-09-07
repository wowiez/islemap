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
            var result = await _updateService.PrepareUpdateAsync(
                progress => Dispatcher.Invoke(() =>
                    VersionStatusLabel.Text = $"ISLE LIVE MAP · ĐANG TẢI BẢN MỚI {progress}%"),
                _shutdown.Token);

            switch (result.State)
            {
                case UpdatePreparationState.Ready:
                    VersionStatusLabel.Text = $"BẢN {result.Version} ĐÃ TẢI XONG";
                    ApplyUpdateButton.Visibility = Visibility.Visible;
                    break;
                case UpdatePreparationState.DevelopmentBuild:
                    VersionStatusLabel.Text = $"ISLE LIVE MAP · v{CurrentVersion()} · PORTABLE/DEV";
                    break;
                case UpdatePreparationState.Unavailable:
                    VersionStatusLabel.Text = $"ISLE LIVE MAP · v{CurrentVersion()} · UPDATE OFFLINE";
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

    private void ApplyUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyUpdateButton.IsEnabled = false;
        VersionStatusLabel.Text = "ĐANG CHUẨN BỊ KHỞI ĐỘNG LẠI…";
        if (_updateService.ScheduleApplyAndRestart())
        {
            Application.Current.Shutdown();
            return;
        }

        ApplyUpdateButton.IsEnabled = true;
        VersionStatusLabel.Text = $"ISLE LIVE MAP · v{CurrentVersion()} · UPDATE OFFLINE";
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
