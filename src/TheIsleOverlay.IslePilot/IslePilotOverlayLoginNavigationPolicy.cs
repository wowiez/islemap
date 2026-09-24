namespace TheIsleOverlay.IslePilot;

public static class IslePilotOverlayLoginNavigationPolicy
{
    private static readonly string[] HttpsHostSuffixes =
    [
        "islepilot.eu",
        "steamcommunity.com",
        "steampowered.com"
    ];

    public static bool IsAllowed(string? rawUri) => IsAllowed(rawUri, null);

    public static bool IsAllowed(string? rawUri, Uri? serviceBaseUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(
                uri.Scheme,
                IslePilotOverlayAuthService.CallbackScheme,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Servers hosted on their own domain (for example 3.sdvn.org) keep the
        // Steam handshake on that host.
        if (serviceBaseUri is not null &&
            string.Equals(uri.IdnHost, serviceBaseUri.IdnHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HttpsHostSuffixes.Any(suffix =>
            string.Equals(uri.IdnHost, suffix, StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase));
    }
}
