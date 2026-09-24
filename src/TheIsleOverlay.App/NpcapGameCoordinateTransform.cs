using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

internal static class NpcapGameCoordinateTransform
{
    public static WorldLocation ToAssetLocation(WorldLocation unrealLocation) => new()
    {
        // Preserves Unreal X (North/South) and Y (East/West) matching Copy Asset Location structure.
        // GatewayMapProjection projects location.Y to Left (Horizontal) and location.X to Top (Vertical).
        X = unrealLocation.X,
        Y = unrealLocation.Y,
        Z = unrealLocation.Z
    };
}
