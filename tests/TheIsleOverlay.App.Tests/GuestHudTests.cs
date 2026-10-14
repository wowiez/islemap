using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class GuestHudTests
{
    [Theory]
    [InlineData(56d, 56d, 100d, "0", "56 / 56")]
    [InlineData(null, null, 94d, "0.#", "94%")]
    [InlineData(56d, null, null, "0", "56 / -")]
    [InlineData(757.9d, null, null, "0.#", "757.9 / -")]
    [InlineData(null, null, null, "0.#", "- / -")]
    public void VitalText_ShowsPacketValueBeforeItsMaximumArrives(
        double? current, double? maximum, double? percent, string format, string expected) =>
        Assert.Equal(expected, MainWindow.VitalText(new(current, maximum, percent, false), format));

    [Fact]
    public void BarPercent_UsesHighestSeenValueUntilTheMaximumArrives()
    {
        var peak = 0d;
        Assert.Equal(100d, MainWindow.BarPercent(new(1000d, null, null, false), ref peak));   // full stamina, max pending
        Assert.Equal(50d, MainWindow.BarPercent(new(500d, null, null, false), ref peak));     // drains against the peak
        Assert.Equal(0d, MainWindow.BarPercent(new(0d, null, null, false), ref peak));
        Assert.Equal(25d, MainWindow.BarPercent(new(250d, 1000d, 25d, false), ref peak));      // real percentage wins
        Assert.Null(MainWindow.BarPercent(new(null, null, null, false), ref peak));
        var fresh = 0d;
        Assert.Null(MainWindow.BarPercent(new(0d, null, null, false), ref fresh));            // nothing to scale against yet
    }

    [Fact]
    public void DinosaurHeaderRow_HoldsSpeciesAndGrowthSoGuestModeCanHideBoth()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "MainWindow.xaml"));
        XName name = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";
        var row = Assert.Single(document.Descendants(), e => (string?)e.Attribute(name) == "DinosaurHeaderRow");
        Assert.Contains(row.Descendants(), e => (string?)e.Attribute(name) == "SpeciesLabel");
        Assert.Contains(row.Descendants(), e => (string?)e.Attribute(name) == "GrowthLabel");
        Assert.DoesNotContain(row.Descendants(), e => (string?)e.Attribute(name) == "HealthBar");
    }

    [Fact]
    public void KillFeedSettings_StartHiddenUntilAnSbtcSessionIsKnown()
    {
        XName name = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";
        var overlay = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "MainWindow.xaml"));
        var row = Assert.Single(overlay.Descendants(), e => (string?)e.Attribute(name) == "MapKillFeedSettingRow");
        Assert.Equal("Collapsed", (string?)row.Attribute("Visibility"));
        Assert.Contains(row.Descendants(), e => (string?)e.Attribute(name) == "MapKillFeedToggleButton");
        var guide = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "GuideWindow.xaml"));
        var card = Assert.Single(guide.Descendants(), e => (string?)e.Attribute(name) == "GuideMapKillFeedCard");
        Assert.Contains(card.Descendants(), e => (string?)e.Attribute(name) == "GuideMapKillFeedButton");
    }
}
