namespace TheIsleOverlay.IslePilot;

public sealed record IslePilotOverlayOptions
{
    public static Uri ServiceBaseUri { get; } = new("https://islepilot.eu/");
    public static Uri WebSocketUri { get; } = new("wss://islepilot.eu/ows");

    public required string OverlayToken { get; init; }
    public string? PersonaName { get; init; }
    public TimeSpan MeRefreshInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MapRefreshInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan MarkersRefreshInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan LiveDataLifetime { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan PositionFallbackAfter { get; init; } = TimeSpan.FromSeconds(6);
    public TimeSpan WebSocketConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan RestRequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RestTimeoutRetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public int RestTimeoutRetryCount { get; init; } = 1;
}
