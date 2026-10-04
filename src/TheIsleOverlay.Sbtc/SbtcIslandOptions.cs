namespace TheIsleOverlay.Sbtc;

public sealed record SbtcIslandOptions
{
    public Uri BaseUri { get; init; } = new("https://sbtcislandd.com/");

    /// <summary>Every cookie the Steam sign-in left on the site, as a Cookie header.</summary>
    public required string SessionCookieHeader { get; init; }

    /// <summary>Platform query the site uses to pick the server shard.</summary>
    public string Platform { get; init; } = "steam";
}
