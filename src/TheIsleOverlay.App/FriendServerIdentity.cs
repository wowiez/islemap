namespace TheIsleOverlay.App;

public static class FriendServerIdentity
{
    public static string Normalize(string server)
    {
        var normalized = string.Join(" ", server.Normalize().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var compact = string.Concat(normalized.Where(char.IsLetterOrDigit));
        if (compact is "sdvn3" or "sdvn3123123123" or "sdvn3x3" or "sdvn3x3grow" or "seavnsdvn3x3" or "seavnsdvn3x3grow"
            || normalized == "3.sdvn.org") return "sdvn3";
        if (compact is "sbtc" or "sbtcisland") return "sbtc";
        return normalized;
    }

    public static FriendContext? Resolve(string? reportedServer, DateTimeOffset? webAt,
        string? packetEndpoint, DateTimeOffset? packetAt, bool packetLive, DateTimeOffset now)
    {
        static bool Fresh(DateTimeOffset? at, DateTimeOffset now) => at is { } time && now >= time && now - time < TimeSpan.FromSeconds(10);
        var server = Fresh(webAt, now) && !string.IsNullOrWhiteSpace(reportedServer) ? Normalize(reportedServer) : null;
        var endpoint = packetLive && Fresh(packetAt, now) && !string.IsNullOrWhiteSpace(packetEndpoint) ? packetEndpoint : null;
        return server is null && endpoint is null ? null : new FriendContext(server ?? endpoint!, Endpoint: endpoint);
    }
}
