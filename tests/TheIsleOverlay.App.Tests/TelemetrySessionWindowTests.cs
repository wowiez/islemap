using System.Reflection;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class TelemetrySessionWindowTests
{
    [Fact]
    public void MainWindow_ConsumesSessionInsteadOfOwningPollingTimer()
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.Null(typeof(MainWindow).GetField("_refreshTimer", fields));
        var sessionField = typeof(MainWindow).GetField("_telemetrySession", fields);
        Assert.NotNull(sessionField);
        Assert.Equal(typeof(ITelemetrySession), sessionField.FieldType);
        Assert.NotNull(typeof(MainWindow).GetConstructor([typeof(ITelemetrySession), typeof(string)]));
    }

    [Fact]
    public void LiveServerHeading_FollowsWithinOneFastVisualResponseWindow()
    {
        // The live heading is followed on every rendered frame instead of by a
        // queued one-shot animation, so the guarantee is the follow time constant.
        var rateField = typeof(MainWindow).GetField(
            "HeadingFollowRate",
            BindingFlags.Static | BindingFlags.NonPublic);
        var rate = Assert.IsType<double>(rateField?.GetValue(null));

        Assert.InRange(1000d / rate, 1d, 80d);
    }
}
