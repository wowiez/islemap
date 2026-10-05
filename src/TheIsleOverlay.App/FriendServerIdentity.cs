namespace TheIsleOverlay.App;

public static class FriendServerIdentity
{
    public static string Normalize(string server)
    {
        var normalized = string.Join(" ", server.Normalize().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var compact = string.Concat(normalized.Where(char.IsLetterOrDigit));
        if (compact is "sdvn3" or "sdvn3123123123" or "sdvn3x3" or "sdvn3x3grow" or "seavnsdvn3x3"
            || normalized == "3.sdvn.org") return "sdvn3";
        if (compact is "sbtc" or "sbtcisland") return "sbtc";
        return normalized;
    }
}
