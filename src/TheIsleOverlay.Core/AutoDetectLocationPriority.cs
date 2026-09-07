namespace TheIsleOverlay.Core;

public static class AutoDetectLocationPriority
{
    public static bool HasNewLocation(
        WorldLocation? locationAtCopy,
        DateTimeOffset? mapUpdatedAtCopy,
        WorldLocation? currentLocation,
        DateTimeOffset? currentMapUpdatedAt)
    {
        if (locationAtCopy is null || currentLocation is null)
        {
            return false;
        }

        if (mapUpdatedAtCopy is not null &&
            (currentMapUpdatedAt is null || currentMapUpdatedAt <= mapUpdatedAtCopy))
        {
            return false;
        }

        // A newer Live Map coordinate must take priority even when the player
        // moved only a tiny amount. Cache only an exactly repeated X/Y pair.
        return currentLocation.X != locationAtCopy.X ||
               currentLocation.Y != locationAtCopy.Y;
    }
}
