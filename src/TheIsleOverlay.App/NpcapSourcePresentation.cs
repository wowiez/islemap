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

    // The action is the only way to (re)start the Npcap driver, so it is offered in
    // every state the user can act on - including a capture that opened the card but
    // never produced a position, which is exactly when the driver needs a nudge.
    // "Stopped" means the source is switched off and the toggle is the right control.
    public static bool ShouldShowAction(bool enabled, NpcapSourceStatus status) =>
        enabled && status is NpcapSourceStatus.Unavailable
            or NpcapSourceStatus.Faulted
            or NpcapSourceStatus.Listening
            or NpcapSourceStatus.WaitingForGame;

    public static string ActionText(NpcapSourceStatus status) =>
        status switch
        {
            NpcapSourceStatus.Unavailable => "TẢI NPCAP",
            NpcapSourceStatus.Listening or NpcapSourceStatus.WaitingForGame => "BẬT LẠI DRIVER",
            _ => "MỞ LẠI"
        };
}
