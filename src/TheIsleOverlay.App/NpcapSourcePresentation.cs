namespace TheIsleOverlay.App;

internal static class NpcapSourcePresentation
{
    public static bool IsOwnPositionAuthoritative(
        bool enabled,
        NpcapSourceStatus status,
        bool hasPosition) =>
        enabled && hasPosition && status == NpcapSourceStatus.Live;

    public static string StatusText(bool enabled, NpcapSourceStatus status) =>
        !enabled
            ? "NPCAP · OFF"
            : status switch
            {
                NpcapSourceStatus.Live => "NPCAP · LIVE",
                NpcapSourceStatus.WaitingForGame or NpcapSourceStatus.Listening => "NPCAP · CONNECTING",
                NpcapSourceStatus.Unavailable => "NPCAP · NOT INSTALLED",
                NpcapSourceStatus.Faulted => "NPCAP · ERROR",
                _ => "NPCAP · STOPPED"
            };

    public static string PositionStatusOrFallback(
        bool enabled,
        NpcapSourceStatus status,
        string fallback) =>
        enabled && status is (NpcapSourceStatus.Live or
            NpcapSourceStatus.WaitingForGame or NpcapSourceStatus.Listening)
            ? StatusText(true, status)
            : fallback;

    public static bool ShouldShowAction(bool enabled, NpcapSourceStatus status) =>
        enabled && status is NpcapSourceStatus.Unavailable or NpcapSourceStatus.Faulted;

    public static string ActionText(NpcapSourceStatus status) =>
        status == NpcapSourceStatus.Unavailable ? "TẢI NPCAP" : "MỞ LẠI";
}
