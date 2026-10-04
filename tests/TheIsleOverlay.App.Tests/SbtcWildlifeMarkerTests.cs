using System.Windows.Media;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class SbtcWildlifeMarkerTests
{
    [Theory]
    [InlineData(1d, 0.8d, 18d)]
    [InlineData(4d, 0.8d, 38d)]
    [InlineData(8d, 0.8d, 48d)]
    [InlineData(1d, 0.55d, 18d)]
    [InlineData(4d, 0.55d, 38d)]
    [InlineData(8d, 0.55d, 48d)]
    public void LargeMapSizing_PreservesReadableScreenSizeInBothWindowLayouts(double zoom, double fit, double expected)
    {
        var size = SbtcWildlifeMarker.LargeMapSize(zoom, fit);
        Assert.Equal(expected, size * zoom * fit, precision: 8);
    }

    [Theory]
    [InlineData("Boar")]
    [InlineData("Deer")]
    [InlineData("Goat")]
    [InlineData("Ceratosaurus")]
    [InlineData("FishSchool")]
    public void SpeciesArtwork_IsBundledAndHasMultipleColouredParts(string species)
    {
        var image = Assert.IsType<DrawingImage>(SbtcWildlifeMarker.ImageFor(species, "#FFFFFF"));
        Assert.True(image.IsFrozen);
        Assert.True(Assert.IsType<DrawingGroup>(image.Drawing).Children.Count > 3);
        Assert.Same(image, SbtcWildlifeMarker.ImageFor(species.ToLowerInvariant(), "#000000"));
    }

    [Fact]
    public void UnknownSpecies_HasVisibleFallbackWithoutBreakingTheMap()
    {
        var image = SbtcWildlifeMarker.ImageFor("Unknown new animal", "invalid-colour");
        Assert.True(image.IsFrozen);
        Assert.True(image.Width > 0d && image.Height > 0d);
    }

    [Fact]
    public void WildlifePins_RemainSeparateAndHaveNoMergedSpeciesLabel()
    {
        var features = SbtcZoneOverlay.Create("SBTC Island",
        [
            new MapPointOfInterestTelemetry { Name = "Deer", CategoryId = "wildlife", Points = [new MapPoint(0.1d, 0.2d)] },
            new MapPointOfInterestTelemetry { Name = "Deer", CategoryId = "wildlife", Points = [new MapPoint(0.3d, 0.4d)] }
        ], hasHostZones: true);
        Assert.Equal(2, features.Count);
        Assert.All(features, feature => Assert.Equal(SbtcZoneKind.Wildlife, feature.Kind));
        Assert.Empty(SbtcZoneOverlay.CreateLabels(features));
        Assert.NotEqual(SbtcZoneOverlay.Signature(features), SbtcZoneOverlay.Signature([features[0]]));
    }
}
