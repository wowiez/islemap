using System.IO;
using Microsoft.Web.WebView2.Core;

namespace TheIsleOverlay.App;

internal static class IslePilotWebViewEnvironment
{
    private static readonly Lazy<Task<CoreWebView2Environment>> Shared = new(
        CreateAsync,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static Task<CoreWebView2Environment> GetAsync() => Shared.Value;

    private static Task<CoreWebView2Environment> CreateAsync()
    {
        Directory.CreateDirectory(AppPaths.IslePilotWebView2Profile);
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--disable-gpu-process-crash-limit --enable-webgl"
        };
        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: AppPaths.IslePilotWebView2Profile,
            options: options);
    }
}
