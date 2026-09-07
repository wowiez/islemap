using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

public partial class IslePilotSteamLoginWindow : Window
{
    private static readonly Lazy<Task<CoreWebView2Environment>> SharedEnvironment = new(
        CreateSharedEnvironmentAsync,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private bool _completed;
    private bool _resettingAccount;

    public IslePilotSteamLoginWindow()
    {
        InitializeComponent();
    }

    public IslePilotOverlayAuthResult? Credentials { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await SharedEnvironment.Value;
            await LoginBrowser.EnsureCoreWebView2Async(environment);

            LoginBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            LoginBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            LoginBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            LoginBrowser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            LoginBrowser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            LoginBrowser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            LoginBrowser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            LoginBrowser.CoreWebView2.NavigationStarting += Browser_NavigationStarting;
            LoginBrowser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
            LoginBrowser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;
            await ClearSteamSessionAsync();
            NavigateToLogin();
        }
        catch (Exception exception)
        {
            BrowserLoadingPanel.Visibility = Visibility.Visible;
            LoginStatusLabel.Text = $"Không mở được đăng nhập Steam: {FriendlyMessage(exception)}";
        }
    }

    private void Browser_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (TryCompleteFromCallback(e.Uri))
        {
            e.Cancel = true;
            return;
        }

        if (!IslePilotOverlayLoginNavigationPolicy.IsAllowed(e.Uri))
        {
            e.Cancel = true;
            LoginStatusLabel.Text = "Đã chặn điều hướng nằm ngoài IslePilot và Steam.";
        }
    }

    private void Browser_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        BrowserLoadingPanel.Visibility = Visibility.Collapsed;
        if (!e.IsSuccess && !_completed)
        {
            LoginStatusLabel.Text = "Trang đăng nhập không tải được. Kiểm tra mạng rồi bấm THỬ LẠI.";
        }
    }

    private void Browser_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (TryCompleteFromCallback(e.Uri))
        {
            return;
        }

        if (IslePilotOverlayLoginNavigationPolicy.IsAllowed(e.Uri))
        {
            LoginBrowser.CoreWebView2.Navigate(e.Uri);
            return;
        }

        LoginStatusLabel.Text = "Đã chặn cửa sổ nằm ngoài IslePilot và Steam.";
    }

    private bool TryCompleteFromCallback(string? callback)
    {
        if (!Uri.TryCreate(callback, UriKind.Absolute, out var uri)
            || !string.Equals(
                uri.Scheme,
                IslePilotOverlayAuthService.CallbackScheme,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IslePilotOverlayAuthService.TryParseCallback(callback, out var credentials))
        {
            LoginStatusLabel.Text = "IslePilot trả về callback không hợp lệ. Hãy thử đăng nhập lại.";
            return true;
        }

        Credentials = credentials;
        _completed = true;
        DialogResult = true;
        Close();
        return true;
    }

    private void NavigateToLogin()
    {
        if (LoginBrowser.CoreWebView2 is null)
        {
            return;
        }

        BrowserLoadingPanel.Visibility = Visibility.Visible;
        LoginStatusLabel.Text = "Đang chuyển tới IslePilot và Steam…";
        LoginBrowser.CoreWebView2.Navigate(IslePilotOverlayAuthService.LoginUri.AbsoluteUri);
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => NavigateToLogin();

    private async void SwitchAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_resettingAccount || LoginBrowser.CoreWebView2 is null)
        {
            return;
        }

        _resettingAccount = true;
        SwitchAccountButton.IsEnabled = false;
        RetryButton.IsEnabled = false;
        try
        {
            await ClearSteamSessionAsync();
            NavigateToLogin();
        }
        catch (Exception exception)
        {
            BrowserLoadingPanel.Visibility = Visibility.Collapsed;
            LoginStatusLabel.Text = $"Không xóa được phiên Steam cũ: {FriendlyMessage(exception)}";
        }
        finally
        {
            _resettingAccount = false;
            SwitchAccountButton.IsEnabled = true;
            RetryButton.IsEnabled = true;
        }
    }

    private async Task ClearSteamSessionAsync()
    {
        if (LoginBrowser.CoreWebView2 is null)
        {
            return;
        }

        BrowserLoadingPanel.Visibility = Visibility.Visible;
        LoginStatusLabel.Text = "Đang xóa phiên Steam cũ để chọn tài khoản…";
        LoginBrowser.CoreWebView2.CookieManager.DeleteAllCookies();
        await Task.Yield();
    }

    private static Task<CoreWebView2Environment> CreateSharedEnvironmentAsync()
    {
        Directory.CreateDirectory(AppPaths.IslePilotWebView2Profile);
        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: AppPaths.IslePilotWebView2Profile);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (LoginBrowser.CoreWebView2 is not null)
        {
            LoginBrowser.CoreWebView2.NavigationStarting -= Browser_NavigationStarting;
            LoginBrowser.CoreWebView2.NavigationCompleted -= Browser_NavigationCompleted;
            LoginBrowser.CoreWebView2.NewWindowRequested -= Browser_NewWindowRequested;
        }

        if (!_completed)
        {
            Credentials = null;
        }

        LoginBrowser.Dispose();
    }

    private static string FriendlyMessage(Exception exception) => exception switch
    {
        WebView2RuntimeNotFoundException => "Máy chưa có Microsoft Edge WebView2 Runtime.",
        _ => exception.Message
    };
}
