using System.IO;
using System.Xml.Linq;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App.Tests;

public sealed class MutationGuideCatalogTests
{
    [Fact]
    public void Catalog_CoversTwentyThreeUniqueSpeciesWithThreePicksPerTrack()
    {
        Assert.Equal(23, MutationGuideCatalog.Species.Count);
        Assert.Equal(
            MutationGuideCatalog.Species.Count,
            MutationGuideCatalog.Species.Select(species => species.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var species in MutationGuideCatalog.Species)
        {
            Assert.False(string.IsNullOrWhiteSpace(species.Summary));
            foreach (var track in Enum.GetValues<MutationTrack>())
            {
                var recommendations = species.Recommendations
                    .Where(item => item.Track == track)
                    .OrderBy(item => item.Priority)
                    .ToArray();
                Assert.Equal(3, recommendations.Length);
                Assert.Equal([1, 2, 3], recommendations.Select(item => item.Priority));
                Assert.All(recommendations, recommendation =>
                {
                    Assert.Contains(recommendation.Name, recommendation.DisplayName, StringComparison.Ordinal);
                    Assert.Contains(recommendation.VietnameseName, recommendation.DisplayName, StringComparison.Ordinal);
                });
            }
        }
    }

    [Fact]
    public void GuideWindow_HasOverviewMapGarageAndMutationPages()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "GuideWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";
        Assert.Equal("True", (string?)document.Root!.Attribute("AllowsTransparency"));
        Assert.Equal("NoResize", (string?)document.Root.Attribute("ResizeMode"));

        foreach (var name in new[]
                 {
                     "OverviewPage", "MapPage", "GaragePage", "MutationPage", "SpeciesComboBox", "MutationCards",
                     "OverviewNavButton", "MapNavButton", "GarageNavButton", "MutationNavButton",
                     "GarageCards", "GarageRefreshButton", "GarageParkButton", "GarageStatePanel",
                     "GarageModeLabel", "GarageCommandOverlay", "GarageCommandConfirmButton"
                 })
        {
            Assert.Single(document.Descendants(), element =>
                string.Equals((string?)element.Attribute(nameAttribute), name, StringComparison.Ordinal));
        }

        Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "GuideMapHost", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Descendants(), element =>
            string.Equals((string?)element.Attribute("Source"), "Assets/GatewayMap.webp", StringComparison.Ordinal));
        Assert.Single(document.Descendants(), element => element.Name.LocalName == "Popup");
        var selectionText = Assert.Single(document.Descendants(), element =>
            string.Equals(
                (string?)element.Attribute("Text"),
                "{Binding SelectedItem.Name, RelativeSource={RelativeSource AncestorType=ComboBox}}",
                StringComparison.Ordinal));
        Assert.Equal("#FFFFFF", (string?)selectionText.Attribute("Foreground"));
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ScrollViewer" &&
            string.Equals((string?)element.Attribute("VerticalScrollBarVisibility"), "Hidden", StringComparison.Ordinal));
        Assert.True(document.Descendants().Count(element => element.Name.LocalName == "Path") >= 7);
    }

    [Fact]
    public void GaragePresentation_FormatsBilingualVitalsAndSanitizesPalette()
    {
        var card = GarageDinoCardPresentation.From(new IslePilotOverlayGarageDinoDto
        {
            Name = "Tank",
            Species = "Pachycephalosaurus",
            Gender = "Male",
            Growth = 120,
            Health = 95,
            Hunger = 64.5,
            Thirst = -4,
            Stamina = 80,
            IsPrimeElder = true,
            Palette = new IslePilotOverlayGaragePaletteDto
            {
                Display = "#AABBCC",
                Body = "not-a-color",
                Markings = "#112233"
            }
        });

        Assert.Equal("Tank", card.DisplayName);
        Assert.Equal("Đực · Male", card.Gender);
        Assert.Equal(100, card.Growth);
        Assert.Equal("100%", card.GrowthLabel);
        Assert.Equal("64.5%", card.HungerLabel);
        Assert.Equal(0, card.Thirst);
        Assert.Equal("PRIME ELDER", card.PrimeLabel);
        Assert.False(card.LiveSwap);
        Assert.Equal("LẤY RA", card.ActionLabel);
        Assert.Equal("#AABBCC", card.AccentColor);
        Assert.Equal("#AABBCC", card.BodyColor);
        Assert.Equal(["#AABBCC", "#112233"], card.PaletteColors);
        Assert.True(card.HasPreview);
        Assert.Equal(
            "pack://application:,,,/Assets/DinoThumbnails/pachycephalosaurus.png",
            card.PreviewUrl);
    }

    [Fact]
    public void GaragePresentation_ConvertsNormalizedIslePilotRatiosToPercent()
    {
        var card = GarageDinoCardPresentation.From(new IslePilotOverlayGarageDinoDto
        {
            Id = "dino-1",
            Species = "Pachycephalosaurus",
            Growth = 1,
            Health = 1,
            Hunger = 0.64,
            Thirst = 0.7,
            Stamina = 0.82
        }, liveSwap: true);

        Assert.Equal("dino-1", card.DinoId);
        Assert.Equal(100, card.Growth);
        Assert.Equal("100%", card.GrowthLabel);
        Assert.Equal("64%", card.HungerLabel);
        Assert.Equal("70%", card.ThirstLabel);
        Assert.Equal("82%", card.StaminaLabel);
        Assert.True(card.LiveSwap);
        Assert.Equal("ĐỔI SANG", card.ActionLabel);
    }

    [Fact]
    public void GaragePresentation_UsesCleanFallbackWhenIslePilotHasNoModel()
    {
        var card = GarageDinoCardPresentation.From(new IslePilotOverlayGarageDinoDto
        {
            Species = "Baryonyx"
        });

        Assert.False(card.HasPreview);
        Assert.Empty(card.PreviewUrl);
    }
}
