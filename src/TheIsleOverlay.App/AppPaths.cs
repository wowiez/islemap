using System.IO;

namespace TheIsleOverlay.App;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IsleLiveMapData");

    public static string WebView2Profile { get; } = Path.Combine(Root, "WebView2");

    public static string IslePilotWebView2Profile { get; } = Path.Combine(
        Root,
        "WebView2-IslePilot");

    public static string IslePilotCredential { get; } = Path.Combine(
        Root,
        "islepilot-overlay.credential");

    // Servers that run their own IslePilot instance (custom domain) keep their own
    // overlay token, so every such source stores credentials in its own file.
    public static string IslePilotCredentialFor(string sourceId) => Path.Combine(
        Root,
        $"islepilot-overlay-{sourceId}.credential");

    public static string OverlayLayoutSettings { get; } = Path.Combine(
        Root,
        "overlay-layout.json");

    public static string PlayerPathTrail { get; } = Path.Combine(
        Root,
        "player-path-trail.json");

    /// <summary>Steam cookies of servers that run their own site (SBTC Island, ...), DPAPI protected.</summary>
    public static string WebsiteSessions { get; } = Path.Combine(Root, "website-sessions.dat");

    /// <summary>Zones as the server sent them, written for calibration.
    /// </summary>
    public static string ZoneDump { get; } = Path.Combine(Root, "zones.json");

    /// <summary>Validated mass samples from the player's replication, with sample age.
    /// </summary>
    public static string NpcapWeightLog { get; } =
        Environment.GetEnvironmentVariable("ISLELIVEMAP_WEIGHT_LOG_PATH") is { Length: > 0 } path
            ? Path.GetFullPath(path) : Path.Combine(Root, "npcap-weight.txt");

    public static string CrashLog { get; } = Path.Combine(Root, "crash.log");

}
