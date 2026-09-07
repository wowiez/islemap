using System.IO;

namespace TheIsleOverlay.App;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wowiez",
        "IsleLiveMap");

    public static string WebView2Profile { get; } = Path.Combine(Root, "WebView2");

    public static string IslePilotWebView2Profile { get; } = Path.Combine(
        Root,
        "WebView2-IslePilot");

    public static string IslePilotCredential { get; } = Path.Combine(
        Root,
        "islepilot-overlay.credential");

    public static string OverlayLayoutSettings { get; } = Path.Combine(
        Root,
        "overlay-layout.json");

    public static string CrashLog { get; } = Path.Combine(Root, "crash.log");
}
