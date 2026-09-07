using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class HomeSteamLoginTests
{
    [Fact]
    public void SteamLogin_UsesAnIsolatedProfileAndOffersAccountSwitching()
    {
        Assert.NotEqual(AppPaths.WebView2Profile, AppPaths.IslePilotWebView2Profile);

        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "IslePilotSteamLoginWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";
        var button = Assert.Single(document.Descendants(), element =>
            string.Equals(
                (string?)element.Attribute(nameAttribute),
                "SwitchAccountButton",
                StringComparison.Ordinal));

        Assert.Equal("ĐỔI TÀI KHOẢN", (string?)button.Attribute("Content"));
        Assert.Equal("SwitchAccountButton_Click", (string?)button.Attribute("Click"));
    }

    [Fact]
    public void Home_ShowsIslePilotAndTheTwoRequiredDirectWebsiteConnections()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals((string?)element.Attribute(nameAttribute), name, StringComparison.Ordinal));

        Assert.NotEqual("Collapsed", (string?)Control("SteamLoginButton").Attribute("Visibility"));
        Assert.NotNull(Control("SteamAccountSelector"));
        Assert.Equal(
            "AddSteamAccountButton_Click",
            (string?)Control("AddSteamAccountButton").Attribute("Click"));
        Assert.Equal("XÓA TÀI KHOẢN", (string?)Control("LogoutSteamButton").Attribute("Content"));
        Assert.NotEqual("Collapsed", (string?)Control("EraSourceButton").Attribute("Visibility"));
        Assert.Equal("era", (string?)Control("EraSourceButton").Attribute("Tag"));
        Assert.NotEqual("Collapsed", (string?)Control("PandoraSourceButton").Attribute("Visibility"));
        Assert.Equal("pandora", (string?)Control("PandoraSourceButton").Attribute("Tag"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => new[] { "DinoSourceButton", "PremiumSourceButton", "HoHoSourceButton" }
                .Contains((string?)element.Attribute(nameAttribute), StringComparer.Ordinal));
    }

    [Fact]
    public void DinoVietnam_UsesItsOwnIslePilotWebsiteSession()
    {
        var source = TelemetrySourceDefinition.DinoVietnam;

        Assert.Equal("https://dinovietnam.islepilot.eu/", source.BaseUri.AbsoluteUri);
        Assert.Equal(
            "https://dinovietnam.islepilot.eu/api/player/steam/login?redirect=%2Fmap",
            source.LoginUri.AbsoluteUri);
        Assert.Equal(TelemetrySourceKind.IslePilot, source.Kind);
        Assert.Equal("dinovietnam", source.ServerSlug);
        Assert.Equal("islepilot_player", source.CookieName);
        Assert.Equal(TimeSpan.FromSeconds(2), source.PollingInterval);
        Assert.Same(source, TelemetrySourceDefinition.FromId("dinovietnam"));
    }

    [Fact]
    public void DinoVietnamPremium_UsesItsOwnIslePilotWebsiteSession()
    {
        var source = TelemetrySourceDefinition.DinoVietnamPremium;

        Assert.Equal("https://dinovietnampremium.islepilot.eu/", source.BaseUri.AbsoluteUri);
        Assert.Equal(
            "https://dinovietnampremium.islepilot.eu/api/player/steam/login?redirect=%2Fmap",
            source.LoginUri.AbsoluteUri);
        Assert.Equal(TelemetrySourceKind.IslePilot, source.Kind);
        Assert.Equal("dinovietnampremium", source.ServerSlug);
        Assert.Equal("islepilot_player", source.CookieName);
        Assert.Equal(TimeSpan.FromSeconds(2), source.PollingInterval);
        Assert.Same(source, TelemetrySourceDefinition.FromId("dinovietnampremium"));
    }

    [Fact]
    public void Pandora_SourceCapturesTheCompleteHostSessionForItsExpressApi()
    {
        var source = TelemetrySourceDefinition.Pandora;

        Assert.Equal("https://islapandora.eu/", source.BaseUri.AbsoluteUri);
        Assert.Equal("https://islapandora.eu/live-map", source.LoginUri.AbsoluteUri);
        Assert.Equal(TelemetrySourceKind.Pandora, source.Kind);
        Assert.Equal(TimeSpan.FromSeconds(1), source.PollingInterval);
        Assert.True(source.CaptureAllHostCookies);
        Assert.Same(source, TelemetrySourceDefinition.FromId("pandora"));
    }

    [Fact]
    public void PollingSources_UseLowLatencyRefreshIntervals()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), TelemetrySourceDefinition.EraGaming.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), TelemetrySourceDefinition.Pandora.PollingInterval);
    }

    [Fact]
    public void Home_DoesNotContainDonateOrDeveloperPromotionPanels()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.xaml"));
        var allText = string.Join(
            " ",
            document.Descendants().SelectMany(element => new[]
            {
                (string?)element.Attribute("Text"),
                (string?)element.Attribute("Content")
            }));

        Assert.DoesNotContain("PHÁT TRIỂN", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DONATE", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FACEBOOK", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("YOUTUBE", allText, StringComparison.OrdinalIgnoreCase);
    }
}
