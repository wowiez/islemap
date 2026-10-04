using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class SbtcZoneOverlayTests
{
    [Fact]
    public void Create_UsesIslePilotPurpleForBluePatrolsAndPreservesOtherColors()
    {
        var zones = SbtcZoneOverlay.Create("SBTC ISLAND",
            [Poi("Patrol", "Patrol Zones", 4, "polygon", color: "#7aa2ff"),
             Poi("Migration", "Migration Zones", 4, "polygon", color: "#f59e0b"),
             Poi("Sanctuary", "Sanctuaries", 1, "circle", color: "#34d399"),
             Poi("Water", "waters", 1, "label", color: "#38bdf8")],
            hasHostZones: true);

        Assert.Equal("#A78BFA", zones[0].Color);
        Assert.Equal("#f59e0b", zones[1].Color);
        Assert.Equal("#34d399", zones[2].Color);
        Assert.Equal("#38bdf8", zones[3].Color);
        Assert.Equal("#A78BFA", Assert.Single(SbtcZoneOverlay.CreateLabels(zones),
            label => label.Kind == SbtcZoneKind.Patrol).Color);
    }

    [Fact]
    public void Signature_ChangesWhenAnyVertexOrLabelAnchorChanges()
    {
        var zone = new SbtcZoneFeature("Zone", SbtcZoneKind.Sanctuary,
            [new MapPoint(0.1, 0.2), new MapPoint(0.3, 0.2), new MapPoint(0.3, 0.4)]);
        var changed = zone with { Points = [zone.Points[0], new MapPoint(0.35, 0.2), zone.Points[2]] };
        Assert.NotEqual(SbtcZoneOverlay.Signature([zone]), SbtcZoneOverlay.Signature([changed]));
        Assert.NotEqual(SbtcZoneOverlay.Signature([zone]),
            SbtcZoneOverlay.Signature([zone with { LabelLocation = new MapPoint(0.2, 0.3) }]));
    }

    [Fact]
    public void Labels_KeepSeparateWebAnchorsForRepeatedNames()
    {
        var zones = new[]
        {
            new SbtcZoneFeature("Highland", SbtcZoneKind.Uncategorized, [new MapPoint(0.1, 0.1)],
                LabelLocation: new MapPoint(0.2, 0.2)),
            new SbtcZoneFeature("Highland", SbtcZoneKind.Uncategorized, [new MapPoint(0.8, 0.8)],
                LabelLocation: new MapPoint(0.7, 0.7))
        };
        var labels = SbtcZoneOverlay.CreateLabels(zones);
        Assert.Equal(2, labels.Count);
        Assert.Equal(new MapPoint(0.2, 0.2), labels[0].Center);
        Assert.Equal(new MapPoint(0.7, 0.7), labels[1].Center);
    }

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
            ],
            hasHostZones: true);

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
            [Poi("Delta", "Locations", 1)],
            hasHostZones: true);

        Assert.Empty(features);
    }

    [Fact]
    public void Create_IgnoresUnknownSinglePointPois()
    {
        var features = SbtcZoneOverlay.Create(
            "SBTC Gateway",
            [Poi("Random food spawn", "Food", 1)],
            hasHostZones: true);

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

        var features = SbtcZoneOverlay.Create("SBTC Gateway", [], fallback, hasHostZones: true);

        Assert.Same(fallback, features);
    }

    [Fact]
    public void Create_KeepsBundledRegionsAlongsideAnAiOnlyOrPlaceOnlyFeed()
    {
        SbtcZoneFeature[] fallback =
        [
            new("Patrol", SbtcZoneKind.Patrol, [new(.1,.1), new(.2,.1), new(.2,.2)]),
            new("Migration", SbtcZoneKind.Migration, [new(.3,.3), new(.4,.3), new(.4,.4)])
        ];
        var animal = Poi("Deer", "wildlife", 1);
        var features = SbtcZoneOverlay.Create("SBTC Island", [animal], fallback, hasHostZones: true);
        Assert.Equal(3, features.Count);
        Assert.Same(fallback[0], features[0]);
        Assert.Same(fallback[1], features[1]);
        Assert.Equal(SbtcZoneKind.Wildlife, features[2].Kind);

        var places = SbtcZoneOverlay.Create("SBTC Island",
            [animal, Poi("Cascades", "waters", 1, "label")], fallback, hasHostZones: true);
        Assert.Equal(4, places.Count);
        Assert.Equal("Cascades", places[3].Name);
        // A successful live region set still replaces the bundled geometry.
        var recovered = SbtcZoneOverlay.Create("SBTC Island",
            [animal, Poi("Live Patrol", "Patrol Zones", 3)], fallback, hasHostZones: true);
        Assert.Equal(2, recovered.Count);
        Assert.DoesNotContain(recovered, feature => ReferenceEquals(feature, fallback[0]));
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
            isIslePilotServer: true,
            hasHostZones: true);

        Assert.Same(fallback, features);
    }

    [Fact]
    public void Create_PrefersHostPoisForAnyIslePilotServer()
    {
        var features = SbtcZoneOverlay.Create(
            "DinoVietNam Premium",
            [Poi("Premium Patrol", "Patrol Zones", 4)],
            fallback: [],
            isIslePilotServer: true,
            hasHostZones: true);

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
            fallback,
            hasHostZones: true);

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

        var features = SbtcZoneOverlay.Create("SBTC ISLAND", pois, hasHostZones: true);

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
    public void Create_KeepsTheFixedZoneSetWhenTheHostSendsNothing()
    {
        var fallback = new[]
        {
            new SbtcZoneFeature(
                "Delta (MMZ)",
                SbtcZoneKind.Migration,
                [new MapPoint(0.1, 0.1), new MapPoint(0.2, 0.1), new MapPoint(0.2, 0.2)])
        };

        // The polygons are the same on every server, so the fixed set is drawn even when
        // the host publishes no zones at all.
        Assert.Single(SbtcZoneOverlay.Create("SBTC Gateway", [], fallback));
        Assert.Single(SbtcZoneOverlay.Create("DinoVietNam", [], fallback, isIslePilotServer: true));

        // When the host does send zones but its feed is stale, the fixed set stands in.
        Assert.Single(SbtcZoneOverlay.Create("SBTC Gateway", [Poi("Delta", "Migration Zones", 3)], fallback));
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

    [Fact]
    public void BundledZones_RenderWithoutAKnownServer()
    {
        // The bundled polygons belong to the map, so they are drawn even before a
        // dinosaur (and therefore a server name) is known.
        var bundled = new[]
        {
            new SbtcZoneFeature(
                "Migration zone 1",
                SbtcZoneKind.Migration,
                [new MapPoint(0.1, 0.1), new MapPoint(0.2, 0.1), new MapPoint(0.2, 0.2)])
        };

        var features = SbtcZoneOverlay.Create(null, null, bundled, isIslePilotServer: false, hasHostZones: false);

        var zone = Assert.Single(features);
        Assert.Equal("Migration zone 1", zone.Name);
    }
}
