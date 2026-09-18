using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TheIsleOverlay.App;

public sealed class SbtcVoiceControl : ContentControl, IDisposable
{
    internal static readonly Uri VoiceUri = new("https://islepilot.eu/p/sbtcisland/voice");

    private const string OverlayCssScript = """
        (() => {
          if (location.hostname !== 'islepilot.eu') return;
          const apply = () => {
            if (document.getElementById('isle-live-map-voice-style')) return;
            const style = document.createElement('style');
            style.id = 'isle-live-map-voice-style';
            style.textContent = `
              header, aside { display: none !important; }
              html, body { background: #0d0f10 !important; }
              body { overflow: auto !important; }
              main { max-width: none !important; margin: 0 !important; padding: 14px !important; }
              [class*="min-h-screen"] { min-height: 0 !important; }
              [class*="max-w-"] { max-width: none !important; }
            `;
            document.documentElement.appendChild(style);
          };
          apply();
          new MutationObserver(apply).observe(document.documentElement, { childList: true, subtree: true });
        })();
        """;

    private WebView2CompositionControl? _browser;
    private bool _enabled;
    private bool _starting;
    private bool _disposed;

    public SbtcVoiceControl()
    {
        Background = new SolidColorBrush(Color.FromRgb(13, 15, 16));
        Loaded += (_, _) => StartIfNeeded();
        Unloaded += (_, _) =>
        {
            if (Window.GetWindow(this) is null)
            {
                Stop();
            }
        };
        ShowStatus("VOICE CHỈ HOẠT ĐỘNG KHI ĐANG CHƠI SBTC");
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || _enabled == enabled) return;
        _enabled = enabled;
        if (enabled)
        {
            StartIfNeeded();
        }
        else
        {
            Stop();
            ShowStatus("VOICE ĐÃ NGẮT · KHÔNG CÒN Ở SERVER SBTC");
        }
    }

    public void Retry()
    {
        if (!_enabled || _disposed) return;
        Stop();
        StartIfNeeded();
    }

    private async void StartIfNeeded()
    {
        if (!_enabled || _starting || _browser is not null || _disposed || !IsLoaded) return;
        _starting = true;
        ShowStatus("ĐANG MỞ SBTC VOICE CHAT…");
        try
        {
            var browser = new WebView2CompositionControl
            {
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 13, 15, 16),
                ZoomFactor = 0.82d
            };
            _browser = browser;
            // CompositionControl initialization is reliable only after the
            // child has been attached to the loaded F8 visual tree.
            Content = browser;
            await browser.EnsureCoreWebView2Async(await IslePilotWebViewEnvironment.GetAsync());
            if (!_enabled || _disposed || !ReferenceEquals(_browser, browser)) return;

            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            browser.CoreWebView2.NavigationStarting += Browser_NavigationStarting;
            browser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
            browser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;
            browser.CoreWebView2.ProcessFailed += Browser_ProcessFailed;
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(OverlayCssScript);
            browser.CoreWebView2.Navigate(VoiceUri.AbsoluteUri);
        }
        catch (Exception exception) when (!_disposed)
        {
            Stop();
            ShowStatus($"KHÔNG MỞ ĐƯỢC VOICE · {FriendlyMessage(exception)}");
        }
        finally
        {
            _starting = false;
        }
    }

    internal static bool IsAllowedNavigation(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return IsHostOrSubdomain(uri.IdnHost, "islepilot.eu") ||
               IsHostOrSubdomain(uri.IdnHost, "steamcommunity.com") ||
               IsHostOrSubdomain(uri.IdnHost, "steampowered.com") ||
               IsHostOrSubdomain(uri.IdnHost, "discord.com") ||
               IsHostOrSubdomain(uri.IdnHost, "discordapp.com");
    }

    private static bool IsHostOrSubdomain(string host, string suffix) =>
        host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase);

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsAllowedNavigation(e.Uri)) e.Cancel = true;
    }

    private void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess &&
            e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled &&
            _enabled)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!_enabled || _disposed) return;
                Stop();
                ShowStatus("VOICE KHÔNG TẢI ĐƯỢC · KIỂM TRA MẠNG RỒI BẤM TẢI LẠI");
            });
        }
    }

    private void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (IsAllowedNavigation(e.Uri)) _browser?.CoreWebView2.Navigate(e.Uri);
    }

    private void Browser_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (!_enabled) return;
        Stop();
        ShowStatus("VOICE ĐÃ DỪNG · BẤM TẢI LẠI ĐỂ KẾT NỐI");
    }

    private void ShowStatus(string message)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromRgb(152, 164, 158)),
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center
        });
        Content = panel;
    }

    private void Stop()
    {
        var browser = _browser;
        _browser = null;
        if (browser?.CoreWebView2 is not null)
        {
            browser.CoreWebView2.NavigationStarting -= Browser_NavigationStarting;
            browser.CoreWebView2.NavigationCompleted -= Browser_NavigationCompleted;
            browser.CoreWebView2.NewWindowRequested -= Browser_NewWindowRequested;
            browser.CoreWebView2.ProcessFailed -= Browser_ProcessFailed;
            try { browser.CoreWebView2.Navigate("about:blank"); } catch (InvalidOperationException) { }
        }
        browser?.Dispose();
        if (ReferenceEquals(Content, browser)) Content = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enabled = false;
        Stop();
    }

    private static string FriendlyMessage(Exception exception) => exception switch
    {
        WebView2RuntimeNotFoundException => "máy chưa có WebView2 Runtime",
        _ => "hãy kiểm tra mạng rồi bấm Tải lại"
    };
}
