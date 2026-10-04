using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.App;

public sealed class GarageModelControl : ContentControl
{
    public static readonly DependencyProperty AssetDownloaderProperty = DependencyProperty.RegisterAttached(
        "AssetDownloader", typeof(Func<Uri, CancellationToken, Task<byte[]>>), typeof(GarageModelControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty PreviewProviderProperty = DependencyProperty.RegisterAttached(
        "PreviewProvider", typeof(Func<string, int, CancellationToken, Task<SbtcSkinPreview>>), typeof(GarageModelControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    private static readonly HttpClient Client = CreateHttpClient();
    private static readonly string Cache = Path.Combine(AppPaths.Root, "GarageModels");
    private static readonly ConcurrentDictionary<string, Task<ModelBundle>> Downloads = new();
    private static readonly Dictionary<string, ModelDefinition> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Allosaurus"] = new("Allosaurus", "T_Allosaurus_Adult_Pattern_1.png", "T_Allosaurus_N.webp", "T_Allosaurus_M.png", "T_Allosaurus_RAC.webp"),
        ["Austroraptor"] = new("Austro", "T_Austroraptor_Adult_Pattern_1.png", "T_Austroraptor_Adult_N.webp", null, "T_Austroraptor_RAC.webp"),
        ["Beipiaosaurus"] = new("Beipi", "T_Beipiaosaurus_Pattern_Adult_1.png", "Beipaosaurus_Adult_Normal.webp", null, "T_Beipiaosaurus_Adult_RAC.webp"),
        ["Carnotaurus"] = new("Carno", "T_Carno_Adult_Male_Pattern_1.png", "T_Carno_Default_New_N.webp", "T_Carno_Adult_Mouth-Claws-Teeth.png", "T_Carno_Default_New_RAC.webp"),
        ["Ceratosaurus"] = new("Cerato", "T_Ceratosaurus_Adult_Pattern_1.png", "T_Cerato_Adult_N.webp", "T_Cerato_Adult_Mouth-Teeth-Claws.png", "T_Cerato_Adult_RAC.webp"),
        ["Deinosuchus"] = new("Deino", "T_Deinosuchus_Adult_Pattern_M.png", "T_Deinosuchus_N.webp", "T_Deinosuchus_Detail_M.png", "T_Deinosuchus_RAC.webp"),
        ["Diabloceratops"] = new("Dibble", "T_Diabloceratops_Adult_Pattern_1.png", "T_Diablo_Adult_N.webp", "T_Diablo_Adult_Mouth-Claws.png", "T_Diablo_RAC.webp"),
        ["Dilophosaurus"] = new("Dilo", "T_Dilophosaurus_Adult_Pattern_Default.png", "T_Dilo_Base_N.webp", null, "T_Dilo_Base_RAC.webp"),
        ["Dryosaurus"] = new("Dryo", "T_Dryosaurus_Adult_Pattern_1.png", "T_Dryosaurus_N.webp", "T_Dryosaurus_Detail_M.png", "T_Dryosaurus_RAC.webp"),
        ["Gallimimus"] = new("Galli", "T_Gallimimus_Adult_Pattern_1.png", "T_Galli_Adult_Base_N.webp", "T_Gallimimus_Adult_TMC.png", "T_Galli_Adult_Base_RAC.webp"),
        ["Herrerasaurus"] = new("Herrera", "Herrera_Adult_Pattern.png", "T_Herrerasaurus_N.webp", "T_Herrerasaurus_M.png", "T_Herrerasaurus_RAC.webp"),
        ["Hypsilophodon"] = new("Hypsi", "T_Hypsilophodon_Adult_Pattern_1_Male.png", "T_Hypsilophodon_N.webp", null, "T_Hypsilophodon_RAC.webp"),
        ["Kentrosaurus"] = new("Kentro", "T_Kentrosaurus_Adult_Pattern_1.png", "T_Kentrosaurus_Normal.webp", "T_Kentrosaurus_Adult_Mask_TMC.png", "T_Kentrosaurus_Mask_RAC.webp"),
        ["Maiasaura"] = new("Maiasaura", "T_Maiasaura_Pattern_Adult_1.png", "T_Maiasaura_N.webp", "T_Maiasaura_M.png", "T_Maiasaura_RAC.webp"),
        ["Omniraptor"] = new("Omni", "T_Omniraptor_Adult_Pattern_1.png", "T_Omniraptor_N.webp", "T_Omniraptor_Detail_M.png", "T_Omniraptor_RAC.webp"),
        ["Pachycephalosaurus"] = new("Pachy", "T_Pachycephalosaurus_Adult_Pattern_1.png", "T_Pachy_Default_New_N.webp", "T_Pachycephalosaurus_Mask_TMC.png", "T_Pachy_Default_New_RAC.webp"),
        ["Pteranodon"] = new("Pter", "T_Pteranodon_Adult_Pattern_1.png", "T_Pteranodon_N.webp", "T_Pteranodon_Detail_M.png", "T_Pteranodon_RAC.webp"),
        ["Stegosaurus"] = new("Stego", "T_Stegosaurus_Adult_Pattern_1.png", "T_Stegosaurus_N.webp", "T_Stegosaurus_Detail_M.png", "T_Stegosaurus_RAC.webp"),
        ["Tenontosaurus"] = new("Teno", "T_Tenontosaurus_Adult_Pattern_1.png", "T_Tenontosaurus_N.webp", "T_Tenontosaurus_Detail_M.png", "T_Tenontosaurus_RAC.webp"),
        ["Triceratops"] = new("Triceratops", "T_Triceratops_Adult_Pattern_1.png", "T_Triceratops_N.webp", "T_Triceratops_Detail_M.png", "T_Triceratops_RAC.webp"),
        ["Troodon"] = new("Troodon", "T_Troodon_Adult_Pattern_1.png", "T_Troodon_N.webp", "T_Troodon_Detail_M.png", "T_Troodon_RAC.webp"),
        ["Tyrannosaurus"] = new("Tyrannosaurus", "T_Tyrannosaurus_Adult_Pattern_1.png", "T_Tyrannosaurus_N.webp", "T_Tyrannosaurus_Detail_M.png", "T_Tyrannosaurus_RAC.webp")
    };
    private sealed record ModelDefinition(string Folder, string Pattern, string Normal, string? Tmc, string? Rac);
    internal sealed record ModelBundle(string Model, string Pattern, string Diffuse, string? Utility,
        IReadOnlyDictionary<string, string> UtilityChannels);
    private WebView2CompositionControl? _browser;
    private string? _requestedSpecies;
    private IslePilotOverlayGaragePaletteDto? _requestedPalette;
    private int _requestedPattern;
    private bool _viewerReady;
    private int _generation;
    public GarageModelControl()
    {
        Height = 190;
        Loaded += (_, _) => Start();
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) Start(); else Stop(); };
        Unloaded += (_, _) => Stop();
        PreviewMouseWheel += GarageModelControl_PreviewMouseWheel;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GarageDinoCardPresentation dino)
            {
                SetModel(dino.Species, dino.SkinPalette, dino.Pattern);
            }
        };
    }

    private void GarageModelControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // WebView2's composition child lets the outer WPF ScrollViewer see the
        // wheel before the page's JS listener. Consume it here and forward only
        // the zoom delta to the viewer, so zooming never scrolls the page.
        e.Handled = true;
        if (_browser?.CoreWebView2 is not null)
        {
            _ = _browser.CoreWebView2.ExecuteScriptAsync($"window.__viewerZoom && window.__viewerZoom({e.Delta});");
        }
    }

    public void SetModel(string? species, IslePilotOverlayGaragePaletteDto? palette, int pattern = 0)
    {
        var normalized = species?.Trim();
        if (normalized?.StartsWith("BP_", StringComparison.OrdinalIgnoreCase) == true) normalized = normalized[3..];
        if (normalized?.EndsWith("_C", StringComparison.OrdinalIgnoreCase) == true) normalized = normalized[..^2];
        var speciesChanged = !string.Equals(_requestedSpecies, normalized, StringComparison.OrdinalIgnoreCase) || _requestedPattern != pattern;
        _requestedSpecies = normalized;
        _requestedPalette = palette;
        _requestedPattern = Math.Max(0, pattern);
        if (speciesChanged)
        {
            Stop();
            if (IsVisible && IsLoaded) Start();
        }
        else
        {
            SendPalette();
        }
    }

    public void Reload()
    {
        Stop();
        if (IsVisible && IsLoaded) Start();
    }

    private async void Start()
    {
        if (_browser is not null || !IsVisible) return;
        if (string.IsNullOrWhiteSpace(_requestedSpecies) && DataContext is GarageDinoCardPresentation dino)
        {
            _requestedSpecies = dino.Species;
            _requestedPalette = dino.SkinPalette;
        }
        if (string.IsNullOrWhiteSpace(_requestedSpecies))
        {
            ShowMessage("CHƯA CÓ DINO ĐỂ HIỂN THỊ 3D");
            return;
        }
        var generation = ++_generation;
        var species = _requestedSpecies!;
        var patternIndex = _requestedPattern;
        var downloadKey = species + "|" + patternIndex;
        var assetDownloader = GetValue(AssetDownloaderProperty) as Func<Uri, CancellationToken, Task<byte[]>>;
        var previewProvider = GetValue(PreviewProviderProperty) as Func<string, int, CancellationToken, Task<SbtcSkinPreview>>;
        ShowMessage("ĐANG LẤY DỮ LIỆU 3D MODEL…");
        WebView2CompositionControl? browser = null;
        try
        {
            browser = new WebView2CompositionControl();
            _browser = browser;
            Content = browser;
        }
        catch (Exception ex)
        {
            CrashReporter.Write("GarageModelControl.CreateBrowser", ex);
            Stop();
            ShowMessage("Thiết bị không hỗ trợ DirectComposition 3D. Hãy cập nhật driver đồ họa.");
            return;
        }

        try
        {
            var preview = previewProvider is null ? SbtcSkinPreview.Basic
                : await previewProvider(species, patternIndex, CancellationToken.None);
            if (generation != _generation) return;
            downloadKey += "|" + preview.Build + "|" + preview.PatternAsset + "|" + preview.UtilityAsset;
            var assets = await Downloads.GetOrAdd(downloadKey, _ => DownloadAsync(species, patternIndex, assetDownloader, preview));
            if (generation != _generation) return;
            var environment = await IslePilotWebViewEnvironment.GetAsync();
            if (generation != _generation) return;
            await browser.EnsureCoreWebView2Async(environment);
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
            browser.CoreWebView2.WebMessageReceived += (sender, e) =>
            {
                if (generation != _generation || e.Source != "https://isle-viewer.local/viewer.html") return;
                try
                {
                    using var message = JsonDocument.Parse(e.WebMessageAsJson);
                    if (message.RootElement.TryGetProperty("initialized", out _))
                    {
                        _viewerReady = true;
                        browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                        {
                            model = AssetUrl(assets.Model),
                            pattern = AssetUrl(assets.Pattern),
                            diffuse = AssetUrl(assets.Diffuse),
                            utility = AssetUrl(assets.Utility),
                            utilityChannels = assets.UtilityChannels,
                            palette = _requestedPalette
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                    }
                    else if (message.RootElement.TryGetProperty("ready", out _))
                    {
                        SendPalette();
                    }
                    else if (message.RootElement.TryGetProperty("error", out _))
                    {
                        Downloads.TryRemove(downloadKey, out _);
                        Stop();
                        ShowMessage("Model 3D bị lỗi. Bấm Làm mới để tải lại.");
                    }
                }
                catch (Exception jsonEx)
                {
                    CrashReporter.Write("GarageModelControl.WebMessage", jsonEx);
                }
            };
            browser.CoreWebView2.ProcessFailed += (_, _) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        Stop();
                        ShowMessage("Tiến trình 3D đã tạm dừng (GPU crash). Bấm Làm mới để thử lại.");
                    }
                    catch { }
                });
            };
            browser.CoreWebView2.Navigate("https://isle-viewer.local/viewer.html");
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            CrashReporter.Write("GarageModelControl.WebView2RuntimeNotFound", ex);
            Downloads.TryRemove(downloadKey, out _);
            Stop();
            ShowMessage("Máy chưa cài đặt Microsoft Edge WebView2 Runtime để xem 3D.");
        }
        catch (Exception ex) when (generation == _generation && ex is HttpRequestException or TelemetryAuthenticationException or TaskCanceledException)
        {
            Downloads.TryRemove(downloadKey, out _);
            Stop();
            ShowMessage("Chưa tải được skin SBTC. Kiểm tra phiên Steam / kết nối rồi bấm Làm mới.");
        }
        catch (Exception ex) when (generation == _generation)
        {
            CrashReporter.Write("GarageModelControl.Start", ex);
            Downloads.TryRemove(downloadKey, out _);
            Stop();
            ShowMessage("Không thể khởi động 3D model trên thiết bị này. Bấm Làm mới để thử lại.");
        }
        catch (Exception) when (generation != _generation) { }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) IsleLiveMap/1.8");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("model/gltf-binary"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return client;
    }

    internal static async Task<ModelBundle> DownloadAsync(string species, int patternIndex = 0,
        Func<Uri, CancellationToken, Task<byte[]>>? download = null, SbtcSkinPreview? preview = null)
    {
        if (!Models.ContainsKey(species)) throw new InvalidDataException("Species has no 3D model.");
        preview ??= SbtcSkinPreview.Basic;
        // SBTC asset folders use canonical species spelling.
        var canonicalSpecies = Models.Keys.First(key =>
            string.Equals(key, species, StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(Cache);
        var baseUri = $"https://sbtcislandd.com/assets/dino/{canonicalSpecies}/";
        var buildKey = preview.Build.Length == 0 ? "" : "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preview.Build)))[..12];
        var cachePrefix = "sbtc-" + canonicalSpecies + buildKey;
        var model = DownloadFileAsync(new Uri(baseUri + "mesh.glb"),
            Path.Combine(Cache, cachePrefix + ".glb"), IsValidGlb, download);
        var pattern = DownloadImageAsync(baseUri, $"pattern_{patternIndex}",
            Path.Combine(Cache, cachePrefix + $"-pattern-{patternIndex}"), download, preview.PatternAsset);
        var diffuse = DownloadImageAsync(baseUri, "diffuse", Path.Combine(Cache, cachePrefix + "-diffuse"), download);
        var utility = DownloadOptionalImageAsync(baseUri, preview.UtilityAsset,
            Path.Combine(Cache, cachePrefix + "-utility-" + preview.UtilityAsset), download);
        await Task.WhenAll(new Task[] { model, pattern, diffuse, utility }).ConfigureAwait(false);
        return new ModelBundle(await model, await pattern, await diffuse, await utility, preview.UtilityChannels);
    }

    internal static async Task<string> DownloadImageAsync(string baseUri, string stem, string cachePath,
        Func<Uri, CancellationToken, Task<byte[]>>? download, string? exactAsset = null)
    {
        // Studio contracts can name a specific mask. Older assets have either format.
        var candidates = new[] { exactAsset, stem + ".webp", stem + ".png" }
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal);
        Exception? last = null;
        foreach (var name in candidates)
        {
            try
            {
                return await DownloadFileAsync(new Uri(baseUri + name), cachePath + "-" + name,
                    IsValidImage, download).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden }) throw;
                last = ex;
            }
        }
        throw new HttpRequestException("Không tải được texture skin SBTC.", last);
    }

    private static async Task<string?> DownloadOptionalImageAsync(string baseUri, string? file, string path, Func<Uri, CancellationToken, Task<byte[]>>? download)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        try { return await DownloadFileAsync(new Uri(baseUri + file), path, IsValidImage, download).ConfigureAwait(false); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            return null;
        }
    }

    private static async Task<string> DownloadFileAsync(Uri uri, string path, Func<string, bool> validator, Func<Uri, CancellationToken, Task<byte[]>>? download)
    {
        if (validator(path)) return path;
        TryDeleteModel(path);
        var temp = path + ".tmp";
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            TryDeleteModel(temp);
            try
            {
                if (download is not null)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    await File.WriteAllBytesAsync(temp, await download(uri, timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                }
                else
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.Referrer = new Uri("https://sbtcislandd.com/studio");
                    using var response = await Client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    await using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    await using (var destination = new FileStream(
                                     temp,
                                     FileMode.CreateNew,
                                     FileAccess.Write,
                                     FileShare.None,
                                     128 * 1024,
                                     FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await source.CopyToAsync(destination).ConfigureAwait(false);
                    }
                }
                if (!validator(temp)) throw new InvalidDataException("Invalid or incomplete 3D viewer asset.");
                File.Move(temp, path, overwrite: true);
                return path;
            }
            catch (Exception exception) when (exception is HttpRequestException or
                                               TaskCanceledException or
                                               IOException or
                                               InvalidDataException)
            {
                lastError = exception;
                TryDeleteModel(temp);
                if (exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound }) throw;
                if (attempt < 3) await Task.Delay(TimeSpan.FromMilliseconds(350 * attempt)).ConfigureAwait(false);
            }
        }
        throw new HttpRequestException("Không tải được model SBTC. Kiểm tra phiên Steam và kết nối mạng.", lastError);
    }

    internal static bool IsValidImage(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 16) return false;
            Span<byte> header = stackalloc byte[12];
            if (stream.Read(header) != header.Length) return false;
            return header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                   (header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool IsValidGlb(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 20 || stream.Length > uint.MaxValue) return false;
            Span<byte> header = stackalloc byte[12];
            if (stream.Read(header) != header.Length ||
                header[0] != (byte)'g' || header[1] != (byte)'l' ||
                header[2] != (byte)'T' || header[3] != (byte)'F') return false;
            var version = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
            var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]);
            return version == 2 && declaredLength == stream.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDeleteModel(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string? AssetUrl(string? path) => path is null
        ? null
        : "https://isle-model.local/" + Path.GetFileName(path);

    private void ShowMessage(string message)
    {
        var panel = new Grid();
        var backgroundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "GarageViewer", "forest-waterfall.png");
        if (File.Exists(backgroundPath))
        {
            panel.Background = new ImageBrush(new BitmapImage(new Uri(backgroundPath, UriKind.Absolute)))
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                Opacity = 0.72
            };
        }

        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(74, 3, 12, 9))
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 236, 228)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 5,
                ShadowDepth = 1,
                Opacity = 0.9
            }
        });
        Content = panel;
    }

    private void SendPalette()
    {
        if (!_viewerReady || _browser?.CoreWebView2 is null) return;
        try
        {
            _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                palette = _requestedPalette
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Stop()
    {
        ++_generation;
        _viewerReady = false;
        try
        {
            _browser?.Dispose();
        }
        catch (Exception ex)
        {
            CrashReporter.Write("GarageModelControl.Stop", ex);
        }
        finally
        {
            _browser = null;
            Content = null;
        }
    }
}
