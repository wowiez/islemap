using System.Net.Http;
using TheIsleOverlay.Core;
using TheIsleOverlay.EraGaming;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.Pandora;

namespace TheIsleOverlay.App;

public enum TelemetrySourceKind
{
    EraGaming,
    IslePilot,
    Pandora,

    // An IslePilot instance on the server's own domain: same overlay API and Steam
    // handshake, but hosted outside islepilot.eu.
    IslePilotHosted
}

public sealed record TelemetrySourceDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ShortName { get; init; }
    public required TelemetrySourceKind Kind { get; init; }
    public required Uri BaseUri { get; init; }
    public required Uri LoginUri { get; init; }
    public required string CookieName { get; init; }
    public string? ServerSlug { get; init; }
    public bool CaptureAllHostCookies { get; init; }

    public TimeSpan PollingInterval => Kind switch
    {
        TelemetrySourceKind.EraGaming => TimeSpan.FromMilliseconds(500),
        TelemetrySourceKind.Pandora => TimeSpan.FromSeconds(1),
        _ => TimeSpan.FromSeconds(2)
    };

    public ITelemetryProvider CreateProvider(HttpClient httpClient, string cookieValue) => Kind switch
    {
        TelemetrySourceKind.EraGaming => new EraGamingTelemetryProvider(
            httpClient,
            new EraGamingOptions
            {
                BaseUri = BaseUri,
                SessionCookie = cookieValue
            }),
        TelemetrySourceKind.IslePilot => new IslePilotTelemetryProvider(
            httpClient,
            new IslePilotOptions
            {
                BaseUri = BaseUri,
                ServerSlug = ServerSlug ?? throw new InvalidOperationException("IslePilot source requires a server slug."),
                DisplayName = DisplayName,
                PlayerCookie = cookieValue
            }),
        TelemetrySourceKind.Pandora => new PandoraTelemetryProvider(
            httpClient,
            new PandoraOptions
            {
                BaseUri = BaseUri,
                SessionCookieHeader = cookieValue
            }),
        TelemetrySourceKind.IslePilotHosted => throw new NotSupportedException(
            "IslePilot servers on a custom domain connect through the overlay session, not a cookie provider."),
        _ => throw new ArgumentOutOfRangeException()
    };

    public static TelemetrySourceDefinition EraGaming { get; } = new()
    {
        Id = "era",
        DisplayName = "EraGaming",
        ShortName = "ERA",
        Kind = TelemetrySourceKind.EraGaming,
        BaseUri = new Uri("https://eragamingvn.net/"),
        LoginUri = new Uri("https://eragamingvn.net/live-map"),
        CookieName = "era_session"
    };

    public static TelemetrySourceDefinition DinoVietnam { get; } = new()
    {
        Id = "dinovietnam",
        DisplayName = "DinoVietNam",
        ShortName = "DINO VN",
        Kind = TelemetrySourceKind.IslePilot,
        BaseUri = new Uri("https://dinovietnam.islepilot.eu/"),
        LoginUri = new Uri("https://dinovietnam.islepilot.eu/api/player/steam/login?redirect=%2Fmap"),
        CookieName = "islepilot_player",
        ServerSlug = "dinovietnam"
    };

    public static TelemetrySourceDefinition DinoVietnamPremium { get; } = new()
    {
        Id = "dinovietnampremium",
        DisplayName = "DinoVietNam Premium",
        ShortName = "DINO VIP",
        Kind = TelemetrySourceKind.IslePilot,
        BaseUri = new Uri("https://dinovietnampremium.islepilot.eu/"),
        LoginUri = new Uri("https://dinovietnampremium.islepilot.eu/api/player/steam/login?redirect=%2Fmap"),
        CookieName = "islepilot_player",
        ServerSlug = "dinovietnampremium"
    };

    public static TelemetrySourceDefinition HoHo { get; } = new()
    {
        Id = "hoho",
        DisplayName = "HoHo",
        ShortName = "HOHO",
        Kind = TelemetrySourceKind.IslePilot,
        BaseUri = new Uri("https://hoho.islepilot.eu/"),
        LoginUri = new Uri("https://hoho.islepilot.eu/map"),
        CookieName = "islepilot_player",
        ServerSlug = "hoho"
    };

    public static TelemetrySourceDefinition Pandora { get; } = new()
    {
        Id = "pandora",
        DisplayName = "PANDORA",
        ShortName = "PANDORA",
        Kind = TelemetrySourceKind.Pandora,
        BaseUri = new Uri("https://islapandora.eu/"),
        LoginUri = new Uri("https://islapandora.eu/live-map"),
        CookieName = "website session",
        CaptureAllHostCookies = true
    };

    public static TelemetrySourceDefinition Sdvn3 { get; } = new()
    {
        Id = "sdvn3",
        DisplayName = "[SEA/VN]-SDVN-#3-X3",
        ShortName = "SDVN #3",
        Kind = TelemetrySourceKind.IslePilotHosted,
        BaseUri = new Uri("https://3.sdvn.org/"),
        LoginUri = new Uri("https://3.sdvn.org/api/player/steam/login?redirect=%2Fp%2Fsdvn3123123123%2Fmap"),
        CookieName = "islepilot_player",
        ServerSlug = "sdvn3123123123"
    };

    public static IReadOnlyList<TelemetrySourceDefinition> All { get; } =
    [
        EraGaming,
        DinoVietnam,
        DinoVietnamPremium,
        HoHo,
        Sdvn3,
        Pandora
    ];

    public string AccountLabel => Kind == TelemetrySourceKind.IslePilotHosted
        ? ShortName
        : DisplayName;

    public static TelemetrySourceDefinition? FromId(string? id) =>
        All.FirstOrDefault(source => string.Equals(source.Id, id, StringComparison.OrdinalIgnoreCase));
}
