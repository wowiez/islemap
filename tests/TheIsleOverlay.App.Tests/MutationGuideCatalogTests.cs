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
    public void GuideWindow_HasOverviewMapAndMutationPages()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "GuideWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";
        Assert.Equal("True", (string?)document.Root!.Attribute("AllowsTransparency"));
        Assert.Equal("NoResize", (string?)document.Root.Attribute("ResizeMode"));

        foreach (var name in new[]
                 {
                     "OverviewPage", "MapPage", "MutationPage", "SpeciesComboBox", "MutationCards",
                     "OverviewNavButton", "MapNavButton", "MutationNavButton", "KillFeedPage",
                     "KillFeedNavButton", "KillFeedSpeciesFilter", "KillFeedRows"
                 })
        {
            Assert.Single(document.Descendants(), element =>
                string.Equals((string?)element.Attribute(nameAttribute), name, StringComparison.Ordinal));
        }

        // The SBTC vault and native skin editor are available inside F8.
        foreach (var name in new[] { "GaragePage", "SkinEditorPage", "GarageNavButton", "SkinEditorNavButton" })
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
                (string?)element.Attribute("Content"),
                "{Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType=ComboBox}}",
                StringComparison.Ordinal));
        Assert.Equal("#FFFFFF", (string?)selectionText.Attribute("TextElement.Foreground"));
        Assert.Equal("{Binding SelectionBoxItemTemplate, RelativeSource={RelativeSource AncestorType=ComboBox}}",
            (string?)selectionText.Attribute("ContentTemplate"));
        Assert.Equal("{Binding ItemTemplateSelector, RelativeSource={RelativeSource AncestorType=ComboBox}}",
            (string?)selectionText.Attribute("ContentTemplateSelector"));
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ScrollViewer" &&
            string.Equals((string?)element.Attribute("VerticalScrollBarVisibility"), "Hidden", StringComparison.Ordinal));
        Assert.True(document.Descendants().Count(element => element.Name.LocalName == "Path") >= 7);
    }

}
