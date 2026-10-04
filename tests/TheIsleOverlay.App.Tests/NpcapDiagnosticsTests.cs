namespace TheIsleOverlay.App.Tests;

public sealed class NpcapDiagnosticsTests
{
    [Fact]
    public void FormatPorts_BoundsLongListsSoWeightStatusRemainsReadable()
    {
        var text = NpcapDiagnostics.FormatPorts(Enumerable.Range(54950, 80).Reverse().ToArray());
        Assert.Equal("54950,54951,54952,54953,54954,54955… (80 cổng)", text);
        Assert.Equal("chưa thấy tiến trình game", NpcapDiagnostics.FormatPorts([]));
        Assert.Equal("50123,50124", NpcapDiagnostics.FormatPorts([50124, 50123]));
    }
}
