using System.Globalization;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public static class FriendPresentation
{
    public static string NameAndDistance(string name, FriendPresence? friend, MapPoint? own)
    {
        if (friend is not { Online: true, SameServer: true, Position: { } position } || own is not { } point
            || !Valid(point.Left, point.Top) || !Valid(position.X, position.Y)) return name;
        var distance = GatewayMapProjection.DistanceMeters(point, new MapPoint(position.X, position.Y));
        var text = distance < 1000 ? $"{Math.Round(distance).ToString(CultureInfo.InvariantCulture)} m"
            : $"{(distance / 1000).ToString("0.0", CultureInfo.InvariantCulture)} km";
        return $"{name} · {text}";
    }

    public static string Detail(FriendPresence? friend) => friend is not { Online: true } ? "Ngoại tuyến"
        : string.Join(" · ", new[] { friend.Species, !friend.SameServer ? "Khác server / chưa trong game" : friend.Position is null ? "Vị trí đang ẩn" : null }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static bool Valid(double x, double y) => double.IsFinite(x) && double.IsFinite(y) && x is >= 0 and <= 1 && y is >= 0 and <= 1;
}
