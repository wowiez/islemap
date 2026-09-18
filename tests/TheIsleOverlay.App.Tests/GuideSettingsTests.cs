using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class GuideSettingsTests
{
    [Fact]
    public void VoiceFeature_RemainsDisabledUntilItsOverlayIsReady()
    {
        Assert.False(GuideWindow.VoiceFeatureEnabled);
    }

    [Fact]
    public void F8Guide_ContainsNpcapFallbackCopyAssetAndHotkeySettings()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "GuideWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";
        XName keyAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Key";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                name,
                StringComparison.Ordinal));

        Assert.NotNull(Control("SettingsNavButton"));
        Assert.NotNull(Control("SkinEditorNavButton"));
        Assert.NotNull(Control("SkinEditorPage"));
        Assert.NotNull(Control("SkinEditorModel"));
        Assert.NotNull(Control("VoiceNavButton"));
        Assert.NotNull(Control("VoicePage"));
        Assert.NotNull(Control("VoiceControl"));
        Assert.Equal("Collapsed", (string?)Control("VoiceNavButton").Attribute("Visibility"));
        Assert.Equal("VoiceNavButton_Click", (string?)Control("VoiceNavButton").Attribute("Click"));
        Assert.NotNull(Control("SkinBodyHex"));
        Assert.Equal("SkinHexInput_TextChanged", (string?)Control("SkinBodyHex").Attribute("TextChanged"));
        Assert.Equal("Collapsed", (string?)Control("SettingsPage").Attribute("Visibility"));
        Assert.NotNull(Control("NpcapStatusLabel"));
        Assert.Equal("NpcapToggleButton_Click", (string?)Control("NpcapToggleButton").Attribute("Click"));
        Assert.Equal("NpcapActionButton_Click", (string?)Control("NpcapActionButton").Attribute("Click"));
        Assert.Equal("AutoHideOutsideGameButton_Click", (string?)Control("AutoHideOutsideGameButton").Attribute("Click"));
        Assert.DoesNotContain(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "CopyAssetToggleButton", StringComparison.Ordinal));
        Assert.True(document.Descendants().Count(element =>
            string.Equals((string?)element.Attribute("Text"), "TỰ ĐỘNG", StringComparison.Ordinal)) >= 2);
        Assert.Contains(document.Descendants(), element =>
            string.Equals((string?)element.Attribute("Text"), "IslePilot Realtime + REST", StringComparison.Ordinal));

        var navStyle = Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(keyAttribute), "NavButton", StringComparison.Ordinal));
        Assert.Contains(navStyle.Descendants(), element =>
            element.Name.LocalName == "Border" && (string?)element.Attribute("CornerRadius") == "8");

        var actionStyle = Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(keyAttribute), "SettingsActionButton", StringComparison.Ordinal));
        Assert.Contains(actionStyle.Elements(), element =>
            element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("Property") == "MinWidth" &&
            (string?)element.Attribute("Value") == "64");
    }

    [Theory]
    [InlineData("https://islepilot.eu/p/sbtcisland/voice", true)]
    [InlineData("https://steamcommunity.com/openid/login", true)]
    [InlineData("https://store.steampowered.com/login", true)]
    [InlineData("https://discord.com/oauth2/authorize", true)]
    [InlineData("https://cdn.discordapp.com/assets/icon.png", true)]
    [InlineData("http://islepilot.eu/p/sbtcisland/voice", false)]
    [InlineData("https://islepilot.eu.example.com/p/sbtcisland/voice", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    public void SbtcVoice_OnlyNavigatesWithinTheOfficialLoginChain(string url, bool expected) =>
        Assert.Equal(expected, SbtcVoiceControl.IsAllowedNavigation(url));

}
