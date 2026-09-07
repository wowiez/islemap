using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public sealed record PrimeQuestDisplay(string Text, bool Completed);

public static class PrimeQuestPresentation
{
    public static PrimeQuestDisplay Create(PrimeQuestTelemetry quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        var rawName = string.IsNullOrWhiteSpace(quest.Name) ? "Prime objective" : quest.Name.Trim();

        return new PrimeQuestDisplay(
            Translate(rawName),
            quest.Done == true);
    }

    private static string Translate(string name)
    {
        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sanctuary")) return "Ghé Sanctuary khi còn non";
        if (normalized.Contains("nested")) return "Được sinh ra từ tổ";
        if (normalized.Contains("perfect diet")) return "Đạt đủ 3 chất dinh dưỡng (mỗi chất ≥ 1%)";
        if (normalized.Contains("mass migration")) return "Ghé vùng Đại di cư";
        if (normalized.Contains("migration") && normalized.Contains('2')) return "Ghé 2 vùng Di cư";
        if (normalized.Contains("patrol") && normalized.Contains('4')) return "Ghé 4 vùng Tuần tra";
        if (normalized.Contains("infertile")) return "Không bị Vô sinh";
        if (normalized.Contains("muscle spasm")) return "Không bị Co thắt cơ";
        if (normalized.Contains("raise children") || normalized.Contains("subadult")) return "Nuôi con tới Subadult";
        if (normalized.Contains("hypsi") || normalized.Contains("troodon") || normalized.Contains("beipi"))
            return "Chơi Hypsi / Troodon / Beipi / Dryo / Deino";
        return name;
    }
}
