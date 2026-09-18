using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

internal static class NpcapGameCoordinateTransform
{
    public static WorldLocation ToAssetLocation(WorldLocation unrealLocation) => new()
    {
        // Verified against simultaneous SBTC packet capture and IslePilot REST:
        // the movement RPC already uses the same X/Y order as Asset Location.
        // Swapping these axes sends North Jungle coordinates to Mud Flats.
        X = unrealLocation.X,
        Y = unrealLocation.Y,
        Z = unrealLocation.Z
    };
}
