namespace TheIsleOverlay.Core;

public static class ClipboardRouteDestination
{
    public static bool TryParse(string? clipboardText, out MapPoint destination)
    {
        destination = default;
        if (!ClipboardCoordinateParser.TryParse(clipboardText, out var location))
        {
            return false;
        }

        destination = GatewayMapProjection.Project(location);
        return true;
    }
}
