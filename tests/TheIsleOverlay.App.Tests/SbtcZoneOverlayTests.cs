using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class SbtcZoneOverlayTests
{
    [Theory]
    [InlineData("SBTC Evrima", true)]
    [InlineData("[S.B.T.C] Gateway", true)]
    [InlineData("DinoVietNam", false)]
    [InlineData(null, false)]
    public void IsSbtcServer_MatchesOnlySbtcNames(string? server, bool expected) =>
        Assert.Equal(expected, SbtcZoneOverlay.IsSbtcServer(server));

    [Fact]
    public void Create_ClassifiesServerMapZonesAndKeepsTheirGeometry()
    {
        var features = SbtcZoneOverlay.Create(
            "SBTC Gateway",
            [
                Poi("Delta (MMZ)", "Migration Zones", 3),
                Poi("West Rail", "Patrol Zones", 4),
                Poi("Highlands", "Locations", 1),
                Poi("Sanctuary", "Sanctuaries", 8)
            ]);

        Assert.Collection(
            features,
            feature => Assert.Equal(SbtcZoneKind.Migration, feature.Kind),
            feature => Assert.Equal(SbtcZoneKind.Patrol, feature.Kind),
            feature => Assert.Equal(SbtcZoneKind.Sanctuary, feature.Kind));
        Assert.Equal(3, features[0].Points.Count);
        Assert.NotEmpty(SbtcZoneOverlay.Signature(features));
    }

    [Fact]
    public void Create_DoesNotShowZonesOnOtherServers()
    {
        var features = SbtcZoneOverlay.Create(
            "Another Gateway Server",
            [Poi("Delta", "Locations", 1)]);

        Assert.Empty(features);
    }

    [Fact]
    public void Create_IgnoresUnknownSinglePointPois()
    {
        var features = SbtcZoneOverlay.Create(
            "SBTC Gateway",
            [Poi("Random food spawn", "Food", 1)]);

        Assert.Empty(features);
    }

    [Fact]
    public void Create_UsesBundledFallbackWhenSbtcApiHasNoZones()
    {
        var fallback = new[]
        {
            new SbtcZoneFeature(
                "Delta (MMZ)",
                SbtcZoneKind.Migration,
                [new MapPoint(0.1, 0.1), new MapPoint(0.2, 0.1), new MapPoint(0.2, 0.2)])
        };

        var features = SbtcZoneOverlay.Create("SBTC Gateway", [], fallback);

        Assert.Same(fallback, features);
    }

    [Fact]
    public void Create_UsesGatewayZonesForAnyIslePilotServer()
    {
        var fallback = new[]
        {
            new SbtcZoneFeature(
                "Delta (MMZ)",
                SbtcZoneKind.Migration,
                [new MapPoint(0.1, 0.1), new MapPoint(0.2, 0.1), new MapPoint(0.2, 0.2)])
        };

        var features = SbtcZoneOverlay.Create(
            "DinoVietNam",
            [],
            fallback,
            isIslePilotServer: true);

        Assert.Same(fallback, features);
    }

    [Fact]
    public void Create_PrefersHostPoisForAnyIslePilotServer()
    {
        var features = SbtcZoneOverlay.Create(
            "DinoVietNam Premium",
            [Poi("Premium Patrol", "Patrol Zones", 4)],
            fallback: [],
            isIslePilotServer: true);

        var feature = Assert.Single(features);
        Assert.Equal("Premium Patrol", feature.Name);
    }

    [Fact]
    public void Create_PrefersSparseLiveApiDataOverStaleFallback()
    {
        var fallback = Enumerable.Range(0, 25)
            .Select(index => new SbtcZoneFeature(
                $"Zone {index}",
                SbtcZoneKind.Patrol,
                [new MapPoint(0.1, 0.1), new MapPoint(0.2, 0.1), new MapPoint(0.2, 0.2)]))
            .ToArray();

        var features = SbtcZoneOverlay.Create(
            "SBTC Gateway",
            [Poi("Only one API zone", "Patrol Zones", 3)],
            fallback);

        var live = Assert.Single(features);
        Assert.Equal("Only one API zone", live.Name);
    }

    [Fact]
    public void Create_MatchesSbtcDefaultVisibleCategoryCounts()
    {
        var pois = Enumerable.Range(0, 24).Select(index => Poi($"Location {index}", "Locations", 1))
            .Concat(Enumerable.Range(0, 6).Select(index => Poi($"Sanctuary {index}", "Sanctuaries", 1, "circle", 0.012, "#34d399")))
            .Concat(Enumerable.Range(0, 6).Select(index => Poi($"Migration {index}", "Migration Zones", 4, "polygon", 0.02, "#f59e0b")))
            .Concat(Enumerable.Range(0, 28).Select(index => Poi($"Patrol {index}", "Patrol Zones", 5, "polygon", 0.02, "#a78bfa")))
            .Concat(Enumerable.Range(0, 6).Select(index => Poi($"Hunting {index}", null, 1, "circle", 0.05, "#38bdf8")))
            .ToArray();

        var features = SbtcZoneOverlay.Create("SBTC ISLAND", pois);

        Assert.Equal(46, features.Count);
        Assert.Equal(6, features.Count(feature => feature.Kind == SbtcZoneKind.Sanctuary));
        Assert.Equal(6, features.Count(feature => feature.Kind == SbtcZoneKind.Migration));
        Assert.Equal(28, features.Count(feature => feature.Kind == SbtcZoneKind.Patrol));
        Assert.Equal(6, features.Count(feature => feature.Kind == SbtcZoneKind.Uncategorized));
        Assert.DoesNotContain(features, feature => feature.Kind == SbtcZoneKind.Location);
        var hunting = Assert.Single(features, feature => feature.Name == "Hunting 0");
        Assert.Equal("circle", hunting.Shape);
        Assert.Equal(0.05, hunting.Size);
        Assert.Equal("#38bdf8", hunting.Color);
    }

    [Fact]
    public void CreateLabels_DeduplicatesSameZoneNameAndKind()
    {
        var features = new[]
        {
            new SbtcZoneFeature(
                "West Rail",
                SbtcZoneKind.Patrol,
                [new MapPoint(0.1, 0.1), new MapPoint(0.2, 0.1), new MapPoint(0.2, 0.2)]),
            new SbtcZoneFeature(
                " west   rail ",
                SbtcZoneKind.Patrol,
                [new MapPoint(0.7, 0.7), new MapPoint(0.8, 0.7), new MapPoint(0.8, 0.8)]),
            new SbtcZoneFeature(
                "West Rail",
                SbtcZoneKind.Migration,
                [new MapPoint(0.4, 0.4)])
        };

        var labels = SbtcZoneOverlay.CreateLabels(features);

        Assert.Equal(2, labels.Count);
        var patrol = Assert.Single(labels, label => label.Kind == SbtcZoneKind.Patrol);
        Assert.Equal("West Rail", patrol.Name);
        Assert.Equal(0.466666d, patrol.Center.Left, 5);
        Assert.Equal(0.433333d, patrol.Center.Top, 5);
    }

    private static MapPointOfInterestTelemetry Poi(
        string name,
        string? category,
        int pointCount,
        string? shape = null,
        double? size = null,
        string? color = null) => new()
    {
        Name = name,
        CategoryName = category,
        CategoryId = category,
        Shape = shape,
        Size = size,
        Color = color,
        Points = Enumerable.Range(0, pointCount)
            .Select(index => new MapPoint(0.1d + index * 0.01d, 0.2d + index * 0.01d))
            .ToArray()
    };
}
