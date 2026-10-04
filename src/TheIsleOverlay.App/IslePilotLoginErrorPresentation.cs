namespace TheIsleOverlay.App;

public static class IslePilotLoginErrorPresentation
{
    // A host can answer the overlay sign-in with a JSON error page instead of the
    // Steam handoff (for example when the server owner switched the overlay off).
    // The login window shows that page, so turn the payload into a sentence.
    public static string? DescribeHostError(string? pageText, string host)
    {
        if (string.IsNullOrWhiteSpace(pageText))
        {
            return null;
        }

        if (pageText.Contains("overlay_disabled", StringComparison.OrdinalIgnoreCase))
        {
            return $"Server {host} đang tắt tính năng overlay (overlay_disabled). " +
                   "Báo admin server bật lại, hoặc dùng Copy Asset + NPCAP để xem vị trí.";
        }

        if (pageText.Contains("\"error\"", StringComparison.OrdinalIgnoreCase) &&
            pageText.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return $"Phiên đăng nhập cho {host} không còn hợp lệ. Hãy đăng nhập lại bằng Steam.";
        }

        if (pageText.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
        {
            return $"Server {host} trả về lỗi khi mở overlay. Xem chi tiết ngay trong cửa sổ này.";
        }

        return null;
    }
}
