using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace TheIsleOverlay.App;

/// <summary>
/// Opens the IslePilot website in the app's own browser profile and keeps every JSON
/// answer it receives. The website session can show the map even when the overlay API
/// for that account is switched off, so the zone polygons are read straight from the
/// page instead of guessing an endpoint - they are saved for embedding into the app.
/// </summary>
public partial class IslePilotZoneSnifferWindow : Window
{
    private readonly string _outputDirectory = Path.Combine(AppPaths.Root, "islepilot-sniff");
    private int _saved;
    private int _seen;

    public IslePilotZoneSnifferWindow()
    {
        InitializeComponent();
    }

    public string OutputDirectory => _outputDirectory;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_outputDirectory);
            var environment = await IslePilotWebViewEnvironment.GetAsync();
            await SnifferBrowser.EnsureCoreWebView2Async(environment);
            SnifferBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            SnifferBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            SnifferBrowser.CoreWebView2.WebResourceResponseReceived += Browser_ResponseReceived;
            SnifferBrowser.CoreWebView2.Navigate("https://islepilot.eu/map");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            SnifferStatusLabel.Text = "Máy chưa có Microsoft Edge WebView2 Runtime.";
        }
        catch (Exception exception)
        {
            SnifferStatusLabel.Text = $"Không mở được trang: {exception.Message}";
        }
    }

    private async void Browser_ResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            var uri = e.Request.Uri;
            if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Not every response carries a Content-Type, and reading a missing header
            // throws instead of returning null.
            var contentType = e.Response.Headers.Contains("Content-Type")
                ? e.Response.Headers.GetHeader("Content-Type")
                : null;
            if (contentType is null || !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using var content = await e.Response.GetContentAsync();
            if (content is null)
            {
                return;
            }

            _seen++;
            SnifferStatusLabel.Text =
                $"Đang đọc dữ liệu… (đã thấy {_seen} phản hồi JSON, {_saved} có vùng)";

            using var reader = new StreamReader(content);
            var body = await reader.ReadToEndAsync();
            if (body.Length < 64)
            {
                return;
            }

            // Only answers that look like map data are kept.
            var looksLikeZones = body.Contains("sanctuar", StringComparison.OrdinalIgnoreCase) ||
                                 body.Contains("patrol", StringComparison.OrdinalIgnoreCase) ||
                                 body.Contains("migration", StringComparison.OrdinalIgnoreCase);
            if (!looksLikeZones)
            {
                return;
            }

            var name = $"response-{++_saved:D2}-{new Uri(uri).AbsolutePath.Trim('/').Replace('/', '-')}.json";
            await File.WriteAllTextAsync(Path.Combine(_outputDirectory, name), body);
            SnifferStatusLabel.Text =
                $"Đã lưu {_saved} phản hồi có vùng (đã kiểm {_seen} phản hồi JSON) vào {_outputDirectory}.";
            _seen = 0;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidOperationException
            or JsonException
            or UriFormatException
            or ObjectDisposedException
            or System.Runtime.InteropServices.COMException)
        {
            // Sniffing is best effort: a page that closes mid-response must not crash it.
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_outputDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _outputDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
            SnifferStatusLabel.Text = $"Không mở được thư mục: {exception.Message}";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (SnifferBrowser.CoreWebView2 is not null)
        {
            SnifferBrowser.CoreWebView2.WebResourceResponseReceived -= Browser_ResponseReceived;
        }

        SnifferBrowser.Dispose();
    }
}
