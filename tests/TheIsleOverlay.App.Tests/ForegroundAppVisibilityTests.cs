using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class ForegroundAppVisibilityTests
{
    [Theory]
    [InlineData("TheIsle")]
    [InlineData("theisle")]
    [InlineData("TheIsleClient-Win64-Shipping")]
    public void GameProcesses_AreAllowed(string processName) =>
        Assert.True(ForegroundAppVisibility.IsAllowedProcessName(processName));

    [Theory]
    [InlineData("explorer")]
    [InlineData("chrome")]
    [InlineData("")]
    public void OtherProcesses_AreHidden(string processName) =>
        Assert.False(ForegroundAppVisibility.IsAllowedProcessName(processName));
}
