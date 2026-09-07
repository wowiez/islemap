using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class ClipboardCoordinateParserTests
{
    [Theory]
    [InlineData("27,840.493, -242,657.129, 30,707.498", 27840.493, -242657.129, 30707.498)]
    [InlineData("(Lat: -231,654.353 Long: 52,099.673 Alt: 29,328.085)", -231654.353, 52099.673, 29328.085)]
    [InlineData("27.840,493; -242.657,129; 30.707,498", 27840.493, -242657.129, 30707.498)]
    [InlineData("-371,863.941, 209,441.418, 24,865.853", -371863.941, 209441.418, 24865.853)]
    public void TryParse_AcceptsAssetLocationFormats(string text, double x, double y, double z)
    {
        Assert.True(ClipboardCoordinateParser.TryParse(text, out var location));
        Assert.Equal(x, location.X, precision: 6);
        Assert.Equal(y, location.Y, precision: 6);
        Assert.Equal(z, location.Z!.Value, precision: 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("version 1.2.3")]
    [InlineData("2026-08-23")]
    [InlineData("9999999999, 123")]
    public void TryParse_IgnoresUnrelatedClipboardText(string? text) =>
        Assert.False(ClipboardCoordinateParser.TryParse(text, out _));
}
