using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class PrimeQuestPresentationTests
{
    [Theory]
    [InlineData("Visit a Sanctuary as a juvenile", "Ghé Sanctuary khi còn non")]
    [InlineData("Get nested in", "Được sinh ra từ tổ")]
    [InlineData("Get perfect diet (1% of each)", "Đạt đủ 3 chất dinh dưỡng (mỗi chất ≥ 1%)")]
    [InlineData("Visit Mass Migration zone", "Ghé vùng Đại di cư")]
    [InlineData("Never get Infertile", "Không bị Vô sinh")]
    [InlineData("Never get Muscle spasms", "Không bị Co thắt cơ")]
    public void Create_TranslatesKnownPrimeQuestNames(string source, string expected)
    {
        var result = PrimeQuestPresentation.Create(new PrimeQuestTelemetry { Name = source });

        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void Create_DoesNotInventPerQuestProgress()
    {
        var result = PrimeQuestPresentation.Create(new PrimeQuestTelemetry
        {
            Name = "Visit 4 Patrol zones",
            Done = false
        });

        Assert.Equal("Ghé 4 vùng Tuần tra", result.Text);
        Assert.DoesNotContain("/4", result.Text);
        Assert.False(result.Completed);
    }

    [Fact]
    public void Create_UsesOnlyTheCompletionFlag()
    {
        var result = PrimeQuestPresentation.Create(new PrimeQuestTelemetry
        {
            Name = "Visit 2 Migration zones",
            Done = true
        });

        Assert.Equal("Ghé 2 vùng Di cư", result.Text);
        Assert.True(result.Completed);
    }
}
