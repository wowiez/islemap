using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TheIsleOverlay.App;

public sealed class GarageModelControl : ContentControl
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly string Cache = Path.Combine(AppPaths.Root, "GarageModels");
    private static readonly ConcurrentDictionary<string, Task<string>> Downloads = new();
    private static readonly Lazy<Task<CoreWebView2Environment>> Environment = new(() =>
        CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.Root, "WebView2-Garage")));
    private static readonly Dictionary<string, string> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Allosaurus"] = "Allosaurus", ["Austroraptor"] = "Austro", ["Beipiaosaurus"] = "Beipi",
        ["Carnotaurus"] = "Carno", ["Ceratosaurus"] = "Cerato", ["Deinosuchus"] = "Deino",
        ["Diabloceratops"] = "Dibble", ["Dilophosaurus"] = "Dilo", ["Dryosaurus"] = "Dryo",
        ["Gallimimus"] = "Galli", ["Herrerasaurus"] = "Herrera", ["Hypsilophodon"] = "Hypsi",
        ["Kentrosaurus"] = "Kentro", ["Maiasaura"] = "Maiasaura", ["Omniraptor"] = "Omni",
        ["Pachycephalosaurus"] = "Pachy", ["Pteranodon"] = "Pter", ["Stegosaurus"] = "Stego",
        ["Tenontosaurus"] = "Teno", ["Triceratops"] = "Triceratops", ["Troodon"] = "Troodon",
        ["Tyrannosaurus"] = "Tyrannosaurus"
    };
    private WebView2CompositionControl? _browser;
    private int _generation;
    public GarageModelControl()
    {
        Height = 190;
        Loaded += (_, _) => Start();
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) Start(); else Stop(); };
        Unloaded += (_, _) => Stop();
        DataContextChanged += (_, _) => { Stop(); if (IsVisible && IsLoaded) Start(); };
    }

    private async void Start()
    {
        if (_browser is not null || !IsVisible || DataContext is not GarageDinoCardPresentation dino) return;
        var generation = ++_generation;
        ShowMessage("Đang tải model 3D…");
        var browser = new WebView2CompositionControl();
        _browser = browser;
        try
        {
            var model = await Downloads.GetOrAdd(dino.Species, DownloadAsync);
            if (generation != _generation) return;
            await browser.EnsureCoreWebView2Async(await Environment.Value);
            if (generation != _generation) return;
            browser.CoreWebView2.SetVirtualHostNameToFolderMapping("isle-viewer.local",
                Path.Combine(AppContext.BaseDirectory, "Assets", "GarageViewer"), CoreWebView2HostResourceAccessKind.DenyCors);
            browser.CoreWebView2.SetVirtualHostNameToFolderMapping("isle-model.local", Cache, CoreWebView2HostResourceAccessKind.Allow);
            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            browser.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "isle-viewer.local") e.Cancel = true;
            };
            browser.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            browser.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                if (generation != _generation || e.Source != "https://isle-viewer.local/viewer.html") return;
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                if (message.RootElement.TryGetProperty("initialized", out var initialized))
                    browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                    {
                        model = "https://isle-model.local/" + Path.GetFileName(model),
                        palette = dino.SkinPalette
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            };
            browser.CoreWebView2.ProcessFailed += (_, _) => { Stop(); ShowMessage("3D đã tạm dừng. Mở lại Garage để thử lại."); };
            Content = browser;
            browser.CoreWebView2.Navigate("https://isle-viewer.local/viewer.html");
        }
        catch (Exception) when (generation == _generation)
        {
            Downloads.TryRemove(dino.Species, out _);
            Stop(); ShowMessage("Không tải được model 3D. Kiểm tra mạng rồi mở lại Garage.");
        }
        catch (Exception) when (generation != _generation) { }
    }

    private static async Task<string> DownloadAsync(string species)
    {
        if (!Models.TryGetValue(species, out var folder)) throw new InvalidDataException("Species has no 3D model.");
        Directory.CreateDirectory(Cache);
        var path = Path.Combine(Cache, species + "-v12.glb");
        if (File.Exists(path) && new FileInfo(path).Length > 20) return path;
        var uri = new Uri($"https://islepilot.eu/cdn/skinviewer/{folder}/{species}.glb?v=12");
        using var response = await Client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length < 20 || bytes[0] != (byte)'g' || bytes[1] != (byte)'l' || bytes[2] != (byte)'T' || bytes[3] != (byte)'F')
            throw new InvalidDataException("Invalid GLB model.");
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    private void ShowMessage(string message) => Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray };
    private void Stop()
    {
        ++_generation;
        _browser?.Dispose(); _browser = null; Content = null;
    }
}
